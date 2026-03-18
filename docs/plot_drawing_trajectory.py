"""Plot the Session 5 dual star+circle drawing trajectory for thesis figures."""
import numpy as np
import matplotlib.pyplot as plt
import matplotlib
matplotlib.rcParams['font.family'] = 'serif'

# Star vertices (radius 35mm) in work object frame (X-Y plane at Z=0)
star_pts = {
    'S1': (0, 35),
    'S2': (33.3, 10.8),
    'S3': (20.6, -28.3),
    'S4': (-20.6, -28.3),
    'S5': (-33.3, 10.8),
}

# Star drawing order: S1->S3->S5->S2->S4->S1
star_order = ['S1', 'S3', 'S5', 'S2', 'S4', 'S1']
star_x = [star_pts[p][0] for p in star_order]
star_y = [star_pts[p][1] for p in star_order]

# Circle (radius 40mm) - generate smooth arc
theta = np.linspace(np.pi/2, np.pi/2 + 2*np.pi, 100)  # start from top (0,40)
circle_x = 40 * np.cos(theta)
circle_y = 40 * np.sin(theta)

# Two work objects
wobj1_offset = (0, 80)   # wobj1 Y offset in mm
wobj2_offset = (0, -80)  # wobj2 Y offset in mm

fig, axes = plt.subplots(1, 2, figsize=(12, 5))

# --- Plot 1: Single pattern (close-up) ---
ax1 = axes[0]
ax1.plot(star_x, star_y, 'b-', linewidth=1.5, label='Star (MoveL)')
ax1.plot(circle_x, circle_y, 'r-', linewidth=1.5, label='Circle (MoveC)')
# Mark vertices
for name, (x, y) in star_pts.items():
    ax1.plot(x, y, 'bo', markersize=4)
ax1.set_xlim(-55, 55)
ax1.set_ylim(-55, 55)
ax1.set_aspect('equal')
ax1.set_xlabel('X (mm)')
ax1.set_ylabel('Y (mm)')
ax1.set_title('Single Pattern: Star + Circle')
ax1.grid(True, alpha=0.3)

# --- Plot 2: Dual pattern (both work objects) ---
ax2 = axes[1]
for i, (wobj_name, offset) in enumerate([('wobj1', wobj1_offset), ('wobj2', wobj2_offset)]):
    ox, oy = offset
    # Star
    sx = [x + ox for x in star_x]
    sy = [y + oy for y in star_y]
    ax2.plot(sx, sy, 'b-', linewidth=1.5, label='Star (MoveL)' if i == 0 else None)
    # Circle
    cx = circle_x + ox
    cy = circle_y + oy
    ax2.plot(cx, cy, 'r-', linewidth=1.5, label='Circle (MoveC)' if i == 0 else None)
    # Label
    arrow_y = 25 if oy > 0 else -25
    ax2.annotate(wobj_name, xy=(ox, oy), xytext=(-38, arrow_y),
                 fontsize=9, style='italic', ha='left',
                 arrowprops=dict(arrowstyle='->', color='gray', lw=0.8))

# Show approach paths (dashed)
# Home -> approach wobj1 -> pattern1 -> approach wobj1 -> approach wobj2 -> pattern2 -> home
ax2.plot([0, wobj1_offset[0]], [0, wobj1_offset[1]], 'g--', linewidth=0.8, alpha=0.5)
ax2.plot([wobj1_offset[0], wobj2_offset[0]], [wobj1_offset[1], wobj2_offset[1]],
         'g--', linewidth=0.8, alpha=0.5, label='Transition (MoveJ)')
ax2.plot([wobj2_offset[0], 0], [wobj2_offset[1], 0], 'g--', linewidth=0.8, alpha=0.5)

ax2.set_aspect('equal')
ax2.set_xlabel('X (mm)')
ax2.set_ylabel('Y (mm)')
ax2.set_title('Dual Pattern: Two Work Objects')
ax2.grid(True, alpha=0.3)

# Shared legend at bottom
handles1, labels1 = ax1.get_legend_handles_labels()
handles2, labels2 = ax2.get_legend_handles_labels()
all_handles = handles1 + [h for h, l in zip(handles2, labels2) if l not in labels1]
all_labels = labels1 + [l for l in labels2 if l not in labels1]
fig.legend(all_handles, all_labels, loc='lower center', ncol=3, fontsize=9,
           bbox_to_anchor=(0.5, -0.02))
plt.tight_layout()
plt.subplots_adjust(bottom=0.15)
plt.savefig('C:/Users/sam/code/Robotstudio_mcp/robotstudio-mcp/docs/images/session5_star_circle_trajectory.png',
            dpi=200, bbox_inches='tight')
plt.savefig('C:/Users/sam/code/Robotstudio_mcp/robotstudio-mcp/docs/images/session5_star_circle_trajectory.svg',
            bbox_inches='tight')
print("Saved to docs/images/session5_star_circle_trajectory.png and .svg")
