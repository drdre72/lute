#!/usr/bin/env python3
"""Phase 30: Replace all CSG trees with proper MeshInstance3D trees.
Uses tapered CylinderMesh for trunks, SphereMesh clusters for foliage,
with bark.png and foliage.png textures from OWDB demo.
Shapes match the OWDB demo tree style."""
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

BARK_TEX = 'res://addons/open-world-database/demo/resources/tree/bark.png'
FOLIAGE_TEX = 'res://addons/open-world-database/demo/resources/tree/foliage.png'

# Bark material with texture (for MeshInstance3D surface_material_override)
def bark_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': BARK_TEX,
        'roughness': 0.92,
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 100.0
    }

def foliage_mat(green_var=0.0):
    g = max(0.3, 0.64 + green_var)
    return {
        'class': 'StandardMaterial3D',
        'transparency': 2,
        'alpha_scissor_threshold': 0.777,
        'alpha_antialiasing_mode': 0,
        'albedo_color': {'r': 0.67, 'g': g, 'b': 0.31, 'a': 1},
        'albedo_texture': FOLIAGE_TEX,
        'roughness': 0.95,
        'uv1_scale': {'x': 0.5, 'y': 0.5, 'z': 0.5},
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 150.0
    }

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
# 1. Find and delete ALL old tree nodes (CSG + old)
# ============================================================
print("=== Finding Old Trees ===")

r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

old_tree_parts = []
def find_old_trees(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    if name.startswith('RTree_') or name.startswith('PTree_') or name.startswith('DTree_') or \
       name.startswith('ScatterTree') or name.startswith('ForTree'):
        old_tree_parts.append(full_path)
    for child in node.get('children', []):
        find_old_trees(child, full_path)

find_old_trees(tree)
print(f"  Found {len(old_tree_parts)} old tree parts to delete")

# Delete in reverse order (children before parents)
for p in sorted(old_tree_parts, reverse=True):
    call_tool('node_delete', {'node_path': p})

print(f"  Deleted all old tree parts")

# ============================================================
# 2. Tree builder using MeshInstance3D (proper shapes + textures)
# ============================================================

def make_round_tree(parent, x, y, z, scale=1.0, seed=0):
    """Realistic deciduous tree: tapered trunk + cluster of offset foliage spheres."""
    random.seed(seed)
    name = f'RTree_{seed}'
    base = f'{parent}/{name}'
    
    # Trunk — tapered cylinder (narrow top, wider bottom)
    sa(parent, 'MeshInstance3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.1 * scale,
        'bottom_radius': 0.25 * scale,
        'height': 4.0 * scale,
        'radial_segments': 7,
        'rings': 0
    })
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 2.0 * scale, 'z': z})
    ss(f'{base}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-2, 2),
        'y': random.uniform(0, 360),
        'z': random.uniform(-2, 2)
    })
    ss(f'{base}_Trunk', 'surface_material_override/0', bark_mat())
    
    # Foliage cluster — 5-6 offset spheres like the OWDB demo tree
    foliage_count = random.randint(5, 7)
    for i in range(foliage_count):
        # Random offset around trunk top
        angle = (i / float(foliage_count)) * 6.28318 + random.uniform(-0.3, 0.3)
        radius = random.uniform(0.4, 0.9) * scale
        fx = x + math.cos(angle) * radius
        fy = y + (3.5 + random.uniform(-0.5, 1.0)) * scale
        fz = z + math.sin(angle) * radius
        fr = random.uniform(0.7, 1.1) * scale
        
        sa(parent, 'MeshInstance3D', f'{name}_Leaf{i}')
        ss(f'{base}_Leaf{i}', 'mesh', {
            'class': 'SphereMesh',
            'radius': fr,
            'height': fr * 2.0,
            'radial_segments': 7,
            'rings': 4
        })
        ss(f'{base}_Leaf{i}', 'position', {'x': fx, 'y': fy, 'z': fz})
        ss(f'{base}_Leaf{i}', 'rotation_degrees', {
            'x': random.uniform(0, 30),
            'y': random.uniform(0, 360),
            'z': random.uniform(0, 30)
        })
        ss(f'{base}_Leaf{i}', 'surface_material_override/0', foliage_mat(random.uniform(-0.06, 0.06)))

def make_pine_tree(parent, x, y, z, scale=1.0, seed=0):
    """Pine tree: tapered trunk + 3 stacked cone-shaped foliage layers."""
    random.seed(seed)
    name = f'PTree_{seed}'
    base = f'{parent}/{name}'
    
    # Trunk
    sa(parent, 'MeshInstance3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08 * scale,
        'bottom_radius': 0.2 * scale,
        'height': 3.0 * scale,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 1.5 * scale, 'z': z})
    ss(f'{base}_Trunk', 'surface_material_override/0', bark_mat())
    
    # 3 cone layers (using cylinder with very small top radius = cone)
    cone_configs = [
        {'top': 0.02, 'bottom': 1.5, 'height': 3.0, 'y_offset': 3.0},
        {'top': 0.02, 'bottom': 1.0, 'height': 2.5, 'y_offset': 5.0},
        {'top': 0.02, 'bottom': 0.5, 'height': 2.0, 'y_offset': 6.8},
    ]
    
    for i, cfg in enumerate(cone_configs):
        sa(parent, 'MeshInstance3D', f'{name}_Cone{i}')
        ss(f'{base}_Cone{i}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': cfg['top'] * scale,
            'bottom_radius': cfg['bottom'] * scale,
            'height': cfg['height'] * scale,
            'radial_segments': 7,
            'rings': 0
        })
        ss(f'{base}_Cone{i}', 'position', {'x': x, 'y': y + cfg['y_offset'] * scale, 'z': z})
        ss(f'{base}_Cone{i}', 'surface_material_override/0', pine_mat())

def make_dead_tree(parent, x, y, z, scale=1.0, seed=0):
    """Dead tree: bare tapered trunk with angled branch stubs."""
    random.seed(seed)
    name = f'DTree_{seed}'
    base = f'{parent}/{name}'
    
    # Main trunk
    sa(parent, 'MeshInstance3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08 * scale,
        'bottom_radius': 0.22 * scale,
        'height': 5.0 * scale,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 2.5 * scale, 'z': z})
    ss(f'{base}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-5, 5),
        'y': random.uniform(0, 360),
        'z': random.uniform(-5, 5)
    })
    ss(f'{base}_Trunk', 'surface_material_override/0', dead_bark_mat())
    
    # 3 branch stubs
    for b in range(3):
        angle = random.uniform(0, 360)
        height = random.uniform(2.0, 4.0) * scale
        bx = x + math.cos(math.radians(angle)) * 0.3 * scale
        by = y + height
        bz = z + math.sin(math.radians(angle)) * 0.3 * scale
        
        sa(parent, 'MeshInstance3D', f'{name}_Branch{b}')
        ss(f'{base}_Branch{b}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.03 * scale,
            'bottom_radius': 0.08 * scale,
            'height': 1.5 * scale,
            'radial_segments': 5,
            'rings': 0
        })
        ss(f'{base}_Branch{b}', 'position', {'x': bx, 'y': by, 'z': bz})
        ss(f'{base}_Branch{b}', 'rotation_degrees', {
            'x': random.uniform(40, 70),
            'y': angle,
            'z': 0
        })
        ss(f'{base}_Branch{b}', 'surface_material_override/0', dead_bark_mat())

# ============================================================
# 3. Place trees — same positions as before
# ============================================================
seed_counter = 1000

# Town trees (20)
print("\n=== Placing Town Trees ===")
random.seed(42)
for i in range(20):
    x = random.uniform(-60, 60)
    z = random.uniform(50, 170)
    if 15 < x < 45 and 105 < z < 135:
        continue
    if 25 < x < 35 and 60 < z < 140:
        continue
    s = random.uniform(0.8, 1.4)
    t = random.random()
    if t < 0.6:
        make_round_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    elif t < 0.85:
        make_pine_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    else:
        make_dead_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  20 town trees placed!")

# Forest trees (35) — more pines, denser
print("=== Placing Forest Trees ===")
random.seed(77)
for i in range(35):
    x = random.uniform(-40, 40)
    z = random.uniform(140, 180)
    s = random.uniform(0.7, 1.6)
    t = random.random()
    if t < 0.3:
        make_round_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    elif t < 0.85:
        make_pine_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    else:
        make_dead_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  35 forest trees placed!")

# Lake trees (8)
print("=== Placing Lake Trees ===")
random.seed(55)
for i in range(8):
    x = random.uniform(-15, 55)
    z = random.uniform(170, 200)
    s = random.uniform(0.6, 1.0)
    make_round_tree('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  8 lake trees placed!")

# Grove trees (6)
print("=== Placing Grove Trees ===")
random.seed(333)
for i in range(6):
    x = random.uniform(-40, -20)
    z = random.uniform(90, 110)
    s = random.uniform(0.8, 1.3)
    make_round_tree('TownArea/HiddenGrove', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  6 grove trees placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print(f"Phase 30 complete — {seed_counter - 1000} trees rebuilt with MeshInstance3D + textures!")
