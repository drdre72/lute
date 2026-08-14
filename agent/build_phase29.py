#!/usr/bin/env python3
"""Phase 29: Upgrade existing trees with real bark + foliage textures
from the OWDB demo resources. Applies textured materials to trunk and
canopy nodes, adds alpha-scissor foliage, and improves canopy shape
to match the demo tree style."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    return r

# Texture paths from OWDB demo
BARK_TEX = 'res://addons/open-world-database/demo/resources/tree/bark.png'
FOLIAGE_TEX = 'res://addons/open-world-database/demo/resources/tree/foliage.png'

# Bark material with texture
def bark_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': BARK_TEX,
        'roughness': 0.92,
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 100.0
    }

# Foliage material with alpha scissor (cutout leaves)
def foliage_mat(green_tint=0.0):
    g = 0.64 + green_tint
    return {
        'class': 'StandardMaterial3D',
        'transparency': 2,  # Alpha scissor
        'alpha_scissor_threshold': 0.777,
        'alpha_antialiasing_mode': 0,
        'albedo_color': {'r': 0.67, 'g': g, 'b': 0.31, 'a': 1},
        'albedo_texture': FOLIAGE_TEX,
        'roughness': 0.95,
        'uv1_scale': {'x': 0.5, 'y': 0.5, 'z': 0.5},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 150.0
    }

# Pine material (darker, using foliage texture with dark tint)
def pine_mat():
    return {
        'class': 'StandardMaterial3D',
        'transparency': 2,
        'alpha_scissor_threshold': 0.777,
        'alpha_antialiasing_mode': 0,
        'albedo_color': {'r': 0.15, 'g': 0.35, 'b': 0.12, 'a': 1},
        'albedo_texture': FOLIAGE_TEX,
        'roughness': 0.93,
        'uv1_scale': {'x': 0.4, 'y': 0.4, 'z': 0.4},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 150.0
    }

# Dead tree bark (no foliage, just weathered bark)
def dead_bark_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': BARK_TEX,
        'albedo_color': {'r': 0.5, 'g': 0.4, 'b': 0.3, 'a': 1},
        'roughness': 0.95,
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 80.0
    }

# ============================================================
# Get all tree nodes from scene
# ============================================================
print("=== Finding All Trees ===")

r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

tree_nodes = []
def find_trees(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    # Match our tree naming patterns
    if name.startswith('RTree_') or name.startswith('PTree_') or name.startswith('DTree_'):
        tree_nodes.append(full_path)
    for child in node.get('children', []):
        find_trees(child, full_path)

find_trees(tree)

# Group by tree base name (each tree has _Trunk, _Canopy1/2/3, _Cone1/2/3, _Branch0/1/2)
tree_bases = {}
for p in tree_nodes:
    parts = p.rsplit('_', 1)
    if len(parts) == 2:
        base = parts[0]
        part = parts[1]
        if base not in tree_bases:
            tree_bases[base] = {'path': base, 'parts': [], 'parent': '/'.join(p.rsplit('/', 1)[:-1])}
        tree_bases[base]['parts'].append(part)

print(f"  Found {len(tree_bases)} trees to upgrade")

# ============================================================
# Upgrade each tree
# ============================================================
print("\n=== Upgrading Trees ===")

upgraded = 0
for base_name, info in tree_bases.items():
    parent = info['parent']
    parts = info['parts']
    
    is_round = base_name.split('/')[-1].startswith('RTree_')
    is_pine = base_name.split('/')[-1].startswith('PTree_')
    is_dead = base_name.split('/')[-1].startswith('DTree_')
    
    if is_round:
        # Upgrade round tree: bark on trunk, foliage texture on canopies
        trunk_path = f'{base_name}_Trunk'
        if 'Trunk' in parts:
            ss(trunk_path, 'material_override', bark_mat())
            # Make trunk slightly tapered (cylinder with different top/bottom radius)
            # CSG cylinders don't have separate top/bottom, but we can use less sides for organic look
            ss(trunk_path, 'sides', 7)
        
        # Apply foliage to canopy layers with slight color variation
        for i, canopy_part in enumerate(['Canopy1', 'Canopy2', 'Canopy3']):
            canopy_path = f'{base_name}_{canopy_part}'
            if canopy_part in parts:
                green_var = random.uniform(-0.05, 0.05)
                ss(canopy_path, 'material_override', foliage_mat(green_var))
                # Use low-poly spheres for performance
                ss(canopy_path, 'radial_segments', 7)
                ss(canopy_path, 'rings', 4)
        
        upgraded += 1
        
    elif is_pine:
        # Upgrade pine tree: bark on trunk, dark foliage on cones
        trunk_path = f'{base_name}_Trunk'
        if 'Trunk' in parts:
            ss(trunk_path, 'material_override', bark_mat())
            ss(trunk_path, 'sides', 6)
        
        for i, cone_part in enumerate(['Cone1', 'Cone2', 'Cone3']):
            cone_path = f'{base_name}_{cone_part}'
            if cone_part in parts:
                ss(cone_path, 'material_override', pine_mat())
                ss(cone_path, 'sides', 7)
        
        upgraded += 1
        
    elif is_dead:
        # Upgrade dead tree: weathered bark on trunk and branches
        trunk_path = f'{base_name}_Trunk'
        if 'Trunk' in parts:
            ss(trunk_path, 'material_override', dead_bark_mat())
            ss(trunk_path, 'sides', 6)
        
        for branch_part in parts:
            if branch_part.startswith('Branch'):
                branch_path = f'{base_name}_{branch_part}'
                ss(branch_path, 'material_override', dead_bark_mat())
                ss(branch_path, 'sides', 5)
        
        upgraded += 1

print(f"\n  Upgraded {upgraded} trees with real textures!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 29 complete — trees upgraded with bark + foliage textures!")
