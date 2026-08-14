#!/usr/bin/env python3
"""Phase 31: Upgrade rocks with rock.jpg texture from OWDB demo.
Replace CSG sphere rocks with CapsuleMesh-based rocks like the demo,
and add scattered small stones."""
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

ROCK_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/rock.jpg'

def rock_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': ROCK_TEX,
        'roughness': 0.9,
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 50.0
    }

# ============================================================
# 1. Find and delete old rock nodes
# ============================================================
print("=== Finding Old Rocks ===")

r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

old_rocks = []
def find_rocks(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    if name.startswith('Rock_') or name.startswith('ScatterRock') or name.startswith('MtnRock'):
        old_rocks.append(full_path)
    for child in node.get('children', []):
        find_rocks(child, full_path)

find_rocks(tree)
print(f"  Found {len(old_rocks)} old rock parts")

for p in sorted(old_rocks, reverse=True):
    call_tool('node_delete', {'node_path': p})
print("  Deleted old rocks")

# ============================================================
# 2. Rock builder — CapsuleMesh clusters like OWDB demo
# ============================================================

def make_rock(parent, x, y, z, scale=1.0, seed=0):
    """Rock made of 2-3 angled capsule meshes with rock texture."""
    random.seed(seed)
    name = f'Rock_{seed}'
    base = f'{parent}/{name}'
    
    # Main rock body
    sa(parent, 'MeshInstance3D', f'{name}_Body')
    ss(f'{base}_Body', 'mesh', {
        'class': 'CapsuleMesh',
        'radius': 0.5 * scale,
        'height': 1.0 * scale,
        'radial_segments': 5,
        'rings': 1
    })
    ss(f'{base}_Body', 'position', {'x': x, 'y': y + 0.3 * scale, 'z': z})
    ss(f'{base}_Body', 'rotation_degrees', {
        'x': random.uniform(0, 360),
        'y': random.uniform(0, 360),
        'z': random.uniform(0, 360)
    })
    ss(f'{base}_Body', 'scale', {
        'x': random.uniform(0.8, 1.3),
        'y': random.uniform(0.6, 1.0),
        'z': random.uniform(0.8, 1.3)
    })
    ss(f'{base}_Body', 'surface_material_override/0', rock_mat())
    
    # Secondary rock piece
    if random.random() < 0.7:
        sa(parent, 'MeshInstance3D', f'{name}_Piece2')
        ss(f'{base}_Piece2', 'mesh', {
            'class': 'CapsuleMesh',
            'radius': 0.3 * scale,
            'height': 0.6 * scale,
            'radial_segments': 5,
            'rings': 1
        })
        ss(f'{base}_Piece2', 'position', {
            'x': x + random.uniform(-0.4, 0.4) * scale,
            'y': y + random.uniform(0.1, 0.5) * scale,
            'z': z + random.uniform(-0.4, 0.4) * scale
        })
        ss(f'{base}_Piece2', 'rotation_degrees', {
            'x': random.uniform(0, 360),
            'y': random.uniform(0, 360),
            'z': random.uniform(0, 360)
        })
        ss(f'{base}_Piece2', 'surface_material_override/0', rock_mat())
    
    # Third small piece
    if random.random() < 0.4:
        sa(parent, 'MeshInstance3D', f'{name}_Piece3')
        ss(f'{base}_Piece3', 'mesh', {
            'class': 'CapsuleMesh',
            'radius': 0.2 * scale,
            'height': 0.4 * scale,
            'radial_segments': 5,
            'rings': 1
        })
        ss(f'{base}_Piece3', 'position', {
            'x': x + random.uniform(-0.5, 0.5) * scale,
            'y': y + random.uniform(0.0, 0.3) * scale,
            'z': z + random.uniform(-0.5, 0.5) * scale
        })
        ss(f'{base}_Piece3', 'rotation_degrees', {
            'x': random.uniform(0, 360),
            'y': random.uniform(0, 360),
            'z': random.uniform(0, 360)
        })
        ss(f'{base}_Piece3', 'surface_material_override/0', rock_mat())

# ============================================================
# 3. Place rocks on mountains (large)
# ============================================================
print("\n=== Placing Mountain Rocks ===")
random.seed(99)
seed_counter = 5000
for i in range(15):
    x = random.uniform(-30, 30)
    z = random.uniform(220, 250)
    s = random.uniform(1.5, 3.0)
    make_rock('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  15 mountain rocks placed!")

# ============================================================
# 4. Place rocks in forest (medium)
# ============================================================
print("=== Placing Forest Rocks ===")
random.seed(88)
for i in range(12):
    x = random.uniform(-35, 35)
    z = random.uniform(145, 175)
    s = random.uniform(0.6, 1.2)
    make_rock('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  12 forest rocks placed!")

# ============================================================
# 5. Place rocks near lake shore (small)
# ============================================================
print("=== Placing Lake Shore Rocks ===")
random.seed(44)
for i in range(10):
    x = random.uniform(-10, 50)
    z = random.uniform(175, 195)
    s = random.uniform(0.4, 0.8)
    make_rock('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  10 lake shore rocks placed!")

# ============================================================
# 6. Place small stones along path
# ============================================================
print("=== Placing Path Stones ===")
random.seed(22)
for i in range(15):
    x = random.uniform(26, 34)
    z = random.uniform(30, 140)
    s = random.uniform(0.2, 0.5)
    side = random.choice([-1, 1])
    make_rock('TownArea/Terrain', x + side * random.uniform(2, 5), 0, z, scale=s, seed=seed_counter)
    seed_counter += 1
print("  15 path stones placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print(f"Phase 31 complete — {seed_counter - 5000} rocks placed with rock.jpg texture!")
