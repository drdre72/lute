#!/usr/bin/env python3
"""Phase 27: Replace simple cone trees with realistic multi-layer trees.
Adds: layered canopy, trunk taper, branch hints, bark texture variation,
dead trees, and pine variants. Deletes old simple trees first."""
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

# ============================================================
# 1. Delete old simple trees (trunk + single cone foliage)
# ============================================================
print("=== Deleting Old Simple Trees ===")

# Get scene tree to find old tree nodes
r = call_tool('get_scene_tree', {})
tree = r.get('tree', {})

old_trees = []
def find_old_trees(node, path=''):
    name = node.get('name', '')
    full_path = path + '/' + name if path else name
    # Match ScatterTree, ForTree patterns (trunk and foliage parts)
    if name.startswith('ScatterTree') or name.startswith('ForTree'):
        old_trees.append(full_path)
    for child in node.get('children', []):
        find_old_trees(child, full_path)

find_old_trees(tree)

# Deduplicate parent paths (each tree has _Trunk and _Foliage)
tree_parents = set()
for p in old_trees:
    # Remove _Trunk or _Foliage suffix to get base
    base = p.rsplit('_', 1)[0]
    tree_parents.add(base)

print(f"  Found {len(tree_parents)} old trees to replace")

# Delete all old tree parts
for p in sorted(old_trees, reverse=True):
    r = call_tool('node_delete', {'node_path': p})
    
print(f"  Deleted {len(old_trees)} old tree nodes")

# ============================================================
# 2. Realistic tree materials
# ============================================================

# Bark — dark weathered wood
BARK_MAT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.18, 'g': 0.10, 'b': 0.05, 'a': 1},
    'roughness': 0.92,
    'metallic': 0.0
}

BARK_MAT_LIGHT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.25, 'g': 0.15, 'b': 0.08, 'a': 1},
    'roughness': 0.9
}

# Canopy — deep green with variation
def canopy_mat(g_offset=0, r_offset=0):
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {
            'r': max(0.05, 0.08 + r_offset),
            'g': max(0.10, 0.20 + g_offset),
            'b': max(0.04, 0.06 + r_offset * 0.5),
            'a': 1
        },
        'roughness': 0.95
    }

# Dead tree — no foliage, bare branches
DEAD_BARK = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.20, 'g': 0.16, 'b': 0.10, 'a': 1},
    'roughness': 0.95
}

# Pine — darker, bluish green
PINE_MAT = {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.05, 'g': 0.14, 'b': 0.08, 'a': 1},
    'roughness': 0.93
}

# ============================================================
# 3. Tree builder functions
# ============================================================

def make_round_tree(parent, x, y, z, scale=1.0, seed=0):
    """Multi-layer deciduous tree with tapered trunk and 3 canopy layers."""
    random.seed(seed)
    name = f'RTree_{seed}'
    base = f'{parent}/{name}'
    
    # Trunk — tapered (use cylinder, slightly tilted)
    sa(parent, 'CSGCylinder3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'radius', 0.35 * scale)
    ss(f'{base}_Trunk', 'height', 4.0 * scale)
    ss(f'{base}_Trunk', 'sides', 8)
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 2.0 * scale, 'z': z})
    ss(f'{base}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-2, 2),
        'y': random.uniform(0, 360),
        'z': random.uniform(-2, 2)
    })
    ss(f'{base}_Trunk', 'material_override', BARK_MAT)
    
    # Canopy layer 1 — widest, lowest
    sa(parent, 'CSGSphere3D', f'{name}_Canopy1')
    ss(f'{base}_Canopy1', 'radius', 2.0 * scale)
    ss(f'{base}_Canopy1', 'position', {'x': x, 'y': y + 4.5 * scale, 'z': z})
    ss(f'{base}_Canopy1', 'material_override', canopy_mat(g_offset=random.uniform(-0.03, 0.03)))
    
    # Canopy layer 2 — medium, middle
    sa(parent, 'CSGSphere3D', f'{name}_Canopy2')
    ss(f'{base}_Canopy2', 'radius', 1.5 * scale)
    ss(f'{base}_Canopy2', 'position', {'x': x + random.uniform(-0.3, 0.3), 'y': y + 6.0 * scale, 'z': z + random.uniform(-0.3, 0.3)})
    ss(f'{base}_Canopy2', 'material_override', canopy_mat(g_offset=random.uniform(-0.04, 0.04)))
    
    # Canopy layer 3 — smallest, top
    sa(parent, 'CSGSphere3D', f'{name}_Canopy3')
    ss(f'{base}_Canopy3', 'radius', 1.0 * scale)
    ss(f'{base}_Canopy3', 'position', {'x': x + random.uniform(-0.2, 0.2), 'y': y + 7.2 * scale, 'z': z + random.uniform(-0.2, 0.2)})
    ss(f'{base}_Canopy3', 'material_override', canopy_mat(g_offset=random.uniform(-0.05, 0.05), r_offset=random.uniform(-0.02, 0.02)))

def make_pine_tree(parent, x, y, z, scale=1.0, seed=0):
    """Pine tree with stacked cones."""
    random.seed(seed)
    name = f'PTree_{seed}'
    base = f'{parent}/{name}'
    
    # Trunk
    sa(parent, 'CSGCylinder3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'radius', 0.25 * scale)
    ss(f'{base}_Trunk', 'height', 3.0 * scale)
    ss(f'{base}_Trunk', 'sides', 6)
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 1.5 * scale, 'z': z})
    ss(f'{base}_Trunk', 'material_override', BARK_MAT)
    
    # Cone 1 — bottom, widest
    sa(parent, 'CSGCylinder3D', f'{name}_Cone1')
    ss(f'{base}_Cone1', 'radius', 1.5 * scale)
    ss(f'{base}_Cone1', 'height', 3.0 * scale)
    ss(f'{base}_Cone1', 'sides', 8)
    ss(f'{base}_Cone1', 'position', {'x': x, 'y': y + 3.0 * scale, 'z': z})
    ss(f'{base}_Cone1', 'material_override', PINE_MAT)
    
    # Cone 2 — middle
    sa(parent, 'CSGCylinder3D', f'{name}_Cone2')
    ss(f'{base}_Cone2', 'radius', 1.0 * scale)
    ss(f'{base}_Cone2', 'height', 2.5 * scale)
    ss(f'{base}_Cone2', 'sides', 8)
    ss(f'{base}_Cone2', 'position', {'x': x, 'y': y + 5.0 * scale, 'z': z})
    ss(f'{base}_Cone2', 'material_override', PINE_MAT)
    
    # Cone 3 — top
    sa(parent, 'CSGCylinder3D', f'{name}_Cone3')
    ss(f'{base}_Cone3', 'radius', 0.5 * scale)
    ss(f'{base}_Cone3', 'height', 2.0 * scale)
    ss(f'{base}_Cone3', 'sides', 8)
    ss(f'{base}_Cone3', 'position', {'x': x, 'y': y + 6.8 * scale, 'z': z})
    ss(f'{base}_Cone3', 'material_override', PINE_MAT)

def make_dead_tree(parent, x, y, z, scale=1.0, seed=0):
    """Dead tree — bare trunk with branch stubs."""
    random.seed(seed)
    name = f'DTree_{seed}'
    base = f'{parent}/{name}'
    
    # Main trunk
    sa(parent, 'CSGCylinder3D', f'{name}_Trunk')
    ss(f'{base}_Trunk', 'radius', 0.3 * scale)
    ss(f'{base}_Trunk', 'height', 5.0 * scale)
    ss(f'{base}_Trunk', 'sides', 6)
    ss(f'{base}_Trunk', 'position', {'x': x, 'y': y + 2.5 * scale, 'z': z})
    ss(f'{base}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-5, 5),
        'y': random.uniform(0, 360),
        'z': random.uniform(-5, 5)
    })
    ss(f'{base}_Trunk', 'material_override', DEAD_BARK)
    
    # Branch stubs (small cylinders at angles)
    for b in range(3):
        angle = random.uniform(0, 360)
        height = random.uniform(2.0, 4.0) * scale
        sa(parent, 'CSGCylinder3D', f'{name}_Branch{b}')
        ss(f'{base}_Branch{b}', 'radius', 0.12 * scale)
        ss(f'{base}_Branch{b}', 'height', 1.5 * scale)
        ss(f'{base}_Branch{b}', 'sides', 5)
        ss(f'{base}_Branch{b}', 'position', {
            'x': x + math.cos(math.radians(angle)) * 0.5 * scale,
            'y': y + height,
            'z': z + math.sin(math.radians(angle)) * 0.5 * scale
        })
        ss(f'{base}_Branch{b}', 'rotation_degrees', {
            'x': random.uniform(40, 70),
            'y': angle,
            'z': 0
        })
        ss(f'{base}_Branch{b}', 'material_override', DEAD_BARK)

# ============================================================
# 4. Place realistic trees on town terrain
# ============================================================
print("\n=== Placing Realistic Town Trees ===")

random.seed(42)
town_trees = 20
seed_counter = 1000
for i in range(town_trees):
    x = random.uniform(-60, 60)
    z = random.uniform(50, 170)
    
    # Skip buildings and path
    if 15 < x < 45 and 105 < z < 135:
        continue
    if 25 < x < 35 and 60 < z < 140:
        continue
    
    s = random.uniform(0.8, 1.4)
    tree_type = random.random()
    
    if tree_type < 0.6:
        make_round_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    elif tree_type < 0.85:
        make_pine_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    else:
        make_dead_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    
    seed_counter += 1

print(f"  {town_trees} realistic town trees placed!")

# ============================================================
# 5. Place realistic forest trees (denser, more pines)
# ============================================================
print("\n=== Placing Realistic Forest Trees ===")

random.seed(77)
forest_trees = 35
for i in range(forest_trees):
    x = random.uniform(-40, 40)
    z = random.uniform(140, 180)
    s = random.uniform(0.7, 1.6)
    tree_type = random.random()
    
    if tree_type < 0.3:
        make_round_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    elif tree_type < 0.85:
        make_pine_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    else:
        make_dead_tree('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    
    seed_counter += 1

print(f"  {forest_trees} realistic forest trees placed!")

# ============================================================
# 6. Place a few trees near lake
# ============================================================
print("\n=== Placing Lake Trees ===")

random.seed(55)
lake_trees = 8
for i in range(lake_trees):
    x = random.uniform(-15, 55)
    z = random.uniform(170, 200)
    s = random.uniform(0.6, 1.0)
    make_round_tree('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

print(f"  {lake_trees} lake trees placed!")

# ============================================================
# 7. Place trees in hidden grove
# ============================================================
print("\n=== Placing Grove Trees ===")

random.seed(333)
grove_trees = 6
for i in range(grove_trees):
    x = random.uniform(-40, -20)
    z = random.uniform(90, 110)
    s = random.uniform(0.8, 1.3)
    make_round_tree('TownArea/HiddenGrove', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

print(f"  {grove_trees} grove trees placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print(f"Phase 27 complete — {town_trees + forest_trees + lake_trees + grove_trees} realistic trees placed!")
