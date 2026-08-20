#!/usr/bin/env python3
"""Phase 23: Strategic camera placement — 6 cameras for full world coverage.
Uses spatial geometry to minimize cameras while covering all regions.
Removes old redundant cameras, creates optimized set."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

def sa(parent, type, name):
    r = add(parent, type, name)
    print(f"  +{name}: {r}")
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r}")
    return r

# ============================================================
# Remove old redundant cameras
# ============================================================
print("=== Removing Old Cameras ===")

# AltarCamera redundant with DoorCamera
r = call_tool('node_delete', {'node_path': 'AltarCamera'})
print(f"  Deleted AltarCamera: {r}")

# OverlookCamera will be covered by Grand Overview
r = call_tool('node_delete', {'node_path': 'TownArea/Terrain/OverlookCamera'})
print(f"  Deleted OverlookCamera: {r}")

# ============================================================
# Camera 1: GRAND OVERVIEW
# Position: (0, 100, 120) looking down ~55°
# Coverage: 150x150 units — sees temple, town, forest, lake
# At y=100 with 75° FOV: horizontal coverage = 2*100*tan(37.5°) ≈ 153 units
# Pitch -55° tilts view to cover z=-20 to z=270
# ============================================================
print("\n=== Cam 1: Grand Overview ===")

sa('.', 'Camera3D', 'Cam_GrandOverview')
ss('Cam_GrandOverview', 'position', {'x': 0, 'y': 100, 'z': 120})
ss('Cam_GrandOverview', 'rotation_degrees', {'x': -55, 'y': 0, 'z': 0})
ss('Cam_GrandOverview', 'fov', 75)
ss('Cam_GrandOverview', 'far', 500)
print("  Covers: entire world (temple → mountains)")

# ============================================================
# Camera 2: TEMPLE NAVE (repurpose DemoCamera)
# Position: (0, 8, -8) looking slightly up toward altar
# Coverage: temple interior, pillars, vault, altar
# ============================================================
print("\n=== Cam 2: Temple Nave ===")

ss('DemoCamera', 'position', {'x': 0, 'y': 8, 'z': -12})
ss('DemoCamera', 'rotation_degrees', {'x': -3, 'y': 0, 'z': 0})
ss('DemoCamera', 'fov', 75)
ss('DemoCamera', 'far', 200)
print("  Covers: temple nave interior")

# ============================================================
# Camera 3: TOWN STREET LEVEL
# Position: (30, 4, 92) looking toward town center (+Z)
# Coverage: town gate, square, buildings, market, fountain
# At y=4, FOV 75°, looking +Z: sees ~30 units wide at 20 units distance
# ============================================================
print("\n=== Cam 3: Town Street ===")

sa('TownArea', 'Camera3D', 'Cam_TownStreet')
ss('TownArea/Cam_TownStreet', 'position', {'x': 30, 'y': 4, 'z': 92})
ss('TownArea/Cam_TownStreet', 'rotation_degrees', {'x': -2, 'y': 0, 'z': 0})
ss('TownArea/Cam_TownStreet', 'fov', 80)
ss('TownArea/Cam_TownStreet', 'far', 200)
print("  Covers: town gate, square, buildings, market")

# ============================================================
# Camera 4: TOWN AERIAL (repurpose TownCamera)
# Position: (25, 45, 125) looking down ~65°
# Coverage: 70x70 units — entire town layout from above
# At y=45, FOV 70°: coverage = 2*45*tan(35°) ≈ 63 units
# ============================================================
print("\n=== Cam 4: Town Aerial ===")

ss('TownArea/TownCamera', 'position', {'x': 25, 'y': 45, 'z': 125})
ss('TownArea/TownCamera', 'rotation_degrees', {'x': -65, 'y': 0, 'z': 0})
ss('TownArea/TownCamera', 'fov', 70)
ss('TownArea/TownCamera', 'far', 300)
print("  Covers: town layout from above (buildings, walls, training grounds)")

# ============================================================
# Camera 5: FOREST & LAKE
# Position: (15, 25, 165) looking toward castle (+Z), slight down
# Coverage: forest path, lake, castle, mountains, waterfall
# At y=25, pitch -15°, FOV 80°: sees ~80 units forward, 60 wide
# ============================================================
print("\n=== Cam 5: Forest & Lake ===")

sa('TownArea/LakeRegion', 'Camera3D', 'Cam_ForestLake')
ss('TownArea/LakeRegion/Cam_ForestLake', 'position', {'x': 15, 'y': 25, 'z': 165})
ss('TownArea/LakeRegion/Cam_ForestLake', 'rotation_degrees', {'x': -15, 'y': 5, 'z': 0})
ss('TownArea/LakeRegion/Cam_ForestLake', 'fov', 80)
ss('TownArea/LakeRegion/Cam_ForestLake', 'far', 400)
print("  Covers: forest, lake, castle, mountains, waterfall")

# ============================================================
# Camera 6: PATH JOURNEY
# Position: (28, 12, 55) looking toward town (+Z), slight down
# Coverage: winding path, stream, bridge, vegetation, town gate in distance
# At y=12, pitch -8°, FOV 75°: sees ~60 units forward
# ============================================================
print("\n=== Cam 6: Path Journey ===")

sa('TownArea', 'Camera3D', 'Cam_PathJourney')
ss('TownArea/Cam_PathJourney', 'position', {'x': 28, 'y': 12, 'z': 55})
ss('TownArea/Cam_PathJourney', 'rotation_degrees', {'x': -8, 'y': 0, 'z': 0})
ss('TownArea/Cam_PathJourney', 'fov', 75)
ss('TownArea/Cam_PathJourney', 'far', 300)
print("  Covers: winding path, stream, bridge, town gate in distance")

# ============================================================
# Summary
# ============================================================
print("\n=== Camera Coverage Map ===")
print("  Cam 1 (GrandOverview):  (0, 100, 120) pitch-55° — ENTIRE WORLD")
print("  Cam 2 (DemoCamera):     (0,   8, -12) pitch-3°  — TEMPLE NAVE")
print("  Cam 3 (TownStreet):    (30,   4,  92) pitch-2°  — TOWN STREET LEVEL")
print("  Cam 4 (TownCamera):    (25,  45, 125) pitch-65° — TOWN AERIAL")
print("  Cam 5 (ForestLake):    (15,  25, 165) pitch-15° — FOREST + LAKE + CASTLE")
print("  Cam 6 (PathJourney):   (28,  12,  55) pitch-8°  — PATH TEMPLE→TOWN")
print("  Total: 6 cameras for 290-unit-deep world")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 23 complete — 6 strategic cameras placed!")
