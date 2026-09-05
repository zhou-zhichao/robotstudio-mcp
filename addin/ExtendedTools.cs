using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.MotionDomain;
using ABB.Robotics.Controllers.RapidDomain;
using ABB.Robotics.RobotStudio;
using ABB.Robotics.RobotStudio.Stations;
using ABB.Robotics.RobotStudio.Stations.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobotStudioMcpAddin
{
    public partial class Addin
    {
        private static readonly object ExtendedLock = new object();
        private static readonly Dictionary<string, JObject> ExtendedCatalog = ReadExtendedCatalog();
        private static readonly HashSet<string> ExtendedPaths = new HashSet<string>(ExtendedCatalog.Keys);

        private static Dictionary<string, JObject> ReadExtendedCatalog()
        {
            using (var stream = typeof(Addin).Assembly.GetManifestResourceStream("RobotStudioMcpAddin.ExtendedTools.json"))
            using (var reader = new StreamReader(stream))
                return JArray.Parse(reader.ReadToEnd()).Cast<JObject>().ToDictionary(t => (string)t["path"]);
        }

        private static void ValidateExtendedInput(string path, JObject input)
        {
            var schema = ExtendedCatalog[path]["inputSchema"];
            foreach (string name in schema["required"].Values<string>())
                if (input[name] == null) throw new ArgumentException("Missing parameter: " + name);
            foreach (var property in input.Properties())
            {
                var rule = schema["properties"][property.Name];
                if (rule == null) throw new ArgumentException("Unknown parameter: " + property.Name);
                string type = (string)rule["type"];
                if (type == "string") RequiredText(input, property.Name);
                if (type == "number" || type == "integer")
                {
                    double value = Number(input, property.Name, rule["minimum"] == null ? double.MinValue : (double)rule["minimum"],
                        rule["maximum"] == null ? double.MaxValue : (double)rule["maximum"]);
                    if (type == "integer" && value != Math.Truncate(value)) throw new ArgumentException(property.Name + " must be an integer.");
                }
                if (rule["enum"] != null && !rule["enum"].Any(v => JToken.DeepEquals(v, property.Value)))
                    throw new ArgumentException("Invalid value for " + property.Name);
            }
        }

        private static string HandleExtended(string path, string body, out int statusCode)
        {
            try
            {
                var input = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                ValidateExtendedInput(path, input);
                object result;
                lock (ExtendedLock)
                {
                    if (path == "/rapid/validate")
                    {
                        var issues = RapidPrecheck.Check(RequiredText(input, "code"));
                        result = new { success = true, valid = issues.Count == 0, issues,
                            scope = "Structural precheck only; not compilation, reachability or collision validation." };
                    }
                    else if (path.StartsWith("/paths/") || path == "/targets/create" || path == "/station/save" || path == "/speed/simulation")
                        result = OnStationThread(() => StationOperation(path, input));
                    else result = ControllerOperation(path, input);
                }
                statusCode = 200;
                return JsonConvert.SerializeObject(result);
            }
            catch (JsonException ex) { statusCode = 400; return ExtendedError(ex); }
            catch (ArgumentException ex) { statusCode = 400; return ExtendedError(ex); }
            catch (InvalidOperationException ex) { statusCode = 409; return ExtendedError(ex); }
            catch (Exception ex) { statusCode = 500; return ExtendedError(ex); }
        }

        private static string ExtendedError(Exception ex)
        {
            return JsonConvert.SerializeObject(new { success = false, error = ex.GetType().Name, message = ex.Message });
        }

        private static string RequiredText(JObject input, string key)
        {
            var value = input[key];
            if (value == null || value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value))
                throw new ArgumentException(key + " must be a nonempty string.");
            return (string)value;
        }

        private static string Identifier(JObject input, string key, string fallback = null)
        {
            string value = input[key] == null ? fallback : RequiredText(input, key);
            if (value == null || !Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z_0-9]{0,31}$"))
                throw new ArgumentException(key + " must be a RAPID identifier (1-32 characters).");
            return value;
        }

        private static double Number(JObject input, string key, double minimum, double maximum, double? fallback = null)
        {
            if (input[key] == null && fallback.HasValue) return fallback.Value;
            var token = input[key];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw new ArgumentException(key + " must be a number.");
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentException(key + " is outside the supported range.");
            return value;
        }

        private static Station RequireStation()
        {
            var station = Project.ActiveProject as Station;
            if (station == null) throw new InvalidOperationException("Open a RobotStudio station first.");
            return station;
        }

        private static object OnStationThread(Func<object> action)
        {
            var graphic = GraphicControl.ActiveGraphicControl;
            if (graphic == null) throw new InvalidOperationException("An active 3D view is required for station operations.");
            if (!graphic.InvokeRequired) return action();
            var completion = new TaskCompletionSource<object>();
            graphic.BeginInvoke(new Action(() => {
                try { completion.SetResult(action()); }
                catch (Exception ex) { completion.SetException(ex); }
            }));
            // A timeout does not cancel a queued SDK mutation. Never retry automatically.
            if (!((IAsyncResult)completion.Task).AsyncWaitHandle.WaitOne(60000))
                throw new TimeoutException("Station operation timed out; outcome unknown. Inspect station state before retrying.");
            return completion.Task.GetAwaiter().GetResult();
        }

        private static object WithUndo(string label, Func<object> action)
        {
            Project.UndoContext.BeginUndoStep(label);
            try { return action(); }
            catch { Project.UndoContext.CancelUndoStep(CancelUndoStepType.Rollback); throw; }
            finally { Project.UndoContext.EndUndoStep(); }
        }

        private static RsTask StationTask(Station station, JObject input)
        {
            string name = Identifier(input, "taskName", station.ActiveTask == null ? "T_ROB1" : station.ActiveTask.Name);
            var matches = Walk(station).OfType<RsTask>().Where(t => t.Name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Select a station with exactly one matching task: " + name);
            return matches[0];
        }

        private static IEnumerable<ProjectObject> Walk(ProjectObject root)
        {
            foreach (var child in root.Children)
            {
                yield return child;
                foreach (var nested in Walk(child)) yield return nested;
            }
        }

        private static object StationOperation(string path, JObject input)
        {
            var station = RequireStation();
            if (path == "/station/save")
            {
                if (station.FileInfo == null || string.IsNullOrWhiteSpace(station.FileInfo.FullName)) throw new InvalidOperationException("Save the station once in RobotStudio before using save_station.");
                station.Save();
                return new { success = true, path = station.FileInfo.FullName };
            }
            if (path == "/speed/simulation")
            {
                double multiplier = Number(input, "multiplier", 0.1, 10);
                Simulator.SimulationSpeed = multiplier;
                return new { success = true, simulationMultiplier = Simulator.SimulationSpeed };
            }
            var task = StationTask(station, input);
            if (path == "/paths/list")
                return new { success = true, taskName = task.Name, paths = task.PathProcedures.Select(p => new { name = p.Name, moduleName = p.ModuleName, instructionCount = p.Instructions.Count }).ToArray() };
            if (path == "/targets/create") return WithUndo("MCP create target", () => CreateStationTarget(task, input));
            string pathName = Identifier(input, "pathName");
            var paths = task.PathProcedures.Where(p => p.Name == pathName).ToArray();
            if (path == "/paths/create")
            {
                if (paths.Length != 0) throw new ArgumentException("Path already exists: " + pathName);
                string moduleName = Identifier(input, "moduleName", "McpPaths");
                return WithUndo("MCP create path", () => {
                    var created = new RsPathProcedure(pathName) { ModuleName = moduleName, ShowName = true, Visible = true };
                    task.PathProcedures.Add(created);
                    return new { success = true, pathName, taskName = task.Name, synchronized = false };
                });
            }
            if (paths.Length != 1) throw new ArgumentException("Path not found or ambiguous: " + pathName);
            var selected = paths[0];
            if (path == "/paths/targets")
            {
                var items = new List<object>();
                foreach (RsInstruction instruction in selected.Instructions)
                {
                    var move = instruction as RsMoveInstruction;
                    items.Add(new { instruction = instruction.Name,
                        workObject = move == null || move.GetWorkObject() == null ? null : move.GetWorkObject().Name,
                        targets = move == null ? new object[0] : move.GetAllRobTargets().Select(TargetInfo).ToArray() });
                }
                return new { success = true, taskName = task.Name, pathName, instructions = items };
            }
            return WithUndo("MCP append path instruction", () => AppendStationTarget(task, selected, input));
        }

        private static object TargetInfo(RsRobTarget target)
        {
            return new { name = target.Name, xMm = target.Frame.X * 1000, yMm = target.Frame.Y * 1000, zMm = target.Frame.Z * 1000,
                rxDeg = target.Frame.RX * 180 / Math.PI, ryDeg = target.Frame.RY * 180 / Math.PI, rzDeg = target.Frame.RZ * 180 / Math.PI,
                configurationStatus = target.ConfigurationStatus.ToString() };
        }

        private static RsWorkObject WorkObject(RsTask task, JObject input)
        {
            string name = Identifier(input, "workObject", task.ActiveWorkObject == null ? "wobj0" : task.ActiveWorkObject.Name);
            var matches = task.DataDeclarations.OfType<RsWorkObject>().Where(w => w.Name == name).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Work object not found or ambiguous: " + name);
            return matches[0];
        }

        private static RsRobTarget FindTarget(RsTask task, string name)
        {
            var matches = task.DataDeclarations.OfType<RsRobTarget>().Where(t => t.Name == name).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Target not found or ambiguous: " + name);
            return matches[0];
        }

        private static object CreateStationTarget(RsTask task, JObject input)
        {
            string name = Identifier(input, "targetName");
            if (task.DataDeclarations.Any(d => d.Name == name)) throw new ArgumentException("Declaration already exists: " + name);
            var wobj = WorkObject(task, input);
            var target = new RsRobTarget { Name = name };
            target.Frame.X = Number(input, "xMm", -100000, 100000) / 1000;
            target.Frame.Y = Number(input, "yMm", -100000, 100000) / 1000;
            target.Frame.Z = Number(input, "zMm", -100000, 100000) / 1000;
            target.Frame.RX = Number(input, "rxDeg", -360, 360, 0) * Math.PI / 180;
            target.Frame.RY = Number(input, "ryDeg", -360, 360, 0) * Math.PI / 180;
            target.Frame.RZ = Number(input, "rzDeg", -360, 360, 0) * Math.PI / 180;
            task.DataDeclarations.Add(target);
            task.Targets.Add(new RsTarget(wobj, target));
            return new { success = true, target = TargetInfo(target), workObject = wobj.Name, synchronized = false, reachabilityChecked = false };
        }

        private static object AppendStationTarget(RsTask task, RsPathProcedure path, JObject input)
        {
            var target = FindTarget(task, Identifier(input, "targetName"));
            var wobj = WorkObject(task, input);
            string tool = Identifier(input, "toolName", task.ActiveTool == null ? "tool0" : task.ActiveTool.Name);
            if (!task.DataDeclarations.OfType<RsToolData>().Any(t => t.Name == tool)) throw new ArgumentException("Tool not found: " + tool);
            string motion = input["motion"] == null ? "linear" : RequiredText(input, "motion");
            RsMoveInstruction instruction;
            if (motion == "circular")
            {
                var via = FindTarget(task, Identifier(input, "viaTargetName"));
                if (via == target) throw new ArgumentException("Circular via and end targets must differ.");
                instruction = new RsMoveInstruction(task, "Move", "Default", wobj.Name, via.Name, target.Name, tool);
            }
            else
            {
                if (motion != "linear" && motion != "joint") throw new ArgumentException("Unknown motion type.");
                if (input["viaTargetName"] != null) throw new ArgumentException("viaTargetName is only valid for circular motion.");
                instruction = new RsMoveInstruction(task, "Move", "Default", motion == "linear" ? MotionType.Linear : MotionType.Joint, wobj.Name, target.Name, tool);
            }
            path.Instructions.Add(instruction);
            return new { success = true, pathName = path.Name, instructionCount = path.Instructions.Count, synchronized = false, reachabilityChecked = false };
        }

        private static object ControllerOperation(string path, JObject input)
        {
            var station = RequireStation();
            if (station.Irc5Controllers.Count != 1) throw new InvalidOperationException("These tools require exactly one virtual controller in the station.");
            using (var controller = TryGetController(station))
            {
                if (controller == null || !controller.IsVirtual) throw new InvalidOperationException("A connected virtual controller is required.");
                controller.Logon(UserInfo.DefaultUser);
                if (path == "/robot/pose")
                {
                    string frame = input["frame"] == null ? "world" : RequiredText(input, "frame");
                    CoordinateSystemType coordinates;
                    switch (frame) {
                        case "world": coordinates = CoordinateSystemType.World; break;
                        case "base": coordinates = CoordinateSystemType.Base; break;
                        case "tool": coordinates = CoordinateSystemType.Tool; break;
                        case "workobject": coordinates = CoordinateSystemType.WorkObject; break;
                        default: throw new ArgumentException("Unknown coordinate frame.");
                    }
                    var units = controller.MotionSystem.MechanicalUnits;
                    string unitName = input["mechanicalUnit"] == null ? null : RequiredText(input, "mechanicalUnit");
                    var matches = units.Cast<MechanicalUnit>().Where(u => unitName == null || u.Name == unitName).ToArray();
                    if (matches.Length != 1) throw new ArgumentException("Specify mechanicalUnit when multiple units are present.");
                    var unit = matches[0];
                    var pose = unit.GetPosition(coordinates);
                    return new { success = true, frame, units = "mm", mechanicalUnit = unit.Name, timestamp = DateTime.UtcNow.ToString("o"),
                        position = new { x = pose.Trans.X, y = pose.Trans.Y, z = pose.Trans.Z },
                        orientation = new { q1 = pose.Rot.Q1, q2 = pose.Rot.Q2, q3 = pose.Rot.Q3, q4 = pose.Rot.Q4 },
                        tool = unit.Tool.Name, workObject = unit.WorkObject.Name };
                }
                if (path == "/speed/get") return new { success = true, simulationMultiplier = Simulator.SimulationSpeed, speedOverridePercent = controller.MotionSystem.SpeedRatio };
                if (path == "/speed/override")
                {
                    double percent = Number(input, "percent", 0, 100);
                    if (percent != Math.Truncate(percent)) throw new ArgumentException("percent must be an integer.");
                    using (Mastership.Request(controller)) controller.MotionSystem.SpeedRatio = (int)percent;
                    return new { success = true, speedOverridePercent = controller.MotionSystem.SpeedRatio };
                }
                if (path.StartsWith("/controller/")) return ReadControllerStorage(station, controller, path, input);
                var task = controller.Rapid.GetTask(Identifier(input, "taskName", "T_ROB1"));
                if (task == null) throw new ArgumentException("RAPID task not found.");
                if (path == "/rapid/check")
                {
                    var reasons = new List<string>();
                    if (controller.Rapid.ExecutionStatus != ExecutionStatus.Stopped) reasons.Add("Controller is not stopped.");
                    if (!task.Enabled) reasons.Add("Task is disabled.");
                    if (controller.OperatingMode != ControllerOperatingMode.Auto) reasons.Add("Controller is not in automatic mode.");
                    if (controller.State != ControllerState.MotorsOn) reasons.Add("Motors are not on.");
                    bool checkedProgram = controller.Rapid.ExecutionStatus == ExecutionStatus.Stopped;
                    var errors = checkedProgram ? task.CheckProgram().Errors.Select(e => (object)new { taskName = e.TaskName, moduleName = e.ModuleName, line = e.Line, column = e.Column, detail = e.ToString() }).ToArray() : new object[0];
                    if (errors.Length > 0) reasons.Add("Current RAPID program has compiler errors.");
                    return new { success = true, ready = reasons.Count == 0, reasons, compilerChecked = checkedProgram, errors,
                        controllerState = controller.State.ToString(), taskName = task.Name,
                        scope = "Controller prerequisites and current program only. No collision, reachability or physical safety assessment." };
                }
                string controllerId = station.Irc5Controllers[0].SystemId;
                string root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RobotStudioMcp", "backups", new Guid(controllerId).ToString("N"), task.Name);
                if (path == "/program/backups")
                {
                    var backups = Directory.Exists(root) ? Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "manifest.json")))
                        .Select(d => JObject.Parse(File.ReadAllText(Path.Combine(d, "manifest.json")))).ToArray() : new JObject[0];
                    return new { success = true, taskName = task.Name, backups };
                }
                if (controller.Rapid.ExecutionStatus != ExecutionStatus.Stopped) throw new InvalidOperationException("Stop RAPID execution before saving or restoring a program.");
                using (Mastership.Request(controller.Rapid))
                {
                    if (path == "/program/save") return new { success = true, backup = SaveProgram(task, root, controllerId) };
                    string id = RequiredText(input, "backupId");
                    if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("Invalid backupId.");
                    string directory = SafeChild(root, id);
                    var manifest = JObject.Parse(File.ReadAllText(SafeChild(directory, "manifest.json")));
                    if ((string)manifest["controllerId"] != controllerId || (string)manifest["taskName"] != task.Name)
                        throw new ArgumentException("Backup does not belong to this controller/task.");
                    string program = SafeChild(directory, (string)manifest["programFile"]);
                    var recovery = SaveProgram(task, root, controllerId);
                    try
                    {
                        if (!task.LoadProgramFromFile(program, RapidLoadMode.Replace, 30000))
                            throw new InvalidOperationException("Controller reported program load errors.");
                        return new { success = true, restoredBackupId = id, recoveryBackup = recovery, executionStarted = false };
                    }
                    catch (Exception ex)
                    {
                        return new { success = false, error = "Restore failed; no automatic rollback was attempted.", message = ex.Message + " Recovery backup: " + (string)recovery["backupId"] + " at " + (string)recovery["directory"], recoveryBackup = recovery };
                    }
                }
            }
        }

        private static JObject SaveProgram(ABB.Robotics.Controllers.RapidDomain.Task task, string root, string controllerId)
        {
            string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);
            task.SaveProgramToFile(directory);
            var programs = Directory.GetFiles(directory, "*.pgf", SearchOption.AllDirectories);
            if (programs.Length != 1) throw new IOException("Backup incomplete: expected one .pgf file in " + directory);
            var manifest = new JObject { ["backupId"] = id, ["controllerId"] = controllerId, ["taskName"] = task.Name,
                ["createdAt"] = DateTime.UtcNow.ToString("o"), ["directory"] = directory,
                ["programFile"] = programs[0].Substring(directory.Length + 1) };
            File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest.ToString());
            return manifest;
        }

        // Only read beneath a known controller root. Reject Windows ADS, rooted paths and junctions.
        internal static string SafeChild(string root, string relative)
        {
            if (relative == null || Path.IsPathRooted(relative) || relative.Contains(":")) throw new ArgumentException("A relative controller path is required.");
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = relative.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string result = normalizedRoot;
            if ((File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked roots are not supported.");
            foreach (string part in parts)
            {
                if (part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith(".") || part.EndsWith(" "))
                    throw new ArgumentException("Invalid controller path segment.");
                result = Path.Combine(result, part);
                if ((File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked paths are not supported.");
            }
            return result;
        }

        private static object ReadControllerStorage(Station station, Controller controller, string path, JObject input)
        {
            string root = controller.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new InvalidOperationException("Virtual controller HOME is unavailable.");
            string file;
            if (path == "/controller/config")
            {
                string name = RequiredText(input, "fileName");
                if (!new[] { "SYS.cfg", "EIO.cfg", "SIO.cfg", "MOC.cfg" }.Contains(name)) throw new ArgumentException("Unsupported configuration file.");
                string system = station.Irc5Controllers[0].SystemPath;
                file = SafeChild(system, "SYSPAR/" + name);
            }
            else file = SafeChild(root, input["relativePath"] == null ? "" : RequiredText(input, "relativePath"));
            if (path == "/controller/files")
            {
                var entries = Directory.EnumerateFileSystemEntries(file).Take(501).ToArray();
                return new { success = true, truncated = entries.Length > 500, entries = entries.Take(500).Select(p => new {
                    name = Path.GetFileName(p), directory = Directory.Exists(p), linked = (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0
                }).ToArray() };
            }
            var extensions = new[] { ".mod", ".sys", ".pgf", ".cfg", ".txt", ".log", ".json", ".xml" };
            if (!extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) throw new ArgumentException("Only controller text files are supported.");
            if (new FileInfo(file).Length > 1048576) throw new ArgumentException("File exceeds the 1 MiB limit.");
            return new { success = true, name = Path.GetFileName(file), content = File.ReadAllText(file) };
        }
    }
}
