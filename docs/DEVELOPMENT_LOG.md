# Development Log

This is an honest record of building the RobotStudio MCP Server Bridge. It was developed iteratively by a human operator and Claude (Anthropic AI, model claude-opus-4-6) working together through Claude Code. Every failure, wrong assumption, and debugging detour is documented here — because that's what real development looks like.

## Timeline

### Session 1: Initial Setup (commit `3c51d1c`)

The original codebase came from a forked repo. It had the basic C# add-in structure and TypeScript MCP server, but nothing worked yet. The add-in wouldn't load in RobotStudio.

### Session 2: Fixing the Add-in Loading Failure (commit `cb973fa`)

**Problem:** The add-in showed an X mark in RobotStudio's Add-Ins manager — it failed to load silently with no useful error messages.

**Debugging journey:**

This took multiple iterations because RobotStudio gives almost zero diagnostic information when an add-in fails. Each attempt required: edit code → build → close RobotStudio → deploy DLL → reopen RobotStudio → check if the X is gone.

**Failure 1: HttpListener needs admin privileges**

The original code used `System.Net.HttpListener` to serve the REST API. On Windows, `HttpListener` requires URL ACL registration (`netsh http add urlacl`) for non-admin processes. RobotStudio runs as a normal user, so `HttpListener.Start()` throws `HttpListenerException` (Access Denied). The add-in catches this silently and fails to initialize.

> I (Claude) initially didn't realize this was the issue. I was looking at manifest problems and SDK reference issues first. The actual fix was replacing `HttpListener` with `System.Net.Sockets.TcpListener`, which can bind to `127.0.0.1` without special permissions. This meant writing a manual HTTP parser — reading request lines, headers, and body from raw TCP streams.

**Failure 2: Wrong deployment path**

We initially deployed to `%LocalAppData%\ABB\RobotStudio\Addins\` because some documentation suggested this path. RobotStudio completely ignored it. It only scans:
```
C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\
```

**Failure 3: C# language version**

The .csproj targets .NET Framework 4.8, and when building with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`, the C# compiler is version 5. I kept writing C# 6+ features (`?.`, `$""`, `out var`, `catch when`) and the build kept failing with cryptic syntax errors. Had to rewrite everything in C# 5 style:
- `?.` → explicit null checks
- `$"..."` → `string.Concat()` or `+`
- `out var x` → declare `x` separately
- `catch when (...)` → `if` inside `catch`

**Failure 4: rsaddin manifest**

The `.rsaddin` XML manifest needed `<Dependencies>Online</Dependencies>` (not `None` or `Station`) and `<Platform>Any</Platform>` to match what RobotStudio expects. Found this by examining the manifest format of official ABB add-ins.

**Resolution:** After fixing all four issues, the add-in loaded successfully (green checkmark) and the HTTP server started on port 8080.

---

### Session 3: Adding RAPID Upload & Execution (commit `91fd502`)

Added 4 new MCP tools and 4 new HTTP endpoints for RAPID program management. The C# side required understanding the ABB RobotStudio SDK's RAPID domain API. The TypeScript side was straightforward — just bridging HTTP calls.

This session was mostly successful, but the real testing happened in Session 4.

---

### Session 4: The RAPID Upload Debugging Marathon (commit `e780d0f`)

This was the hardest part. What should have been a simple "upload code and run" turned into 5+ hours of debugging. Here's every failure in order:

**Failure 1: Generic exception with no details**

First upload attempt returned: `"Exception of type 'System.Exception' was thrown."` — the most useless error message possible.

Root cause: Missing `controller.Logon(UserInfo.DefaultUser)`. The ABB SDK requires explicit authentication even for local virtual controllers. Without login, all write operations fail with a generic exception.

Fix: Added `controller.Logon(UserInfo.DefaultUser)` after `Controller.Connect()`. Also wrapped each step in separate try-catch blocks to get better error localization.

**Failure 2: PutFile doesn't work on virtual controllers**

After fixing the login, the next error was `PutFile` failing. The code was doing:
```csharp
controller.FileSystem.PutFile(tempFile, controllerFilePath, true);
```

This works on real robot controllers over network, but on virtual controllers, the HOME path is just a local Windows directory like:
```
C:/Users/sam/Documents/RobotStudio/Projects/Project7/Virtual Controllers/IRB120_3_58/HOME/
```

Fix: Replaced `PutFile()` with `File.Copy()`. Since `GetEnvironmentVariable("HOME")` returns a local path for virtual controllers, we can just copy files directly.

**Failure 3: UTF-8 BOM kills RAPID parser**

The .mod file uploaded successfully to the HOME directory, but the controller rejected it with a syntax error. After much head-scratching, I (Claude) checked the file in hex and found the first 3 bytes were `EF BB BF` — the UTF-8 BOM (Byte Order Mark).

The RAPID parser does NOT accept BOM. It treats those bytes as invalid characters at the start of the file.

Root cause: In C#, `Encoding.UTF8` includes BOM by default. You have to explicitly create `new UTF8Encoding(false)` to suppress it.

Also found that `\n` (LF) line endings cause issues — RAPID expects `\r\n` (CRLF). Added normalization:
```csharp
string normalizedCode = request.Code.Replace("\r\n", "\n").Replace("\n", "\r\n");
File.WriteAllText(tempFile, normalizedCode, new UTF8Encoding(false));
```

**Failure 4: "Global routine name main ambiguous"**

After fixing encoding, the module loaded but the program check reported errors. The human operator sent a screenshot showing TWO modules under T_ROB1 Program Modules — both `McpTest` (from an earlier failed attempt) and `Module1`, each containing a `main()` procedure.

First fix attempt: Delete the same-name module before loading:
```csharp
rapidTask.GetModule(moduleName).Delete();
```

This didn't help because the stale module had a DIFFERENT name (`McpTest` vs `Module1`).

Final fix: Delete ALL program modules before loading:
```csharp
Module[] modules = rapidTask.GetModules();
for (int m = 0; m < modules.Length; m++)
{
    string mName = modules[m].Name;
    if (mName == "BASE" || mName == "user")
        continue;  // keep system modules
    modules[m].Delete();
}
```

**Failure 5: ModuleType enum not found**

Initially tried to filter modules by type (`modules[m].Type == ModuleType.Program`), but the `ModuleType` enum isn't exposed in the referenced SDK assemblies. Changed to name-based filtering: skip "BASE" and "user" (case-insensitive), delete everything else.

**Finally working!**

After all these fixes, the full cycle worked:
```
upload → resetpp → start → (robot moves) → status: Stopped
```

The test program moved the IRB120 through three joint positions and returned to home. Joint readback confirmed the motion was correct.

---

### Session 5: PPU055 Lab Exercise Adaptation

The human operator wanted to implement a university lab exercise (PPU055 "Robotised Engraving" for IRB1400) on an IRB120 robot using the MCP tools.

**Phase 1: Understanding the exercise**

Read a 22-page PDF describing the lab. Key requirements:
- Draw a logo on paper within 80x80mm square
- Use MoveL and MoveC (at least one circular arc)
- ~20 target positions
- v100 speed on paper, pen perpendicular, 40mm above when not drawing

**Phase 2: Single pattern — Success**

Designed a star+circle pattern (5-pointed star inside a circle). First test at z=200mm with work object at [400, 0, 200] — worked perfectly on the first try. The robot traced the star (5 MoveL segments) and circle (4 MoveC quarter-arcs) smoothly.

**Phase 3: Dual pattern — Multiple failures**

The operator asked for two patterns on the ground, 180 degrees apart. This is where things got interesting.

**Failure 1: Wrist singularity at ground level (z=0)**

Moved the work objects to z=0 (ground level). Error: "Close to singularity". When the tool points straight down (`[0, 0, 1, 0]`) and the robot reaches to the ground, J5 approaches 0 degrees — the wrist singularity point.

**Failure 2: SingArea \Wrist made it worse**

Added `SingArea \Wrist;` to handle the singularity. New error: "Joint Out of Range — rob1_4 out of working range". The singularity avoidance algorithm causes J4 and J6 to spin rapidly in opposite directions, and J4 exceeded its ±160 degree limit.

**Failure 3: Tilting the tool orientation didn't help enough**

Changed tool orientation from `[0, 0, 1, 0]` (straight down) to `[0.131, 0, 0.991, 0]` (15 degrees tilt from vertical). Still got J4 out of range. The tilt wasn't enough to move J5 away from the singularity zone.

**Failure 4: Raising to z=100 — still failed**

Even at z=100mm (not ground level), the same J4 error occurred. The SingArea instruction was the main culprit.

**Failure 5: z=200 with rotated wobj — still failed!**

This was surprising. Went back to z=200 (which worked for the single pattern), removed SingArea, but added wobj rotation for the 180-degree-opposite pattern. Error: J4 out of range.

Root cause: The second work object was rotated 180 degrees about Z (`orientation: [0, 0, 0, 1]`). This caused the tool orientation in world frame to change from "180 about Y" to "180 about X" — a completely different wrist configuration requiring J4 to rotate ~180 degrees to transition, exceeding ±160 degree limits.

**Failure 6: Even without rotation — still failed!**

Changed both work objects to identity orientation (no rotation), just offset in Y. STILL failed with J4 out of range.

Root cause discovery: Checked the joint positions and found J4=159.43 degrees, J5=2.7 degrees. The robot was STUCK in a bad configuration from the previous failed run! Every subsequent program start tried to move from this bad position, immediately hitting the J4 limit.

**Final fix: MoveAbsJ to home first**

Added `MoveAbsJ [[0, 0, 0, 0, 30, 0], ...], v200, fine, tool0;` as the very first instruction. This moves the robot to a safe home position using joint-space interpolation (no Cartesian path, no singularity issues) before doing anything else.

Both patterns then executed successfully.

**Key lesson:** After a failed run leaves the robot in an unknown/bad configuration, you MUST start the next program with MoveAbsJ to a known safe joint position. MoveJ or MoveL from a bad configuration will just fail again.

---

### Session 6: Drawing Numbers on the Ground

The operator provided a RAPID program (from a PPU055 lab exercise) that draws the number "2" on a vertical surface using a pen tool, gripper, and custom work objects. The task was to adapt it to draw numbers on a horizontal "ground" surface in simulation using the MCP tools. The number was iterated through "2" → "3" → "4".

**Phase 1: Adapting the original program**

The original program used:
- Custom tool `PENNTCP` with specific TCP offsets and 45-degree tilt
- Pen pick/place routines (`HamtaPenna`/`LamnaPenna`) with digital output `grip1`
- Two work objects (`REFRAM_PAPPER` for pen station, `REFRAM_RUTA2` for drawing)
- Drawing in the Y-Z plane of the work object, with X as the approach direction

For simulation on the ground, we:
- Replaced `PENNTCP` with `tool0`
- Removed pen pick/place routines and signal handling
- Used proven work object settings from earlier sessions: `[400, 0, 200]` with identity orientation
- Used `[0, 0, 1, 0]` target orientation (tool pointing down)
- Added `MoveAbsJ jHome`, `ConfL \Off`, `ConfJ \Off` per established best practices

**Phase 2: Drawing "2" — heart shape mistake then fix**

First attempt to draw a "2" produced a heart/leaf shape due to a closed-loop path. Redesigned as 3 open strokes (semicircle + diagonal + baseline) and it worked.

**Phase 3: Changed to "3" — MoveC >240° error**

Designed "3" as two C-shaped arcs (top and bottom halves), each a single MoveC. The bottom arc failed with "Circle uncertain — Circle too large > 240 degrees." A C-shape (left→right→left) inherently spans >240° when the bulge is wide enough. Fixed by splitting each arc into two quarter-arcs (~90° each).

**Phase 4: Changed to "4" — success on first attempt**

The "4" uses only MoveL (no arcs), with two strokes and a pen lift between them: an L-shape (vertical down + horizontal crossbar) and a full-height vertical line on the right side. Ran successfully on the first attempt.

**Phase 5: Combined "34" — two numbers side by side**

Combined "3" and "4" into a single program using two work objects offset 100mm apart in Y: `wobj3` at [400, 50, 200] and `wobj4` at [400, -50, 200]. The robot draws "3" first, transitions via approach point and MoveJ to the second work object, then draws "4". Ran successfully on the first attempt.

**Phase 6: Drawing "5" — success on first attempt**

The "5" is a single continuous stroke (no pen lift): top horizontal bar right-to-left, vertical down on the left, then a bottom C-curve split into 2 quarter-arcs. Reuses the same arc-splitting pattern from the "3". Ran successfully on the first attempt.

**Technical details of MCP interaction:**
- Upload via `POST /rapid/upload` using Node.js to properly handle RAPID backslash escaping (`\Off`, `\WObj`) in JSON
- Direct `curl` failed with "Bad JSON escape sequence: \O" — Newtonsoft.Json on the C# side rejects `\O` as an invalid JSON escape
- Workaround: Write RAPID code to a .mod file, read it with Node.js `fs.readFileSync()`, and use `JSON.stringify()` for proper escaping
- Execution via `POST /rapid/execute` with resetpp → start (cycle: once)
- Status confirmed via `GET /rapid/status` and `GET /rapid/errors`

---

### Session 7: First Green Box on Orange Side Experiment

**Goal:** Modify the existing pick & place program so the first green (large/gr) box is placed on the orange (small/pq) side instead of its normal location.

**Understanding the original code:**

The program (Module1 + CalibData) implements a conveyor pick & place system:
- `create_box` generates 3 small (orange) + 3 large (green) boxes alternately on a conveyor
- Sensors detect box type: `DI_Sensor_Inf=1, DI_Sensor_Sup=0` → small (orange), `DI_Sensor_Inf=1, DI_Sensor_Sup=1` → large (green)
- Small boxes picked at z=80mm, placed at `WO_Place_pq` [522, -650, -259]
- Large boxes picked at z=180mm, placed at `WO_Place_gr` [566, 1365, -259]
- Stacking offsets: `off_pq` increments by `height_pq=100`, `off_gr` increments by `height_gr=200`

**New feature — used `GET /rapid/source` endpoint:**

This session was the first to use the newly added `GET /rapid/modules` and `POST /rapid/source` endpoints to read RAPID code directly from the virtual controller. This allowed reading both Module1 and CalibData source code without needing the human operator to copy-paste.

**Code modification:**

Added a `VAR num green_count:=0;` counter and modified `PathCaja_gr`:
```rapid
PROC PathCaja_gr()
    Path_Pick_gr;
    IF green_count = 0 THEN
        ! EXPERIMENT: First green box goes to orange (pq) side
        Path_Place_pq;
    ELSE
        Path_Place_gr;
    ENDIF
    green_count:=green_count+1;
ENDPROC
```

**Failure 1: Upload deleted CalibData**

The `/rapid/upload` endpoint deletes ALL non-system modules before loading the new one. This means uploading Module1 also deleted CalibData (which contained `TCP_VentosaTool`, `WO_Pick`, `WO_Place_pq`, `WO_Place_gr`). Module1 loaded but had RAPID errors because all tool/wobj references were undefined.

Error message: `"Errors in RAPID program: Task T_ROB1: There are errors in the RAPID program."`

**Fix:** Merged CalibData declarations directly into Module1:
```rapid
PERS tooldata TCP_VentosaTool:=[TRUE,[[0,0,184],[1,0,0,0]],[1,[0,-0.818,79.529],[1,0,0,0],0,0,0]];
TASK PERS wobjdata WO_Pick:=[FALSE,TRUE,"",[[846,535,176],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
TASK PERS wobjdata WO_Place_pq:=[FALSE,TRUE,"",[[522,-650,-259],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
TASK PERS wobjdata WO_Place_gr:=[FALSE,TRUE,"",[[566,1365,-259],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
```

**Attempt 2: Upload merged module — success**

With all declarations in a single Module1, the upload succeeded. The program ran through:
1. Created 3 small + 3 large boxes on conveyor
2. First orange box → picked → placed on pq side ✓
3. First green box → picked → **placed on pq side** (experiment!) ✓
4. Subsequent orange boxes → placed on pq side ✓
5. Subsequent green boxes → placed on gr side (normal behavior) ✓

The operator confirmed: "已经成功运行了" (successfully running).

**Observation:** The `Path_Place_pq` uses `off_pq` offset and increments by `height_pq=100`. Since the green box (height 200mm) was placed with a 100mm offset increment, the stacking height calculation may not be physically accurate for the green box. However, the robot motion executed without errors.

**Failure 2: Green box knocks orange box off — release too deep (+40)**

Using the original `Path_Place_pq` (release clearance +40mm), the green box (200mm tall) descends too far. Its bottom hits the already-placed orange box and pushes it off the shelf. The +40 clearance was calibrated for the small 100mm box.

**Failure 3: Added +100mm extra offset to off_pq — J3 out of range**

Attempted to raise the placement by adding `off_pq := off_pq + (height_gr - height_pq)` (+100mm) before calling `Path_Place_pq`. This pushed the release point to z=484 in the work object frame. Error: "Position outside reach — Joint 3 outside working area." The pq placement location is near the edge of the IRB120's workspace, and the extra height exceeded J3 limits.

**Failure 4: Changed create_box WaitTime from 2→5 — robot stops picking**

Increased WaitTime between box creations to prevent conveyor overlap. But `create_box` now took 30 seconds (instead of 12), and boxes arrived at the sensor during `create_box` before the WHILE loop started. They passed through without being detected. The robot never entered the picking loop.

**Failure 5: Restructured to sequential create-one-pick-one — lost parallelism**

Changed main() to create one box → WaitUntil sensor → pick, repeating in a FOR loop. This eliminated overlapping but also eliminated the conveyor parallelism. The operator wanted box creation and picking to happen concurrently (original behavior).

**Failure 6: Restored original create_box — conveyor box overlap returned**

Reverted to original `create_box` (WaitTime 2) + WHILE TRUE loop. Boxes overlapped again on the conveyor after simulation reset. This appeared to be a simulation state issue — the conveyor speed or Smart Component state was inconsistent after multiple resets and module reloads.

**Failure 7: Green box release at +90mm — suction cup doesn't release**

Created a dedicated `Path_Place_gr_on_pq` procedure with +90mm release clearance (instead of +40). The green box stayed attached and was carried back to the conveyor. **Root cause (corrected):** +90mm is still far too low for a 200mm green box. The TCP needs to be at `off_pq+140` to reach the box top surface. At +90, the TCP is still 50mm inside the box mesh — the Smart Component cannot release when the suction cup is embedded inside the geometry.

**Failure 8: +60mm release — suction cup still doesn't release**

Same root cause as Failure 7. At +60, the TCP is 80mm inside the 200mm green box. Even +40 (which barely works for 100mm orange) leaves the TCP 100mm inside the green box. The correct offset for green on pq side is **+140** (= +40 + height difference of 100mm).

**Attempt 9: Reverted to original +40mm — green box placed successfully!**

Went back to using the original `Path_Place_pq` with +40mm clearance. The green box was successfully placed on the orange (pq) side — the suction released and the box stayed.

**Failure 9: Second orange box placed too low — clipping through green box**

After the green box was placed, `off_pq` was only incremented by `height_pq` (100mm) instead of `height_gr` (200mm). The second orange box was placed at a height that assumed a 100mm-tall box below it, but the green box is 200mm tall. The orange box clipped through the top of the green box.

```rapid
PROC Path_Place_gr_on_pq()
    MoveJ Target_60_pq,v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveL offs(Target_50_pq,0,0,off_pq),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveLDO offs(Target_40_Place_pq,0,0,off_pq+60),v500,fine,TCP_VentosaTool\WObj:=WO_Place_pq,DO_Ventosa,0;
    WaitTime 1;
    MoveL offs(Target_50_pq,0,0,off_pq),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    off_pq:=off_pq+height_gr;
    MoveL Target_60_pq,v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveJ HOME,v1000,z100,TCP_VentosaTool\WObj:=wobj0;
ENDPROC
```

**Failure 10: Added off_pq correction AFTER Path_Place_pq — green box won't release again**

After Failure 9, added `off_pq := off_pq + (height_gr - height_pq)` AFTER `Path_Place_pq` in `PathCaja_gr` (green_count=0 branch). This line only affects the NEXT box's placement height, not the current release point. However, the green box again failed to detach from the suction cup. The robot carried it back without releasing.

This is puzzling because the identical `Path_Place_pq` with +40mm clearance worked in Attempt 9. The only code difference is the extra offset line AFTER the placement call. Possible causes:
- Simulation state inconsistency after reset (suction cup Smart Component may behave differently across resets)
- The added line itself is not the cause — this may be a non-deterministic simulation issue with the suction release at +40mm being at the edge of the contact threshold

```rapid
PROC PathCaja_gr()
    Path_Pick_gr;
    IF green_count = 0 THEN
        ! EXPERIMENT: First green box goes to orange (pq) side
        Path_Place_pq;
        ! Correct offset: green box is 200mm tall, Path_Place_pq only added 100mm
        off_pq:=off_pq+(height_gr-height_pq);
    ELSE
        Path_Place_gr;
    ENDIF
    green_count:=green_count+1;
ENDPROC
```

**Attempt 11: Release offset +140mm — SUCCESS ✓**

After the human operator corrected the AI's misunderstanding of the failure mechanism, the root cause became clear: the TCP (suction cup) was ending up **inside the box mesh** at release time. The fix was purely geometric:

- Orange box (100mm) on pq side: `+40` works → TCP at box top ✓
- Green box (200mm) on gr side: `+40` works → Target_40_Place_gr base Z is 100mm higher (344 vs 244), compensating for the taller box ✓
- Green box (200mm) on pq side: needs `+140` = `+40 + (200-100)` → TCP at box top ✓

All previous attempts (+5, +40, +60, +90) failed because the TCP was 50–135mm inside the green box mesh. With +140, the TCP is exactly at the green box top surface, and the Smart Component releases cleanly.

```rapid
PROC Path_Place_gr_on_pq()
    ! Green box (200mm) on pq side: release offset = +140mm
    ! Formula: orange +40 works for 100mm box. Green needs +40+(200-100)=+140
    ! so TCP is at the box top surface, not inside the box mesh.
    MoveJ Target_60_pq,v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveL offs(Target_50_pq,0,0,off_pq),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveLDO offs(Target_40_Place_pq,0,0,off_pq+140),v500,fine,TCP_VentosaTool\WObj:=WO_Place_pq,DO_Ventosa,0;
    WaitTime 1;
    MoveL offs(Target_50_pq,0,0,off_pq),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    off_pq:=off_pq+height_gr;
    MoveL Target_60_pq,v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
    MoveJ HOME,v1000,z100,TCP_VentosaTool\WObj:=wobj0;
ENDPROC
```

The original `create_box` order (orange first) was restored — the issue was never about box arrival order, but about the release height calculation.

**Lessons learned:**
1. The upload endpoint's "delete all modules" behavior is destructive — must merge or upload modules in dependency order
2. Reading RAPID source via API (`/rapid/source`) is much faster than manual copy-paste for understanding existing code
3. Simple IF/counter logic in RAPID works well for conditional placement behavior
4. When merging modules, `TASK PERS` and `PERS` declarations can coexist in a single module
5. ~~RobotStudio suction cup Smart Components require physical surface contact to release — releasing in mid-air keeps the box attached~~ **CORRECTED:** The real issue is that when the TCP (suction cup) descends too low, it ends up **physically inside the box mesh**. The Smart Component cannot release the suction when the cup is embedded inside the geometry. The box doesn't need to "touch the surface" — the TCP just needs to be **at or above the box top surface** when releasing.
6. Placement clearance values are tightly coupled to box dimensions — the release offset must account for box height so the TCP stays at the box top surface. Formula: for pq side, orange (100mm) uses +40, green (200mm) needs +40+(200-100)=**+140**. For gr side, +40 already works for 200mm green because Target_40_Place_gr has a higher base Z (344 vs 244).
7. The create_box timing and conveyor parallelism are tightly coupled — `create_box` must finish quickly so the WHILE sensor loop starts before boxes pass the sensor
8. When the pq placement is near the robot's workspace boundary, adding height offsets can push J3 out of range — there is a narrow window between "too deep" and "out of reach"

---

## Lessons Learned

### About RobotStudio SDK

1. `controller.Logon(UserInfo.DefaultUser)` is mandatory for any write operation
2. `Mastership.Request(controller.Rapid)` is required for RAPID domain modifications
3. Virtual controllers use local file paths — `File.Copy()` works, `PutFile()` doesn't
4. RAPID files must be UTF-8 without BOM, with CRLF line endings
5. `LoadModuleFromFile` with `RapidLoadMode.Replace` does NOT replace — it adds alongside
6. Always delete existing modules before loading to avoid name conflicts
7. The `ModuleType` enum is not accessible in the SDK assemblies we referenced

### About RAPID Programming

1. Wrist singularity (J5=0) is a real hazard when tool points straight down
2. `SingArea \Wrist` can make singularity worse by spinning J4 past its limits
3. Always start programs with `MoveAbsJ` to a known safe joint position
4. `ConfL \Off; ConfJ \Off;` disables configuration checking (useful for simulation)
5. Work object rotation changes tool orientation in world frame — different wrist configuration needed
6. After a failed run, the robot retains its bad joint positions — must recover first

### About RAPID Upload via JSON

1. RAPID uses backslash for optional arguments (`\Off`, `\WObj`) — these conflict with JSON escape sequences
2. `curl` with inline JSON fails because `\O` and `\W` are invalid JSON escapes
3. Best approach: write RAPID code to a .mod file, then use Node.js `JSON.stringify()` to encode it properly
4. When adapting drawing programs between surface orientations, redesign the stroke path — don't just remap coordinates
5. `MoveC` arcs cannot exceed 240 degrees — C-shaped arcs (left→right→left) must be split into two sub-arcs at the apex

### About the Build System

1. MSBuild v4.0 only supports C# 5 syntax — no modern features
2. Post-build deployment to Program Files needs admin rights — use separate deploy script
3. RobotStudio locks the DLL — must close RS before deploying, reopen after
4. Each debug cycle (edit → build → close RS → deploy → reopen RS → test) takes 2-3 minutes

### About Working with AI

This project was built entirely through conversation — a human operator running RobotStudio and an AI (Claude) writing code, reading errors, and iterating. The AI had no visual access to RobotStudio and relied entirely on:
- HTTP API responses (JSON)
- Event log error messages
- Joint position readbacks
- Screenshots sent by the human (for the RAPID editor structure)

Most debugging required the human to close and reopen RobotStudio repeatedly, which was the main bottleneck. The AI could write and iterate on code quickly, but each test cycle required human action.

## Tools & Environment

- **AI**: Claude Opus 4.6 via Claude Code
- **IDE**: None (all code written through Claude Code terminal)
- **Robot**: ABB IRB120 3.58 (virtual controller in RobotStudio 2024)
- **Build**: MSBuild v4.0 (.NET Framework 4.8)
- **OS**: Windows 10 Pro
