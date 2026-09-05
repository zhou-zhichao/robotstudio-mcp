# RobotStudio wordmark experiments

## Cursive version (RobotStudio 2024 / Project9)

`DrawRobotStudioCursive.mod` writes `robotstudio-mcp` using 27 Hershey strokes over a 294 mm wide area. It retains the existing IRB120 work-object placement and uses `tool0` as the simulated writing TCP. No physical pen, paper contact model or new 2025 station is imported.

Run `python experiments/readme_wordmark/generate_cursive.py` from the repository root to regenerate the RAPID module without third-party Python dependencies. The committed glyph subset is extracted from [FLo-ABB/Hershey-ABB-Robot-Handwriting](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting) at commit `7dddaf2443eb4934ea06698c306d220e72758ea2`; its source URL is recorded in `hershey-wordmark-glyphs.json`. Preserve the upstream MIT notice in `LICENSE-Hershey-ABB.txt` when reusing the glyph data.

### Station setup

Create a virtual digital output named `do_pen` (`SignalType=DO`, `Access=All`, no physical device). Restart the stopped virtual controller for the new configuration to take effect. Back up existing modules before uploading: the bridge's replacement upload removes all program modules in the selected task.

In **Simulation → TCP Trace**, choose the IRB120 and configure:

```text
[x] Enable TCP Trace
[ ] Primary color                  <- hides ordinary travel segments
[x] Clear trace at simulation start
[x] Color by signal: do_pen
    (x) Use secondary color: black
        when signal is: high       <- shows only pen-down segments
[ ] Follow moving Workobjects
```

This uses RobotStudio 2024's built-in conditional trace display, rather than requiring the upstream TraceTCP SmartComponent. Both approaches use the same pen-state principle. Configure the trace before starting simulation; RAPID `Set`/`Reset` alone does not hide travel moves.

The program reaches each stroke boundary with `fine`, changes the signal, waits 50 ms for display sampling, and retracts 10 mm along the paper work-object's positive Z before repositioning. Interior stroke points use `MoveL` with `z0`. It starts and ends with the pen signal low; the error handler also clears the output. A manual stop during a stroke may leave the signal high: clear `do_pen` before any manual repositioning.

The coordinates, joint poses and configuration handling are specific to the tested Project9 virtual controller. They are not a general deployment recipe for other robots or physical equipment.

### Original version

`DrawRobotStudioMcp.mod` remains the original geometric wordmark used in the existing README recording. The README hero now uses the owner’s 2026-09-06 recording of the cursive version. The original geometric recording remains available in Git history.

## Validation

On 2026-09-06, the program passed the virtual controller’s `CheckProgram()` with no errors, completed all 27 strokes in Project9 on RobotStudio 2024 and stopped normally. The new user recording shows the completed lettering without pen-up travel traces. Trace display settings were configured through the UI; persistence across reopening the station has not been verified.
