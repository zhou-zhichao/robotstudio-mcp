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
          "Start or stop the RobotStudio simulation. Use action 'start' to begin simulation or 'stop' to end it.",
        inputSchema: {
          type: "object" as const,
          properties: {
            action: {
              type: "string",
              enum: ["start", "stop"],
              description:
                "The simulation control action: 'start' to begin simulation, 'stop' to end simulation.",
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
        name: "get_execution_errors",
        description:
          "Read recent entries from the controller event log including errors, warnings, and informational messages. Returns up to 50 most recent entries sorted by newest first.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
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

        const joints = response.joints;
        const formattedOutput = [
          `Robot Joint Positions (degrees):`,
          `  J1: ${joints.j1.toFixed(3)}°`,
          `  J2: ${joints.j2.toFixed(3)}°`,
          `  J3: ${joints.j3.toFixed(3)}°`,
          `  J4: ${joints.j4.toFixed(3)}°`,
          `  J5: ${joints.j5.toFixed(3)}°`,
          `  J6: ${joints.j6.toFixed(3)}°`,
          ``,
          `Timestamp: ${response.timestamp}`,
        ].join("\n");

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

        if (!action || !["start", "stop"].includes(action)) {
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

        const statusLines = [
          `RobotStudio Status:`,
          `  Station Open: ${response.hasActiveStation ? "Yes" : "No"}`,
        ];

        if (response.hasActiveStation) {
          statusLines.push(
            `  Station Name: ${response.stationName}`,
            `  Virtual Controllers: ${response.virtualControllerCount}`,
            `  Simulation Running: ${response.isSimulationRunning ? "Yes" : "No"}`
          );
        }

        return {
          content: [
            {
              type: "text",
              text: statusLines.join("\n"),
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
              text: [
                `${uploadResponse.message}`,
                `  Module: ${uploadResponse.moduleName}`,
                `  Task: ${uploadResponse.taskName}`,
              ].join("\n"),
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

        return {
          content: [
            {
              type: "text",
              text: `${execResponse.message}\nExecution status: ${execResponse.executionStatus}`,
            },
          ],
        };
      }

      case "get_rapid_execution_status": {
        const statusResponse =
          await fetchFromRobotStudio<RapidStatusResponse>("/rapid/status");

        const lines = [
          `RAPID Execution Status: ${statusResponse.controllerExecutionStatus}`,
          ``,
        ];

        for (const task of statusResponse.tasks) {
          lines.push(`Task: ${task.name}`);
          lines.push(`  Status: ${task.executionStatus}`);
          lines.push(`  Enabled: ${task.enabled}`);
          lines.push(`  Type: ${task.type}`);

          if (task.programPointer) {
            lines.push(
              `  Program Pointer: ${task.programPointer.module}/${task.programPointer.routine} [${task.programPointer.range}]`
            );
          }
          if (task.motionPointer) {
            lines.push(
              `  Motion Pointer: ${task.motionPointer.module}/${task.motionPointer.routine} [${task.motionPointer.range}]`
            );
          }
          lines.push("");
        }

        return {
          content: [
            {
              type: "text",
              text: lines.join("\n"),
            },
          ],
        };
      }

      case "get_execution_errors": {
        const errorLogResponse =
          await fetchFromRobotStudio<EventLogResponse>("/rapid/errors");

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

        const errorLines = [
          `Event Log (${errorLogResponse.messages.length} entries):`,
          ``,
        ];

        for (const msg of errorLogResponse.messages) {
          errorLines.push(
            `[${msg.type}] #${msg.sequenceNumber} - ${msg.title}`
          );
          if (msg.body) {
            errorLines.push(`  ${msg.body}`);
          }
          errorLines.push(
            `  Category: ${msg.categoryName} | Time: ${msg.timestamp}`
          );
          errorLines.push("");
        }

        return {
          content: [
            {
              type: "text",
              text: errorLines.join("\n"),
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
