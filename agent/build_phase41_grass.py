#!/usr/bin/env python3
"""Phase 41b: Apply grass_detail + normal map to grass tufts. Batch arg = batch number."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool
from scene_parser import SceneParser

BATCH = int(sys.argv[1]) if len(sys.argv) > 1 else 0
SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"

GRASS_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_detail.jpg'
NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'

grass_mat = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.3, 'g': 0.5, 'b': 0.15, 'a': 1},
    'albedo_texture': GRASS_TEX,
    'normal_enabled': True,
    'normal_texture': NORMAL_TEX,
    'normal_scale': 1.0,
    'roughness': 0.95,
    'uv1_scale': {'x': 0.1, 'y': 0.1, 'z': 0.1},
    'uv1_triplanar': True,
    'uv1_triplanar_sharpness': 200.0
}

parser = SceneParser(SCENE_PATH)
parser.load()

all_grass = parser.find(name_pattern='Grass*', limit=10000)
PER = 50
start = BATCH * PER
end = min(start + PER, len(all_grass))
batch = all_grass[start:end]

if not batch:
    print(f"Batch {BATCH}: no grass nodes (total={len(all_grass)})")
    sys.exit(0)

print(f"Batch {BATCH}: grass {start}-{end-1} ({len(batch)} nodes)")
updated = 0

for n in batch:
    mat_prop = 'surface_material_override/0' if n.node_type == 'MeshInstance3D' else 'material_override'
    r = call_tool('node_set_property', {'node_path': n.full_path, 'property': mat_prop, 'value': grass_mat})
    if r.get('ok'):
        updated += 1

print(f"Updated {updated}/{len(batch)} grass nodes in batch {BATCH}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
