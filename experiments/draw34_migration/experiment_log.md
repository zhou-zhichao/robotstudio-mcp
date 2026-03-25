# Experiment: Drawing "34" Migration from IRB120 to IRB2400

## Date
2026-03-25

## Objective
Migrate a RAPID program that draws the numbers "3" and "4" from an **IRB120** (small arm used in previous experiments) to the **IRB2400_10_150** in RobotStudio. This tests whether the MCP system can seamlessly adapt existing robot programs to a completely different robot model.

## Summary
Total time: **~3 minutes** end-to-end. The AI handled the migration on the first try — no errors, no debugging needed. It queried the new robot's reach envelope, rescaled coordinates and work objects accordingly, uploaded the module, and ran it successfully. The seamless result demonstrates that the MCP system generalizes across robot models without any manual intervention.

---

## Station Information
- **Robot**: IRB2400_10_150 (reach ~1.55m, working range ~0.85-1.55m)
- **Robot Position**: Origin (0, 0, 0)
- **Station**: Project11
- **Tool**: tool0 (default flange)
- **No additional objects** in the scene (drawing in air)

---

## Original Code Analysis

### Work Objects (Original - Small Arm)
- `wobj3` (for "3"): Position `[400, 50, 200]` — 400mm in front, 50mm left
- `wobj4` (for "4"): Position `[400, -50, 200]` — 400mm in front, 50mm right

### Drawing Dimensions (Original)
- Number "3": ~50mm wide x 75mm tall (X: 10-50, Y: 8-75)
- Number "4": ~40mm wide x 67mm tall (X: 10-50, Y: 8-75)
- Approach height: Z=80mm above wobj
- Pen-up height: Z=40mm above wobj

### Motion Strategy (Original)
- `ConfL \Off; ConfJ \Off;` — configuration monitoring disabled
- Quaternion `[0,0,1,0]` — tool pointing straight down (180-degree rotation about Z)
- "3" drawn with 4 MoveC (circular interpolation) arcs
- "4" drawn with 2 linear strokes (L-shape + vertical)
- v100 for drawing, v500 for transit moves

---

## Migration Decisions & Key Ideas

### 1. Work Object Repositioning
**Problem**: X=400mm is too close for IRB2400 (inside the inner reach limit of ~850mm).
**Solution**: Moved work objects to X=1200mm, well within the optimal working envelope.
- `wobj3`: `[1200, 100, 200]` (was `[400, 50, 200]`)
- `wobj4`: `[1200, -100, 200]` (was `[400, -50, 200]`)
- Y separation doubled from 100mm to 200mm for better visibility

### 2. Drawing Scale-Up (3x)
**Rationale**: Original 50x75mm drawing would be tiny for a 1.5m-reach robot. Scaled 3x for proportional visibility.
- All XY coordinates multiplied by 3
- "3" now spans: ~150mm wide x 225mm tall
- "4" now spans: ~150mm wide x 225mm tall

### 3. Height Adjustments
- Approach height: 80mm -> 200mm (scaled ~2.5x, sufficient clearance)
- Pen-up height: 40mm -> 100mm (scaled ~2.5x)
- Drawing surface: Z=0 relative to wobj (Z=200mm absolute)

### 4. Speed & Motion Kept Unchanged
- Drawing speed v100 and transit speed v500 retained
- MoveC circular arcs preserved for "3" shape fidelity
- MoveL linear moves preserved for "4" straight strokes
- fine zone targets for drawing precision, z10/z50 for transit blending

### 5. Configuration Flexibility
- `ConfL \Off; ConfJ \Off;` retained — essential when migrating between robots with different kinematic configurations
- No confdata changes needed (all targets use default `[0,0,0,0]`)

---

## Code: Before (Original - Small Arm)

```rapid
MODULE DrawModule
    TASK PERS wobjdata wobj3 := [FALSE, TRUE, "",
        [[400, 50, 200], [1, 0, 0, 0]],
        [[0, 0, 0], [1, 0, 0, 0]]];
    TASK PERS wobjdata wobj4 := [FALSE, TRUE, "",
        [[400, -50, 200], [1, 0, 0, 0]],
        [[0, 0, 0], [1, 0, 0, 0]]];
    CONST jointtarget jHome := [[0, 0, 0, 0, 30, 0],
        [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];
    CONST robtarget pApproach := [[0, 0, 80],
        [0, 0, 1, 0], [0, 0, 0, 0],
        [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

    ! Number "3" points: X=10-50, Y=8-75, Z=0/40
    CONST robtarget pT1 := [[10, 75, 0], ...];
    ! ... (original points at 50x75mm scale)

    ! Number "4" points: X=10-50, Y=8-75, Z=0/40
    CONST robtarget pA1 := [[10, 70, 0], ...];
    ! ... (original points at 50x75mm scale)
ENDMODULE
```

## Code: After (Migrated - IRB2400)

```rapid
MODULE DrawModule
    ! Work objects moved from X=400 to X=1200 for IRB2400 reach
    ! Y separation doubled from 100mm to 200mm
    TASK PERS wobjdata wobj3 := [FALSE, TRUE, "",
        [[1200, 100, 200], [1, 0, 0, 0]],
        [[0, 0, 0], [1, 0, 0, 0]]];
    TASK PERS wobjdata wobj4 := [FALSE, TRUE, "",
        [[1200, -100, 200], [1, 0, 0, 0]],
        [[0, 0, 0], [1, 0, 0, 0]]];

    CONST jointtarget jHome := [[0, 0, 0, 0, 30, 0],
        [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

    ! Approach height scaled from 80 to 200mm
    CONST robtarget pApproach := [[0, 0, 200],
        [0, 0, 1, 0], [0, 0, 0, 0],
        [9E+09, 9E+09, 9E+09, 9E+09, 9E+09, 9E+09]];

    ! All drawing points scaled 3x
    ! Number "3": X=30-150, Y=24-225, Z=0/100
    ! Number "4": X=30-150, Y=24-225, Z=0/100
    ! (full code uploaded to RobotStudio as DrawModule)
ENDMODULE
```

---

## Execution Log (Controller Event Log)

### Timeline
| Time | Event |
|------|-------|
| 02:13:06 | User Admin logged on, Automatic mode |
| 02:14:41 | Module1 erased, DrawModule loaded (26728 bytes) |
| 02:14:51 | Program pointer reset to main |
| 02:14:52 | Motors On state |
| 02:14:53 | Regain start/ready, Program started |
| 02:15:17 | Program stopped (task ready - normal completion) |

### Execution Duration
- **Total: ~24 seconds** (02:14:53 to 02:15:17)
- No errors, warnings, or stops during execution

### Execution Status Checkpoints
| Screenshot | Time Offset | Status | Phase |
|-----------|-------------|--------|-------|
| t00 | ~0s | Running | Moving to wobj3 approach |
| t02 | ~2s | Running | DrawThree (top arc) |
| t04 | ~4s | Running | DrawThree (bottom arc) |
| t06 | ~6s | Running | DrawFour (L-shape stroke) |
| t08 | ~8s | Running | DrawFour (vertical stroke / lifting) |
| t10 | ~10s+ | Stopped | Complete, back at main |
| final | post-run | Stopped | Robot at jHome |

### Final Robot State
- Joints: J1=0.00, J2=0.00, J3=0.00, J4=0.00, J5=30.00, J6=0.00
- Matches jHome exactly - successful return to home position

---

## Scaling Reference Table

| Parameter | Original (Small Arm) | Migrated (IRB2400) | Scale Factor |
|-----------|---------------------|-------------------|-------------|
| Wobj X position | 400mm | 1200mm | 3x |
| Wobj Y separation | 100mm (50/-50) | 200mm (100/-100) | 2x |
| Wobj Z height | 200mm | 200mm | 1x |
| Drawing XY coords | 1x | 3x | 3x |
| Approach height | 80mm | 200mm | 2.5x |
| Pen-up height | 40mm | 100mm | 2.5x |
| Drawing speed | v100 | v100 | 1x |
| Transit speed | v500 | v500 | 1x |

---

## Screenshots
All saved to `experiments/draw34_migration/`:
- `screenshot_t00.png` - Robot reaching toward drawing area
- `screenshot_t02.png` - Drawing "3" (top arc)
- `screenshot_t04.png` - Drawing "3" (bottom arc, arm extended low)
- `screenshot_t06.png` - Transitioning to "4"
- `screenshot_t08.png` - Drawing "4" (arm lifted between strokes)
- `screenshot_t10.png` - Close-up after completion
- `screenshot_final.png` - Final state close-up at home

---

## Result
**SUCCESS** - The program executed without errors. The robot:
1. Started from jHome
2. Drew "3" using 4 circular arcs on wobj3 (left side)
3. Drew "4" using 2 linear strokes on wobj4 (right side)
4. Returned to jHome

The migration from a small arm to IRB2400 was accomplished by:
- Moving work objects into the IRB2400's working envelope (X: 400 -> 1200mm)
- Scaling drawing coordinates 3x for proportional visibility
- Keeping motion types, speeds, and configuration settings unchanged
