# Volvo Demo Follow-up

## Background

This document is a follow-up to the demo meeting with Volvo. During the meeting, attendees included Niraj Singh Angomjambam (Volvo), Siyuan Chen, Omkar, and myself (Sam). This project is my master's thesis work — the original idea of using MCP (Model Context Protocol) to bridge AI and RobotStudio was proposed by my supervisor Siyuan Chen. Omkar and Siyuan Chen are both my thesis supervisors.

After the demo, Niraj expressed interest in seeing recorded demo videos. He would like to share these videos internally at Volvo to present at an in-person meeting — if his leadership finds it compelling, there may be an opportunity for further collaboration. I will prepare and send the demo videos to Niraj via email, CC'ing Siyuan Chen and Omkar.

---

## Issue During Demo: Slow Workobject Coordinate Calculation

### What happened
During the live demo, the AI agent needed to place orange blocks on Euro Pallet_2, which required knowing its workobject coordinate system (`WO_Place_pq`). This workobject was not pre-defined in the controller, so the agent had to **manually calculate it** by:

1. Querying the robot base position from the scene graph
2. Querying the pallet position from the scene graph
3. Performing coordinate transformations (station coords → robot world coords → workobject definition)

This multi-step calculation process caused a noticeable delay during the demo.

### Root cause
The MCP server had no tool to **discover** existing RAPID variables by type. The agent could read a variable if it already knew the exact name, but had no way to list what workobjects, targets, or tools were available in the controller.

### What we did to fix it
We added a new MCP tool: `list_rapid_variables`, which allows the AI agent to query all RAPID variable declarations with optional type filtering.

**Example usage:**
```
list_rapid_variables(typeFilter="wobjdata")
→ WO_Pick = [FALSE,TRUE,"",[[846,535,176],[1,0,0,0]],...]
  WO_Place_pq = [FALSE,TRUE,"",[[522,-650,-259],[1,0,0,0]],...]
```

Now instead of calculating coordinates from scratch, the agent can instantly discover and use any workobject already defined in the controller. This eliminates the delay entirely.

**Changes:** Committed and pushed to GitHub ([commit af25487](https://github.com/zhou-zhichao/robotstudio-mcp)).

---

## Schedule Note

Please note that I will be away from Gothenburg from **March 30 to April 6**. I will be back and resume work on **April 7**.

CC: Omkar

---

## New MCP Capabilities (March 23)

### 1. Object Dimension Query (BoundingBox API)

Previously, the MCP had no way to determine the physical size of objects in the scene — the AI could only see positions and names, but not dimensions. This meant block stacking heights, placement offsets, and clearance distances all had to be hardcoded or guessed, which broke whenever the workpiece changed.

`get_scene_objects` now returns each object's 3D bounding box as `sz(x, y, z)` in meters. The AI can query actual workpiece dimensions before writing placement logic — no more hardcoded assumptions.

**Example output:**
```
Caja_pq[Part]      sz(0.2, 0.2, 0.1)   ← orange block 200×200×100mm
Caja_gr[Part]       sz(0.2, 0.2, 0.2)   ← green block 200×200×200mm
Euro Pallet_2[...]  sz(1.2, 0.8, 0.1)   ← pallet 1200×800×100mm
```

**Value:** The AI no longer guesses object sizes. It adapts dynamically to different workpieces — critical for flexible production lines where workpieces change without reprogramming.

