"""Generate the Project9 wordmark from the credited Hershey glyph subset."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TEXT = "robotstudio-mcp"

def build():
    glyphs = json.loads((ROOT / "hershey-wordmark-glyphs.json").read_text(encoding="utf-8"))["glyphs"]
    paths = []
    offset = 0.0
    for char in TEXT:
        glyph = glyphs[char]
        for stroke in glyph["strokes"]:
            paths.append([(x + offset, y) for x, y in stroke])
        offset += glyph["width"]
    xs = [x for s in paths for x, y in s]
    ys = [y for s in paths for x, y in s]
    scale = 294.0 / (max(xs) - min(xs))
    paths = [[((x - min(xs)) * scale, (y - min(ys)) * scale) for x, y in s] for s in paths]
    code = [
        "MODULE DrawRobotStudioCursive",
        "  ! Hershey glyphs: FLo-ABB, MIT; see LICENSE-Hershey-ABB.txt.",
        '  TASK PERS wobjdata wobjText := [FALSE,TRUE,"",[[370,-150,200],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];',
        "  CONST jointtarget jHome := [[0,0,0,0,30,0],[9E9,9E9,9E9,9E9,9E9,9E9]];",
        "  CONST jointtarget jPhoto := [[150,0,0,0,30,0],[9E9,9E9,9E9,9E9,9E9,9E9]];",
        "  VAR robtarget p;",
        "  PERS num strokesDone := 0;",
        "  PROC main()",
        "    ConfL \\Off;",
        "    ConfJ \\Off;",
        "    Reset do_pen;",
        "    strokesDone := 0;",
        "    MoveAbsJ jHome,v100,fine,tool0;",
    ]
    for index, stroke in enumerate(paths):
        x, y = stroke[0]
        code += [f"    ! Stroke {index + 1}", f"    Point {x:.5f},{y:.5f},10;", "    MoveJ p,v100,fine,tool0\\WObj:=wobjText;", f"    Point {x:.5f},{y:.5f},0;", "    MoveL p,v60,fine,tool0\\WObj:=wobjText;", "    Set do_pen;", "    WaitTime 0.05;"]
        for n, (x, y) in enumerate(stroke[1:]):
            zone = "fine" if n == len(stroke) - 2 else "z0"
            code += [f"    Point {x:.5f},{y:.5f},0;", f"    MoveL p,v60,{zone},tool0\\WObj:=wobjText;"]
        x, y = stroke[-1]
        code += ["    Reset do_pen;", "    WaitTime 0.05;", f"    Point {x:.5f},{y:.5f},10;", "    MoveL p,v60,fine,tool0\\WObj:=wobjText;", f"    strokesDone := {index + 1};"]
    code += ["    MoveAbsJ jHome,v100,fine,tool0;", "    MoveAbsJ jPhoto,v100,fine,tool0;", "  ERROR", "    Reset do_pen;", "    RAISE;", "  ENDPROC", "  PROC Point(num x,num y,num z)", "    p := [[-y,x,z],[0,0,1,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]];", "  ENDPROC", "ENDMODULE"]
    (ROOT / "DrawRobotStudioCursive.mod").write_text("\n".join(code) + "\n", encoding="utf-8")
    assert len(paths) > 0 and all(len(s) >= 2 for s in paths)
    print(f"Generated {len(paths)} strokes, 294 mm wide, {max(y for s in paths for x,y in s):.2f} mm high")
    return paths

if __name__ == "__main__":
    build()
