# RobotStudio MCP Server Bridge

A Model Context Protocol (MCP) server that allows AI assistants to interact with ABB RobotStudio. The system consists of two parts:

1. **C# Add-in**: A .NET Framework 4.8 DLL running inside RobotStudio that exposes a local HTTP REST API on port 8080
2. **TypeScript MCP Server**: A Node.js application that exposes MCP tools and communicates with the C# Add-in

## Project Structure

```
/robotstudio-mcp
  /src
    server.ts                     # TypeScript MCP Server
    package.json
    tsconfig.json
  /addin
    RobotStudioAddin.cs           # C# Source for the Add-in
    RobotStudioMcpAddin.csproj    # MSBuild project file
    RobotStudioMcpAddin.rsaddin   # Add-in manifest (XML)
    packages.config               # NuGet dependencies
```

## Prerequisites

- ABB RobotStudio 2024 (or compatible version)
- .NET Framework 4.8 SDK
- Node.js 18 or later
- MSBuild (included with .NET Framework, Visual Studio not required)

## Building the C# Add-in

### 1. Restore NuGet Packages

```bash
cd addin
nuget restore
```

Or manually ensure `packages/Newtonsoft.Json.13.0.3/` exists.

### 2. Build the Add-in

Using the .NET Framework MSBuild (no Visual Studio required):

```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe addin\RobotStudioMcpAddin.csproj /p:Configuration=Release
```

The post-build event will automatically copy the DLL to:
```
C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin\
```

> **Note:** Building with post-build deploy requires administrator privileges since the target is in Program Files.

### 3. Manual Installation (if post-build fails)

Copy these files from `addin\bin\Release\`:
```
RobotStudioMcpAddin.dll
Newtonsoft.Json.dll
```

And the manifest from `addin\`:
```
RobotStudioMcpAddin.rsaddin
```

To:
```
C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin\
```

## Building the TypeScript MCP Server

```bash
cd src
npm install
npm run build
```

## Running the System

### 1. Start RobotStudio

1. Launch ABB RobotStudio
2. Open or create a station with a Virtual Controller
3. The Add-in loads automatically and starts the HTTP server on port 8080
4. Check the RobotStudio Output window for: `MCP Add-in: Started on port 8080`

### 2. Verify the Add-in is Running

Test the HTTP API directly:
```bash
curl http://localhost:8080/health
curl http://localhost:8080/status
curl http://localhost:8080/joints
```

### 3. Configure Claude Desktop

Add to your Claude Desktop configuration (`%AppData%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "robotstudio": {
      "command": "node",
      "args": ["C:/path/to/robotstudio-mcp/src/dist/server.js"]
    }
  }
}
```

### 4. Restart Claude Desktop

After updating the configuration, restart Claude Desktop to load the MCP server.

## Available MCP Tools

### `get_robot_joints`
Read real-time joint positions (J1-J6) from the active robot in RobotStudio Virtual Controller. Returns joint angles in degrees.

### `control_simulation`
Start or stop the RobotStudio simulation.

**Parameters:**
- `action`: `"start"` or `"stop"`

### `get_station_status`
Get the current status of RobotStudio including whether a station is open, simulation state, and virtual controller information.

## HTTP API Endpoints (Add-in)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/status` | GET | Station and simulation status |
| `/joints` | GET | Current joint positions |
| `/simulation` | POST | Control simulation (`{"action": "start\|stop"}`) |

## Troubleshooting

### Add-in shows X (failed to load) in RobotStudio

This was the main issue encountered during development. The root causes and fixes were:

**1. HttpListener requires admin privileges (critical)**

The original implementation used `System.Net.HttpListener` to serve the REST API. On Windows, `HttpListener` requires URL ACL registration (`netsh http add urlacl`) for non-admin processes. Since RobotStudio runs as a normal user, `HttpListener.Start()` throws an `HttpListenerException` (Access Denied), causing the add-in to fail silently.

**Fix:** Replaced `HttpListener` with `System.Net.Sockets.TcpListener`, which can bind to `127.0.0.1` without any special permissions. HTTP request/response parsing is done manually.

**2. Add-in must be deployed to Program Files, not LocalAppData**

RobotStudio only scans for add-ins in its own installation directory:
```
C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\
```

It does **NOT** scan `%LocalAppData%\ABB\RobotStudio\Addins\`. Deploying to LocalAppData will result in the add-in not appearing in the Add-Ins list at all.

**3. rsaddin manifest must follow official conventions**

The `.rsaddin` manifest file should include:
- `<Dependencies>Online</Dependencies>` (not `None` or `Station`)
- `<Platform>Any</Platform>`

These match the format used by official ABB add-ins (IOConfigurator, FleetManagement, etc.).

**4. C# language version must be compatible with MSBuild**

When building with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`, the C# compiler only supports C# 5 syntax. The following C# 6+ features must be avoided:
- `?.` (null-conditional operator) - use explicit null checks
- `$""` (string interpolation) - use string concatenation
- `out var` (inline out variable) - declare variable separately
- `catch when` (exception filters) - use if-check inside catch block

### "Cannot connect to RobotStudio"
- Ensure RobotStudio is running
- Check that the Add-in is loaded (look for `MCP Add-in:` messages in Output window)
- Verify port 8080 is not blocked by firewall
- Test with `curl http://localhost:8080/health`

### "No virtual controller found"
- Open a station in RobotStudio that contains a Virtual Controller
- The Virtual Controller must be running (started)

### Build errors for SDK references
- The `.csproj` dynamically resolves the RobotStudio SDK path from `Program Files (x86)` or `Program Files`
- If your installation is non-standard, update the `<RobotStudioBin>` property in the `.csproj`

## Technical Details

### Architecture

```
AI Assistant <--MCP--> TypeScript Server <--HTTP--> C# Add-in <--SDK--> RobotStudio
                        (Node.js)                    (TcpListener        (Virtual
                        port: stdio                   port: 8080)         Controller)
```

### Key Implementation Notes

- The C# add-in uses a raw `TcpListener` (not `HttpListener`) to avoid Windows URL ACL permission requirements
- HTTP parsing is done manually: reads request line + headers + body from the TCP stream
- The TCP server runs on a background thread with `IsBackground = true` so it doesn't prevent RobotStudio from closing
- CORS headers are included on all responses for local development
- The add-in connects to the Virtual Controller via `Controller.Connect(systemId, ConnectionType.RobotStudio)` using the station's `Irc5Controllers` collection

## License

MIT
