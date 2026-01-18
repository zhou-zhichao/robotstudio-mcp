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

/**
 * Makes an HTTP request to the RobotStudio Add-in API.
 */
async function fetchFromRobotStudio<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const url = `${ROBOTSTUDIO_API_BASE}${endpoint}`;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

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
