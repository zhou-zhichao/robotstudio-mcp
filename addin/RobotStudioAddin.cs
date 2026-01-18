using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABB.Robotics.RobotStudio;
using ABB.Robotics.RobotStudio.Stations;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.RapidDomain;
using ABB.Robotics.Controllers.MotionDomain;
using Newtonsoft.Json;

namespace RobotStudioMcpAddin
{
    /// <summary>
    /// RobotStudio Add-in that exposes a local HTTP REST API for MCP integration.
    /// Provides endpoints to read joint positions and control simulation.
    /// </summary>
    public class Addin
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly object _lock = new object();
        private const int Port = 8080;
        private const string ListenerPrefix = "http://localhost:8080/";

        /// <summary>
        /// Add-in entry point called by RobotStudio when the add-in is loaded.
        /// </summary>
        public static void AddinMain()
        {
            Logger.AddMessage(new LogMessage("RobotStudio MCP Add-in initializing..."));

            // Start the HTTP server on a background thread
            _cts = new CancellationTokenSource();
            Task.Run(() => StartHttpServer(_cts.Token));

            // Register for station events
            Project.UndoContext.StateChanged += OnProjectStateChanged;

            Logger.AddMessage(new LogMessage($"RobotStudio MCP Add-in started. Listening on port {Port}"));
        }

        /// <summary>
        /// Called when the add-in is unloaded.
        /// </summary>
        public static void AddinUnload()
        {
            Logger.AddMessage(new LogMessage("RobotStudio MCP Add-in shutting down..."));

            _cts?.Cancel();

            lock (_lock)
            {
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                    _listener = null;
                }
            }

            Logger.AddMessage(new LogMessage("RobotStudio MCP Add-in stopped."));
        }

        private static void OnProjectStateChanged(object sender, EventArgs e)
        {
            // Handle project state changes if needed
        }

        /// <summary>
        /// Starts the HTTP listener server.
        /// </summary>
        private static void StartHttpServer(CancellationToken cancellationToken)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(ListenerPrefix);
                _listener.Start();

                Logger.AddMessage(new LogMessage($"HTTP server listening on {ListenerPrefix}"));

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Use async pattern with timeout to allow cancellation
                        var result = _listener.BeginGetContext(null, null);
                        var waitHandles = new[] { result.AsyncWaitHandle, cancellationToken.WaitHandle };
                        int index = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(1));

                        if (index == 1 || cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (index == 0)
                        {
                            var context = _listener.EndGetContext(result);
                            Task.Run(() => HandleRequest(context));
                        }
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                    {
                        // Listener was stopped
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage($"HTTP server error: {ex.Message}", LogMessageSeverity.Error));
            }
        }

        /// <summary>
        /// Handles incoming HTTP requests and routes them to appropriate handlers.
        /// </summary>
        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Add CORS headers for local development
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                // Handle CORS preflight
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                string path = request.Url.AbsolutePath.ToLowerInvariant();
                string method = request.HttpMethod.ToUpperInvariant();

                switch (path)
                {
                    case "/joints":
                        if (method == "GET")
                        {
                            HandleGetJoints(context);
                        }
                        else
                        {
                            SendMethodNotAllowed(response, "GET");
                        }
                        break;

                    case "/simulation":
                        if (method == "POST")
                        {
                            HandleSimulationControl(context);
                        }
                        else
                        {
                            SendMethodNotAllowed(response, "POST");
                        }
                        break;

                    case "/status":
                        if (method == "GET")
                        {
                            HandleGetStatus(context);
                        }
                        else
                        {
                            SendMethodNotAllowed(response, "GET");
                        }
                        break;

                    case "/health":
                        HandleHealthCheck(context);
                        break;

                    default:
                        SendNotFound(response, path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage($"Request handling error: {ex.Message}", LogMessageSeverity.Warning));
                SendError(context.Response, 500, "Internal Server Error", ex.Message);
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// Handles GET /joints - Returns current joint positions from the active controller.
        /// </summary>
        private static void HandleGetJoints(HttpListenerContext context)
        {
            try
            {
                var station = Project.ActiveProject as Station;
                if (station == null)
                {
                    SendError(context.Response, 404, "No Active Station", "No station is currently open in RobotStudio.");
                    return;
                }

                // Find the virtual controller
                Controller controller = null;
                foreach (var vc in station.VirtualControllers)
                {
                    try
                    {
                        controller = Controller.GetController(vc.SystemId);
                        if (controller != null)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (controller == null)
                {
                    SendError(context.Response, 404, "No Controller", "No virtual controller found in the station.");
                    return;
                }

                // Get joint positions from the mechanical unit
                JointData jointData = GetJointPositions(controller);

                var responseObj = new JointResponse
                {
                    Success = true,
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Joints = jointData
                };

                SendJsonResponse(context.Response, 200, responseObj);
            }
            catch (Exception ex)
            {
                SendError(context.Response, 500, "Joint Read Error", ex.Message);
            }
        }

        /// <summary>
        /// Gets joint positions from the controller's mechanical unit.
        /// </summary>
        private static JointData GetJointPositions(Controller controller)
        {
            var jointData = new JointData();

            try
            {
                using (Mastership.Request(controller))
                {
                    var motionSystem = controller.MotionSystem;
                    if (motionSystem != null && motionSystem.MechanicalUnits.Count > 0)
                    {
                        var mechUnit = motionSystem.MechanicalUnits[0];
                        var jointTarget = mechUnit.GetPosition();

                        if (jointTarget != null)
                        {
                            var robAx = jointTarget.RobAx;

                            // Values are already in degrees from the RobotStudio API
                            jointData.J1 = Math.Round(robAx.Rax_1, 3);
                            jointData.J2 = Math.Round(robAx.Rax_2, 3);
                            jointData.J3 = Math.Round(robAx.Rax_3, 3);
                            jointData.J4 = Math.Round(robAx.Rax_4, 3);
                            jointData.J5 = Math.Round(robAx.Rax_5, 3);
                            jointData.J6 = Math.Round(robAx.Rax_6, 3);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.AddMessage(new LogMessage($"Error reading joints: {ex.Message}", LogMessageSeverity.Warning));
                throw;
            }

            return jointData;
        }

        /// <summary>
        /// Handles POST /simulation - Controls simulation start/stop.
        /// </summary>
        private static void HandleSimulationControl(HttpListenerContext context)
        {
            try
            {
                // Read request body
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }

                var request = JsonConvert.DeserializeObject<SimulationRequest>(body);

                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    SendError(context.Response, 400, "Invalid Request", "Request body must contain 'action' field with value 'start' or 'stop'.");
                    return;
                }

                string action = request.Action.ToLowerInvariant();
                bool success = false;
                string message = "";

                // Execute on UI thread since Simulator requires it
                Station station = Project.ActiveProject as Station;
                if (station == null)
                {
                    SendError(context.Response, 404, "No Active Station", "No station is currently open in RobotStudio.");
                    return;
                }

                switch (action)
                {
                    case "start":
                        if (!Simulator.IsRunning)
                        {
                            Simulator.Start();
                            success = true;
                            message = "Simulation started.";
                        }
                        else
                        {
                            success = true;
                            message = "Simulation is already running.";
                        }
                        break;

                    case "stop":
                        if (Simulator.IsRunning)
                        {
                            Simulator.Stop();
                            success = true;
                            message = "Simulation stopped.";
                        }
                        else
                        {
                            success = true;
                            message = "Simulation is not running.";
                        }
                        break;

                    default:
                        SendError(context.Response, 400, "Invalid Action", $"Unknown action '{action}'. Use 'start' or 'stop'.");
                        return;
                }

                var responseObj = new SimulationResponse
                {
                    Success = success,
                    Message = message,
                    IsRunning = Simulator.IsRunning
                };

                SendJsonResponse(context.Response, 200, responseObj);
            }
            catch (Exception ex)
            {
                SendError(context.Response, 500, "Simulation Control Error", ex.Message);
            }
        }

        /// <summary>
        /// Handles GET /status - Returns current station and simulation status.
        /// </summary>
        private static void HandleGetStatus(HttpListenerContext context)
        {
            try
            {
                var station = Project.ActiveProject as Station;

                var status = new StatusResponse
                {
                    HasActiveStation = station != null,
                    StationName = station?.Name ?? "",
                    IsSimulationRunning = Simulator.IsRunning,
                    VirtualControllerCount = station?.VirtualControllers?.Count ?? 0
                };

                SendJsonResponse(context.Response, 200, status);
            }
            catch (Exception ex)
            {
                SendError(context.Response, 500, "Status Error", ex.Message);
            }
        }

        /// <summary>
        /// Handles /health - Simple health check endpoint.
        /// </summary>
        private static void HandleHealthCheck(HttpListenerContext context)
        {
            var health = new { status = "ok", timestamp = DateTime.UtcNow.ToString("o") };
            SendJsonResponse(context.Response, 200, health);
        }

        #region Response Helpers

        private static void SendJsonResponse(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void SendError(HttpListenerResponse response, int statusCode, string error, string message)
        {
            var errorObj = new ErrorResponse
            {
                Success = false,
                Error = error,
                Message = message
            };
            SendJsonResponse(response, statusCode, errorObj);
        }

        private static void SendMethodNotAllowed(HttpListenerResponse response, string allowedMethod)
        {
            response.Headers.Add("Allow", allowedMethod);
            SendError(response, 405, "Method Not Allowed", $"This endpoint only supports {allowedMethod} requests.");
        }

        private static void SendNotFound(HttpListenerResponse response, string path)
        {
            SendError(response, 404, "Not Found", $"Endpoint '{path}' not found. Available: /joints, /simulation, /status, /health");
        }

        #endregion
    }

    #region Data Transfer Objects

    public class JointData
    {
        [JsonProperty("j1")]
        public double J1 { get; set; }

        [JsonProperty("j2")]
        public double J2 { get; set; }

        [JsonProperty("j3")]
        public double J3 { get; set; }

        [JsonProperty("j4")]
        public double J4 { get; set; }

        [JsonProperty("j5")]
        public double J5 { get; set; }

        [JsonProperty("j6")]
        public double J6 { get; set; }
    }

    public class JointResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("joints")]
        public JointData Joints { get; set; }
    }

    public class SimulationRequest
    {
        [JsonProperty("action")]
        public string Action { get; set; }
    }

    public class SimulationResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("isRunning")]
        public bool IsRunning { get; set; }
    }

    public class StatusResponse
    {
        [JsonProperty("hasActiveStation")]
        public bool HasActiveStation { get; set; }

        [JsonProperty("stationName")]
        public string StationName { get; set; }

        [JsonProperty("isSimulationRunning")]
        public bool IsSimulationRunning { get; set; }

        [JsonProperty("virtualControllerCount")]
        public int VirtualControllerCount { get; set; }
    }

    public class ErrorResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    #endregion
}
