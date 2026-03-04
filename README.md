# RobotStudio MCP Server Bridge

A Model Context Protocol (MCP) server that allows AI assistants to control ABB RobotStudio — reading joint positions, uploading RAPID programs, and executing robot motions in simulation.

Built as a research experiment by a human + Claude (Anthropic AI) pair. See [docs/DEVELOPMENT_LOG.md](docs/DEVELOPMENT_LOG.md) for the honest story of how this was built, including all the failures.

## Architecture

```
AI Assistant <--MCP--> TypeScript Server <--HTTP--> C# Add-in <--SDK--> RobotStudio
                        (Node.js)                    (TcpListener        (Virtual
                        port: stdio                   port: 8080)         Controller)
```

Two components:

1. **C# Add-in** (.NET Framework 4.8) — Runs inside RobotStudio, exposes HTTP REST API on port 8080
2. **TypeScript MCP Server** (Node.js) — Bridges MCP protocol to HTTP API

## Project Structure

```
/robotstudio-mcp
  /src
    server.ts                     # TypeScript MCP Server (7 tools)
    package.json
    tsconfig.json
  /addin
    RobotStudioAddin.cs           # C# Add-in (8 HTTP endpoints)
    RobotStudioMcpAddin.csproj    # MSBuild project file
    RobotStudioMcpAddin.rsaddin   # Add-in manifest (XML)
    packages.config               # NuGet dependencies
  /docs
    DEVELOPMENT_LOG.md            # Honest development log with failures
    RAPID_EXAMPLES.md             # Tested RAPID programs
  build.ps1                       # Build script
  deploy.ps1                      # Deployment script (requires admin)
```

## Prerequisites

- ABB RobotStudio 2024 (or compatible version)
- .NET Framework 4.8 SDK
- Node.js 18+
- MSBuild (included with .NET Framework)

## Quick Start

### 1. Build & Deploy the C# Add-in

```powershell
# Build
.\build.ps1

# Deploy (run as Administrator)
.\deploy.ps1
```

Or manually:

```bash
# Build
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe addin\RobotStudioMcpAddin.csproj /p:Configuration=Release

# Copy to RobotStudio Addins folder (needs admin)
copy addin\bin\Release\RobotStudioMcpAddin.dll "C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin\"
copy addin\bin\Release\Newtonsoft.Json.dll "C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin\"
copy addin\RobotStudioMcpAddin.rsaddin "C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin\"
```

### 2. Build the TypeScript MCP Server

```bash
cd src
npm install
npm run build
```

### 3. Start RobotStudio

1. Launch RobotStudio
2. Open/create a station with a Virtual Controller
3. Add-in loads automatically, starts HTTP server on port 8080
4. Verify: `curl http://localhost:8080/health`

### 4. Configure Your AI Assistant

Add to Claude Desktop config (`%AppData%\Claude\claude_desktop_config.json`):

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

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `get_robot_joints` | Read real-time joint positions (J1-J6) in degrees |
| `control_simulation` | Start/stop RobotStudio simulation |
| `get_station_status` | Get station, simulation, and controller info |
| `upload_rapid_module` | Upload RAPID code to the virtual controller |
| `control_rapid_execution` | Start/stop/reset RAPID program execution |
| `get_rapid_execution_status` | Get execution status and program pointer |
| `get_execution_errors` | Read event log for errors and warnings |

## HTTP API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/status` | GET | Station and simulation status |
| `/joints` | GET | Current joint positions (J1-J6) |
| `/simulation` | POST | Control simulation (`{"action": "start\|stop"}`) |
| `/rapid/upload` | POST | Upload RAPID module |
| `/rapid/execute` | POST | Control execution (`{"action": "start\|stop\|resetpp"}`) |
| `/rapid/status` | GET | Execution status and program pointer |
| `/rapid/errors` | GET | Recent event log messages |

### RAPID Upload Example

```bash
curl -X POST http://localhost:8080/rapid/upload \
  -H "Content-Type: application/json" \
  -d '{
    "code": "MODULE MyModule\n  PROC main()\n    MoveAbsJ [[0,0,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;\n  ENDPROC\nENDMODULE",
    "moduleName": "MyModule",
    "taskName": "T_ROB1",
    "replaceExisting": true
  }'
```

## Troubleshooting

### Add-in shows X (failed to load) in RobotStudio

See [docs/DEVELOPMENT_LOG.md](docs/DEVELOPMENT_LOG.md) for the full debugging story. Key causes:

1. **HttpListener requires admin** — We use TcpListener instead (no special permissions needed)
2. **Must deploy to Program Files** — RobotStudio only scans `C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\`, not `%LocalAppData%`
3. **C# 5 syntax only** — MSBuild v4.0 doesn't support `?.`, `$""`, `out var`, `catch when`
4. **rsaddin manifest** — Must use `<Dependencies>Online</Dependencies>` and `<Platform>Any</Platform>`

### RAPID upload fails

Common issues we encountered and solved:

1. **"Exception of type 'System.Exception'"** — Add `controller.Logon(UserInfo.DefaultUser)` before write operations
2. **PutFile fails on virtual controllers** — Use `File.Copy()` instead; HOME returns a local Windows path
3. **RAPID syntax error from BOM** — Use `new UTF8Encoding(false)` (no BOM) + normalize to CRLF line endings
4. **"Global routine name main ambiguous"** — Delete ALL existing program modules before loading new ones

### Wrist singularity / Joint out of range

When the robot tool points straight down (orientation `[0, 0, 1, 0]`), J5 approaches 0 degrees, causing wrist singularity. `SingArea \Wrist` can make it worse by spinning J4 past its limits. Solutions:
- Always start programs with `MoveAbsJ` to a known safe position
- Keep the drawing plane at z >= 200mm for IRB120
- Use `ConfL \Off; ConfJ \Off;` to disable configuration checking
- After a failed run, the robot may be stuck in a bad joint configuration — the next program must first recover to home position

## Key Implementation Details

- Raw `TcpListener` instead of `HttpListener` to avoid Windows URL ACL permission requirements
- HTTP parsing done manually: reads request line + headers + body from TCP stream
- Background thread with `IsBackground = true` so it doesn't block RobotStudio shutdown
- CORS headers on all responses for local development
- RAPID files written with `UTF8Encoding(false)` (no BOM) and CRLF line endings
- Module cleanup: deletes all program modules (except BASE and user) before loading to prevent name conflicts
- Controller write operations require `controller.Logon(UserInfo.DefaultUser)` and `Mastership.Request(controller.Rapid)`

## License

MIT
