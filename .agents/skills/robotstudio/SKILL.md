---
name: robotstudio
description: Operate this repository's ABB RobotStudio HTTP add-in through a local CLI, including RAPID programs, simulation, I/O, scene inspection and screenshots. Use for RobotStudio execution and debugging tasks.
---

# RobotStudio local workflow

This is a repository-scoped Skill. Resolve the repository root three directories above this file's directory. Run `node scripts/robotstudio.mjs --help` from that root; use `--describe <command>` for exact parameters. Node.js 18+ and a running HTTP add-in are required; MCP registration and npm installation are not required for this CLI.

Use `health` and `get_station_status` to establish the active station. The default endpoint is `http://127.0.0.1:8080`; `--url` or `ROBOTSTUDIO_API_BASE` overrides it. The script must run on a host that can reach RobotStudio; a cloud container's localhost is not the user's workstation.

Pass parameters through a UTF-8 JSON file using `--params-file`. Upload source with `upload_rapid_module --code-file <file.mod>` to preserve RAPID quoting and line breaks. Commands retain the existing MCP tool names. Nonzero exit codes and `success:false` indicate failure. A timed-out write has an unknown outcome: inspect controller state before retrying; do not blindly retry uploads, I/O changes or motion.

Before changing a program, discover tasks/modules and save affected source using `get_rapid_module_source --params-file <selection.json> --output <backup.json>`. The current add-in's `replaceExisting:true` path deletes multiple program modules, not only the named module. Preserve all affected program modules before replacement, and use a disposable virtual-controller station for experiments. Do not claim automatic rollback: it is not implemented.

Inspect dimensions and coordinate units before calculating positions. Raw HTTP scene transforms/bounds use SDK units; the existing MCP presentation may convert values to millimeters. Read the actual response and source conventions instead of treating raw values as millimeters. For station-specific signals and established procedures, read [CLAUDE.md](../../../CLAUDE.md); apply those names only to the matching station. Use [RAPID examples](../../../docs/RAPID_EXAMPLES.md) when preparing programs.

Execute in observable stages, checking RAPID status, variables/I/O and new event log entries. Historical log errors do not necessarily describe the current run. Use `get_screenshot --output <new-file.png>` and open the returned absolute path with the host's image tool; do not print base64 into the conversation. Output files are never overwritten. Report observations separately from inferred success.

Simulation reset currently includes demo-specific dynamic box cleanup. Do not assume it resets arbitrary stations or restores deleted modules. Stop/start/reset operations should follow the user's requested task; this Skill does not authorize unrelated execution or real-controller use.

RobotStudio 2024 is the existing baseline. 2025 and 2026 remain unverified; see [compatibility plan](../../../docs/COMPATIBILITY_AND_AGENT_INTERFACES.md).

## Extended tools (API version 2)

Use `health` to verify `apiVersion: 2` before calling the 18 extended tools. New tool definitions live in `src/extended-tools.json`; all are shared by CLI and MCP. SDK calls compile against 2024 but require in-host verification; never present compilation as a completed robot test.

- Use `get_robot_pose` for live TCP XYZ in millimeters and quaternion; specify the frame and mechanical unit when ambiguous. Existing scene inspection still returns SDK meters.
- Before a program change, stop execution and use `save_rapid_program`; keep the returned backupId. `load_rapid_program` only restores a backup for the same controller/task and creates a recovery backup before attempting replacement. Failed restores are not automatically rolled back. Read the failure's recovery backup ID before doing anything else.
- Use `validate_rapid --code-file program.mod` for a limited lexical/structural check. Before starting a modified program, use `check_execution_ready` for current SDK compiler errors and controller prerequisites. It does not prove collision freedom or reachability; do not use historical event-log errors as current compiler diagnostics.
- Record `get_speed_settings` before changing `set_simulation_speed` or `set_speed_override`, and restore the prior setting after temporary debug changes.
- `create_target` uses mm and Euler XYZ degrees in the selected work object. New target configurations remain unverified. `create_path` and `append_path_target` change the station model with Undo support; they do not synchronize or execute RAPID. Do not claim that a station path exists in the controller until explicitly synchronized and checked.
- File tools are read-only and scoped to virtual-controller HOME; config reads allow only SYS/EIO/SIO/MOC.cfg. They do not provide arbitrary host file access or configuration writes.

Exact workflows and limitations: [extended tools](../../../docs/EXTENDED_TOOLS.md).
