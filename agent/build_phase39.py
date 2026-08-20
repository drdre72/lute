#!/usr/bin/env python3
"""Phase 39: Fill grass gaps — optimized with fewer RPC calls.
Uses single MeshInstance3D cones per tuft instead of multi-blade clusters."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool
from scene_parser import SceneParser

SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"
FOLIAGE_TEX = 'res://addons/open-world-database/demo/resources/tree/foliage.png'

def grass_material(green_var=0.0):
    g = max(0.25, 0.45 + green_var)
    return {
        'class': 'StandardMaterial3D',
        'transparency': 2,
        'alpha_scissor_threshold': 0.5,
        'alpha_antialiasing_mode': 0,
        'albedo_color': {'r': 0.2, 'g': g, 'b': 0.1, 'a': 1},
        'albedo_texture': FOLIAGE_TEX,
        'roughness': 0.95,
        'uv1_scale': {'x': 0.15, 'y': 0.15, 'z': 0.15},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 200.0
    }

parser = SceneParser(SCENE_PATH)
parser.load()

existing_grass = parser.find(name_pattern='Grass*', limit=5000)
print(f"Existing grass nodes: {len(existing_grass)}")

grass_cells = set()
for g in existing_grass:
    pos = g.position
    if pos:
        grass_cells.add((int(pos[0] // 5), int(pos[2] // 5)))

regions = [
    ('TownArea/Terrain', -55, 65, 50, 145, 40),
    ('TownArea/Terrain', -38, 38, 142, 178, 25),
    ('TownArea/LakeRegion', -15, 55, 192, 212, 20),
    ('TownArea/Graveyard', -55, -25, 190, 220, 12),
    ('TownArea/HiddenGrove', -38, -22, 92, 108, 10),
]

seed = 30000
total = 0

for parent, x_min, x_max, z_min, z_max, count in regions:
    random.seed(hash(parent) % 1000)
    added = 0
    attempts = 0
    while added < count and attempts < count * 3:
        attempts += 1
        x = random.uniform(x_min, x_max)
        z = random.uniform(z_min, z_max)
        cell = (int(x // 5), int(z // 5))
        if cell in grass_cells:
            continue
        if 25 < x < 35 and 25 < z < 140:
            continue
        if 15 < x < 45 and 105 < z < 135:
            continue
        
        s = random.uniform(0.7, 1.3)
        h = random.uniform(0.2, 0.4) * s
        name = f'GrassFill_{seed}'
        
        call_tool('node_add', {'parent_path': parent, 'type': 'MeshInstance3D', 'name': name})
        call_tool('node_set_property', {'node_path': f'{parent}/{name}', 'property': 'mesh', 'value': {
            'class': 'CylinderMesh', 'top_radius': 0.0, 'bottom_radius': 0.12 * s,
            'height': h, 'radial_segments': 5, 'rings': 0
        }})
        call_tool('node_set_property', {'node_path': f'{parent}/{name}', 'property': 'position', 'value': {'x': round(x, 2), 'y': round(h / 2, 2), 'z': round(z, 2)}})
        call_tool('node_set_property', {'node_path': f'{parent}/{name}', 'property': 'rotation_degrees', 'value': {
            'x': random.uniform(-8, 8), 'y': random.uniform(0, 360), 'z': random.uniform(-8, 8)
        }})
        call_tool('node_set_property', {'node_path': f'{parent}/{name}', 'property': 'surface_material_override/0', 'value': grass_material(random.uniform(-0.08, 0.08))})
        
        seed += 1
        added += 1
        grass_cells.add(cell)
    
    total += added
    print(f"  {parent}: +{added} tufts")
    
    if total % 20 == 0:
        call_tool('scene_save', {})

random.seed(999)
for z in range(25, 140, 4):
    for side in [-1, 1]:
        x = 30 + side * random.uniform(3, 7)
        cell = (int(x // 5), int(z // 5))
        if cell not in grass_cells:
            s = random.uniform(0.6, 1.0)
            h = random.uniform(0.18, 0.32) * s
            name = f'GrassFill_{seed}'
            call_tool('node_add', {'parent_path': 'TownArea/Terrain', 'type': 'MeshInstance3D', 'name': name})
            call_tool('node_set_property', {'node_path': f'TownArea/Terrain/{name}', 'property': 'mesh', 'value': {
                'class': 'CylinderMesh', 'top_radius': 0.0, 'bottom_radius': 0.1 * s,
                'height': h, 'radial_segments': 5, 'rings': 0
            }})
            call_tool('node_set_property', {'node_path': f'TownArea/Terrain/{name}', 'property': 'position', 'value': {'x': round(x, 2), 'y': round(h / 2, 2), 'z': float(z)}})
            call_tool('node_set_property', {'node_path': f'TownArea/Terrain/{name}', 'property': 'rotation_degrees', 'value': {'x': 0, 'y': random.uniform(0, 360), 'z': 0}})
            call_tool('node_set_property', {'node_path': f'TownArea/Terrain/{name}', 'property': 'surface_material_override/0', 'value': grass_material(random.uniform(-0.08, 0.08))})
            seed += 1
            total += 1
            grass_cells.add(cell)

print(f"\nTotal grass tufts added: {total}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 39 complete — grass gaps filled!")
