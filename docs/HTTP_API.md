# HTTP API reference

[Back to README](../README.md)

The C# add-in listens on loopback port 8080. The standalone CLI and MCP server both use these routes. POST parameters are JSON request bodies, not query strings.


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


## Inputs and responses

Use `node scripts/robotstudio.mjs --describe <command>` from the repository root for CLI parameter types, required fields and request timeouts. See [the CLI route definitions](../scripts/robotstudio.mjs) and [C# handlers](../addin/RobotStudioAddin.cs) for exact HTTP contracts.

The CLI reports HTTP errors, `success:false`, malformed JSON and timeouts as failures. Raw scene positions and bounds use SDK units (meters); the MCP presentation can convert them to millimeters. Screenshot responses contain `imageBase64`; the CLI saves a PNG and returns its absolute path.

## Existing behavior

- Upload uses BOM-free UTF-8 and CRLF. With `replaceExisting:true` (the default), cleanup attempts to delete modules in the selected task except those named `BASE` or `user`. Back up all affected modules; automatic rollback is not implemented.
- Simulation reset includes demo-specific dynamic box cleanup; it is not a general station restore.
- No automatic write retries are performed by the CLI. After a timeout, inspect actual state before retrying.
- The listener uses TcpListener to avoid HTTP URL ACL registration; copying the add-in into Program Files may still require administrator rights.
- For historical debugging notes and scenario-specific RAPID lessons, read the [development log](DEVELOPMENT_LOG.md) and [RAPID examples](RAPID_EXAMPLES.md).

## Extended routes

All routes below use POST with a JSON body. They require the updated add-in (`health` reports `apiVersion: 2`). See [extended tools](EXTENDED_TOOLS.md) for units, backup rules and validation limits.

| Endpoint | Tool |
|---|---|
| `/robot/pose` | `get_robot_pose` |
| `/speed/get` | `get_speed_settings` |
| `/speed/simulation` | `set_simulation_speed` |
| `/speed/override` | `set_speed_override` |
| `/station/save` | `save_station` |
| `/program/save` | `save_rapid_program` |
| `/program/backups` | `list_rapid_backups` |
| `/program/load` | `load_rapid_program` |
| `/paths/list` | `get_paths` |
| `/paths/targets` | `get_path_targets` |
| `/paths/create` | `create_path` |
| `/targets/create` | `create_target` |
| `/paths/append` | `append_path_target` |
| `/rapid/validate` | `validate_rapid` |
| `/rapid/check` | `check_execution_ready` |
| `/controller/config` | `read_controller_config` |
| `/controller/files` | `list_controller_files` |
| `/controller/file` | `read_controller_file` |
