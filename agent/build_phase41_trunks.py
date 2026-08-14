#!/usr/bin/env python3
"""Phase 41a: Add trunks to trees. Batch arg = tree index range."""
import sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

BATCH = int(sys.argv[1]) if len(sys.argv) > 1 else 0

with open('/tmp/tree_ids.json') as f:
    tree_data = json.load(f)

tree_keys = sorted(tree_data.keys())
# 6 trees per batch
PER = 6
start = BATCH * PER
end = min(start + PER, len(tree_keys))
batch_keys = tree_keys[start:end]

if not batch_keys:
    print(f"Batch {BATCH}: no trees, done")
    sys.exit(0)

BARK_TEX = 'res://addons/open-world-database/demo/resources/tree/trunk_bark.jpg'
NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'

trunk_mat = {
    'class': 'StandardMaterial3D',
    'albedo_texture': BARK_TEX,
    'normal_enabled': True,
    'normal_texture': NORMAL_TEX,
    'normal_scale': 1.0,
    'roughness': 0.9,
    'uv1_scale': {'x': 0.3, 'y': 1.0, 'z': 0.3},
    'uv1_triplanar': True,
    'uv1_triplanar_sharpness': 200.0
}

print(f"Batch {BATCH}: trees {start}-{end-1} ({len(batch_keys)} trees)")
added = 0

for tid in batch_keys:
    info = tree_data[tid]
    parent = info['parent']
    pos = info['pos']
    if not pos:
        print(f"  Skip {tid}: no position")
        continue
    
    x, y, z = pos[0], pos[1], pos[2]
    trunk_name = f'{tid}_Trunk'
    
    # Add trunk cylinder
    r = call_tool('node_add', {'parent_path': parent, 'type': 'MeshInstance3D', 'name': trunk_name})
    if not r.get('ok'):
        print(f"  Failed to add {trunk_name}: {r}")
        continue
    
    path = f'{parent}/{trunk_name}'
    # Tapered cylinder mesh for trunk
    call_tool('node_set_property', {'node_path': path, 'property': 'mesh', 'value': {
        'class': 'CylinderMesh', 'top_radius': 0.15, 'bottom_radius': 0.35,
        'height': 3.5, 'radial_segments': 8, 'rings': 0
    }})
    # Position at base of tree
    call_tool('node_set_property', {'node_path': path, 'property': 'position', 'value': {
        'x': round(x, 2), 'y': 1.75, 'z': round(z, 2)
    }})
    # Slight random rotation
    import random
    random.seed(int(tid.split('_')[1]))
    call_tool('node_set_property', {'node_path': path, 'property': 'rotation_degrees', 'value': {
        'x': random.uniform(-2, 2), 'y': random.uniform(0, 360), 'z': random.uniform(-2, 2)
    }})
    # Apply bark material with normal map
    call_tool('node_set_property', {'node_path': path, 'property': 'surface_material_override/0', 'value': trunk_mat})
    
    added += 1
    print(f"  +{trunk_name} at ({x:.1f}, 1.75, {z:.1f})")

print(f"Added {added} trunks in batch {BATCH}")
r = call_tool('scene_save', {})
print(f"Save: {r}")
