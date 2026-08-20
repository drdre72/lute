#!/usr/bin/env python3
"""Phase 26: Fill empty ground areas with textured planes matching the
terrain PBR texture. Cover gaps between terrain patches, under buildings,
and along the path."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    return r

def sa(parent, type, name):
    r = add(parent, type, name)
    print(f"  +{name}: {r}")
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r}")
    return r

GRASS_MAT = 'res://addons/terrain_generator/Textures/Grass.tres'

# Dark soil material for path
SOIL_MAT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.15, 'g': 0.10, 'b': 0.06, 'a': 1},
    'roughness': 0.95
}

# Stone path material
STONE_PATH_MAT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.25, 'g': 0.23, 'b': 0.20, 'a': 1},
    'roughness': 0.9
}

# Dark water-edge mud
MUD_MAT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.12, 'g': 0.10, 'b': 0.07, 'a': 1},
    'roughness': 0.98
}

# ============================================================
# 1. Fill ground between temple and town (path area z:25-95)
# ============================================================
print("=== Filling Path Ground ===")

# Large grass plane under the path area
sa('TownArea/Terrain', 'CSGBox3D', 'PathGround')
ss('TownArea/Terrain/PathGround', 'size', {'x': 120, 'y': 0.1, 'z': 80})
ss('TownArea/Terrain/PathGround', 'position', {'x': 0, 'y': -0.05, 'z': 60})
ss('TownArea/Terrain/PathGround', 'material_override', GRASS_MAT)

# Soil path strip
sa('TownArea/Terrain', 'CSGBox3D', 'PathStrip')
ss('TownArea/Terrain/PathStrip', 'size', {'x': 6, 'y': 0.12, 'z': 80})
ss('TownArea/Terrain/PathStrip', 'position', {'x': 30, 'y': 0.01, 'z': 60})
ss('TownArea/Terrain/PathStrip', 'material_override', SOIL_MAT)

# Stone path edges (from phase 15)
for side in [-1, 1]:
    sa('TownArea/Terrain', 'CSGBox3D', f'PathEdge_{side}')
    ss(f'TownArea/Terrain/PathEdge_{side}', 'size', {'x': 1.0, 'y': 0.15, 'z': 80})
    ss(f'TownArea/Terrain/PathEdge_{side}', 'position', {'x': 30 + side * 3.5, 'y': 0.02, 'z': 60})
    ss(f'TownArea/Terrain/PathEdge_{side}', 'material_override', STONE_PATH_MAT)

print("  Path ground filled!")

# ============================================================
# 2. Fill ground under town buildings
# ============================================================
print("\n=== Filling Town Ground ===")

# Town square ground
sa('TownArea/TownSquare', 'CSGBox3D', 'TownGround')
ss('TownArea/TownSquare/TownGround', 'size', {'x': 80, 'y': 0.1, 'z': 60})
ss('TownArea/TownSquare/TownGround', 'position', {'x': 30, 'y': -0.05, 'z': 115})
ss('TownArea/TownSquare/TownGround', 'material_override', GRASS_MAT)

# Cobblestone plaza around fountain
sa('TownArea/TownSquare', 'CSGBox3D', 'TownPlaza')
ss('TownArea/TownSquare/TownPlaza', 'size', {'x': 30, 'y': 0.12, 'z': 30})
ss('TownArea/TownSquare/TownPlaza', 'position', {'x': 30, 'y': 0.01, 'z': 115})
ss('TownArea/TownSquare/TownPlaza', 'material_override', STONE_PATH_MAT)

print("  Town ground filled!")

# ============================================================
# 3. Fill ground in forest area
# ============================================================
print("\n=== Filling Forest Ground ===")

sa('TownArea/Terrain', 'CSGBox3D', 'ForestGround')
ss('TownArea/Terrain/ForestGround', 'size', {'x': 100, 'y': 0.1, 'z': 50})
ss('TownArea/Terrain/ForestGround', 'position', {'x': 0, 'y': -0.05, 'z': 160})
ss('TownArea/Terrain/ForestGround', 'material_override', GRASS_MAT)

# Forest path (dirt trail)
sa('TownArea/Terrain', 'CSGBox3D', 'ForestTrail')
ss('TownArea/Terrain/ForestTrail', 'size', {'x': 4, 'y': 0.12, 'z': 50})
ss('TownArea/Terrain/ForestTrail', 'position', {'x': 28, 'y': 0.01, 'z': 160})
ss('TownArea/Terrain/ForestTrail', 'material_override', SOIL_MAT)

print("  Forest ground filled!")

# ============================================================
# 4. Fill ground around lake
# ============================================================
print("\n=== Filling Lake Ground ===")

# Mud shore around lake
sa('TownArea/LakeRegion', 'CSGBox3D', 'LakeShore')
ss('TownArea/LakeRegion/LakeShore', 'size', {'x': 80, 'y': 0.1, 'z': 50})
ss('TownArea/LakeRegion/LakeShore', 'position', {'x': 20, 'y': -0.05, 'z': 185})
ss('TownArea/LakeRegion/LakeShore', 'material_override', MUD_MAT)

# Grass beyond shore
sa('TownArea/LakeRegion', 'CSGBox3D', 'LakeGrass')
ss('TownArea/LakeRegion/LakeGrass', 'size', {'x': 80, 'y': 0.1, 'z': 30})
ss('TownArea/LakeRegion/LakeGrass', 'position', {'x': 20, 'y': -0.05, 'z': 200})
ss('TownArea/LakeRegion/LakeGrass', 'material_override', GRASS_MAT)

print("  Lake ground filled!")

# ============================================================
# 5. Fill ground in training grounds
# ============================================================
print("\n=== Filling Training Ground ===")

sa('TownArea/TrainingGround', 'CSGBox3D', 'TrainingGround')
ss('TownArea/TrainingGround/TrainingGround', 'size', {'x': 40, 'y': 0.1, 'z': 30})
ss('TownArea/TrainingGround/TrainingGround', 'position', {'x': -20, 'y': -0.05, 'z': 118})
ss('TownArea/TrainingGround/TrainingGround', 'material_override', GRASS_MAT)

# Dirt arena floor
sa('TownArea/TrainingGround', 'CSGBox3D', 'ArenaFloor')
ss('TownArea/TrainingGround/ArenaFloor', 'size', {'x': 20, 'y': 0.12, 'z': 15})
ss('TownArea/TrainingGround/ArenaFloor', 'position', {'x': -20, 'y': 0.01, 'z': 118})
ss('TownArea/TrainingGround/ArenaFloor', 'material_override', SOIL_MAT)

print("  Training ground filled!")

# ============================================================
# 6. Fill ground in graveyard
# ============================================================
print("\n=== Filling Graveyard Ground ===")

sa('TownArea/Graveyard', 'CSGBox3D', 'GraveyardGround')
ss('TownArea/Graveyard/GraveyardGround', 'size', {'x': 40, 'y': 0.1, 'z': 30})
ss('TownArea/Graveyard/GraveyardGround', 'position', {'x': 30, 'y': -0.05, 'z': 145})
ss('TownArea/Graveyard/GraveyardGround', 'material_override', GRASS_MAT)

print("  Graveyard ground filled!")

# ============================================================
# 7. Fill ground at temple exterior
# ============================================================
print("\n=== Filling Temple Exterior Ground ===")

sa('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', 'TemplePlaza')
ss('Architecture/Exterior/ExteriorPlaza/TemplePlaza', 'size', {'x': 60, 'y': 0.12, 'z': 40})
ss('Architecture/Exterior/ExteriorPlaza/TemplePlaza', 'position', {'x': 0, 'y': 0.01, 'z': 5})
ss('Architecture/Exterior/ExteriorPlaza/TemplePlaza', 'material_override', STONE_PATH_MAT)

# Grass around temple
sa('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', 'TempleGrass')
ss('Architecture/Exterior/ExteriorPlaza/TempleGrass', 'size', {'x': 100, 'y': 0.1, 'z': 60})
ss('Architecture/Exterior/ExteriorPlaza/TempleGrass', 'position', {'x': 0, 'y': -0.05, 'z': 5})
ss('Architecture/Exterior/ExteriorPlaza/TempleGrass', 'material_override', GRASS_MAT)

print("  Temple exterior ground filled!")

# ============================================================
# 8. Fill hidden grove ground
# ============================================================
print("\n=== Filling Grove Ground ===")

sa('TownArea/HiddenGrove', 'CSGBox3D', 'GroveGround')
ss('TownArea/HiddenGrove/GroveGround', 'size', {'x': 30, 'y': 0.1, 'z': 30})
ss('TownArea/HiddenGrove/GroveGround', 'position', {'x': -30, 'y': -0.05, 'z': 100})
ss('TownArea/HiddenGrove/GroveGround', 'material_override', GRASS_MAT)

print("  Grove ground filled!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 26 complete — all ground gaps filled!")
