# RobotStudio compatibility and agent interface plan

Reviewed: 2026-09-05. Status: local Skill/CLI and configurable build/deployment implemented; host migration remains unverified.

## Implemented increment

- Repository Skill at `.agents/skills/robotstudio/SKILL.md` and standalone `scripts/robotstudio.mjs`, covering all 16 existing HTTP-backed capabilities plus health. MCP remains available. The CLI uses raw HTTP units/results, file-based arguments, finite timeouts, explicit failure exits and PNG artifacts without automatic retries or overwriting outputs.
- Year/path-aware build and deployment scripts, separate `artifacts/<year>` outputs, build metadata checks, and no automatic deployment from compilation.
- Separate experimental .NET 10 project and generated 2026 manifest. This is a starting point for migration, not a successfully compiled or tested 2026 add-in.
- Seven mock HTTP/CLI tests and Skill format validation passed. The 2024 build succeeded with installed SDK assemblies; deprecated simulation and Mastership API warnings remain. No add-in deployment or robot execution was performed during validation.

The proposals below describe the rationale and remaining host migration work. Paths and entry points mentioned as proposals are now implemented where listed above; backup/rollback automation and cross-host behavior verification remain future work.

RobotStudio 2025 and 2026 test environments are unavailable. This document separates official requirements, repository observations, and proposed solutions. Neither version is claimed as supported or tested. The existing 2024 implementation remains the baseline; this review did not rerun its historical experiments.

## Compatibility evidence

| Version | Evidence | Project status |
|---|---|---|
| 2024 | Current project targets .NET Framework 4.8 and defaults to RobotStudio 2024 assemblies. Build/deployment paths are configurable. | Existing implementation and historical experiment baseline. |
| 2025 | Elias and LiskinLabs are the selected implementation references: both use `net48` and RobotStudio 2025 host assemblies. LiskinLabs reports testing with a 2025 IRB 4600 virtual controller. | Reference-based build configuration implemented; no local 2025 build or runtime verification. |
| 2026.1+ | ABB requires .NET 10 for in-process add-ins, new SDK references, and review of removed APIs. | Requires migration; changing the installation year alone is insufficient. |

Sources: [ABB migration guide](https://developercenter.robotstudio.com/api/robotstudio/articles/Introduction/DotNetMigration.html), [Elias project configuration](https://github.com/eliasbitsch/abb-robotstudio-mcp/blob/main/addin/ClaudeBridge.csproj), [LiskinLabs project](https://github.com/LiskinLabs/abb-robotstudio-mcp).

RobotStudio SDK controls station geometry, views and simulation. PC SDK controls RAPID, I/O and controller state. Current PC SDK supports both .NET Framework 4.8 and .NET 10 for standalone applications, but add-ins hosted in RobotStudio 2026.1+ must target .NET 10. RobotStudio version and RobotWare/controller version are separate compatibility dimensions. See [PC SDK](https://developercenter.robotstudio.com/api/pcsdk/).

## 2025 adaptation based on Elias and LiskinLabs

Decision: use these two repositories as the implementation reference for 2025, as requested by the project owner. A local 2025 test environment is not required to record and ship this configuration; support claims remain limited to the available evidence.

Reviewed snapshots: Elias `41355e0` and LiskinLabs `d969a85` (retrieved 2026-09-05).

| Reference design | Application in this repository |
|---|---|
| Both target `net48`. | Keep the existing equivalent `.NET Framework v4.8` target for 2024/2025; .NET 10 is only for the separate 2026 target. |
| Both reference ABB DLLs from `RobotStudio 2025/Bin` with `Private=false`. | `-RobotStudioVersion 2025` selects that installation, with an explicit `-RobotStudioBin` override and no copy-local ABB host DLLs. |
| LiskinLabs deploys to `Bin/Addins/<addin-name>` and uses an autoload manifest. | Keep our own add-in name, existing autoload manifest and the same host-relative deployment layout. |
| LiskinLabs uses a loopback TcpListener. | Retain our existing loopback listener on port 8080 and HTTP contract, shared by CLI and MCP. |
| Both use modern C# tooling. | Discover Visual Studio MSBuild or accept `-MSBuildPath`; resolve repository paths relative to scripts. |

Deliberate differences: preserve the current project format, pinned Newtonsoft.Json dependency and only the ABB references our implementation needs. Their extra Controllers/Environment and HTTP references serve their broader implementation; adding unused dependencies or copying their complete tool surface is unnecessary for this adaptation. Build and deployment remain separate, with artifacts under `artifacts/2025` and deployment metadata checks.

```powershell
.\build.ps1 -RobotStudioVersion 2025
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
# Close RobotStudio, then install when ready:
.\deploy.ps1 -RobotStudioVersion 2025
```

Sources: [Elias build configuration](https://github.com/eliasbitsch/abb-robotstudio-mcp/blob/41355e0/addin/ClaudeBridge.csproj), [LiskinLabs build configuration](https://github.com/LiskinLabs/abb-robotstudio-mcp/blob/d969a85/addin/ClaudeBridge.csproj), [LiskinLabs installer](https://github.com/LiskinLabs/abb-robotstudio-mcp/blob/d969a85/install-addin.ps1).

Remaining evidence gap: this project's controller discovery, Mastership, module export, simulation and screenshots have not been built or exercised against a 2025 host. Reference implementations inform the design but do not prove binary or behavioral compatibility of this DLL.

## Proposed 2026 adaptation

Use an SDK-style C# project targeting `net10.0-windows`. Enable Windows Forms support because the current code uses WinForms controls and UI dispatch. Reference the .NET 10 ABB assemblies for the selected host and use compatible dependencies. The existing .NET Framework DLL must not be presented as a 2026 build.

If maintaining both generations is worthwhile, ABB documents multi-targeting with `net48;net10.0-windows`. Each target needs conditional references to its matching ABB SDK; the two outputs cannot share one unconditional set of DLL paths. Start with separate build targets rather than introducing a large abstraction framework.

Review these existing code areas first:

| Area | Proposed response if migration breaks it |
|---|---|
| `Station.Irc5Controllers` / `TryGetController` | Check the selected SDK's supported discovery API. Isolate a version-specific implementation behind the existing helper if needed. No replacement API is asserted here without verification. |
| `Mastership.Request(controller.Rapid)` | Check current PC SDK signatures and ownership behavior; use the documented replacement if obsolete members were removed. Retain guaranteed release/disposal. |
| `SaveToFile`, controller HOME paths and module loading | Recheck file location, encoding, permissions and replacement semantics with a virtual controller. Preserve BOM-free UTF-8 and explicit error reporting. |
| Screenshot and `BeginInvoke` paths | Check supported view/control access, host thread affinity and timeout behavior. Never report success before the operation has completed. |
| JSON serialization and assembly resolution | Verify dependency versions and add-in assembly loading against .NET 10; do not copy old host runtime assemblies into the new package. |

Do not invent reflection fallbacks that return success when a required property or method is unavailable. Return an explicit unsupported-capability error instead.

For distribution packages, ABB documents `RobotStudio/Add-In-net10.0/` for the new target and `RobotStudio/Add-In/` for the Framework target. For a standalone 2026 manifest, use `MinimumHostVersion` of `26.1`; generate separate metadata so older hosts do not try loading the new DLL. These are package/manifest rules, not claims that SDK DLLs moved into those folders. See [distribution package structure](https://developercenter.robotstudio.com/api/robotstudio/articles/Concepts/Distribution-Package/DP_Structure.html).

The standalone PC SDK default installation directory is `C:\Program Files (x86)\ABB\SDK\PCSDK version`. That is different from referencing assemblies in a RobotStudio installation. Resolve actual paths from the selected installation rather than assuming a universal new path. See [PC SDK installation and host assembly behavior](https://developercenter.robotstudio.com/api/pcsdk/articles/Manual/Installation-and-development-environment/Installation-overview.html).

## What can be verified without a host

- Review source and manifests against official SDK documentation.
- If matching SDK/reference assemblies become available, compile each target without deploying. Compilation alone does not establish that an add-in loads or behaves correctly.
- Test an HTTP client against recorded or synthetic responses: timeout handling, malformed JSON, application-level errors, request encoding and screenshot decoding. These tests verify the client, not RobotStudio compatibility.
- Keep per-target evidence labels: `proposed`, `compiled`, `host-loaded`, `workflow-tested`. Record exact RobotStudio, SDK and RobotWare versions when evidence becomes available.

Future host acceptance should cover add-in loading, health/status, controller discovery, joints, module source, variable/I/O reads, screenshots and scene bounds. On a disposable virtual-controller station, also verify upload/read-back, variable/I/O writes, start/stop/reset, errors and restoration of the original module. Do not promote 2025/2026 to supported until these checks pass.

## Skills and MCP: interface decision

There is no evidence in the reviewed official documentation that MCP has been universally replaced by Skills. Skills package workflow instructions, references and optional scripts; MCP provides a tool interface. Official Codex documentation supports both, including Skills that declare MCP dependencies. See [Skills](https://learn.chatgpt.com/docs/build-skills) and [MCP](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

For this repository, an MCP-free local path is technically plausible because the C# add-in already exposes HTTP independently of MCP:

```text
Existing:
Agent -> MCP server (TypeScript) -> HTTP add-in (C#) -> ABB SDK

Proposed local alternative:
Agent -> Skill instructions -> small CLI/script -> HTTP add-in (C#) -> ABB SDK
```

The alternative removes the TypeScript MCP server from that execution path, not the C# add-in or SDK dependency. A `SKILL.md` alone cannot execute a robot operation: the host must be able to run the script and reach the add-in. A cloud shell's `localhost` is not the RobotStudio workstation. Removing the add-in too would require another backend, such as RWS, with reduced station/view capabilities.

| Consideration | Skill + local script | Existing MCP |
|---|---|---|
| Local setup | No MCP server registration/process; still requires the add-in and script runtime. | Requires MCP setup and the TypeScript server. |
| Workflow knowledge | Good place for task sequence, RAPID examples and station-specific lessons. | Can be combined with the same Skill. |
| Tool contract | CLI must validate arguments and return structured errors. | Existing tool schemas, dispatch and MCP result handling. |
| Screenshots | Decode to a local image artifact and use the host's image-reading capability. | Existing screenshot handling already formats MCP image content. |
| Client portability | Depends on Skills support, shell access and local networking. | Useful for clients exposing MCP tools without arbitrary shell execution. |
| Context and latency | On-demand instructions and batched scripts may help; subprocess startup and outputs also have costs. | Costs depend on tool discovery and client behavior; not all schemas are necessarily loaded eagerly. |

No performance or reliability advantage has been measured here. Removing a protocol does not automatically improve robot task success or make version migration disappear.

## Recommended next implementation

For personal use in a local coding agent, prefer experimenting with a Skill plus one small CLI as the default entry point. Keep MCP available as an optional adapter until there is evidence that no required client uses it. Do not maintain two separate copies of controller business logic.

The CLI should cover existing HTTP capabilities, including timeouts, response `success` checks, compact JSON, nonzero failure exit codes, and screenshot-file output. Use file-based RAPID inputs to avoid shell quoting damage. Do not dump base64 images into model text. Do not automatically retry writes or motion commands after an ambiguous timeout.

The Skill should reference existing RAPID examples and generalizable lessons from `CLAUDE.md`: inspect state and object dimensions, prepare the program, preserve the original module, execute in stages, inspect errors and observations, then report the actual result. Keep station-specific signal names in an optional reference; do not turn demo-specific box cleanup into a universal reset rule. Enforce mandatory preconditions in executable code rather than relying only on prose.

First compare both entry points on the same read-only tasks (status, module source, screenshot) when a working baseline is available. Compare setup effort, successful outcomes, error clarity, context use and latency. Only then decide whether to stop maintaining MCP. The implemented increment adds the local entry point and build scaffolding; it does not claim a completed 2025/2026 migration.
