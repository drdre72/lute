#!/usr/bin/env python3
"""Phase 40: Grass fill in small batches — takes a batch number arg.
Each batch adds ~15 tufts in a specific region to stay well under timeout."""
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

BATCH = int(sys.argv[1]) if len(sys.argv) > 1 else 0

# Each batch covers a different sub-region
BATCHES = [
    # (parent, x_min, x_max, z_min, z_max, count)
    ('TownArea/Terrain', -55, -20, 50, 80, 15),      # 0: NW town edge
    ('TownArea/Terrain', -20, 15, 50, 80, 15),       # 1: W town
    ('TownArea/Terrain', 35, 65, 50, 80, 15),        # 2: NE town edge
    ('TownArea/Terrain', -55, -20, 80, 110, 15),     # 3: W mid
    ('TownArea/Terrain', 35, 65, 80, 110, 15),       # 4: E mid
    ('TownArea/Terrain', -55, -20, 110, 145, 15),    # 5: SW town
    ('TownArea/Terrain', 35, 65, 110, 145, 15),      # 6: SE town
    ('TownArea/Terrain', -38, 0, 142, 178, 15),      # 7: Forest W
    ('TownArea/Terrain', 0, 38, 142, 178, 15),       # 8: Forest E
    ('TownArea/LakeRegion', -15, 20, 192, 212, 12),  # 9: Lake W
    ('TownArea/LakeRegion', 20, 55, 192, 212, 12),   # 10: Lake E
    ('TownArea/Graveyard', -55, -25, 190, 220, 12),  # 11: Graveyard
    ('TownArea/HiddenGrove', -38, -22, 92, 108, 10), # 12: Grove
    ('TownArea/Terrain', -10, 10, 25, 50, 12),       # 13: Path start edges
    ('TownArea/Terrain', -10, 10, 145, 165, 12),     # 14: Path end edges
]

if BATCH >= len(BATCHES):
    print(f"No batch {BATCH}, max is {len(BATCHES)-1}")
    sys.exit(0)

parent, x_min, x_max, z_min, z_max, count = BATCHES[BATCH]

parser = SceneParser(SCENE_PATH)
parser.load()

existing = parser.find(name_pattern='Grass*', limit=5000)
grass_cells = set()
for g in existing:
    pos = g.position
    if pos:
        grass_cells.add((int(pos[0] // 5), int(pos[2] // 5)))

print(f"Batch {BATCH}: {parent} x=[{x_min},{x_max}] z=[{z_min},{z_max}] target={count}")
print(f"Existing grass cells: {len(grass_cells)}")

random.seed(BATCH * 1000 + 42)
seed = 40000 + BATCH * 100
added = 0
attempts = 0

while added < count and attempts < count * 4:
    attempts += 1
    x = random.uniform(x_min, x_max)
    z = random.uniform(z_min, z_max)
    cell = (int(x // 5), int(z // 5))
    if cell in grass_cells:
        continue
    # Skip paths and buildings
    if 25 < x < 35 and 25 < z < 140:
        continue
    if 15 < x < 45 and 105 < z < 135:
        continue
    
    s = random.uniform(0.7, 1.3)
    h = random.uniform(0.2, 0.4) * s
    name = f'GrassB{BATCH}_{seed}'
    
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

print(f"Added {added} tufts in batch {BATCH}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
