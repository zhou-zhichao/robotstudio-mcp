#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ErrorCode,
  McpError,
} from "@modelcontextprotocol/sdk/types.js";

// Configuration
const ROBOTSTUDIO_API_BASE = "http://localhost:8080";
const REQUEST_TIMEOUT_MS = 10000;

// Types for RobotStudio API responses
interface JointData {
  j1: number;
  j2: number;
  j3: number;
  j4: number;
  j5: number;
  j6: number;
}

interface JointResponse {
  success: boolean;
  timestamp: string;
  joints: JointData;
}

interface SimulationResponse {
  success: boolean;
  message: string;
  isRunning: boolean;
}

interface StatusResponse {
  hasActiveStation: boolean;
  stationName: string;
  isSimulationRunning: boolean;
  virtualControllerCount: number;
}

interface ErrorResponse {
  success: boolean;
  error: string;
  message: string;
}

// RAPID API response types
interface RapidUploadResponse {
  success: boolean;
  message: string;
  moduleName: string;
  taskName: string;
}

interface RapidExecuteResponse {
  success: boolean;
  message: string;
  executionStatus: string;
}

interface TaskStatusData {
  name: string;
  executionStatus: string;
  enabled: boolean;
  type: string;
  programPointer?: {
    module: string;
    routine: string;
    range: string;
  };
  motionPointer?: {
    module: string;
    routine: string;
    range: string;
  };
}

interface RapidStatusResponse {
  success: boolean;
  controllerExecutionStatus: string;
  tasks: TaskStatusData[];
}

interface RapidSourceResponse {
  success: boolean;
  taskName: string;
  moduleName: string;
  filePath: string;
  code: string;
}

interface RapidModuleInfo {
  name: string;
  isSystem: boolean;
}

interface RapidTaskModulesData {
  taskName: string;
  modules: RapidModuleInfo[];
}

interface RapidModulesListResponse {
  success: boolean;
  tasks: RapidTaskModulesData[];
}

interface EventLogMessageData {
  sequenceNumber: number;
  timestamp: string;
  title: string;
  body: string;
  categoryName: string;
  type: string;
}

interface EventLogResponse {
  success: boolean;
  messages: EventLogMessageData[];
}

interface RapidVariableResponse {
  success: boolean;
  taskName: string;
  moduleName: string;
  variableName: string;
  value: string;
  dataType: string;
}

interface IOSignalData {
  name: string;
  type: string;
  value: string;
  logicalState?: string;
}

interface IOSignalsResponse {
  success: boolean;
  signalCount: number;
  signals: IOSignalData[];
}

interface SetRapidVariableResponse {
  success: boolean;
  message: string;
  taskName: string;
  moduleName: string;
  variableName: string;
  previousValue: string;
  newValue: string;
  dataType: string;
}

interface SetIOSignalResponse {
  success: boolean;
  message: string;
  signalName: string;
  signalType: string;
  previousValue: string;
  newValue: string;
  logicalState?: string;
}

interface RapidVariableInfoData {
  moduleName: string;
  name: string;
  dataType: string;
  scope: string;
  value: string;
}

interface ListVariablesResponse {
  success: boolean;
  taskName: string;
  variableCount: number;
  variables: RapidVariableInfoData[];
}

interface PositionData {
  x: number;
  y: number;
  z: number;
}

interface EulerAnglesData {
  rx: number;
  ry: number;
  rz: number;
}

interface SceneObjectData {
  name: string;
  typeName: string;
  depth: number;
  visible: boolean;
  position?: PositionData;
  globalPosition?: PositionData;
  eulerAngles?: EulerAnglesData;
  children?: SceneObjectData[];
}

interface SceneObjectsResponse {
  success: boolean;
  stationName: string;
  objectCount: number;
  objects: SceneObjectData[];
}

interface ScreenshotResponse {
  success: boolean;
  message: string;
  imageBase64: string;
  width: number;
  height: number;
  mimeType: string;
  timestamp: string;
}

/**
 * Makes an HTTP request to the RobotStudio Add-in API.
 */
async function fetchFromRobotStudio<T>(
  endpoint: string,
  options: RequestInit = {},
  timeoutMs: number = REQUEST_TIMEOUT_MS
): Promise<T> {
  const url = `${ROBOTSTUDIO_API_BASE}${endpoint}`;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, {
      ...options,
      signal: controller.signal,
      headers: {
        "Content-Type": "application/json",
        ...options.headers,
      },
    });

    const data = await response.json();

    if (!response.ok) {
      const errorData = data as ErrorResponse;
      throw new Error(
        errorData.message || `HTTP ${response.status}: ${response.statusText}`
      );
    }

    return data as T;
  } catch (error) {
    if (error instanceof Error) {
      // Handle connection refused (RobotStudio not running)
      if (
        error.cause &&
        typeof error.cause === "object" &&
        "code" in error.cause &&
        error.cause.code === "ECONNREFUSED"
      ) {
        throw new McpError(
          ErrorCode.InternalError,
          "Cannot connect to RobotStudio. Ensure RobotStudio is running and the MCP Add-in is loaded."
        );
      }

      // Handle timeout
      if (error.name === "AbortError") {
        throw new McpError(
          ErrorCode.InternalError,
          "Request to RobotStudio timed out. The application may be unresponsive."
        );
      }

      // Handle fetch errors (connection refused, etc.)
      if (error.message.includes("fetch failed")) {
        throw new McpError(
          ErrorCode.InternalError,
          "Cannot connect to RobotStudio. Ensure RobotStudio is running and the MCP Add-in is loaded on port 8080."
        );
      }

      throw new McpError(ErrorCode.InternalError, error.message);
    }

    throw new McpError(ErrorCode.InternalError, "Unknown error occurred");
  } finally {
    clearTimeout(timeoutId);
  }
}

/**
 * Creates and configures the MCP server.
 */
function createServer(): Server {
  const server = new Server(
    {
      name: "robotstudio-mcp",
      version: "1.0.0",
    },
    {
      capabilities: {
        tools: {},
      },
    }
  );

  // Register tool listing handler
  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: [
      {
        name: "get_robot_joints",
        description:
          "Read real-time joint positions (J1-J6) from the active robot in RobotStudio Virtual Controller. Returns joint angles in degrees.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "control_simulation",
        description:
          "Start, stop, or reset the RobotStudio simulation. Use 'reset' to stop simulation AND clear all dynamically created objects (boxes, parts) from the scene.",
        inputSchema: {
          type: "object" as const,
          properties: {
            action: {
              type: "string",
              enum: ["start", "stop", "reset"],
              description:
                "The simulation control action: 'start' to begin simulation, 'stop' to end simulation, 'reset' to stop and clear all dynamic objects.",
            },
          },
          required: ["action"],
        },
      },
      {
        name: "get_station_status",
        description:
          "Get the current status of RobotStudio including whether a station is open, simulation state, and virtual controller information.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "upload_rapid_module",
        description:
          "Upload RAPID source code as a module to the virtual controller. The module is written to a .mod file and loaded into the specified RAPID task. Cannot upload while RAPID execution is running.",
        inputSchema: {
          type: "object" as const,
          properties: {
            code: {
              type: "string",
              description:
                "The RAPID source code to upload. Must be a complete MODULE block.",
            },
            moduleName: {
              type: "string",
              description:
                "Name of the module (without .mod extension). Defaults to 'McpModule'.",
            },
            taskName: {
              type: "string",
              description:
                "Name of the RAPID task to load the module into. Defaults to 'T_ROB1'.",
            },
            replaceExisting: {
              type: "boolean",
              description:
                "Whether to replace an existing module with the same name. Defaults to true.",
            },
          },
          required: ["code"],
        },
      },
      {
        name: "control_rapid_execution",
        description:
          "Control RAPID program execution: start, stop, or reset the program pointer. Use 'resetpp' before 'start' to run from the beginning.",
        inputSchema: {
          type: "object" as const,
          properties: {
            action: {
              type: "string",
              enum: ["start", "stop", "resetpp"],
              description:
                "The execution control action: 'start' to begin execution, 'stop' to halt, 'resetpp' to reset program pointer to main.",
            },
            taskName: {
              type: "string",
              description:
                "RAPID task name (only used for 'resetpp'). Defaults to 'T_ROB1'.",
            },
            executionMode: {
              type: "string",
              enum: ["continuous", "step_over", "step_in"],
              description:
                "Execution mode for 'start' action. Defaults to 'continuous'.",
            },
            cycle: {
              type: "string",
              enum: ["once", "forever"],
              description:
                "Execution cycle for 'start' action. Defaults to 'once'.",
            },
            stopMode: {
              type: "string",
              enum: ["instruction", "cycle", "immediate"],
              description:
                "Stop mode for 'stop' action. Defaults to 'instruction'.",
            },
          },
          required: ["action"],
        },
      },
      {
        name: "get_rapid_execution_status",
        description:
          "Get the current RAPID execution status including overall controller state and per-task details (execution status, program pointer position, motion pointer position).",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "get_rapid_module_source",
        description:
          "Read the current RAPID module source code from the RobotStudio virtual controller. When moduleName is omitted, the first non-system program module in the task is returned.",
        inputSchema: {
          type: "object" as const,
          properties: {
            taskName: {
              type: "string",
              description:
                "RAPID task name. Defaults to 'T_ROB1'.",
            },
            moduleName: {
              type: "string",
              description:
                "Module name to read. Defaults to the first non-system module in the task.",
            },
          },
          required: [],
        },
      },
      {
        name: "list_rapid_modules",
        description:
          "List all RAPID modules loaded in the virtual controller, grouped by task. Shows module names and whether each is a system module. Useful for discovering available modules before reading their source code with get_rapid_module_source.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "get_execution_errors",
        description:
          "Read recent entries from the controller event log including errors, warnings, and informational messages. Returns up to 50 most recent entries sorted by newest first.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "get_screenshot",
        description:
          "Capture a screenshot of the current RobotStudio 3D view. Returns an image showing the current state of the station including robot position, workpieces, and simulation state. Useful for visually verifying robot positions, paths, and scene layout.",
        inputSchema: {
          type: "object" as const,
          properties: {
            width: {
              type: "number",
              description:
                "Image width in pixels (default 1280, max 3840).",
            },
            height: {
              type: "number",
              description:
                "Image height in pixels (default 720, max 2160).",
            },
          },
          required: [],
        },
      },
      {
        name: "read_rapid_variable",
        description:
          "Read the current value of a RAPID variable from the virtual controller. Can read any VAR, PERS, or CONST variable including num, bool, string, robtarget, tooldata, wobjdata, etc. Useful for monitoring program state during or after execution.",
        inputSchema: {
          type: "object" as const,
          properties: {
            variableName: {
              type: "string",
              description:
                "Name of the RAPID variable to read (e.g. 'off_pq', 'green_count', 'TCP_VentosaTool').",
            },
            taskName: {
              type: "string",
              description:
                "RAPID task name. Defaults to 'T_ROB1'.",
            },
            moduleName: {
              type: "string",
              description:
                "Module containing the variable. Defaults to first non-system module.",
            },
          },
          required: ["variableName"],
        },
      },
      {
        name: "get_io_signals",
        description:
          "Read I/O signal values from the virtual controller. Returns digital and analog input/output signals with their current values. When signalName is provided, returns only that signal; otherwise returns all signals. Useful for checking sensor states, actuator outputs, and Smart Component signals.",
        inputSchema: {
          type: "object" as const,
          properties: {
            signalName: {
              type: "string",
              description:
                "Optional: name of a specific signal to read (e.g. 'DI_Sensor_Inf', 'DO_Ventosa'). If omitted, returns all signals.",
            },
          },
          required: [],
        },
      },
      {
        name: "get_scene_objects",
        description:
          "Read positions and orientations of objects in the RobotStudio station scene graph. Returns a tree with name, type, position (x,y,z mm, g=global/l=local), euler angles (rx,ry,rz deg), and visibility. Use nameFilter to search by name.",
        inputSchema: {
          type: "object" as const,
          properties: {
            nameFilter: {
              type: "string",
              description:
                "Optional: filter objects by name (case-insensitive substring match). E.g. 'box', 'conveyor', 'tool'.",
            },
            includeChildren: {
              type: "boolean",
              description:
                "Optional: whether to include child objects in the hierarchy (default: true). Set to false for a flat top-level list.",
            },
          },
          required: [],
        },
      },
      {
        name: "set_rapid_variable",
        description:
          "Write a value to a RAPID variable (VAR or PERS) in the virtual controller. Supports all RAPID data types including num, bool, string, robtarget, tooldata, wobjdata, etc. The value must be provided as a RAPID-formatted string (e.g. '10', 'TRUE', '\"hello\"', '[[500,0,400],[1,0,0,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]]' for robtarget). Cannot write while RAPID execution is running.",
        inputSchema: {
          type: "object" as const,
          properties: {
            variableName: {
              type: "string",
              description:
                "Name of the RAPID variable to write (e.g. 'my_num', 'target_pos', 'offset').",
            },
            value: {
              type: "string",
              description:
                "The value to set, as a RAPID-formatted string. Examples: '42' for num, 'TRUE' for bool, '\"hello\"' for string, '[[500,0,400],[1,0,0,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]]' for robtarget.",
            },
            taskName: {
              type: "string",
              description:
                "RAPID task name. Defaults to 'T_ROB1'.",
            },
            moduleName: {
              type: "string",
              description:
                "Module containing the variable. Defaults to first non-system module.",
            },
          },
          required: ["variableName", "value"],
        },
      },
      {
        name: "list_rapid_variables",
        description:
          "List all RAPID variable declarations (VAR, PERS, CONST) across modules in a task. Useful for discovering workobjects (wobjdata), robot targets (robtarget), tool data (tooldata), and other RAPID data without knowing variable names in advance. Returns variable name, data type, scope, and current value. Use typeFilter to find specific types (e.g. 'wobjdata' for work objects, 'robtarget' for targets, 'tooldata' for tools).",
        inputSchema: {
          type: "object" as const,
          properties: {
            typeFilter: {
              type: "string",
              description:
                "Optional: filter by RAPID data type (e.g. 'wobjdata', 'robtarget', 'tooldata', 'num', 'bool'). Case-insensitive.",
            },
            moduleName: {
              type: "string",
              description:
                "Optional: only list variables from this module. If omitted, scans all non-system modules.",
            },
            taskName: {
              type: "string",
              description:
                "RAPID task name. Defaults to 'T_ROB1'.",
            },
          },
          required: [],
        },
      },
      {
        name: "set_io_signal",
        description:
          "Set the value of an I/O signal in the virtual controller. For digital signals, use 1 (HIGH) or 0 (LOW). For analog signals, use the desired numeric value. Useful for simulating sensor inputs, triggering actuators, and testing Smart Component logic.",
        inputSchema: {
          type: "object" as const,
          properties: {
            signalName: {
              type: "string",
              description:
                "Name of the I/O signal to set (e.g. 'DI_Sensor_Inf', 'DO_Ventosa', 'AI_Pressure').",
            },
            value: {
              type: "number",
              description:
                "The value to set. For digital signals: 0 (LOW) or 1 (HIGH). For analog signals: any numeric value within the signal's range.",
            },
          },
          required: ["signalName", "value"],
        },
      },
    ],
  }));

  // Register tool call handler
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    switch (name) {
      case "get_robot_joints": {
        const response = await fetchFromRobotStudio<JointResponse>("/joints");

        const j = response.joints;
        const formattedOutput = `J1=${j.j1.toFixed(2)} J2=${j.j2.toFixed(2)} J3=${j.j3.toFixed(2)} J4=${j.j4.toFixed(2)} J5=${j.j5.toFixed(2)} J6=${j.j6.toFixed(2)} (deg)`;

        return {
          content: [
            {
              type: "text",
              text: formattedOutput,
            },
          ],
        };
      }

      case "control_simulation": {
        const action = (args as { action?: string })?.action;

        if (!action || !["start", "stop", "reset"].includes(action)) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Invalid action. Use 'start' or 'stop'."
          );
        }

        const response = await fetchFromRobotStudio<SimulationResponse>(
          "/simulation",
          {
            method: "POST",
            body: JSON.stringify({ action }),
          }
        );

        return {
          content: [
            {
              type: "text",
              text: `${response.message}\nSimulation running: ${response.isRunning}`,
            },
          ],
        };
      }

      case "get_station_status": {
        const response = await fetchFromRobotStudio<StatusResponse>("/status");

        if (!response.hasActiveStation) {
          return { content: [{ type: "text", text: "No station open." }] };
        }

        return {
          content: [
            {
              type: "text",
              text: `Station: ${response.stationName} | Controllers: ${response.virtualControllerCount} | Simulation: ${response.isSimulationRunning ? "running" : "stopped"}`,
            },
          ],
        };
      }

      case "upload_rapid_module": {
        const uploadArgs = args as {
          code?: string;
          moduleName?: string;
          taskName?: string;
          replaceExisting?: boolean;
        };

        if (!uploadArgs?.code) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'code'."
          );
        }

        const uploadBody = {
          code: uploadArgs.code,
          moduleName: uploadArgs.moduleName || "McpModule",
          taskName: uploadArgs.taskName || "T_ROB1",
          replaceExisting:
            uploadArgs.replaceExisting !== undefined
              ? uploadArgs.replaceExisting
              : true,
        };

        const uploadResponse = await fetchFromRobotStudio<RapidUploadResponse>(
          "/rapid/upload",
          {
            method: "POST",
            body: JSON.stringify(uploadBody),
          },
          30000 // 30s timeout for upload
        );

        return {
          content: [
            {
              type: "text",
              text: `Uploaded ${uploadResponse.moduleName} to ${uploadResponse.taskName}.`,
            },
          ],
        };
      }

      case "control_rapid_execution": {
        const execArgs = args as {
          action?: string;
          taskName?: string;
          executionMode?: string;
          cycle?: string;
          stopMode?: string;
        };

        if (
          !execArgs?.action ||
          !["start", "stop", "resetpp"].includes(execArgs.action)
        ) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Invalid action. Use 'start', 'stop', or 'resetpp'."
          );
        }

        const execBody: Record<string, string> = {
          action: execArgs.action,
        };
        if (execArgs.taskName) execBody.taskName = execArgs.taskName;
        if (execArgs.executionMode)
          execBody.executionMode = execArgs.executionMode;
        if (execArgs.cycle) execBody.cycle = execArgs.cycle;
        if (execArgs.stopMode) execBody.stopMode = execArgs.stopMode;

        const execResponse =
          await fetchFromRobotStudio<RapidExecuteResponse>(
            "/rapid/execute",
            {
              method: "POST",
              body: JSON.stringify(execBody),
            }
          );

        // After stop, poll until execution actually stops (max 5s)
        let finalStatus = execResponse.executionStatus;
        if (execArgs.action === "stop") {
          const pollStart = Date.now();
          while (
            finalStatus.toLowerCase() !== "stopped" &&
            Date.now() - pollStart < 5000
          ) {
            await new Promise((r) => setTimeout(r, 500));
            try {
              const statusCheck =
                await fetchFromRobotStudio<RapidStatusResponse>(
                  "/rapid/status"
                );
              finalStatus = statusCheck.controllerExecutionStatus;
            } catch {
              break;
            }
          }
        }

        return {
          content: [
            {
              type: "text",
              text: `${execResponse.message}\nExecution status: ${finalStatus}`,
            },
          ],
        };
      }

      case "get_rapid_execution_status": {
        const statusResponse =
          await fetchFromRobotStudio<RapidStatusResponse>("/rapid/status");

        const lines = [`Controller: ${statusResponse.controllerExecutionStatus}`];

        for (const task of statusResponse.tasks) {
          let taskLine = `${task.name}: ${task.executionStatus}`;
          if (task.programPointer) {
            taskLine += ` PP=${task.programPointer.module}/${task.programPointer.routine}[${task.programPointer.range}]`;
          }
          if (task.motionPointer) {
            taskLine += ` MP=${task.motionPointer.module}/${task.motionPointer.routine}[${task.motionPointer.range}]`;
          }
          lines.push(taskLine);
        }

        return {
          content: [{ type: "text", text: lines.join("\n") }],
        };
      }

      case "get_rapid_module_source": {
        const sourceArgs = args as {
          taskName?: string;
          moduleName?: string;
        };

        const sourceResponse = await fetchFromRobotStudio<RapidSourceResponse>(
          "/rapid/source",
          {
            method: "POST",
            body: JSON.stringify({
              taskName: sourceArgs?.taskName || "T_ROB1",
              moduleName: sourceArgs?.moduleName,
            }),
          },
          30000
        );

        return {
          content: [
            {
              type: "text",
              text: `--- ${sourceResponse.moduleName} (${sourceResponse.taskName}) ---\n${sourceResponse.code}`,
            },
          ],
        };
      }

      case "list_rapid_modules": {
        const modulesResponse =
          await fetchFromRobotStudio<RapidModulesListResponse>(
            "/rapid/modules"
          );

        const lines: string[] = [];
        for (const task of modulesResponse.tasks) {
          const mods = task.modules
            .map((m) => m.name + (m.isSystem ? "*" : ""))
            .join(", ");
          lines.push(`${task.taskName}: ${mods}`);
        }

        return {
          content: [{ type: "text", text: lines.join("\n") + "\n(* = system)" }],
        };
      }

      case "get_execution_errors": {
        const errorLogResponse =
          await fetchFromRobotStudio<EventLogResponse>(
            "/rapid/errors",
            {},
            20000 // 20s timeout — controller may be slow after stop
          );

        if (errorLogResponse.messages.length === 0) {
          return {
            content: [
              {
                type: "text",
                text: "No event log messages found.",
              },
            ],
          };
        }

        const errorLines: string[] = [];
        for (const msg of errorLogResponse.messages) {
          let line = `[${msg.type}] ${msg.title}`;
          if (msg.body) line += ` - ${msg.body}`;
          errorLines.push(line);
        }

        return {
          content: [
            {
              type: "text",
              text: `${errorLogResponse.messages.length} entries:\n${errorLines.join("\n")}`,
            },
          ],
        };
      }

      case "get_screenshot": {
        const screenshotArgs = args as {
          width?: number;
          height?: number;
        };

        const body: Record<string, number> = {};
        if (screenshotArgs?.width) body.width = screenshotArgs.width;
        if (screenshotArgs?.height) body.height = screenshotArgs.height;

        const screenshotResponse =
          await fetchFromRobotStudio<ScreenshotResponse>(
            "/screenshot",
            {
              method: "POST",
              body: JSON.stringify(body),
            },
            20000 // 20s timeout — screenshot may take time on UI thread
          );

        return {
          content: [
            {
              type: "image",
              data: screenshotResponse.imageBase64,
              mimeType: screenshotResponse.mimeType,
            },
            {
              type: "text",
              text: `${screenshotResponse.width}x${screenshotResponse.height}`,
            },
          ],
        };
      }

      case "read_rapid_variable": {
        const varArgs = args as {
          variableName?: string;
          taskName?: string;
          moduleName?: string;
        };

        if (!varArgs?.variableName) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'variableName'."
          );
        }

        const varBody: Record<string, string> = {
          variableName: varArgs.variableName,
        };
        if (varArgs.taskName) varBody.taskName = varArgs.taskName;
        if (varArgs.moduleName) varBody.moduleName = varArgs.moduleName;

        const varResponse =
          await fetchFromRobotStudio<RapidVariableResponse>(
            "/rapid/variable",
            {
              method: "POST",
              body: JSON.stringify(varBody),
            }
          );

        return {
          content: [
            {
              type: "text",
              text: `${varResponse.moduleName}/${varResponse.variableName} (${varResponse.dataType}) = ${varResponse.value}`,
            },
          ],
        };
      }

      case "get_io_signals": {
        const ioArgs = args as {
          signalName?: string;
        };

        const ioBody: Record<string, string> = {};
        if (ioArgs?.signalName) ioBody.signalName = ioArgs.signalName;

        const ioResponse =
          await fetchFromRobotStudio<IOSignalsResponse>(
            "/io/signals",
            {
              method: "POST",
              body: JSON.stringify(ioBody),
            }
          );

        if (ioResponse.signals.length === 0) {
          return { content: [{ type: "text", text: "No signals found." }] };
        }

        const ioLines: string[] = [];
        for (const sig of ioResponse.signals) {
          ioLines.push(`${sig.name}(${sig.type})=${sig.value}`);
        }

        return {
          content: [{ type: "text", text: ioLines.join(", ") }],
        };
      }

      case "get_scene_objects": {
        const sceneArgs = args as {
          nameFilter?: string;
          includeChildren?: boolean;
        };

        const sceneBody: Record<string, unknown> = {};
        if (sceneArgs?.nameFilter) sceneBody.nameFilter = sceneArgs.nameFilter;
        if (sceneArgs?.includeChildren !== undefined)
          sceneBody.includeChildren = sceneArgs.includeChildren;

        const sceneResponse =
          await fetchFromRobotStudio<SceneObjectsResponse>(
            "/scene/objects",
            {
              method: "POST",
              body: JSON.stringify(sceneBody),
            }
          );

        const sceneLines: string[] = [];

        function formatObject(obj: SceneObjectData, indent: string): void {
          let line = `${indent}${obj.name}[${obj.typeName}]`;
          if (obj.globalPosition) {
            line += ` g(${obj.globalPosition.x},${obj.globalPosition.y},${obj.globalPosition.z})`;
          } else if (obj.position) {
            line += ` l(${obj.position.x},${obj.position.y},${obj.position.z})`;
          }
          if (obj.eulerAngles) {
            line += ` r(${obj.eulerAngles.rx},${obj.eulerAngles.ry},${obj.eulerAngles.rz})`;
          }
          if (!obj.visible) line += " hidden";
          sceneLines.push(line);
          if (obj.children) {
            for (const child of obj.children) {
              formatObject(child, indent + "  ");
            }
          }
        }

        for (const obj of sceneResponse.objects) {
          formatObject(obj, "");
        }

        return {
          content: [{ type: "text", text: `${sceneResponse.objectCount} objects:\n${sceneLines.join("\n")}` }],
        };
      }

      case "set_rapid_variable": {
        const setVarArgs = args as {
          variableName?: string;
          value?: string;
          taskName?: string;
          moduleName?: string;
        };

        if (!setVarArgs?.variableName) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'variableName'."
          );
        }

        if (setVarArgs.value === undefined || setVarArgs.value === null) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'value'."
          );
        }

        const setVarBody: Record<string, string> = {
          variableName: setVarArgs.variableName,
          value: setVarArgs.value,
        };
        if (setVarArgs.taskName) setVarBody.taskName = setVarArgs.taskName;
        if (setVarArgs.moduleName) setVarBody.moduleName = setVarArgs.moduleName;

        const setVarResponse =
          await fetchFromRobotStudio<SetRapidVariableResponse>(
            "/rapid/variable/set",
            {
              method: "POST",
              body: JSON.stringify(setVarBody),
            }
          );

        return {
          content: [
            {
              type: "text",
              text: `${setVarResponse.moduleName}/${setVarResponse.variableName}: ${setVarResponse.previousValue} -> ${setVarResponse.newValue}`,
            },
          ],
        };
      }

      case "list_rapid_variables": {
        const lvArgs = args as {
          typeFilter?: string;
          moduleName?: string;
          taskName?: string;
        };

        const lvBody: Record<string, string> = {};
        if (lvArgs?.typeFilter) lvBody.typeFilter = lvArgs.typeFilter;
        if (lvArgs?.moduleName) lvBody.moduleName = lvArgs.moduleName;
        if (lvArgs?.taskName) lvBody.taskName = lvArgs.taskName;

        const lvResponse =
          await fetchFromRobotStudio<ListVariablesResponse>(
            "/rapid/variables",
            {
              method: "POST",
              body: JSON.stringify(lvBody),
            }
          );

        if (lvResponse.variables.length === 0) {
          return {
            content: [
              { type: "text", text: "No variables found." },
            ],
          };
        }

        const lvLines: string[] = [];
        lvLines.push(
          `${lvResponse.variableCount} variables in task ${lvResponse.taskName}:`
        );
        for (const v of lvResponse.variables) {
          lvLines.push(
            `  ${v.scope} ${v.dataType} ${v.moduleName}/${v.name} = ${v.value}`
          );
        }

        return {
          content: [{ type: "text", text: lvLines.join("\n") }],
        };
      }

      case "set_io_signal": {
        const setIoArgs = args as {
          signalName?: string;
          value?: number;
        };

        if (!setIoArgs?.signalName) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'signalName'."
          );
        }

        if (setIoArgs.value === undefined || setIoArgs.value === null) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "Missing required parameter 'value'."
          );
        }

        const setIoResponse =
          await fetchFromRobotStudio<SetIOSignalResponse>(
            "/io/signals/set",
            {
              method: "POST",
              body: JSON.stringify({
                signalName: setIoArgs.signalName,
                value: setIoArgs.value,
              }),
            }
          );

        return {
          content: [
            {
              type: "text",
              text: `${setIoResponse.signalName}: ${setIoResponse.previousValue} -> ${setIoResponse.newValue}`,
            },
          ],
        };
      }

      default:
        throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${name}`);
    }
  });

  // Error handling
  server.onerror = (error) => {
    console.error("[MCP Error]", error);
  };

  return server;
}

/**
 * Main entry point.
 */
async function main(): Promise<void> {
  const server = createServer();
  const transport = new StdioServerTransport();

  await server.connect(transport);

  console.error("RobotStudio MCP Server running on stdio");
}

main().catch((error) => {
  console.error("Fatal error:", error);
  process.exit(1);
});
