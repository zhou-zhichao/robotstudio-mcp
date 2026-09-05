# Community scenarios

[Back to README](../README.md) · [中文首页](../README.zh-CN.md)

A curated catalog of third-party RobotStudio stations, reviewed on 2026-09-06. These are external scenario candidates, not validated examples of this bridge. Package paths were checked through the GitHub tree API; only the handwriting package was downloaded and inspected. None of these five stations was executed here.

The links below pin the reviewed upstream commits. Download the desired `.rspag` from its file page and open it with RobotStudio’s **File → Open / Unpack & Work** workflow, using a separate destination folder. Check the required RobotWare and controller options before restoring a virtual controller. Our bridge currently requires exactly one virtual controller for extended controller operations, so multi-controller stations need further adaptation.

This catalog links upstream packages rather than redistributing them. Upstream assets retain their original licensing; this repository’s MIT license does not relicense those assets. The coffee project is excluded from this collection.

## Sorting production line

Two robots exchange parts through a conveyor, with IRB1200 and GoFa variants. Includes a robot-free template and RAPID modules.

- Source and credit: [rparak/ABB-RobotStudio-SortingProductionLine](https://github.com/rparak/ABB-RobotStudio-SortingProductionLine).
- Reviewed commit: [`3affa542803b`](https://github.com/rparak/ABB-RobotStudio-SortingProductionLine/commit/3affa542803b2bcbd548baf3bf60c4496825432c).
- License reported by GitHub: MIT.
- Compatibility: Upstream README: RobotStudio 2021.1.2; RobotWare 6.10.0 (IRB1200) or 7.2.1 (GoFa).
- Candidate tests: controller selection, conveyor I/O, gripper state and inter-robot coordination.

Packages:

- [Solution_CRB_15000_GoFa/Final/Solution_Sorting_Production_Line.rspag](https://github.com/rparak/ABB-RobotStudio-SortingProductionLine/blob/3affa542803b2bcbd548baf3bf60c4496825432c/Solution_CRB_15000_GoFa/Final/Solution_Sorting_Production_Line.rspag)
- [Solution_IRB_1200/Final/Solution_Sorting_Production_Line.rspag](https://github.com/rparak/ABB-RobotStudio-SortingProductionLine/blob/3affa542803b2bcbd548baf3bf60c4496825432c/Solution_IRB_1200/Final/Solution_Sorting_Production_Line.rspag)
- [Template/Solution_Sorting_Production_Line_Template.rspag](https://github.com/rparak/ABB-RobotStudio-SortingProductionLine/blob/3affa542803b2bcbd548baf3bf60c4496825432c/Template/Solution_Sorting_Production_Line_Template.rspag)

## YuMi Tower of Hanoi

YuMi moves rings between three towers. Includes two methods, a template, RAPID modules and a Python solver.

- Source and credit: [rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi](https://github.com/rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi).
- Reviewed commit: [`5dae985ccf11`](https://github.com/rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi/commit/5dae985ccf11b5c14b27a56c0a2381d0c521d072).
- License reported by GitHub: MIT.
- Compatibility: Upstream README: RobotStudio 2022.3; RobotWare 6.13.03.
- Candidate tests: rule-constrained planning, left/right task selection, synchronization and final-state verification.

Packages:

- [RSPAG/Method_1/ABB_YUMI_Tower_of_Hanoi.rspag](https://github.com/rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi/blob/5dae985ccf11b5c14b27a56c0a2381d0c521d072/RSPAG/Method_1/ABB_YUMI_Tower_of_Hanoi.rspag)
- [RSPAG/Method_2/ABB_YUMI_Tower_of_Hanoi.rspag](https://github.com/rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi/blob/5dae985ccf11b5c14b27a56c0a2381d0c521d072/RSPAG/Method_2/ABB_YUMI_Tower_of_Hanoi.rspag)
- [Template/ABB_YUMI_Tower_of_Hanoi_Template.rspag](https://github.com/rparak/ABB-RobotStudio-YUMI-Tower-of_Hanoi/blob/5dae985ccf11b5c14b27a56c0a2381d0c521d072/Template/ABB_YUMI_Tower_of_Hanoi_Template.rspag)

## Dual IRB2600 assembly cell

Two IRB2600ID robots perform pick-and-place, simulated welding and quality-based sorting; one uses a linear external axis. The camera inspection outcome is a random variable, not a vision implementation.

- Source and credit: [jorgeserranoo/abb-irb2600-robotic-assembly-cell](https://github.com/jorgeserranoo/abb-irb2600-robotic-assembly-cell).
- Reviewed commit: [`95400344f83a`](https://github.com/jorgeserranoo/abb-irb2600-robotic-assembly-cell/commit/95400344f83afc5391ac80700044915a9e79f080).
- License reported by GitHub: not detected; no redistribution permission is inferred.
- Compatibility: Upstream README recommends RobotStudio 2021 or later; required RobotWare has not been independently established.
- Candidate tests: external-axis targets, tool/work-object changes, I/O handshakes and workflow branches.

Packages:

- [RapidProject.rspag](https://github.com/jorgeserranoo/abb-irb2600-robotic-assembly-cell/blob/95400344f83afc5391ac80700044915a9e79f080/RapidProject.rspag)

## FlexPicker sorting and stacking

An IRB360 FlexPicker sorts three part types into separate three-dimensional stacks. The package includes the conveyor, parts and RAPID program.

- Source and credit: [andyzaur/ConveyorBelt-FlexPicker](https://github.com/andyzaur/ConveyorBelt-FlexPicker).
- Reviewed commit: [`ba29e9868a9e`](https://github.com/andyzaur/ConveyorBelt-FlexPicker/commit/ba29e9868a9e6fa136e195d849ad4b583c3654d5).
- License reported by GitHub: not detected; no redistribution permission is inferred.
- Compatibility: Upstream README says RobotStudio 6.x or later; exact RobotWare requirements remain to be checked.
- Candidate tests: part classification, stacking dimensions and signal-driven part generation.

Packages:

- [Project2.rspag](https://github.com/andyzaur/ConveyorBelt-FlexPicker/blob/ba29e9868a9e6fa136e195d849ad4b583c3654d5/Project2.rspag)

## Hershey handwriting

Hershey cursive vector strokes are converted to RAPID character procedures, with scale, character advance and pen-state signals.

- Source and credit: [FLo-ABB/Hershey-ABB-Robot-Handwriting](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting).
- Reviewed commit: [`7dddaf2443eb`](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/commit/7dddaf2443eb4934ea06698c306d220e72758ea2).
- License reported by GitHub: MIT.
- Compatibility: Downloaded station XML has SaveVersion="25.2.11266.1" (RobotStudio 2025.2). Do not assume that the packaged station opens in 2024. Rebuilding a 2024 station using the source is a separate, untested adaptation.
- Candidate tests: text generation, stroke boundaries, pen-state-controlled trace and drawing scale.

Packages:

- [Simulation/HandWriting.rspag](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/blob/7dddaf2443eb4934ea06698c306d220e72758ea2/Simulation/HandWriting.rspag)

## How handwriting hides pen-up motion

The result is controlled during simulation, not cleaned up in video editing:

```text
RAPID Set / Reset do_pen
          |
          v
Station I/O connection: controller.do_pen -> TraceTCP.Enabled
          |
          v
Draw the active TCP trace only while Enabled = 1
```

The inspected `Simulation/HandWriting.rspag` contains:

- `Station/Project22.rsstnx`: an I/O connection from `IRB1200_5_90.do_pen` to the `TraceTCP` SmartComponent’s `Enabled` input; its `Robot` property references the station robot. The component also exposes `Clear`.
- `Controller Data/IRB1200_5_90/SYSPAR/EIO.cfg`: the `do_pen` digital output.
- The RAPID stroke sequence: move to the first point with `fine`, `Set do_pen`, draw the stroke, reach its final point with `fine`, `Reset do_pen`, then retract with `RelTool(...,0,0,-2)` in that tool’s frame.

The generator loads the Hershey `cursive` font and emits successive `MoveL` instructions. Interior points use `z0`, endpoints use `fine`. This is a vector polyline representation with controller corner blending, not a spline generator. It advances the work-object X offset after each character and supports character scaling.

To adapt the idea to our drawing station, define a digital output, connect it to a TraceTCP component for the correct robot, and toggle it at stroke boundaries. Clear old traces before recording and ensure an independently enabled global TCP trace is not also displaying travel moves. Merely inserting `Set do_pen` into RAPID is insufficient without the station signal connection. Retraction direction must be derived from our own tool and paper frames; the upstream negative tool-Z offset must not be copied blindly.

This is a documented adaptation recipe, not a change to the current demo station or its recording. The same visible artifact can arise from an always-enabled TCP trace; the current station’s wiring must be inspected before assigning a definite cause.

Source evidence: [generator](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/blob/7dddaf2443eb4934ea06698c306d220e72758ea2/src/hersheyToRapid.py), [character procedures](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/blob/7dddaf2443eb4934ea06698c306d220e72758ea2/RAPID/hersheyCursiveModule.mod), [main program](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/blob/7dddaf2443eb4934ea06698c306d220e72758ea2/RAPID/Module1.mod), [inspected station package](https://github.com/FLo-ABB/Hershey-ABB-Robot-Handwriting/blob/7dddaf2443eb4934ea06698c306d220e72758ea2/Simulation/HandWriting.rspag).
