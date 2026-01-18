# RobotStudio MCP Server Bridge

A Model Context Protocol (MCP) server that allows AI assistants to interact with ABB RobotStudio. The system consists of two parts:

1. **C# Add-in**: A .NET Framework DLL running inside RobotStudio that exposes a local HTTP REST API on port 8080
2. **TypeScript MCP Server**: A Node.js application that exposes MCP tools and communicates with the C# Add-in

## Project Structure

```
/robotstudio-mcp
  /src
    server.ts        # TypeScript MCP Server
    package.json
    tsconfig.json
  /addin
    RobotStudioAddin.cs   # C# Source for the Add-in
    RobotStudioMcpAddin.csproj
    addin.xml             # Add-in manifest
    packages.config       # NuGet dependencies
```

## Prerequisites

- ABB RobotStudio 2024 (or compatible version)
- .NET Framework 4.8 SDK
- Node.js 18 or later
- Visual Studio 2022 (or MSBuild)

## Building the C# Add-in

### 1. Update SDK References

Edit `addin/RobotStudioMcpAddin.csproj` and update the RobotStudio SDK paths to match your installation:

```xml
<Reference Include="ABB.Robotics.RobotStudio">
  <HintPath>C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\ABB.Robotics.RobotStudio.dll</HintPath>
</Reference>
```

### 2. Restore NuGet Packages

```bash
cd addin
nuget restore
```

Or using Visual Studio, right-click the solution and select "Restore NuGet Packages".

### 3. Build the Add-in

Using MSBuild:
```bash
cd addin
msbuild RobotStudioMcpAddin.csproj /p:Configuration=Release
```

Or open `RobotStudioMcpAddin.csproj` in Visual Studio and build.

The post-build event will automatically copy the DLL to:
```
%LocalAppData%\ABB\RobotStudio\Addins\RobotStudioMcpAddin\
```

### 4. Manual Installation (if needed)

If the post-build event doesn't run, manually copy these files:
```
addin\bin\Release\RobotStudioMcpAddin.dll
addin\bin\Release\Newtonsoft.Json.dll
addin\addin.xml
```

To:
```
%LocalAppData%\ABB\RobotStudio\Addins\RobotStudioMcpAddin\
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
4. Check the RobotStudio Output window for: "RobotStudio MCP Add-in started. Listening on port 8080"

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
      "args": ["C:/Users/YOUR_USERNAME/robotstudio-mcp/src/dist/server.js"]
    }
  }
}
```

Replace `YOUR_USERNAME` with your actual Windows username.

### 4. Restart Claude Desktop

After updating the configuration, restart Claude Desktop to load the MCP server.

## Available MCP Tools

### `get_robot_joints`
Read real-time joint positions (J1-J6) from the active robot in RobotStudio Virtual Controller. Returns joint angles in degrees.

**Example response:**
```
Robot Joint Positions (degrees):
  J1: 0.000°
  J2: 0.000°
  J3: 0.000°
  J4: 0.000°
  J5: 90.000°
  J6: 0.000°
```

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

### "Cannot connect to RobotStudio"
- Ensure RobotStudio is running
- Check that the Add-in is loaded (look for message in Output window)
- Verify port 8080 is not blocked by firewall
- Test with `curl http://localhost:8080/health`

### "No virtual controller found"
- Open a station in RobotStudio that contains a Virtual Controller
- The Virtual Controller must be running (started)

### Add-in not loading
- Check the add-in folder: `%LocalAppData%\ABB\RobotStudio\Addins\RobotStudioMcpAddin\`
- Ensure `addin.xml`, `RobotStudioMcpAddin.dll`, and `Newtonsoft.Json.dll` are present
- Check RobotStudio version compatibility in `addin.xml`

### Build errors for SDK references
- Update the `<HintPath>` elements in the `.csproj` file to match your RobotStudio installation path
- Common paths:
  - `C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\`
  - `C:\Program Files\ABB\RobotStudio 2024\Bin\`

## License

MIT
