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
