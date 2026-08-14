#!/usr/bin/env python3
"""Phase 22: Tune terrain heights, add Scatter3D for trees on terrain,
adjust building positions to sit on terrain surface."""
import sys, os, math, random
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
# 1. Tune terrain — lower town terrain so buildings sit better
# ============================================================
print("=== Tuning Terrain ===")

# Town terrain — lower height for flatter town area
ss('TownArea/Terrain/TownTerrain', 'height_scale', 2.0)
ss('TownArea/Terrain/TownTerrain', 'noise', {
    'class': 'FastNoiseLite',
    'seed': 42,
    'frequency': 0.02,
    'noise_type': 0
})

# Temple terrain — very flat
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'height_scale', 1.0)
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'noise', {
    'class': 'FastNoiseLite',
    'seed': 55,
    'frequency': 0.015,
    'noise_type': 0
})

# Forest — keep varied but not too extreme
ss('TownArea/Terrain/ForestTerrain', 'height_scale', 6.0)

# Mountains — taller
ss('TownArea/Terrain/MountainTerrain', 'height_scale', 30.0)

print("  Terrain tuned!")

# ============================================================
# 2. Add Scatter3D for trees on town terrain
# ============================================================
print("\n=== Tree Scatter ===")

# We can't easily assign PackedScenes via RPC, but we can add the Scatter3D
# node and configure its properties. The user can assign tree scenes in editor.
sa('TownArea/Terrain', 'Scatter3D', 'TreeScatter')
ss('TownArea/Terrain/TreeScatter', 'count', 50)
ss('TownArea/Terrain/TreeScatter', 'area_size', {'x': 100.0, 'y': 100.0})
ss('TownArea/Terrain/TreeScatter', 'random_rotation', True)
ss('TownArea/Terrain/TreeScatter', 'random_scale', True)
ss('TownArea/Terrain/TreeScatter', 'min_scale', 0.8)
ss('TownArea/Terrain/TreeScatter', 'max_scale', 1.5)
ss('TownArea/Terrain/TreeScatter', 'position', {'x': 0, 'y': 0, 'z': 110})

print("  Tree scatter node added (assign tree scenes in editor to populate)!")

# ============================================================
# 3. Add Scatter3D for forest vegetation
# ============================================================
print("\n=== Forest Scatter ===")

sa('TownArea/Terrain', 'Scatter3D', 'ForestScatter')
ss('TownArea/Terrain/ForestScatter', 'count', 80)
ss('TownArea/Terrain/ForestScatter', 'area_size', {'x': 80.0, 'y': 80.0})
ss('TownArea/Terrain/ForestScatter', 'random_rotation', True)
ss('TownArea/Terrain/ForestScatter', 'random_scale', True)
ss('TownArea/Terrain/ForestScatter', 'min_scale', 0.6)
ss('TownArea/Terrain/ForestScatter', 'max_scale', 1.8)
ss('TownArea/Terrain/ForestScatter', 'position', {'x': 0, 'y': 0, 'z': 160})

print("  Forest scatter node added!")

# ============================================================
# 4. Add Scatter3D for mountain rocks
# ============================================================
print("\n=== Mountain Scatter ===")

sa('TownArea/Terrain', 'Scatter3D', 'MountainScatter')
ss('TownArea/Terrain/MountainScatter', 'count', 40)
ss('TownArea/Terrain/MountainScatter', 'area_size', {'x': 180.0, 'y': 80.0})
ss('TownArea/Terrain/MountainScatter', 'random_rotation', True)
ss('TownArea/Terrain/MountainScatter', 'random_scale', True)
ss('TownArea/Terrain/MountainScatter', 'min_scale', 1.0)
ss('TownArea/Terrain/MountainScatter', 'max_scale', 3.0)
ss('TownArea/Terrain/MountainScatter', 'position', {'x': 0, 'y': 0, 'z': 220})

print("  Mountain scatter node added!")

# ============================================================
# 5. Add Scatter3D for lake reeds
# ============================================================
print("\n=== Lake Scatter ===")

sa('TownArea/LakeRegion', 'Scatter3D', 'LakeScatter')
ss('TownArea/LakeRegion/LakeScatter', 'count', 30)
ss('TownArea/LakeRegion/LakeScatter', 'area_size', {'x': 60.0, 'y': 60.0})
ss('TownArea/LakeRegion/LakeScatter', 'random_rotation', True)
ss('TownArea/LakeRegion/LakeScatter', 'random_scale', True)
ss('TownArea/LakeRegion/LakeScatter', 'min_scale', 0.5)
ss('TownArea/LakeRegion/LakeScatter', 'max_scale', 1.2)
ss('TownArea/LakeRegion/LakeScatter', 'position', {'x': 20, 'y': 0, 'z': 180})

print("  Lake scatter node added!")

# ============================================================
# 6. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 22 complete — terrain tuned + 4 Scatter3D nodes added!")
