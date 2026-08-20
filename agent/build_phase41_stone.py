#!/usr/bin/env python3
"""Phase 41c: Apply stone_wall + normal map to buildings. Batch arg = batch number."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool
from scene_parser import SceneParser

BATCH = int(sys.argv[1]) if len(sys.argv) > 1 else 0
SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"

STONE_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/stone_wall.jpg'
NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'

stone_mat = {
    'class': 'StandardMaterial3D',
    'albedo_texture': STONE_TEX,
    'normal_enabled': True,
    'normal_texture': NORMAL_TEX,
    'normal_scale': 0.5,
    'roughness': 0.9,
    'uv1_scale': {'x': 0.3, 'y': 0.3, 'z': 0.3},
    'uv1_triplanar': True,
    'uv1_triplanar_sharpness': 200.0
}

parser = SceneParser(SCENE_PATH)
parser.load()

# Find building/wall nodes — Walls, Foundation, Column, Pillar, Tower, Arch types
import re
building_patterns = ['*Walls*', '*Foundation*', '*Column*', '*Pillar*', '*Tower*', '*Arch*', '*Buttress*', '*Exterior*']
building_nodes = []
for pat in building_patterns:
    nodes = parser.find(name_pattern=pat, limit=500)
    for n in nodes:
        if n.node_type in ('CSGBox3D', 'CSGCylinder3D', 'MeshInstance3D', 'CSGCombiner3D'):
            if n not in building_nodes:
                building_nodes.append(n)

PER = 30
start = BATCH * PER
end = min(start + PER, len(building_nodes))
batch = building_nodes[start:end]

if not batch:
    print(f"Batch {BATCH}: no building nodes (total={len(building_nodes)})")
    sys.exit(0)

print(f"Batch {BATCH}: buildings {start}-{end-1} ({len(batch)} nodes)")
updated = 0

for n in batch:
    mat_prop = 'surface_material_override/0' if n.node_type == 'MeshInstance3D' else 'material_override'
    r = call_tool('node_set_property', {'node_path': n.full_path, 'property': mat_prop, 'value': stone_mat})
    if r.get('ok'):
        updated += 1

print(f"Updated {updated}/{len(batch)} building nodes in batch {BATCH}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
