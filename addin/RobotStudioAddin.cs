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
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using (client)
                using (var stream = client.GetStream())
                {
                    // Read HTTP request
                    var requestLine = "";
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var headerBuilder = new StringBuilder();
                    var buffer = new byte[8192];
                    var received = new StringBuilder();

                    // Read until we find end of headers (\r\n\r\n)
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

                    // Read remaining body if Content-Length is specified
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

                    // Parse request line: "GET /path HTTP/1.1"
                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2)
                    {
                        SendResponse(stream, 400, "Bad Request");
                        return;
                    }

                    var method = parts[0].ToUpperInvariant();
                    var path = parts[1].ToLowerInvariant();

                    // Remove query string
                    var qIdx = path.IndexOf('?');
                    if (qIdx >= 0) path = path.Substring(0, qIdx);

                    // Handle CORS preflight
                    if (method == "OPTIONS")
                    {
                        SendResponse(stream, 200, "");
                        return;
                    }

                    // Route request
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
                        default:
                            statusCode = 404;
                            responseJson = JsonConvert.SerializeObject(new ErrorResponse
                            {
                                Success = false,
                                Error = "Not Found",
                                Message = "Endpoint '" + path + "' not found. Available: /joints, /simulation, /status, /health"
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
            var statusText = statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : statusCode == 400 ? "Bad Request" : statusCode == 405 ? "Method Not Allowed" : "Error";
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

    #endregion
}
