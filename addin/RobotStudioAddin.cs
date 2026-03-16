using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABB.Robotics.RobotStudio;
using ABB.Robotics.RobotStudio.Stations;
using ABB.Robotics.RobotStudio.Stations.Forms;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.MotionDomain;
using ABB.Robotics.Controllers.RapidDomain;
using ABB.Robotics.Controllers.EventLogDomain;
using ABB.Robotics.Controllers.IOSystemDomain;
using Newtonsoft.Json;

namespace RobotStudioMcpAddin
{
    public class Addin
    {
        private static TcpListener _tcpListener;
        private static CancellationTokenSource _cts;
        private static readonly object _lock = new object();
        private const int Port = 8080;

        public static void AddinMain()
        {
            try
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Initializing..."));
                _cts = new CancellationTokenSource();
                var thread = new Thread(() => RunServer(_cts.Token));
                thread.IsBackground = true;
                thread.Start();
                Logger.AddMessage(new LogMessage("MCP Add-in: Started on port " + Port));
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Failed to start - " + ex.Message, LogMessageSeverity.Error));
            }
        }

        public static void AddinUnload()
        {
            try
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Shutting down..."));
                if (_cts != null) _cts.Cancel();
                lock (_lock)
                {
                    if (_tcpListener != null) _tcpListener.Stop();
                    _tcpListener = null;
                }
                Logger.AddMessage(new LogMessage("MCP Add-in: Stopped."));
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Error during shutdown - " + ex.Message, LogMessageSeverity.Warning));
            }
        }

        private static void RunServer(CancellationToken ct)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, Port);
                _tcpListener.Start();
                Logger.AddMessage(new LogMessage("MCP Add-in: TCP server listening on 127.0.0.1:" + Port));

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (!_tcpListener.Pending())
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                        var client = _tcpListener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                    }
                    catch (SocketException)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Server error - " + ex.ToString(), LogMessageSeverity.Error));
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 5000;

                using (client)
                using (var stream = client.GetStream())
                {
                    var requestLine = "";
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var buffer = new byte[65536];
                    var received = new StringBuilder();

                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        received.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                        if (received.ToString().Contains("\r\n\r\n"))
                            break;
                    }

                    var rawRequest = received.ToString();
                    var headerEnd = rawRequest.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEnd < 0)
                    {
                        SendResponse(stream, 400, "Bad Request");
                        return;
                    }

                    var headerSection = rawRequest.Substring(0, headerEnd);
                    var body = rawRequest.Substring(headerEnd + 4);
                    var lines = headerSection.Split(new[] { "\r\n" }, StringSplitOptions.None);

                    if (lines.Length == 0)
                    {
                        SendResponse(stream, 400, "Bad Request");
                        return;
                    }

                    requestLine = lines[0];
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var colonIdx = lines[i].IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var key = lines[i].Substring(0, colonIdx).Trim();
                            var val = lines[i].Substring(colonIdx + 1).Trim();
                            headers[key] = val;
                        }
                    }

                    string clStr;
                    int contentLength;
                    if (headers.TryGetValue("Content-Length", out clStr) && int.TryParse(clStr, out contentLength))
                    {
                        while (Encoding.UTF8.GetByteCount(body) < contentLength)
                        {
                            bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) break;
                            body += Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        }
                    }

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2)
                    {
                        SendResponse(stream, 400, "Bad Request");
                        return;
                    }

                    var method = parts[0].ToUpperInvariant();
                    var path = parts[1].ToLowerInvariant();

                    var qIdx = path.IndexOf('?');
                    if (qIdx >= 0) path = path.Substring(0, qIdx);

                    if (method == "OPTIONS")
                    {
                        SendResponse(stream, 200, "");
                        return;
                    }

                    string responseJson;
                    int statusCode = 200;

                    switch (path)
                    {
                        case "/health":
                            responseJson = JsonConvert.SerializeObject(new { status = "ok", timestamp = DateTime.UtcNow.ToString("o") });
                            break;
                        case "/joints":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetJoints(out statusCode);
                            break;
                        case "/simulation":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleSimulation(body, out statusCode);
                            break;
                        case "/status":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetStatus(out statusCode);
                            break;
                        case "/rapid/upload":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleRapidUpload(body, out statusCode);
                            break;
                        case "/rapid/execute":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleRapidExecute(body, out statusCode);
                            break;
                        case "/rapid/status":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleRapidStatus(out statusCode);
                            break;
                        case "/rapid/source":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleRapidSource(body, out statusCode);
                            break;
                        case "/rapid/modules":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleListModules(out statusCode);
                            break;
                        case "/rapid/errors":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetErrors(out statusCode);
                            break;
                        case "/screenshot":
                            if (method != "GET" && method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleScreenshot(body, out statusCode);
                            break;
                        case "/rapid/variable":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleReadVariable(body, out statusCode);
                            break;
                        case "/rapid/variable/set":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleSetVariable(body, out statusCode);
                            break;
                        case "/io/signals":
                            if (method != "GET" && method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetIOSignals(body, out statusCode);
                            break;
                        case "/io/signals/set":
                            if (method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleSetIOSignal(body, out statusCode);
                            break;
                        case "/scene/objects":
                            if (method != "GET" && method != "POST") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetSceneObjects(body, out statusCode);
                            break;
                        default:
                            statusCode = 404;
                            responseJson = JsonConvert.SerializeObject(new ErrorResponse
                            {
                                Success = false,
                                Error = "Not Found",
                                Message = "Endpoint '" + path + "' not found. Available: /health, /joints, /status, /simulation, /rapid/upload, /rapid/execute, /rapid/status, /rapid/source, /rapid/modules, /rapid/errors, /rapid/variable, /rapid/variable/set, /io/signals, /io/signals/set, /scene/objects, /screenshot"
                            });
                            break;
                    }

                    SendResponse(stream, statusCode, responseJson);
                }
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage("MCP Add-in: Request error - " + ex.Message, LogMessageSeverity.Warning));
            }
        }

        private static void SendResponse(NetworkStream stream, int statusCode, string jsonBody)
        {
            string statusText;
            switch (statusCode)
            {
                case 200: statusText = "OK"; break;
                case 400: statusText = "Bad Request"; break;
                case 404: statusText = "Not Found"; break;
                case 405: statusText = "Method Not Allowed"; break;
                case 409: statusText = "Conflict"; break;
                case 422: statusText = "Unprocessable Entity"; break;
                case 500: statusText = "Internal Server Error"; break;
                default: statusText = "Error"; break;
            }

            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(statusCode).Append(" ").AppendLine(statusText);
            sb.AppendLine("Content-Type: application/json; charset=utf-8");
            sb.Append("Content-Length: ").AppendLine(bodyBytes.Length.ToString());
            sb.AppendLine("Access-Control-Allow-Origin: *");
            sb.AppendLine("Access-Control-Allow-Methods: GET, POST, OPTIONS");
            sb.AppendLine("Access-Control-Allow-Headers: Content-Type");
            sb.AppendLine("Connection: close");
            sb.AppendLine();

            var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        #region Existing Handlers

        private static string HandleGetJoints(out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                JointData jointData;
                using (controller)
                {
                    jointData = GetJointPositions(controller);
                }

                statusCode = 200;
                return JsonConvert.SerializeObject(new JointResponse
                {
                    Success = true,
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Joints = jointData
                }, Formatting.Indented);
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Joint Read Error", Message = ex.Message });
            }
        }

        private static Controller TryGetController(Station station)
        {
            if (station == null || station.Irc5Controllers == null || station.Irc5Controllers.Count == 0)
                return null;

            for (int i = 0; i < station.Irc5Controllers.Count; i++)
            {
                var vc = station.Irc5Controllers[i];
                if (vc == null || string.IsNullOrWhiteSpace(vc.SystemId))
                    continue;

                Guid systemId;
                if (!Guid.TryParse(vc.SystemId, out systemId))
                    continue;

                try
                {
                    return Controller.Connect(systemId, ConnectionType.RobotStudio, false);
                }
                catch (Exception ex)
                {
                    Logger.AddMessage(new LogMessage("MCP Add-in: Failed to connect to controller " + vc.SystemId + ": " + ex.Message, LogMessageSeverity.Warning));
                }
            }

            return null;
        }

        private static JointData GetJointPositions(Controller controller)
        {
            var jointData = new JointData();

            using (Mastership.Request(controller))
            {
                var motionSystem = controller.MotionSystem;
                if (motionSystem != null && motionSystem.MechanicalUnits.Count > 0)
                {
                    var mechUnit = motionSystem.MechanicalUnits[0];
                    var jointTarget = mechUnit.GetPosition();
                    var robAx = jointTarget.RobAx;

                    jointData.J1 = Math.Round(robAx.Rax_1, 3);
                    jointData.J2 = Math.Round(robAx.Rax_2, 3);
                    jointData.J3 = Math.Round(robAx.Rax_3, 3);
                    jointData.J4 = Math.Round(robAx.Rax_4, 3);
                    jointData.J5 = Math.Round(robAx.Rax_5, 3);
                    jointData.J6 = Math.Round(robAx.Rax_6, 3);
                }
            }

            return jointData;
        }

        private static string HandleSimulation(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<SimulationRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'action' field with value 'start' or 'stop'." });
                }

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                string action = request.Action.ToLowerInvariant();
                string message;

                switch (action)
                {
                    case "start":
                        if (!IsSimulationRunning())
                        {
                            Simulator.Start();
                            message = "Simulation started.";
                        }
                        else
                        {
                            message = "Simulation is already running.";
                        }
                        break;
                    case "stop":
                        if (IsSimulationRunning())
                        {
                            Simulator.Stop();
                            message = "Simulation stopped.";
                        }
                        else
                        {
                            message = "Simulation is not running.";
                        }
                        break;
                    case "reset":
                        if (IsSimulationRunning())
                        {
                            Simulator.Stop();
                        }
                        // Delete dynamically created boxes (Caja_gr_N, Caja_pq_N) on UI thread
                        int deletedCount = 0;
                        var gc2 = GraphicControl.ActiveGraphicControl;
                        if (gc2 != null)
                        {
                            var resetWait = new ManualResetEvent(false);
                            gc2.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    var toDelete = new List<GraphicComponent>();
                                    for (int idx = 0; idx < station.GraphicComponents.Count; idx++)
                                    {
                                        var c = station.GraphicComponents[idx];
                                        try
                                        {
                                            foreach (var child in c.Children)
                                            {
                                                var childGc = child as GraphicComponent;
                                                if (childGc == null) continue;
                                                string n = childGc.Name ?? "";
                                                if ((n.StartsWith("Caja_gr_") || n.StartsWith("Caja_pq_"))
                                                    && int.TryParse(n.Substring(8), out _))
                                                {
                                                    toDelete.Add(childGc);
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    foreach (var obj in toDelete)
                                    {
                                        try
                                        {
                                            var parent = obj.Parent as SmartComponent;
                                            if (parent != null)
                                            {
                                                parent.GraphicComponents.Remove(obj);
                                            }
                                            obj.Delete();
                                            deletedCount++;
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                                finally { resetWait.Set(); }
                            }));
                            resetWait.WaitOne(10000);
                        }
                        message = "Simulation reset. Deleted " + deletedCount + " dynamic objects.";
                        break;
                    default:
                        statusCode = 400;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Action", Message = "Unknown action '" + action + "'. Use 'start', 'stop', or 'reset'." });
                }

                statusCode = 200;
                return JsonConvert.SerializeObject(new SimulationResponse
                {
                    Success = true,
                    Message = message,
                    IsRunning = IsSimulationRunning()
                }, Formatting.Indented);
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Simulation Control Error", Message = ex.Message });
            }
        }

        private static string HandleGetStatus(out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;

                statusCode = 200;
                return JsonConvert.SerializeObject(new StatusResponse
                {
                    HasActiveStation = station != null,
                    StationName = station != null ? station.Name : "",
                    IsSimulationRunning = IsSimulationRunning(),
                    VirtualControllerCount = station != null && station.Irc5Controllers != null ? station.Irc5Controllers.Count : 0
                }, Formatting.Indented);
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Status Error", Message = ex.Message });
            }
        }

        private static bool IsSimulationRunning()
        {
            try
            {
                return Simulator.State == SimulationState.Running;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region RAPID Handlers

        private static string HandleRapidUpload(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<RapidUploadRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.Code))
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'code' field with RAPID source code." });
                }

                string moduleName = string.IsNullOrEmpty(request.ModuleName) ? "McpModule" : request.ModuleName;
                string taskName = string.IsNullOrEmpty(request.TaskName) ? "T_ROB1" : request.TaskName;
                bool replace = request.ReplaceExisting;

                string fileName = moduleName;
                if (!fileName.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
                    fileName = fileName + ".mod";

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    if (controller.Rapid.ExecutionStatus == ExecutionStatus.Running)
                    {
                        statusCode = 409;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Execution Running", Message = "Cannot upload module while RAPID execution is running. Stop execution first." });
                    }

                    string tempFile = Path.Combine(Path.GetTempPath(), fileName);
                    // RAPID requires no BOM and \r\n line endings
                    string normalizedCode = request.Code.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    File.WriteAllText(tempFile, normalizedCode, new UTF8Encoding(false));

                    try
                    {
                        string homePath;
                        try
                        {
                            homePath = controller.GetEnvironmentVariable("HOME");
                        }
                        catch (Exception envEx)
                        {
                            Logger.AddMessage(new LogMessage("MCP Add-in: GetEnvironmentVariable HOME failed: " + envEx.Message + ", using fallback", LogMessageSeverity.Warning));
                            homePath = controller.FileSystem.RemoteDirectory;
                        }

                        // For virtual controllers, HOME returns a local Windows path.
                        // Copy the file directly instead of using controller.FileSystem.PutFile.
                        string destFile = Path.Combine(homePath.Replace('/', '\\'), fileName);
                        try
                        {
                            File.Copy(tempFile, destFile, true);
                        }
                        catch (Exception copyEx)
                        {
                            statusCode = 500;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "File Copy Failed", Message = "Copy failed: " + copyEx.Message + " (dest: " + destFile + ")" });
                        }

                        // Use controller-relative path for LoadModuleFromFile
                        string controllerFilePath = homePath + "/" + fileName;

                        ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                        if (rapidTask == null)
                        {
                            statusCode = 404;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                        }

                        bool loadSuccess;
                        try
                        {
                            using (Mastership.Request(controller.Rapid))
                            {
                                // Delete ALL program modules before loading to avoid
                                // "Global routine name main ambiguous" from stale modules
                                if (replace)
                                {
                                    try
                                    {
                                        ABB.Robotics.Controllers.RapidDomain.Module[] modules = rapidTask.GetModules();
                                        for (int m = 0; m < modules.Length; m++)
                                        {
                                            string mName = modules[m].Name;
                                            // Skip system modules (BASE, user, etc.)
                                            if (string.Equals(mName, "BASE", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(mName, "user", StringComparison.OrdinalIgnoreCase))
                                            {
                                                continue;
                                            }
                                            modules[m].Delete();
                                            Logger.AddMessage(new LogMessage("MCP Add-in: Deleted program module '" + mName + "' before reload."));
                                        }
                                    }
                                    catch (Exception delEx)
                                    {
                                        Logger.AddMessage(new LogMessage("MCP Add-in: Module cleanup error: " + delEx.Message, LogMessageSeverity.Warning));
                                    }
                                }

                                loadSuccess = rapidTask.LoadModuleFromFile(controllerFilePath, RapidLoadMode.Add);
                            }
                        }
                        catch (Exception loadEx)
                        {
                            statusCode = 500;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Module Load Exception", Message = "LoadModuleFromFile threw: " + loadEx.Message });
                        }

                        if (!loadSuccess)
                        {
                            string errorDetails = ReadRecentErrors(controller, 5);
                            statusCode = 422;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Module Load Failed", Message = "Failed to load module. Errors: " + errorDetails });
                        }

                        statusCode = 200;
                        return JsonConvert.SerializeObject(new RapidUploadResponse
                        {
                            Success = true,
                            Message = "Module '" + moduleName + "' loaded successfully into task '" + taskName + "'.",
                            ModuleName = moduleName,
                            TaskName = taskName
                        }, Formatting.Indented);
                    }
                    finally
                    {
                        try { File.Delete(tempFile); }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Upload Error", Message = ex.Message });
            }
        }

        private static string HandleRapidExecute(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<RapidExecuteRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'action' field with value 'start', 'stop', or 'resetpp'." });
                }

                string action = request.Action.ToLowerInvariant();
                string taskName = string.IsNullOrEmpty(request.TaskName) ? "T_ROB1" : request.TaskName;

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);
                    string message;

                    switch (action)
                    {
                        case "start":
                        {
                            RegainMode regain = RegainMode.Continue;
                            ExecutionMode execMode = ExecutionMode.Continuous;
                            ExecutionCycle cycle = ExecutionCycle.Once;

                            if (!string.IsNullOrEmpty(request.ExecutionMode))
                            {
                                string em = request.ExecutionMode.ToLowerInvariant();
                                if (em == "step_over") execMode = ExecutionMode.StepOver;
                                else if (em == "step_in") execMode = ExecutionMode.StepIn;
                            }

                            if (!string.IsNullOrEmpty(request.Cycle))
                            {
                                string c = request.Cycle.ToLowerInvariant();
                                if (c == "forever") cycle = ExecutionCycle.Forever;
                            }

                            using (Mastership.Request(controller.Rapid))
                            {
                                StartResult result = controller.Rapid.Start(regain, execMode, cycle, StartCheck.CallChain, true);
                                if (result != StartResult.Ok)
                                {
                                    statusCode = 422;
                                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Start Failed", Message = "RAPID start returned: " + result.ToString() });
                                }
                            }
                            message = "RAPID execution started.";
                            break;
                        }
                        case "stop":
                        {
                            StopMode stopMode = StopMode.Instruction;

                            if (!string.IsNullOrEmpty(request.StopMode))
                            {
                                string sm = request.StopMode.ToLowerInvariant();
                                if (sm == "cycle") stopMode = StopMode.Cycle;
                                else if (sm == "immediate") stopMode = StopMode.Immediate;
                            }

                            using (Mastership.Request(controller.Rapid))
                            {
                                controller.Rapid.Stop(stopMode);
                            }
                            message = "RAPID execution stopped.";
                            break;
                        }
                        case "resetpp":
                        {
                            ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                            if (rapidTask == null)
                            {
                                statusCode = 404;
                                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                            }

                            using (Mastership.Request(controller.Rapid))
                            {
                                rapidTask.ResetProgramPointer();
                            }
                            message = "Program pointer reset to main entry point in task '" + taskName + "'.";
                            break;
                        }
                        default:
                            statusCode = 400;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Action", Message = "Unknown action '" + action + "'. Use 'start', 'stop', or 'resetpp'." });
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new RapidExecuteResponse
                    {
                        Success = true,
                        Message = message,
                        ExecutionStatus = controller.Rapid.ExecutionStatus.ToString()
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Execution Control Error", Message = ex.Message });
            }
        }

        private static string HandleRapidStatus(out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    ABB.Robotics.Controllers.RapidDomain.Task[] tasks = controller.Rapid.GetTasks();
                    var taskDataList = new List<TaskStatusData>();

                    for (int i = 0; i < tasks.Length; i++)
                    {
                        var t = tasks[i];
                        var taskData = new TaskStatusData
                        {
                            Name = t.Name,
                            ExecutionStatus = t.ExecutionStatus.ToString(),
                            Enabled = t.Enabled,
                            Type = t.Type.ToString()
                        };

                        try
                        {
                            var pp = t.ProgramPointer;
                            if (pp != null)
                            {
                                taskData.ProgramPointer = new ProgramPointerData
                                {
                                    Module = pp.Module,
                                    Routine = pp.Routine,
                                    Range = pp.Range.ToString()
                                };
                            }
                        }
                        catch { }

                        try
                        {
                            var mp = t.MotionPointer;
                            if (mp != null)
                            {
                                taskData.MotionPointer = new ProgramPointerData
                                {
                                    Module = mp.Module,
                                    Routine = mp.Routine,
                                    Range = mp.Range.ToString()
                                };
                            }
                        }
                        catch { }

                        taskDataList.Add(taskData);
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new RapidStatusResponse
                    {
                        Success = true,
                        ControllerExecutionStatus = controller.Rapid.ExecutionStatus.ToString(),
                        Tasks = taskDataList
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "RAPID Status Error", Message = ex.Message });
            }
        }

        private static string HandleListModules(out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    var tasksResult = new List<RapidTaskModulesData>();
                    ABB.Robotics.Controllers.RapidDomain.Task[] tasks = controller.Rapid.GetTasks();

                    foreach (var task in tasks)
                    {
                        var taskData = new RapidTaskModulesData
                        {
                            TaskName = task.Name,
                            Modules = new List<RapidModuleInfo>()
                        };

                        ABB.Robotics.Controllers.RapidDomain.Module[] modules = task.GetModules();
                        foreach (var module in modules)
                        {
                            taskData.Modules.Add(new RapidModuleInfo
                            {
                                Name = module.Name,
                                IsSystem = module.IsSystem
                            });
                        }

                        tasksResult.Add(taskData);
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new RapidModulesListResponse
                    {
                        Success = true,
                        Tasks = tasksResult
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "List Modules Error", Message = ex.Message });
            }
        }

        private static string HandleRapidSource(string body, out int statusCode)
        {
            try
            {
                RapidSourceRequest request;
                if (string.IsNullOrWhiteSpace(body))
                    request = new RapidSourceRequest();
                else
                    request = JsonConvert.DeserializeObject<RapidSourceRequest>(body) ?? new RapidSourceRequest();

                string taskName = string.IsNullOrEmpty(request.TaskName) ? "T_ROB1" : request.TaskName;

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                    if (rapidTask == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                    }

                    string moduleName = request.ModuleName;
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        ABB.Robotics.Controllers.RapidDomain.Module[] modules = rapidTask.GetModules();
                        for (int i = 0; i < modules.Length; i++)
                        {
                            string candidate = modules[i].Name;
                            if (string.Equals(candidate, "BASE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(candidate, "user", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            moduleName = candidate;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(moduleName))
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Module Not Found", Message = "No non-system RAPID module found in task '" + taskName + "'." });
                    }

                    ABB.Robotics.Controllers.RapidDomain.Module module = rapidTask.GetModule(moduleName);
                    if (module == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse
                        {
                            Success = false,
                            Error = "Module Not Found",
                            Message = "RAPID module '" + moduleName + "' not found in task '" + taskName + "'."
                        });
                    }

                    string tempDir = Path.Combine(Path.GetTempPath(), "rsmcp_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    string tempFileName = module.Name + (module.IsSystem ? ".sys" : ".mod");
                    string tempFilePath = Path.Combine(tempDir, tempFileName);

                    try
                    {
                        module.SaveToFile(tempDir);
                        string sourceCode = File.ReadAllText(tempFilePath, Encoding.UTF8);

                        statusCode = 200;
                        return JsonConvert.SerializeObject(new RapidSourceResponse
                        {
                            Success = true,
                            TaskName = taskName,
                            ModuleName = module.Name,
                            FilePath = tempFilePath,
                            Code = sourceCode
                        }, Formatting.Indented);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "RAPID Source Error", Message = ex.Message });
            }
        }

        private static string HandleGetErrors(out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    var messages = new List<EventLogMessageData>();
                    EventLogCategory[] categories = controller.EventLog.GetCategories();

                    for (int c = 0; c < categories.Length; c++)
                    {
                        EventLogCategory cat = categories[c];
                        try
                        {
                            foreach (EventLogMessage msg in cat.Messages)
                            {
                                messages.Add(new EventLogMessageData
                                {
                                    SequenceNumber = msg.SequenceNumber,
                                    Timestamp = msg.Timestamp.ToString("o"),
                                    Title = msg.Title,
                                    Body = msg.Body,
                                    CategoryName = cat.Name,
                                    Type = msg.Type.ToString()
                                });
                            }
                        }
                        catch { }
                    }

                    messages.Sort(delegate(EventLogMessageData a, EventLogMessageData b) {
                        return b.SequenceNumber.CompareTo(a.SequenceNumber);
                    });

                    if (messages.Count > 50)
                    {
                        messages = messages.GetRange(0, 50);
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new EventLogResponse
                    {
                        Success = true,
                        Messages = messages
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Event Log Error", Message = ex.Message });
            }
        }

        private static string ReadRecentErrors(Controller controller, int maxMessages)
        {
            try
            {
                var sb = new StringBuilder();
                EventLogCategory[] categories = controller.EventLog.GetCategories();
                int count = 0;

                for (int c = 0; c < categories.Length && count < maxMessages; c++)
                {
                    EventLogCategory cat = categories[c];
                    try
                    {
                        foreach (EventLogMessage msg in cat.Messages)
                        {
                            sb.Append("[").Append(cat.Name).Append("] ");
                            sb.Append(msg.Title);
                            if (!string.IsNullOrEmpty(msg.Body))
                            {
                                sb.Append(": ").Append(msg.Body);
                            }
                            sb.Append("; ");
                            count++;
                            if (count >= maxMessages) break;
                        }
                    }
                    catch { }
                }

                return sb.Length > 0 ? sb.ToString() : "No error messages found.";
            }
            catch (Exception ex)
            {
                return "Could not read event log: " + ex.Message;
            }
        }

        private static string HandleScreenshot(string body, out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse
                    {
                        Success = false,
                        Error = "No Active Station",
                        Message = "No station is currently open in RobotStudio."
                    });
                }

                // Parse optional width/height from request body
                int width = 1280;
                int height = 720;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var req = JsonConvert.DeserializeObject<ScreenshotRequest>(body);
                        if (req != null)
                        {
                            if (req.Width > 0) width = Math.Min(req.Width, 3840);
                            if (req.Height > 0) height = Math.Min(req.Height, 2160);
                        }
                    }
                    catch { /* use defaults */ }
                }

                // GraphicControl.ScreenShot must run on the UI thread
                string base64Data = null;
                Exception captureError = null;
                int actualWidth = width;
                int actualHeight = height;

                var gc = GraphicControl.ActiveGraphicControl;
                if (gc == null)
                {
                    statusCode = 500;
                    return JsonConvert.SerializeObject(new ErrorResponse
                    {
                        Success = false,
                        Error = "No Graphic Control",
                        Message = "No active 3D view found in RobotStudio."
                    });
                }

                var waitHandle = new ManualResetEvent(false);

                // Marshal to the UI thread via Control.Invoke (it's a WinForms UserControl)
                gc.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using (Bitmap bmp = gc.ScreenShot(width, height))
                        {
                            if (bmp != null)
                            {
                                actualWidth = bmp.Width;
                                actualHeight = bmp.Height;
                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Png);
                                    base64Data = Convert.ToBase64String(ms.ToArray());
                                }
                            }
                            else
                            {
                                captureError = new Exception("ScreenShot returned null.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        captureError = ex;
                    }
                    finally
                    {
                        waitHandle.Set();
                    }
                }));

                // Wait up to 15 seconds for the UI thread to complete
                if (!waitHandle.WaitOne(15000))
                {
                    statusCode = 500;
                    return JsonConvert.SerializeObject(new ErrorResponse
                    {
                        Success = false,
                        Error = "Timeout",
                        Message = "Screenshot capture timed out waiting for the UI thread."
                    });
                }

                if (captureError != null)
                {
                    statusCode = 500;
                    return JsonConvert.SerializeObject(new ErrorResponse
                    {
                        Success = false,
                        Error = "Screenshot Failed",
                        Message = "Failed to capture screenshot: " + captureError.Message
                    });
                }

                statusCode = 200;
                return JsonConvert.SerializeObject(new ScreenshotResponse
                {
                    Success = true,
                    Message = "Screenshot captured successfully.",
                    ImageBase64 = base64Data,
                    Width = actualWidth,
                    Height = actualHeight,
                    MimeType = "image/png",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse
                {
                    Success = false,
                    Error = "Internal Error",
                    Message = "Screenshot error: " + ex.Message
                });
            }
        }

        #endregion

        #region Scene Objects Handler

        private static string HandleGetSceneObjects(string body, out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                // Parse optional filter from body
                string nameFilter = null;
                bool includeChildren = true;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var req = JsonConvert.DeserializeObject<SceneObjectsRequest>(body);
                        if (req != null)
                        {
                            nameFilter = req.NameFilter;
                            if (req.IncludeChildren.HasValue) includeChildren = req.IncludeChildren.Value;
                        }
                    }
                    catch { /* use defaults */ }
                }

                var objects = new List<SceneObjectData>();

                // Access GraphicComponents on the UI thread since it's a station object
                Exception accessError = null;
                var waitHandle = new ManualResetEvent(false);

                var gc = GraphicControl.ActiveGraphicControl;
                if (gc != null)
                {
                    gc.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            CollectSceneObjects(station.GraphicComponents, objects, nameFilter, includeChildren, 0);
                        }
                        catch (Exception ex)
                        {
                            accessError = ex;
                        }
                        finally
                        {
                            waitHandle.Set();
                        }
                    }));

                    if (!waitHandle.WaitOne(10000))
                    {
                        statusCode = 500;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Timeout", Message = "Timed out reading scene objects from UI thread." });
                    }
                }
                else
                {
                    // Try direct access if no graphic control
                    try
                    {
                        CollectSceneObjects(station.GraphicComponents, objects, nameFilter, includeChildren, 0);
                    }
                    catch (Exception ex)
                    {
                        accessError = ex;
                    }
                }

                if (accessError != null)
                {
                    statusCode = 500;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Access Error", Message = "Failed to read scene objects: " + accessError.Message });
                }

                statusCode = 200;
                return JsonConvert.SerializeObject(new SceneObjectsResponse
                {
                    Success = true,
                    StationName = station.Name,
                    ObjectCount = objects.Count,
                    Objects = objects
                });
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Internal Error", Message = "Scene objects error: " + ex.Message });
            }
        }

        private static void CollectSceneObjects(GraphicComponentCollection components, List<SceneObjectData> result, string nameFilter, bool includeChildren, int depth)
        {
            if (components == null) return;
            for (int i = 0; i < components.Count; i++)
            {
                var comp = components[i];
                if (comp == null) continue;
                ProcessGraphicComponent(comp, result, nameFilter, includeChildren, depth);
            }
        }

        private static void ProcessGraphicComponent(GraphicComponent comp, List<SceneObjectData> result, string nameFilter, bool includeChildren, int depth)
        {
            var obj = new SceneObjectData();
            obj.Name = comp.Name ?? "(unnamed)";
            obj.TypeName = comp.GetType().Name;
            obj.Depth = depth;

            // Get transform (position & rotation)
            try
            {
                var transform = comp.Transform;
                if (transform != null)
                {
                    obj.Position = new PositionData { X = Math.Round(transform.X, 3), Y = Math.Round(transform.Y, 3), Z = Math.Round(transform.Z, 3) };
                    obj.EulerAngles = new EulerAnglesData { RX = Math.Round(transform.RX, 3), RY = Math.Round(transform.RY, 3), RZ = Math.Round(transform.RZ, 3) };

                    // Also get global position
                    try
                    {
                        var gm = transform.GlobalMatrix;
                        var gt = gm.Translation;
                        obj.GlobalPosition = new PositionData { X = Math.Round(gt.x, 3), Y = Math.Round(gt.y, 3), Z = Math.Round(gt.z, 3) };
                    }
                    catch { /* GlobalMatrix may not be available */ }
                }
            }
            catch
            {
                // Some components may not have a valid transform
            }

            // Check if it's visible
            try
            {
                obj.Visible = comp.Visible;
            }
            catch { obj.Visible = true; }

            // If name filter is active and this item doesn't match, only include if a child matches
            bool nameMatches = string.IsNullOrEmpty(nameFilter) || (comp.Name != null && comp.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            // Collect children
            var children = new List<SceneObjectData>();
            if (includeChildren && depth < 5)
            {
                try
                {
                    foreach (var child in comp.Children)
                    {
                        var childGc = child as GraphicComponent;
                        if (childGc != null)
                        {
                            ProcessGraphicComponent(childGc, children, nameFilter, includeChildren, depth + 1);
                        }
                    }
                }
                catch { /* no children or Children not supported */ }
            }

            if (children.Count > 0)
            {
                obj.Children = children;
            }

            // Add this object if it matches or has matching children
            if (nameMatches || children.Count > 0)
            {
                result.Add(obj);
            }
        }

        #endregion

        #region RAPID Variable & IO Handlers

        private static string HandleReadVariable(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<RapidVariableRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.VariableName))
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'variableName'. Optional: 'taskName', 'moduleName'." });
                }

                string taskName = string.IsNullOrEmpty(request.TaskName) ? "T_ROB1" : request.TaskName;

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                    if (rapidTask == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                    }

                    // If moduleName not provided, find the first non-system module
                    string moduleName = request.ModuleName;
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        ABB.Robotics.Controllers.RapidDomain.Module[] modules = rapidTask.GetModules();
                        for (int i = 0; i < modules.Length; i++)
                        {
                            string candidate = modules[i].Name;
                            if (string.Equals(candidate, "BASE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(candidate, "user", StringComparison.OrdinalIgnoreCase))
                                continue;
                            moduleName = candidate;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(moduleName))
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Module Not Found", Message = "No non-system RAPID module found in task '" + taskName + "'." });
                    }

                    RapidData rapidData = rapidTask.GetRapidData(moduleName, request.VariableName);
                    if (rapidData == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Variable Not Found", Message = "RAPID variable '" + request.VariableName + "' not found in module '" + moduleName + "'." });
                    }

                    string valueStr = rapidData.Value.ToString();
                    string dataType = rapidData.RapidType;

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new RapidVariableResponse
                    {
                        Success = true,
                        TaskName = taskName,
                        ModuleName = moduleName,
                        VariableName = request.VariableName,
                        Value = valueStr,
                        DataType = dataType
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Variable Read Error", Message = ex.Message });
            }
        }

        private static string HandleGetIOSignals(string body, out int statusCode)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    // Parse optional filter from body
                    string filterName = null;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            var req = JsonConvert.DeserializeObject<IOSignalRequest>(body);
                            if (req != null) filterName = req.SignalName;
                        }
                        catch { }
                    }

                    var signalList = new List<IOSignalData>();
                    SignalCollection signals = controller.IOSystem.GetSignals(IOFilterTypes.All);

                    foreach (Signal signal in signals)
                    {
                        try
                        {
                            // If filter provided, only return matching signal
                            if (!string.IsNullOrEmpty(filterName))
                            {
                                if (!string.Equals(signal.Name, filterName, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }

                            var sigData = new IOSignalData
                            {
                                Name = signal.Name,
                                Type = signal.Type.ToString(),
                                Value = signal.Value.ToString()
                            };

                            // Try to get logical state for digital signals
                            try
                            {
                                if (signal.Type == SignalType.DigitalInput || signal.Type == SignalType.DigitalOutput)
                                {
                                    sigData.LogicalState = ((int)signal.Value == 1) ? "HIGH" : "LOW";
                                }
                            }
                            catch { }

                            signalList.Add(sigData);
                        }
                        catch { }
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new IOSignalsResponse
                    {
                        Success = true,
                        SignalCount = signalList.Count,
                        Signals = signalList
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "IO Signal Error", Message = ex.Message });
            }
        }

        private static string HandleSetVariable(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<SetRapidVariableRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.VariableName) || request.Value == null)
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'variableName' and 'value'. Optional: 'taskName', 'moduleName'." });
                }

                string taskName = string.IsNullOrEmpty(request.TaskName) ? "T_ROB1" : request.TaskName;

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                    if (rapidTask == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                    }

                    // If moduleName not provided, find the first non-system module
                    string moduleName = request.ModuleName;
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        ABB.Robotics.Controllers.RapidDomain.Module[] modules = rapidTask.GetModules();
                        for (int i = 0; i < modules.Length; i++)
                        {
                            string candidate = modules[i].Name;
                            if (string.Equals(candidate, "BASE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(candidate, "user", StringComparison.OrdinalIgnoreCase))
                                continue;
                            moduleName = candidate;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(moduleName))
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Module Not Found", Message = "No non-system RAPID module found in task '" + taskName + "'." });
                    }

                    RapidData rapidData = rapidTask.GetRapidData(moduleName, request.VariableName);
                    if (rapidData == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Variable Not Found", Message = "RAPID variable '" + request.VariableName + "' not found in module '" + moduleName + "'." });
                    }

                    string dataType = rapidData.RapidType;
                    string oldValue = rapidData.Value.ToString();

                    // Request mastership to write
                    using (Mastership m = Mastership.Request(controller.Rapid))
                    {
                        rapidData.StringValue = request.Value;
                    }

                    string newValue = rapidData.Value.ToString();

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new SetRapidVariableResponse
                    {
                        Success = true,
                        Message = "Variable '" + request.VariableName + "' updated successfully.",
                        TaskName = taskName,
                        ModuleName = moduleName,
                        VariableName = request.VariableName,
                        PreviousValue = oldValue,
                        NewValue = newValue,
                        DataType = dataType
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Variable Write Error", Message = ex.Message });
            }
        }

        private static string HandleSetIOSignal(string body, out int statusCode)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<SetIOSignalRequest>(body);
                if (request == null || string.IsNullOrEmpty(request.SignalName))
                {
                    statusCode = 400;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Request", Message = "Request body must contain 'signalName' and 'value'." });
                }

                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Active Station", Message = "No station is currently open in RobotStudio." });
                }

                Controller controller = TryGetController(station);
                if (controller == null)
                {
                    statusCode = 404;
                    return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "No Controller", Message = "No virtual controller found in the station." });
                }

                using (controller)
                {
                    controller.Logon(UserInfo.DefaultUser);

                    Signal signal = controller.IOSystem.GetSignal(request.SignalName);
                    if (signal == null)
                    {
                        statusCode = 404;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Signal Not Found", Message = "I/O signal '" + request.SignalName + "' not found." });
                    }

                    string oldValue = signal.Value.ToString();

                    // Set signal value (Signal.Value is float)
                    signal.Value = (float)request.Value;

                    string newValue = signal.Value.ToString();
                    string logicalState = null;
                    if (signal.Type == SignalType.DigitalInput || signal.Type == SignalType.DigitalOutput)
                    {
                        logicalState = ((int)signal.Value == 1) ? "HIGH" : "LOW";
                    }

                    statusCode = 200;
                    return JsonConvert.SerializeObject(new SetIOSignalResponse
                    {
                        Success = true,
                        Message = "Signal '" + request.SignalName + "' set to " + newValue + ".",
                        SignalName = request.SignalName,
                        SignalType = signal.Type.ToString(),
                        PreviousValue = oldValue,
                        NewValue = newValue,
                        LogicalState = logicalState
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "IO Signal Write Error", Message = ex.Message });
            }
        }

        #endregion
    }

    #region Data Transfer Objects

    public class JointData
    {
        [JsonProperty("j1")] public double J1 { get; set; }
        [JsonProperty("j2")] public double J2 { get; set; }
        [JsonProperty("j3")] public double J3 { get; set; }
        [JsonProperty("j4")] public double J4 { get; set; }
        [JsonProperty("j5")] public double J5 { get; set; }
        [JsonProperty("j6")] public double J6 { get; set; }
    }

    public class JointResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("timestamp")] public string Timestamp { get; set; }
        [JsonProperty("joints")] public JointData Joints { get; set; }
    }

    public class SimulationRequest
    {
        [JsonProperty("action")] public string Action { get; set; }
    }

    public class SimulationResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("isRunning")] public bool IsRunning { get; set; }
    }

    public class StatusResponse
    {
        [JsonProperty("hasActiveStation")] public bool HasActiveStation { get; set; }
        [JsonProperty("stationName")] public string StationName { get; set; }
        [JsonProperty("isSimulationRunning")] public bool IsSimulationRunning { get; set; }
        [JsonProperty("virtualControllerCount")] public int VirtualControllerCount { get; set; }
    }

    public class ErrorResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    // RAPID Upload
    public class RapidUploadRequest
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("replaceExisting")] public bool ReplaceExisting { get; set; }
    }

    public class RapidUploadResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
    }

    // RAPID Execute
    public class RapidExecuteRequest
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("executionMode")] public string ExecutionMode { get; set; }
        [JsonProperty("cycle")] public string Cycle { get; set; }
        [JsonProperty("stopMode")] public string StopMode { get; set; }
    }

    public class RapidExecuteResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("executionStatus")] public string ExecutionStatus { get; set; }
    }

    // RAPID Status
    public class RapidStatusResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("controllerExecutionStatus")] public string ControllerExecutionStatus { get; set; }
        [JsonProperty("tasks")] public List<TaskStatusData> Tasks { get; set; }
    }

    // RAPID Modules List
    public class RapidModulesListResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("tasks")] public List<RapidTaskModulesData> Tasks { get; set; }
    }

    public class RapidTaskModulesData
    {
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("modules")] public List<RapidModuleInfo> Modules { get; set; }
    }

    public class RapidModuleInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("isSystem")] public bool IsSystem { get; set; }
    }

    public class RapidSourceRequest
    {
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
    }

    public class RapidSourceResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("filePath")] public string FilePath { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
    }

    public class TaskStatusData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("executionStatus")] public string ExecutionStatus { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("programPointer")] public ProgramPointerData ProgramPointer { get; set; }
        [JsonProperty("motionPointer")] public ProgramPointerData MotionPointer { get; set; }
    }

    public class ProgramPointerData
    {
        [JsonProperty("module")] public string Module { get; set; }
        [JsonProperty("routine")] public string Routine { get; set; }
        [JsonProperty("range")] public string Range { get; set; }
    }

    // Event Log
    public class EventLogResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("messages")] public List<EventLogMessageData> Messages { get; set; }
    }

    public class EventLogMessageData
    {
        [JsonProperty("sequenceNumber")] public int SequenceNumber { get; set; }
        [JsonProperty("timestamp")] public string Timestamp { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("body")] public string Body { get; set; }
        [JsonProperty("categoryName")] public string CategoryName { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
    }

    // RAPID Variable
    public class RapidVariableRequest
    {
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("variableName")] public string VariableName { get; set; }
    }

    public class RapidVariableResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("variableName")] public string VariableName { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
        [JsonProperty("dataType")] public string DataType { get; set; }
    }

    // IO Signals
    public class IOSignalRequest
    {
        [JsonProperty("signalName")] public string SignalName { get; set; }
    }

    public class IOSignalsResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("signalCount")] public int SignalCount { get; set; }
        [JsonProperty("signals")] public List<IOSignalData> Signals { get; set; }
    }

    public class IOSignalData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
        [JsonProperty("logicalState")] public string LogicalState { get; set; }
    }

    // Scene Objects
    public class SceneObjectsRequest
    {
        [JsonProperty("nameFilter")] public string NameFilter { get; set; }
        [JsonProperty("includeChildren")] public bool? IncludeChildren { get; set; }
    }

    public class SceneObjectsResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("stationName")] public string StationName { get; set; }
        [JsonProperty("objectCount")] public int ObjectCount { get; set; }
        [JsonProperty("objects")] public List<SceneObjectData> Objects { get; set; }
    }

    public class SceneObjectData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
        [JsonProperty("depth")] public int Depth { get; set; }
        [JsonProperty("visible")] public bool Visible { get; set; }
        [JsonProperty("position", NullValueHandling = NullValueHandling.Ignore)] public PositionData Position { get; set; }
        [JsonProperty("globalPosition", NullValueHandling = NullValueHandling.Ignore)] public PositionData GlobalPosition { get; set; }
        [JsonProperty("eulerAngles", NullValueHandling = NullValueHandling.Ignore)] public EulerAnglesData EulerAngles { get; set; }
        [JsonProperty("children", NullValueHandling = NullValueHandling.Ignore)]
        public List<SceneObjectData> Children { get; set; }
    }

    public class PositionData
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
    }

    public class EulerAnglesData
    {
        [JsonProperty("rx")] public double RX { get; set; }
        [JsonProperty("ry")] public double RY { get; set; }
        [JsonProperty("rz")] public double RZ { get; set; }
    }

    // Set RAPID Variable
    public class SetRapidVariableRequest
    {
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("variableName")] public string VariableName { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
    }

    public class SetRapidVariableResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("taskName")] public string TaskName { get; set; }
        [JsonProperty("moduleName")] public string ModuleName { get; set; }
        [JsonProperty("variableName")] public string VariableName { get; set; }
        [JsonProperty("previousValue")] public string PreviousValue { get; set; }
        [JsonProperty("newValue")] public string NewValue { get; set; }
        [JsonProperty("dataType")] public string DataType { get; set; }
    }

    // Set IO Signal
    public class SetIOSignalRequest
    {
        [JsonProperty("signalName")] public string SignalName { get; set; }
        [JsonProperty("value")] public double Value { get; set; }
    }

    public class SetIOSignalResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("signalName")] public string SignalName { get; set; }
        [JsonProperty("signalType")] public string SignalType { get; set; }
        [JsonProperty("previousValue")] public string PreviousValue { get; set; }
        [JsonProperty("newValue")] public string NewValue { get; set; }
        [JsonProperty("logicalState", NullValueHandling = NullValueHandling.Ignore)] public string LogicalState { get; set; }
    }

    // Screenshot
    public class ScreenshotRequest
    {
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
    }

    public class ScreenshotResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("imageBase64")] public string ImageBase64 { get; set; }
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("mimeType")] public string MimeType { get; set; }
        [JsonProperty("timestamp")] public string Timestamp { get; set; }
    }

    #endregion
}
