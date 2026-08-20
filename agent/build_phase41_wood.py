#!/usr/bin/env python3
"""Phase 41d: Apply wood_slice to stalls/fences/wooden structures. Batch arg = batch number."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool
from scene_parser import SceneParser

BATCH = int(sys.argv[1]) if len(sys.argv) > 1 else 0
SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"

WOOD_TEX = 'res://addons/open-world-database/demo/resources/tree/wood_slice.jpg'
NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'

wood_mat = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.6, 'g': 0.45, 'b': 0.3, 'a': 1},
    'albedo_texture': WOOD_TEX,
    'normal_enabled': True,
    'normal_texture': NORMAL_TEX,
    'normal_scale': 0.7,
    'roughness': 0.85,
    'uv1_scale': {'x': 0.2, 'y': 0.2, 'z': 0.2},
    'uv1_triplanar': True,
    'uv1_triplanar_sharpness': 200.0
}

parser = SceneParser(SCENE_PATH)
parser.load()

# Find wooden nodes — Stall, Fence, Gate, Door, Plank, Dock, Bridge, Well, Sign, Banner, Barrel
wood_patterns = ['*Stall*', '*Fence*', '*Gate*', '*Door*', '*Plank*', '*Dock*', '*Bridge*', '*Well*', '*Sign*', '*Barrel*', '*Boat*', '*Bucket*']
wood_nodes = []
for pat in wood_patterns:
    nodes = parser.find(name_pattern=pat, limit=500)
    for n in nodes:
        if n.node_type in ('CSGBox3D', 'CSGCylinder3D', 'MeshInstance3D'):
            # Skip cut operations and lights
            if 'Cut' in n.name or 'Light' in n.name:
                continue
            if n not in wood_nodes:
                wood_nodes.append(n)

PER = 30
start = BATCH * PER
end = min(start + PER, len(wood_nodes))
batch = wood_nodes[start:end]

if not batch:
    print(f"Batch {BATCH}: no wood nodes (total={len(wood_nodes)})")
    sys.exit(0)

print(f"Batch {BATCH}: wood {start}-{end-1} ({len(batch)} nodes)")
updated = 0

for n in batch:
    mat_prop = 'surface_material_override/0' if n.node_type == 'MeshInstance3D' else 'material_override'
    r = call_tool('node_set_property', {'node_path': n.full_path, 'property': mat_prop, 'value': wood_mat})
    if r.get('ok'):
        updated += 1

print(f"Updated {updated}/{len(batch)} wood nodes in batch {BATCH}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
