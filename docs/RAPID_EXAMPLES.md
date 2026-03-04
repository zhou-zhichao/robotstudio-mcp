# RAPID Examples

These are RAPID programs that were actually tested on an IRB120 3.58 virtual controller via the MCP server. Each example includes what worked, what didn't, and why.

## Example 1: Simple Joint Motion (Working)

The first program that ran successfully. Three joint positions with MoveAbsJ.

```rapid
MODULE Module1
  PROC main()
    MoveAbsJ [[20,10,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;
    MoveAbsJ [[-20,-10,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;
    MoveAbsJ [[0,0,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;
  ENDPROC
ENDMODULE
```

**Upload command:**
```bash
curl -X POST http://localhost:8080/rapid/upload \
  -H "Content-Type: application/json" \
  -d '{"code":"MODULE Module1\n  PROC main()\n    MoveAbsJ [[20,10,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;\n    MoveAbsJ [[-20,-10,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;\n    MoveAbsJ [[0,0,0,0,30,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]], v200, fine, tool0;\n  ENDPROC\nENDMODULE","moduleName":"Module1","taskName":"T_ROB1","replaceExisting":true}'
```

**Result:** Robot moved J1 to +20, J2 to +10, then J1 to -20, J2 to -10, then returned to origin.

---

## Example 2: Star + Circle Pattern (Working)

Single pattern drawing using MoveL and MoveC. A 5-pointed star inscribed in a circle, drawn on a virtual "paper" plane.

Key design decisions:
- Work object at [400, 0, 200] — 400mm forward, 200mm above base
- Tool orientation `[0, 0, 1, 0]` — tool pointing straight down (180 deg about Y axis)
- Star radius 35mm (5 MoveL), circle radius 40mm (4 MoveC quarter-arcs)
- v100 for drawing, v500 for approach/retract

```rapid
MODULE DrawModule
  TASK PERS wobjdata wobjPaper := [FALSE, TRUE, "",
    [[400, 0, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  CONST robtarget pApproach := [[0, 0, 80], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! Star vertices (5-pointed, radius 35mm)
  CONST robtarget pS1 := [[0, 35, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pS2 := [[33.3, 10.8, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pS3 := [[20.6, -28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pS4 := [[-20.6, -28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pS5 := [[-33.3, 10.8, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pS1up := [[0, 35, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! Circle points (radius 40mm)
  CONST robtarget pC1 := [[0, 40, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC2 := [[40, 0, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC3 := [[0, -40, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC4 := [[-40, 0, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  ! Circle midpoints for MoveC via-points
  CONST robtarget pCM12 := [[28.3, 28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCM23 := [[28.3, -28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCM34 := [[-28.3, -28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCM41 := [[-28.3, 28.3, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC1up := [[0, 40, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  PROC main()
    ConfL \Off;
    ConfJ \Off;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobjPaper;
    DrawStar;
    DrawCircle;
    MoveJ pApproach, v500, fine, tool0 \WObj:=wobjPaper;
  ENDPROC

  PROC DrawStar()
    MoveL pS1up, v500, z10, tool0 \WObj:=wobjPaper;
    MoveL pS1, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS3, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS5, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS2, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS4, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS1, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pS1up, v500, z10, tool0 \WObj:=wobjPaper;
  ENDPROC

  PROC DrawCircle()
    MoveL pC1up, v500, z10, tool0 \WObj:=wobjPaper;
    MoveL pC1, v100, fine, tool0 \WObj:=wobjPaper;
    MoveC pCM12, pC2, v100, fine, tool0 \WObj:=wobjPaper;
    MoveC pCM23, pC3, v100, fine, tool0 \WObj:=wobjPaper;
    MoveC pCM34, pC4, v100, fine, tool0 \WObj:=wobjPaper;
    MoveC pCM41, pC1, v100, fine, tool0 \WObj:=wobjPaper;
    MoveL pC1up, v500, z10, tool0 \WObj:=wobjPaper;
  ENDPROC
ENDMODULE
```

**Result:** Successful. Robot traced a 5-pointed star then drew a circle around it. Enable TCP Trace in RobotStudio to see the pattern.

---

## Example 3: Dual Pattern with Home Recovery (Working)

Two identical star+circle patterns side by side. The key addition is `MoveAbsJ` at the start to recover from any previous bad joint configuration.

```rapid
MODULE DrawModule
  TASK PERS wobjdata wobj1 := [FALSE, TRUE, "",
    [[380, 80, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  TASK PERS wobjdata wobj2 := [FALSE, TRUE, "",
    [[380, -80, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  CONST jointtarget jHome := [[0, 0, 0, 0, 30, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! ... (same robtargets as Example 2) ...

  PROC main()
    ConfL \Off;
    ConfJ \Off;

    ! CRITICAL: Always start from known safe position
    MoveAbsJ jHome, v200, fine, tool0;

    ! Pattern 1 (left)
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj1;
    DrawStar wobj1;
    DrawCircle wobj1;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj1;

    ! Pattern 2 (right)
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj2;
    DrawStar wobj2;
    DrawCircle wobj2;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj2;

    MoveAbsJ jHome, v200, fine, tool0;
  ENDPROC

  PROC DrawStar(PERS wobjdata wobj)
    MoveL pS1up, v500, z10, tool0 \WObj:=wobj;
    MoveL pS1, v100, fine, tool0 \WObj:=wobj;
    MoveL pS3, v100, fine, tool0 \WObj:=wobj;
    MoveL pS5, v100, fine, tool0 \WObj:=wobj;
    MoveL pS2, v100, fine, tool0 \WObj:=wobj;
    MoveL pS4, v100, fine, tool0 \WObj:=wobj;
    MoveL pS1, v100, fine, tool0 \WObj:=wobj;
    MoveL pS1up, v500, z10, tool0 \WObj:=wobj;
  ENDPROC

  PROC DrawCircle(PERS wobjdata wobj)
    MoveL pC1up, v500, z10, tool0 \WObj:=wobj;
    MoveL pC1, v100, fine, tool0 \WObj:=wobj;
    MoveC pCM12, pC2, v100, fine, tool0 \WObj:=wobj;
    MoveC pCM23, pC3, v100, fine, tool0 \WObj:=wobj;
    MoveC pCM34, pC4, v100, fine, tool0 \WObj:=wobj;
    MoveC pCM41, pC1, v100, fine, tool0 \WObj:=wobj;
    MoveL pC1up, v500, z10, tool0 \WObj:=wobj;
  ENDPROC
ENDMODULE
```

**Result:** Successful after adding `MoveAbsJ jHome` at the start. Without it, the robot was stuck at J4=159.4 degrees from previous failed runs and every new program immediately hit joint limits.

---

## Failed Attempts (for reference)

### Ground-level drawing (z=0) — FAILED

Placing work objects at z=0 causes wrist singularity. The tool orientation `[0, 0, 1, 0]` (pointing down) makes J5 approach 0 degrees.

### SingArea \Wrist — MADE IT WORSE

Adding `SingArea \Wrist;` to handle singularity caused J4 to spin past ±160 degree limits. The singularity avoidance algorithm's compensation was too aggressive for the IRB120's joint range.

### Work object rotated 180 degrees about Z — FAILED

Using `wobjdata` orientation `[0, 0, 0, 1]` (180 deg about Z) changes the tool orientation in world frame. The resulting wrist configuration requires J4 to be near its limits. Even at z=200mm (which works without rotation), adding the 180-degree wobj rotation caused J4 out of range errors.

### Tilted tool orientation — INSUFFICIENT

Changing orientation from `[0, 0, 1, 0]` to `[0.131, 0, 0.991, 0]` (15 deg tilt) was not enough to move J5 away from the singularity zone at ground level.

---

## Best Practices (learned the hard way)

1. **Always start with `MoveAbsJ` to a known safe position** — joint-space motion has no singularity issues
2. **Keep z >= 200mm for IRB120** when tool points straight down
3. **Use `ConfL \Off; ConfJ \Off;`** to avoid configuration errors in simulation
4. **Don't rotate work objects 180 degrees** about Z when tool points down — use position offsets instead
5. **Don't use `SingArea \Wrist`** on IRB120 when operating near the wrist singularity — it causes J4 overflow
6. **Use PERS wobjdata parameter** to pass work objects to procedures (`PROC DrawStar(PERS wobjdata wobj)`)
7. **MoveC syntax**: `MoveC via_point, end_point, speed, zone, tool \WObj:=wobj` — the via-point defines the arc
