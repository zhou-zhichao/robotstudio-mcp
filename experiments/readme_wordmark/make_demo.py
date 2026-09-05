"""Render the README demo from a selected screen-recording range.

Requires FFmpeg and ffprobe on PATH. The source file is never modified.
Usage: python experiments/readme_wordmark/make_demo.py recording.mp4
"""
import argparse
import json
import math
import subprocess
from pathlib import Path


def run(*args):
    subprocess.run([str(a) for a in args], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--output-dir", type=Path, default=Path("docs/media"))
    parser.add_argument("--start", type=float, default=18.5)
    parser.add_argument("--end", type=float, default=48.5)
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    start, end, hold = args.start, args.end, 3.0
    lead, ramp = 1.5, 6.0
    plateau = end - start - lead - 2 * ramp
    source_info = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "format=duration",
        "-of", "json", str(source)
    ], text=True))
    source_duration = float(source_info["format"]["duration"])
    if not all(math.isfinite(v) for v in (start, end)) or start < 0 or plateau < 0 or end > source_duration:
        parser.error("Range must be within the source and at least 13.5 seconds long")
    # Integrate 1/speed in source time. Each ramp changes continuously between
    # 1x and 4x; only acceleration changes at the ramp boundaries.
    ramp_end = lead + ramp
    down_start = ramp_end + plateau
    clock = (
        f"if(lt(T,{lead}),T,"
        f"if(lt(T,{ramp_end}),{lead}+2*log(1+(T-{lead})/2),"
        f"if(lt(T,{down_start}),{lead}+2*log(4)+(T-{ramp_end})/4,"
        f"{lead}+2*log(4)+{plateau}/4-2*log((4-(T-{down_start})/2)/4))))"
    )
    movie = output / "robotstudio-mcp-demo.mp4"
    gif = output / "robotstudio-mcp-demo.gif"
    poster = output / "robotstudio-mcp-result.png"
    video_filter = (
        "scale=1920:-2:flags=lanczos,setpts=PTS-STARTPTS,"
        f"setpts='{clock}/TB',fps=30,"
        f"tpad=stop_mode=clone:stop_duration={hold},format=yuv420p"
    )
    run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-ss", start, "-t", end-start, "-i", source, "-an",
        "-vf", video_filter, "-c:v", "libx264", "-preset", "medium",
        "-crf", "19", "-movflags", "+faststart", movie)
    info = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "format=duration",
        "-of", "json", str(movie)
    ], text=True))
    motion_duration = float(info["format"]["duration"]) - hold
    run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-i", movie, "-filter_complex",
        f"trim=duration={motion_duration:.6f},fps=16,scale=960:-2:flags=lanczos,split[a][b];"
        "[a]palettegen=max_colors=128:stats_mode=diff[p];"
        "[b][p]paletteuse=dither=none:diff_mode=rectangle",
        "-loop", "0", "-final_delay", "300", gif)
    run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-sseof", "-0.1", "-i", movie, "-frames:v", "1", poster)
    ideal_duration = lead + 4*math.log(4) + plateau/4 + hold
    metadata = {
        "source_filename": source.name,
        "source_duration_seconds": source_duration,
        "source_start_seconds": start,
        "source_end_seconds": end,
        "speed_profile": "1x -> gradual 4x -> gradual 1x",
        "speed_segments_source_seconds": {
            "initial_1x": lead, "ramp_up": ramp,
            "constant_4x": plateau, "ramp_down": ramp
        },
        "final_freeze_seconds": hold,
        "ideal_duration_seconds": round(ideal_duration, 4),
        "gif": {"width": 960, "fps": 16, "loop": "infinite"},
        "mp4": {"width": 1920, "fps": 30, "audio": False},
        "note": "Playback is retimed; it does not show real-time robot speed."
    }
    (output / "edit.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata, indent=2), flush=True)


if __name__ == "__main__":
    main()
