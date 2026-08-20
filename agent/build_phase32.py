#!/usr/bin/env python3
"""Phase 32: Upgrade ground textures with grass.jpg + grass_normal.jpg,
add water plane with water.jpg, and add sand/silt transitions near lake."""
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
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    return r

GRASS_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass.jpg'
GRASS_NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'
SAND_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/sand.jpg'
SILT_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/silt.jpg'
WATER_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/water.jpg'
SNOW_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/snow.png'
ROCK_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/rock.jpg'

# Grass material with normal map
def grass_textured_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': GRASS_TEX,
        'normal_texture': GRASS_NORMAL_TEX,
        'roughness': 0.95,
        'uv1_scale': {'x': 4.0, 'y': 4.0, 'z': 4.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 30.0
    }

def sand_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': SAND_TEX,
        'roughness': 0.9,
        'uv1_scale': {'x': 3.0, 'y': 3.0, 'z': 3.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 30.0
    }

def silt_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': SILT_TEX,
        'roughness': 0.95,
        'uv1_scale': {'x': 3.0, 'y': 3.0, 'z': 3.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 30.0
    }

def water_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': WATER_TEX,
        'albedo_color': {'r': 0.3, 'g': 0.4, 'b': 0.5, 'a': 0.85},
        'transparency': 1,  # Alpha blend
        'roughness': 0.1,
        'metallic': 0.2,
        'uv1_scale': {'x': 5.0, 'y': 5.0, 'z': 5.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 20.0
    }

def snow_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': SNOW_TEX,
        'roughness': 0.8,
        'uv1_scale': {'x': 3.0, 'y': 3.0, 'z': 3.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 40.0
    }

def rock_ground_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': ROCK_TEX,
        'roughness': 0.9,
        'uv1_scale': {'x': 3.0, 'y': 3.0, 'z': 3.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 40.0
    }

# ============================================================
# 1. Find and update existing ground planes with textured materials
# ============================================================
print("=== Upgrading Ground Textures ===")

r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

ground_nodes = []
def find_ground(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    # Match our ground plane names from phase 26
    if name in ['PathGround', 'TownGround', 'ForestGround', 'LakeGrass', 
                'TrainingGround', 'GraveyardGround', 'TempleGrass', 'GroveGround']:
        ground_nodes.append(full_path)
    for child in node.get('children', []):
        find_ground(child, full_path)

find_ground(tree)
print(f"  Found {len(ground_nodes)} ground planes to upgrade")

for p in ground_nodes:
    ss(p, 'material_override', grass_textured_mat())
    print(f"  Upgraded {p.split('/')[-1]}")

# ============================================================
# 2. Add water plane for lake
# ============================================================
print("\n=== Adding Water Plane ===")

sa('TownArea/LakeRegion', 'MeshInstance3D', 'LakeWater')
ss('TownArea/LakeRegion/LakeWater', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 60, 'y': 40}
})
ss('TownArea/LakeRegion/LakeWater', 'position', {'x': 20, 'y': 0.3, 'z': 185})
ss('TownArea/LakeRegion/LakeWater', 'surface_material_override/0', water_mat())
print("  Lake water plane added!")

# ============================================================
# 3. Add sand transition around lake shore
# ============================================================
print("\n=== Adding Sand Shore ===")

sa('TownArea/LakeRegion', 'MeshInstance3D', 'SandShore')
ss('TownArea/LakeRegion/SandShore', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 70, 'y': 15}
})
ss('TownArea/LakeRegion/SandShore', 'position', {'x': 20, 'y': 0.05, 'z': 175})
ss('TownArea/LakeRegion/SandShore', 'surface_material_override/0', sand_mat())
print("  Sand shore added!")

# Silt underwater transition
sa('TownArea/LakeRegion', 'MeshInstance3D', 'SiltBed')
ss('TownArea/LakeRegion/SiltBed', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 55, 'y': 35}
})
ss('TownArea/LakeRegion/SiltBed', 'position', {'x': 20, 'y': 0.02, 'z': 186})
ss('TownArea/LakeRegion/SiltBed', 'surface_material_override/0', silt_mat())
print("  Silt bed added!")

# ============================================================
# 4. Add snow caps on mountains
# ============================================================
print("\n=== Adding Snow Caps ===")

sa('TownArea/Terrain', 'MeshInstance3D', 'SnowCap')
ss('TownArea/Terrain/SnowCap', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 80, 'y': 40}
})
ss('TownArea/Terrain/SnowCap', 'position', {'x': 0, 'y': 15, 'z': 235})
ss('TownArea/Terrain/SnowCap', 'surface_material_override/0', snow_mat())
print("  Snow caps added!")

# Rocky ground on mountain slopes
sa('TownArea/Terrain', 'MeshInstance3D', 'MountainRockGround')
ss('TownArea/Terrain/MountainRockGround', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 80, 'y': 30}
})
ss('TownArea/Terrain/MountainRockGround', 'position', {'x': 0, 'y': 5, 'z': 225})
ss('TownArea/Terrain/MountainRockGround', 'surface_material_override/0', rock_ground_mat())
print("  Mountain rock ground added!")

# ============================================================
# 5. Update path strip with sand texture (dirt path look)
# ============================================================
print("\n=== Upgrading Path Textures ===")

path_nodes = []
def find_paths(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    if name in ['PathStrip', 'ForestTrail', 'ArenaFloor']:
        path_nodes.append(full_path)
    for child in node.get('children', []):
        find_paths(child, full_path)

find_paths(tree)
for p in path_nodes:
    ss(p, 'material_override', sand_mat())
    print(f"  Upgraded {p.split('/')[-1]} with sand texture")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 32 complete — ground textures, water, sand, snow upgraded!")
