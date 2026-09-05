# Extended RobotStudio tools

[Back to README](../README.md)

The 18 tools below extend the original 16 MCP tools. Both MCP and the standalone CLI call the same C# HTTP handlers. No RWS service or credentials are required. Controller operations are restricted to a station containing exactly one connected virtual controller; ambiguous controllers/tasks are rejected.

## Installation and verification status

Build with `./build.ps1`, then close RobotStudio and install with `./deploy.ps1`. If Windows denies access to the Program Files add-in directory, run deployment in an administrator PowerShell. Restart RobotStudio and check `node scripts/robotstudio.mjs health`: the new add-in reports `apiVersion: 2` and `extendedTools: 18`. Restart the MCP client/server after rebuilding TypeScript as well.

This increment passed the 2024 SDK build, TypeScript build, 9 Node tests (including a real MCP stdio client against mocked HTTP), 7 standalone C# RAPID-precheck cases 8 filesystem-boundary cases and 5 direct-HTTP schema-boundary cases. The current process could not overwrite the installed DLL because Program Files access was denied. The new functions have **not** been verified inside RobotStudio. 2025 and 2026 remain unverified. Existing 2024 recording results do not test these additions.

## Command reference

POST parameters are JSON. Omitted optional parameters use the defaults described below. Use `--describe <command>` for schema details. MCP returns the same response fields as the CLI for these tools.

| Tool | Endpoint | Required parameters | Optional parameters |
|---|---|---|---|
| `get_robot_pose` | `/robot/pose` | — | `mechanicalUnit`, `frame` |
| `get_speed_settings` | `/speed/get` | — | — |
| `set_simulation_speed` | `/speed/simulation` | `multiplier` | — |
| `set_speed_override` | `/speed/override` | `percent` | — |
| `save_station` | `/station/save` | — | — |
| `save_rapid_program` | `/program/save` | — | `taskName` |
| `list_rapid_backups` | `/program/backups` | — | `taskName` |
| `load_rapid_program` | `/program/load` | `backupId` | `taskName` |
| `get_paths` | `/paths/list` | — | `taskName` |
| `get_path_targets` | `/paths/targets` | `pathName` | `taskName` |
| `create_path` | `/paths/create` | `pathName` | `taskName`, `moduleName` |
| `create_target` | `/targets/create` | `targetName`, `xMm`, `yMm`, `zMm` | `taskName`, `workObject`, `rxDeg`, `ryDeg`, `rzDeg` |
| `append_path_target` | `/paths/append` | `pathName`, `targetName` | `taskName`, `viaTargetName`, `toolName`, `workObject`, `motion` |
| `validate_rapid` | `/rapid/validate` | `code` | — |
| `check_execution_ready` | `/rapid/check` | — | `taskName` |
| `read_controller_config` | `/controller/config` | `fileName` | — |
| `list_controller_files` | `/controller/files` | — | `relativePath` |
| `read_controller_file` | `/controller/file` | `relativePath` | — |

## TCP and speed

`get_robot_pose` returns XYZ in **millimeters**, quaternion `q1..q4`, the selected coordinate frame, active tool/work object names, mechanical-unit name and timestamp. Frame defaults to `world`; alternatives are `base`, `workobject`, and `tool`. If there is more than one mechanical unit, specify `mechanicalUnit`. This is distinct from `get_scene_objects`, whose raw coordinates use SDK meters.

`get_speed_settings` returns both settings. `set_simulation_speed` accepts a multiplier from 0.1 to 10; `set_speed_override` accepts an integer percentage from 0 to 100. Simulation time scaling and controller speed override are separate settings. Read and keep the previous values before temporary changes. Changing either setting does not rewrite RAPID `speeddata`.

```powershell
node scripts/robotstudio.mjs get_robot_pose
node scripts/robotstudio.mjs get_speed_settings
'{"percent":25}' | Set-Content speed.json -Encoding utf8
node scripts/robotstudio.mjs set_speed_override --params-file speed.json
```

## Save and restore

`save_station` saves the current named station through the SDK on the UI thread. Save an unnamed station once in RobotStudio first. It saves the current station file, not a separate historical copy.

`save_rapid_program` exports a task program to a unique directory and returns `backup.backupId`, directory and manifest metadata. `taskName` defaults to `T_ROB1`. Backups live under `%LOCALAPPDATA%/RobotStudioMcp/backups/<controller-id>/<task>/<backup-id>/`. `list_rapid_backups` lists completed backup manifests for that controller/task.

`load_rapid_program` accepts one of those backup IDs. It requires stopped execution, checks controller/task identity and creates a separate recovery backup before loading with replacement. On failure, the response includes the recovery backup ID and directory. **It does not automatically roll back or start execution.** After loading, inspect `check_execution_ready`, then explicitly reset the program pointer/start only when intended.

These are task-program backups, not full controller or station snapshots. They do not restore physics, I/O state, system configuration or the entire simulation scene. Save/load requests use a 90-second client timeout. API requests are serialized to avoid overlap with the legacy upload/start handlers; a timed-out operation may still complete. Never automatically retry a write.

```powershell
node scripts/robotstudio.mjs save_rapid_program
node scripts/robotstudio.mjs list_rapid_backups
# Use the returned backup.backupId, not a local filename.
'{"taskName":"T_ROB1","backupId":"REPLACE_WITH_RETURNED_ID"}' | Set-Content restore.json -Encoding utf8
node scripts/robotstudio.mjs load_rapid_program --params-file restore.json
```

## Station paths and targets

`taskName` defaults to the active station task. A target position uses millimeters in its chosen work object; `rxDeg`, `ryDeg`, `rzDeg` are Euler XYZ angles in degrees, defaulting to zero. The default work object and tool are the active ones. Existing names and ambiguous declarations are rejected.

`create_target` creates a RAPID target declaration plus a visible station target. `create_path` creates an empty path, with default module `McpPaths`. `append_path_target` adds a linear, joint or circular instruction using existing targets; circular motion requires a distinct `viaTargetName`. The SDK's default Move instruction template supplies speed and zone data. Station mutations use UI dispatch and Undo steps, including rollback of an incomplete edit.

`get_paths` lists paths and instruction counts. `get_path_targets` returns instruction target coordinates, orientation, work object and configuration status.

**These are station model edits.** They are not automatically synchronized into the controller, and target robot configurations/reachability are not solved. Review/configure the target and synchronize the path in RobotStudio before attempting execution. A created path is not evidence of an executable, collision-free trajectory.

## RAPID and readiness checks

```powershell
node scripts/robotstudio.mjs validate_rapid --code-file program.mod
node scripts/robotstudio.mjs check_execution_ready
```

`validate_rapid` ignores strings/comments while checking structural pairs such as MODULE, PROC, FUNC, TRAP, RECORD, block IF, FOR, WHILE and TEST. It reports line-numbered issues and `valid`. It supports compact IF and multiline block IF, but is not a complete RAPID parser or compiler. Call it explicitly before upload; it does not silently change existing upload semantics.

`check_execution_ready` reports `ready`, reasons, current controller state and SDK `Task.CheckProgram()` diagnostics with task/module/line/column while stopped. It requires automatic mode, motors on and an enabled task for readiness. It does not equate historical event-log entries with current compile errors. It does not automatically change motor state, clear errors, start execution, check collisions or assess physical safety.

## Read-only controller files

`list_controller_files` lists up to 500 entries below the virtual controller's HOME; omit `relativePath` for the root. `read_controller_file` permits text extensions `.mod`, `.sys`, `.pgf`, `.cfg`, `.txt`, `.log`, `.json`, and `.xml`, up to 1 MiB. Absolute paths, `..`, alternate data streams and junctions/symbolic links are rejected.

`read_controller_config` reads one saved SYS/EIO/SIO/MOC.cfg file beneath the virtual controller's system `SYSPAR` directory. It does not claim those on-disk files are a live configuration snapshot. Configuration writes, arbitrary host filesystem access and real-controller RWS control are not introduced.

## Design references and acknowledgments

- [Elias Bitsch / abb-robotstudio-mcp](https://github.com/eliasbitsch/abb-robotstudio-mcp/tree/41355e0d02f6efe5d14157dbe4376c2ddae60e6a): TCP, speed, station/path and controller inspection ideas.
- [LiskinLabs / abb-robotstudio-mcp](https://github.com/LiskinLabs/abb-robotstudio-mcp/tree/d969a858ae0b3e62fb34d30676d883fd9ade893e): program save/load, RAPID precheck and readiness-check ideas.
- [ABB: Saving Program](https://developercenter.robotstudio.com/api/robotstudio/articles/How-To/Rapid/SaveProgram.html) and [Creating a Path](https://developercenter.robotstudio.com/api/robotstudio/articles/How-To/Targets-and-Paths/CreatePath.html): SDK API references, checked against installed 2024 assemblies.

Implementation is adapted to this repository's shared C# / HTTP architecture. We do not adopt the reference implementation's assumption that no exception from a collision-check call means no collisions. No collision-check tool is advertised here.
