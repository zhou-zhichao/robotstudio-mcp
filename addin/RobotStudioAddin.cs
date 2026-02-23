using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABB.Robotics.RobotStudio;
using ABB.Robotics.RobotStudio.Stations;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.MotionDomain;
using ABB.Robotics.Controllers.RapidDomain;
using ABB.Robotics.Controllers.EventLogDomain;
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
                        case "/rapid/errors":
                            if (method != "GET") { SendResponse(stream, 405, "{\"error\":\"Method Not Allowed\"}"); return; }
                            responseJson = HandleGetErrors(out statusCode);
                            break;
                        default:
                            statusCode = 404;
                            responseJson = JsonConvert.SerializeObject(new ErrorResponse
                            {
                                Success = false,
                                Error = "Not Found",
                                Message = "Endpoint '" + path + "' not found. Available: /health, /joints, /status, /simulation, /rapid/upload, /rapid/execute, /rapid/status, /rapid/errors"
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
                    default:
                        statusCode = 400;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Invalid Action", Message = "Unknown action '" + action + "'. Use 'start' or 'stop'." });
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
                    if (controller.Rapid.ExecutionStatus == ExecutionStatus.Running)
                    {
                        statusCode = 409;
                        return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Execution Running", Message = "Cannot upload module while RAPID execution is running. Stop execution first." });
                    }

                    string tempFile = Path.Combine(Path.GetTempPath(), fileName);
                    File.WriteAllText(tempFile, request.Code, Encoding.UTF8);

                    try
                    {
                        string homePath = controller.GetEnvironmentVariable("HOME");
                        string controllerFilePath = homePath + "/" + fileName;
                        controller.FileSystem.PutFile(tempFile, controllerFilePath, true);

                        ABB.Robotics.Controllers.RapidDomain.Task rapidTask = controller.Rapid.GetTask(taskName);
                        if (rapidTask == null)
                        {
                            statusCode = 404;
                            return JsonConvert.SerializeObject(new ErrorResponse { Success = false, Error = "Task Not Found", Message = "RAPID task '" + taskName + "' not found." });
                        }

                        RapidLoadMode loadMode = replace ? RapidLoadMode.Replace : RapidLoadMode.Add;
                        bool loadSuccess;
                        using (Mastership.Request(controller.Rapid))
                        {
                            loadSuccess = rapidTask.LoadModuleFromFile(controllerFilePath, loadMode);
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

    #endregion
}
