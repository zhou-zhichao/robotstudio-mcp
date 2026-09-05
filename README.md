# RobotStudio Agent Bridge — Skills, CLI and MCP

A local bridge that allows AI assistants to control ABB RobotStudio — reading joint positions, uploading RAPID programs, and executing robot motions in simulation. Use the repository Skill and dependency-free Node.js CLI, or the existing Model Context Protocol (MCP) server.

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

The optional local CLI bypasses component 2: `Agent -> Skill -> CLI -> HTTP Add-in -> SDK`. Both interfaces use the same C# implementation.

## Project Structure

```
/robotstudio-mcp
  /.agents/skills/robotstudio
    SKILL.md                      # Repository agent workflow
  /scripts
    robotstudio.mjs                # Standalone HTTP CLI (no npm dependencies)
    robotstudio-paths.ps1          # SDK path resolution
  /tests
    robotstudio-cli.test.mjs       # Mock HTTP / CLI tests
  /src
    server.ts                     # TypeScript MCP Server (16 tools)
    package.json
    tsconfig.json
  /addin
    RobotStudioAddin.cs           # C# Add-in (17 HTTP endpoints)
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

**Compatibility:** RobotStudio 2024 is the existing implementation baseline. RobotStudio 2025 and 2026 are not tested by this project; 2026.1+ requires migrating the add-in to .NET 10. See the [compatibility and Skills/MCP interface plan](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md) for evidence, proposed adaptations, and validation limits.

- ABB RobotStudio 2024 (other versions require separate compatibility validation)
- .NET Framework 4.8 SDK
- Node.js 18+
- Visual Studio Build Tools / MSBuild for the Framework add-in
- Newtonsoft.Json 13.0.3 restored under `addin/packages` (for example, `nuget install addin/packages.config -OutputDirectory addin/packages`)

## Quick Start

### Local Skill / CLI (no MCP setup)

Build/deploy the C# add-in below, start RobotStudio and open a station with a virtual controller. Then run from the repository root:

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
node scripts/robotstudio.mjs --describe upload_rapid_module
```

The repository Skill lives in [`.agents/skills/robotstudio/SKILL.md`](.agents/skills/robotstudio/SKILL.md). In Codex, invoke `$robotstudio` while working in this repository. Other agents with local shell access can use the CLI directly or load the same instructions.

Use `--params-file params.json` for JSON arguments and `--code-file program.mod` for RAPID upload. Example `params.json`: `{"moduleName":"Demo","taskName":"T_ROB1","replaceExisting":false}`.

```powershell
node scripts/robotstudio.mjs upload_rapid_module --params-file params.json --code-file program.mod
```

All 16 existing tool names are supported, plus `health`. Run `--help` and `--describe <command>` for the command contract. JSON goes to stdout; failures produce JSON on stderr and a nonzero exit code. `--output` saves JSON for normal commands or PNG for screenshots and never overwrites files. Screenshots require `--output`. `--url` / `ROBOTSTUDIO_API_BASE` selects the HTTP add-in origin; the default is `http://127.0.0.1:8080`. `--timeout` is in milliseconds. Requests are never retried automatically.

The CLI returns raw HTTP JSON, not MCP's formatted text or unit conversions. Interpret scene geometry using the SDK's units (meters for positions/bounds); do not assume raw values are millimeters. The upload endpoint can remove multiple program modules when replacement is enabled. Back up affected modules first; automatic rollback is not implemented. Reset includes existing demo-specific box cleanup.

The CLI requires only Node.js 18+, without npm installation. The MCP route below remains optional.

### 1. Build & Deploy the C# Add-in

```powershell
# Build
.\build.ps1

# Deploy (run as Administrator)
.\deploy.ps1
```

Builds now write to `artifacts/<year>` and never auto-deploy. Both scripts accept `-RobotStudioVersion 2024|2025|2026` and `-RobotStudioBin <matching-installation-Bin>`; the build script also accepts `-MSBuildPath`. Deployment checks build metadata and supports `-WhatIf`.

The 2025 build configuration follows [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp) and [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp): .NET Framework 4.8, references to the selected 2025 host assemblies, and installation under `Bin/Addins`. It remains untested in this project. The separate `addin/RobotStudioMcpAddin.Net10.csproj` is an **uncompiled experimental 2026 migration target**, requiring matching .NET 10 ABB assemblies and `-Experimental` on both scripts. It does not establish 2026 compatibility. See the [compatibility plan](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md).

Example with explicit SDK selection (2025 remains untested):

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
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

#### Claude Code (CLI)

**Option A — CLI command (recommended):**

```bash
# Add globally (available in all projects)
claude mcp add --scope user robotstudio -- node C:/path/to/robotstudio-mcp/src/dist/server.js

# Or add for current project only (default)
claude mcp add robotstudio -- node C:/path/to/robotstudio-mcp/src/dist/server.js
```

This writes the config to `~/.claude.json`. Use `--scope user` to make it available everywhere.

**Option B — Project-level `.mcp.json`:**

Create a `.mcp.json` file in the project root:

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

This makes the MCP server available whenever Claude Code is opened in this directory.

**Option C — Global `settings.json`:**

Add to `~/.claude/settings.json` (Windows: `%USERPROFILE%\.claude\settings.json`):

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

After any option, **fully restart Claude Code** (exit + relaunch, not just a new conversation). Verify with `/mcp` — you should see `robotstudio` listed with all tools available.

#### Claude Desktop

Add to `%AppData%\Claude\claude_desktop_config.json`:

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
| `control_rapid_execution` | Start/stop/reset RAPID program execution |
| `control_simulation` | Start, stop, or reset RobotStudio simulation |
| `get_execution_errors` | Read controller event log errors, warnings, and messages |
| `get_io_signals` | Read digital and analog I/O signal values |
| `get_rapid_execution_status` | Get execution status and program pointer |
| `get_rapid_module_source` | Read RAPID module source code from controller |
| `get_robot_joints` | Read real-time joint positions (J1-J6) in degrees |
| `get_scene_objects` | Read station scene objects, transforms, visibility, and bounding boxes |
| `get_screenshot` | Capture 3D view screenshot (base64 PNG) |
| `get_station_status` | Get station, simulation, and controller info |
| `list_rapid_modules` | List all loaded modules grouped by task |
| `list_rapid_variables` | List RAPID variables across modules, with optional type filtering |
| `read_rapid_variable` | Read the current value of a RAPID variable |
| `set_io_signal` | Set a digital or analog I/O signal value |
| `set_rapid_variable` | Write a RAPID variable value while execution is stopped |
| `upload_rapid_module` | Upload RAPID code to the virtual controller |

## HTTP API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/status` | GET | Station and simulation status |
| `/joints` | GET | Current joint positions (J1-J6) |
| `/simulation` | POST | Control simulation (`{"action": "start\|stop\|reset"}`) |
| `/rapid/upload` | POST | Upload RAPID module |
| `/rapid/execute` | POST | Control execution (`{"action": "start\|stop\|resetpp"}`) |
| `/rapid/status` | GET | Execution status and program pointer |
| `/rapid/source` | POST | Read RAPID module source |
| `/rapid/modules` | GET | List loaded RAPID modules |
| `/rapid/errors` | GET | Recent event log messages |
| `/rapid/variable` | POST | Read a RAPID variable |
| `/rapid/variable/set` | POST | Write a RAPID variable |
| `/rapid/variables` | GET/POST | List RAPID variables; POST accepts filters |
| `/io/signals` | GET/POST | Read I/O values; POST accepts a signal filter |
| `/io/signals/set` | POST | Set an I/O signal value |
| `/scene/objects` | GET/POST | Read scene objects; POST accepts filters |
| `/screenshot` | POST | Capture a RobotStudio 3D view screenshot |

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
3. **Legacy compiler limitations** — The original Framework v4 compiler did not support modern C# syntax. The build script now discovers Visual Studio MSBuild; use that toolchain for current builds.
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

## Validation

```powershell
node --test tests/robotstudio-cli.test.mjs
```

The CLI tests use a local mock HTTP server; they do not establish RobotStudio runtime compatibility. The 2024 add-in build was checked with installed SDK assemblies, without deploying or executing robot motion. Build warnings identify existing deprecated simulation/Mastership APIs. 2025/2026 runtime checks remain outstanding.

## License

MIT
