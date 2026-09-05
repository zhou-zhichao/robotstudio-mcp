# README demonstration media

Recorded by the repository owner in RobotStudio 2024 with the Project9 IRB120 virtual controller on 2026-09-05. The footage shows the robot drawing `robotstudio-mcp`; it is not evidence of RobotStudio 2025/2026 compatibility.

- `robotstudio-mcp-demo.gif`: README hero, 960 x 540, 16 fps, infinite loop with a 3-second final-frame hold.
- `robotstudio-mcp-demo.mp4`: silent 1920 x 1080, 30 fps version with the same edit and final hold.
- `robotstudio-mcp-result.png`: final still for readers who prefer no animation.
- `edit.json`: source range and export settings.

The original recording is 50.55 seconds long. The edit retains source seconds 18.5 through 48.5, excluding the initial wait/approach and the trailing idle footage. Playback remains at 1x for 1.5 source seconds, ramps continuously to 4x over 6 source seconds, stays at 4x for 16.5 source seconds, then ramps back to 1x over 6 source seconds. The completed wordmark is held for 3 seconds. Output is approximately 14.2 seconds; GIF frame timing is quantized to centiseconds. Playback speed does not represent actual robot speed.

The video is only trimmed, retimed and resized. The recorded robot and drawing are not replaced or reconstructed. The original file is kept outside the repository.

## Reproduce

With Python, FFmpeg and ffprobe installed, run from the repository root:

```powershell
python experiments/readme_wordmark/make_demo.py "path/to/2026-09-05 21-02-19.mp4"
```

The script overwrites only its named generated files in `docs/media/`. It does not modify the source recording. RAPID source is in [DrawRobotStudioMcp.mod](../../experiments/readme_wordmark/DrawRobotStudioMcp.mod); its work object and robot targets are specific to the recording station.

## Presentation reference

[VHS](https://github.com/charmbracelet/vhs) places a demo GIF near the top of its README; its tutorial also holds the final output before ending. This repository uses that compact demonstration pattern with its own recording, plus MP4 and still-image alternatives. No third-party media is copied.
