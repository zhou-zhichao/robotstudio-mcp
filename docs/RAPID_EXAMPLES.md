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

## Example 4: Draw "34" — Two Numbers Side by Side (Working)

Drawing two numbers ("3" and "4") side by side on a horizontal surface using two work objects offset in Y. Combines the "3" (4 quarter-arc `MoveC`) and "4" (MoveL-only, 2 strokes) from earlier iterations into a single program.

Key design decisions:
- Two work objects offset 100mm apart in Y: `wobj3` at [400, 50, 200], `wobj4` at [400, -50, 200]
- Same robtargets reused for both numbers (work objects handle positioning)
- Robot moves home → approach wobj3 → draw 3 → approach wobj4 → draw 4 → home
- "3" uses 4 MoveC arcs, "4" uses MoveL with pen lift

```rapid
MODULE DrawModule
  TASK PERS wobjdata wobj3 := [FALSE, TRUE, "",
    [[400, 50, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  TASK PERS wobjdata wobj4 := [FALSE, TRUE, "",
    [[400, -50, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  CONST jointtarget jHome := [[0, 0, 0, 0, 30, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  CONST robtarget pApproach := [[0, 0, 80], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! === Number "3" targets (4 quarter-arcs) ===
  CONST robtarget pT1 := [[10, 75, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pT2 := [[40, 70, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pT3 := [[50, 58, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pT4 := [[40, 46, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pMid3 := [[15, 42, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pB1 := [[40, 36, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pB2 := [[50, 24, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pB3 := [[40, 12, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pB4 := [[10, 8, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pT1Up := [[10, 75, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pB4Up := [[10, 8, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! === Number "4" targets (2 strokes) ===
  CONST robtarget pA1 := [[10, 70, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pA2 := [[10, 30, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pA3 := [[50, 30, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC1 := [[40, 75, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC2 := [[40, 8, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pA1Up := [[10, 70, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pA3Up := [[40, 30, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC1Up := [[40, 75, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pC2Up := [[40, 8, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  PROC main()
    ConfL \Off;
    ConfJ \Off;
    MoveAbsJ jHome, v200, fine, tool0;

    ! Draw "3" (left side)
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj3;
    DrawThree;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj3;

    ! Draw "4" (right side)
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj4;
    DrawFour;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobj4;

    MoveAbsJ jHome, v200, fine, tool0;
  ENDPROC

  PROC DrawThree()
    MoveL pT1Up, v500, z10, tool0 \WObj:=wobj3;
    MoveL pT1, v100, fine, tool0 \WObj:=wobj3;
    MoveC pT2, pT3, v100, fine, tool0 \WObj:=wobj3;
    MoveC pT4, pMid3, v100, fine, tool0 \WObj:=wobj3;
    MoveC pB1, pB2, v100, fine, tool0 \WObj:=wobj3;
    MoveC pB3, pB4, v100, fine, tool0 \WObj:=wobj3;
    MoveL pB4Up, v500, z10, tool0 \WObj:=wobj3;
  ENDPROC

  PROC DrawFour()
    MoveL pA1Up, v500, z10, tool0 \WObj:=wobj4;
    MoveL pA1, v100, fine, tool0 \WObj:=wobj4;
    MoveL pA2, v100, fine, tool0 \WObj:=wobj4;
    MoveL pA3, v100, fine, tool0 \WObj:=wobj4;
    MoveL pA3Up, v500, z10, tool0 \WObj:=wobj4;
    MoveL pC1Up, v500, z10, tool0 \WObj:=wobj4;
    MoveL pC1, v100, fine, tool0 \WObj:=wobj4;
    MoveL pC2, v100, fine, tool0 \WObj:=wobj4;
    MoveL pC2Up, v500, z10, tool0 \WObj:=wobj4;
  ENDPROC
ENDMODULE
```

**Result:** Successful. Robot drew "3" then "4" side by side. Completed without errors, robot returned to home position (J1-J4=0, J5=30, J6=0). Enable TCP Trace in RobotStudio to visualize both numbers.

---

## Example 5: Draw Number "5" on Ground (Working)

Drawing the number "5" as a single continuous stroke: top horizontal bar, left vertical down, then a bottom C-curve (2 quarter-arcs). No pen lift needed.

Key design decisions:
- Single continuous stroke — no pen lift
- Top bar drawn right-to-left, then vertical down, then C-curve
- Bottom curve split into 2 quarter-arcs (~90° each) to stay under 240° MoveC limit
- ~40mm wide × 70mm tall character

```rapid
MODULE DrawModule
  TASK PERS wobjdata wobjGround := [FALSE, TRUE, "",
    [[400, 0, 200], [1, 0, 0, 0]],
    [[0, 0, 0], [1, 0, 0, 0]]];

  CONST jointtarget jHome := [[0, 0, 0, 0, 30, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  CONST robtarget pApproach := [[0, 0, 80], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  ! Single stroke: top bar + left vertical + bottom C-curve
  CONST robtarget pTopR := [[50, 75, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pTopL := [[10, 75, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pMidL := [[10, 42, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCV1 := [[40, 40, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCR := [[50, 28, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pCV2 := [[40, 12, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pBotL := [[10, 8, 0], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pStartUp := [[50, 75, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
  CONST robtarget pEndUp := [[10, 8, 40], [0, 0, 1, 0], [0, 0, 0, 0], [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

  PROC main()
    ConfL \Off;
    ConfJ \Off;
    MoveAbsJ jHome, v200, fine, tool0;
    MoveJ pApproach, v500, z50, tool0 \WObj:=wobjGround;
    DrawFive;
    MoveJ pApproach, v500, fine, tool0 \WObj:=wobjGround;
    MoveAbsJ jHome, v200, fine, tool0;
  ENDPROC

  PROC DrawFive()
    MoveL pStartUp, v500, z10, tool0 \WObj:=wobjGround;
    MoveL pTopR, v100, fine, tool0 \WObj:=wobjGround;
    MoveL pTopL, v100, fine, tool0 \WObj:=wobjGround;
    MoveL pMidL, v100, fine, tool0 \WObj:=wobjGround;
    MoveC pCV1, pCR, v100, fine, tool0 \WObj:=wobjGround;
    MoveC pCV2, pBotL, v100, fine, tool0 \WObj:=wobjGround;
    MoveL pEndUp, v500, z10, tool0 \WObj:=wobjGround;
  ENDPROC
ENDMODULE
```

**Drawing path:**
```
 _________
|
|
 \___
     \
 ___/
```
Top bar: (50,75)→(10,75). Left vertical: (10,75)→(10,42). Bottom curve: two quarter-arcs from (10,42) via (40,40) to (50,28) then via (40,12) to (10,8).

**Result:** Successful on first attempt. Single continuous stroke, no pen lift. Robot returned to home position. Enable TCP Trace to visualize.

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
