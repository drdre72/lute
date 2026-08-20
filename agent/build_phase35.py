#!/usr/bin/env python3
"""Phase 35: Improve building materials with rock/sand textures from OWDB demo.
Apply stone wall texture to temple/town structures, wood texture to fences/gates,
and improve roof materials."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    return r

ROCK_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/rock.jpg'
SAND_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/sand.jpg'
GRASS_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass.jpg'

def stone_wall_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': ROCK_TEX,
        'roughness': 0.92,
        'uv1_scale': {'x': 2.0, 'y': 2.0, 'z': 2.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 50.0
    }

def sand_wall_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': SAND_TEX,
        'roughness': 0.88,
        'uv1_scale': {'x': 2.0, 'y': 2.0, 'z': 2.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 40.0
    }

def roof_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.3, 'g': 0.1, 'b': 0.05, 'a': 1},
        'albedo_texture': ROCK_TEX,
        'roughness': 0.85,
        'uv1_scale': {'x': 3.0, 'y': 3.0, 'z': 3.0},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 60.0
    }

def wood_dark_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.2, 'g': 0.12, 'b': 0.06, 'a': 1},
        'roughness': 0.9
    }

# ============================================================
# Find all building structure nodes (CSGBox3D, CSGCylinder3D)
# and apply textured materials based on their names
# ============================================================
print("=== Finding Building Nodes ===")

r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

building_nodes = []
def find_buildings(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    # Match building-related names
    if any(kw in name for kw in ['Wall', 'Pillar', 'Column', 'Roof', 'Floor', 
                                  'Ceiling', 'Arch', 'Door', 'Gate', 'Tower',
                                  'Foundation', 'Plaza', 'Platform', 'Step',
                                  'Buttress', 'Vault', 'Altar', 'Pedestal',
                                  'Building', 'House', 'Shop', 'Barracks',
                                  'WallSegment', 'CornerWall', 'GateHouse']):
        building_nodes.append(full_path)
    for child in node.get('children', []):
        find_buildings(child, full_path)

find_buildings(tree)
print(f"  Found {len(building_nodes)} building nodes")

# Apply materials based on node name keywords
stone_count = 0
sand_count = 0
roof_count = 0
wood_count = 0

for p in building_nodes:
    name = p.split('/')[-1]
    
    if 'Roof' in name:
        ss(p, 'material_override', roof_mat())
        roof_count += 1
    elif any(kw in name for kw in ['Door', 'Gate', 'Fence', 'Post', 'Rail']):
        ss(p, 'material_override', wood_dark_mat())
        wood_count += 1
    elif any(kw in name for kw in ['Pillar', 'Column', 'Tower', 'Foundation', 
                                    'Buttress', 'Altar', 'Pedestal', 'Platform',
                                    'Step', 'Plaza']):
        ss(p, 'material_override', stone_wall_mat())
        stone_count += 1
    elif any(kw in name for kw in ['Wall', 'Floor', 'Ceiling', 'Arch', 'Vault',
                                    'Building', 'House', 'Shop', 'Barracks']):
        ss(p, 'material_override', sand_wall_mat())
        sand_count += 1
    else:
        ss(p, 'material_override', stone_wall_mat())
        stone_count += 1

print(f"  Stone: {stone_count}, Sand: {sand_count}, Roof: {roof_count}, Wood: {wood_count}")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 35 complete — building materials upgraded with textures!")
