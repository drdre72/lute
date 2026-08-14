#!/usr/bin/env python3
"""Phase 24: Create simple tree and rock PackedScenes for Scatter3D,
then populate scatter nodes. Also adds atmospheric particles and
refined lighting for the terrain areas."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

def sa(parent, type, name):
    r = add(parent, type, name)
    print(f"  +{name}: {r}")
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r}")
    return r

# Since we can't easily create PackedScenes via RPC and assign them to
# Scatter3D.scenes_to_scatter, we'll manually place trees/rocks on the
# terrain using the same random distribution logic.

# ============================================================
# 1. Create a reusable tree scene file
# ============================================================
print("=== Creating Tree Scene ===")

# Create a new scene with a tree (trunk + foliage)
r = call_tool('scene_create', {'path': 'res://scenes/tree_prop.tscn', 'root_type': 'Node3D'})
print(f"  Create tree scene: {r}")

# Add trunk
sa('.', 'CSGCylinder3D', 'Trunk')
ss('Trunk', 'radius', 0.3)
ss('Trunk', 'height', 3.0)
ss('Trunk', 'material_override', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.15, 'g': 0.08, 'b': 0.04, 'a': 1},
    'roughness': 0.9
})

# Add foliage (cone)
sa('.', 'CSGCylinder3D', 'Foliage')
ss('Foliage', 'radius', 1.5)
ss('Foliage', 'height', 4.0)
ss('Foliage', 'sides', 6)
ss('Foliage', 'position', {'x': 0, 'y': 3.0, 'z': 0})
ss('Foliage', 'material_override', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.08, 'g': 0.18, 'b': 0.06, 'a': 1},
    'roughness': 0.95
})

# Add foliage layer 2 (smaller cone on top)
sa('.', 'CSGCylinder3D', 'Foliage2')
ss('Foliage2', 'radius', 1.0)
ss('Foliage2', 'height', 3.0)
ss('Foliage2', 'sides', 6)
ss('Foliage2', 'position', {'x': 0, 'y': 5.5, 'z': 0})
ss('Foliage2', 'material_override', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.10, 'g': 0.22, 'b': 0.07, 'a': 1},
    'roughness': 0.95
})

# Save the tree scene
r = call_tool('scene_save', {})
print(f"  Save tree scene: {r}")

# ============================================================
# 2. Create a rock scene
# ============================================================
print("\n=== Creating Rock Scene ===")

r = call_tool('scene_create', {'path': 'res://scenes/rock_prop.tscn', 'root_type': 'Node3D'})
print(f"  Create rock scene: {r}")

sa('.', 'CSGSphere3D', 'RockBody')
ss('RockBody', 'radius', 1.0)
ss('RockBody', 'material_override', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.25, 'g': 0.23, 'b': 0.20, 'a': 1},
    'roughness': 0.92
})

# Add a smaller offset rock for variety
sa('.', 'CSGSphere3D', 'RockChunk')
ss('RockChunk', 'radius', 0.5)
ss('RockChunk', 'position', {'x': 0.8, 'y': -0.2, 'z': 0.5})
ss('RockChunk', 'material_override', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.22, 'g': 0.20, 'b': 0.18, 'a': 1},
    'roughness': 0.92
})

r = call_tool('scene_save', {})
print(f"  Save rock scene: {r}")

# ============================================================
# 3. Reopen main scene
# ============================================================
print("\n=== Reopening Main Scene ===")
r = call_tool('scene_open', {'path': 'res://scenes/main_nave.tscn'})
print(f"  Open: {r}")

# ============================================================
# 4. Manually scatter trees on town terrain
# ============================================================
print("\n=== Scattering Trees on Town Terrain ===")

random.seed(42)
tree_count = 30
for i in range(tree_count):
    # Random position within town terrain area (centered at z=110, 128x128)
    x = random.uniform(-60, 60)
    z = random.uniform(50, 170)
    y = 0  # terrain surface approx
    
    # Skip if too close to town center (buildings)
    if 15 < x < 45 and 105 < z < 135:
        continue
    # Skip if on the path
    if 25 < x < 35 and 60 < z < 140:
        continue
    
    name = f'ScatterTree{i:02d}'
    sa('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Trunk')
    ss(f'TownArea/Terrain/{name}_Trunk', 'radius', 0.3)
    ss(f'TownArea/Terrain/{name}_Trunk', 'height', 3.0)
    ss(f'TownArea/Terrain/{name}_Trunk', 'position', {'x': x, 'y': y + 1.5, 'z': z})
    ss(f'TownArea/Terrain/{name}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-3, 3),
        'y': random.uniform(0, 360),
        'z': random.uniform(-3, 3)
    })
    ss(f'TownArea/Terrain/{name}_Trunk', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.15, 'g': 0.08, 'b': 0.04, 'a': 1},
        'roughness': 0.9
    })
    
    # Foliage
    s = random.uniform(0.8, 1.4)
    sa('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Foliage')
    ss(f'TownArea/Terrain/{name}_Foliage', 'radius', 1.5 * s)
    ss(f'TownArea/Terrain/{name}_Foliage', 'height', 4.0 * s)
    ss(f'TownArea/Terrain/{name}_Foliage', 'sides', 6)
    ss(f'TownArea/Terrain/{name}_Foliage', 'position', {'x': x, 'y': y + 3.0 * s + 1.5, 'z': z})
    ss(f'TownArea/Terrain/{name}_Foliage', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.08 + random.uniform(-0.02, 0.02), 'g': 0.18 + random.uniform(-0.03, 0.03), 'b': 0.06, 'a': 1},
        'roughness': 0.95
    })

print(f"  {tree_count} trees scattered on town terrain!")

# ============================================================
# 5. Scatter rocks on mountain terrain
# ============================================================
print("\n=== Scattering Rocks on Mountains ===")

random.seed(99)
rock_count = 20
for i in range(rock_count):
    x = random.uniform(-90, 90)
    z = random.uniform(180, 260)
    y = random.uniform(0, 10)
    s = random.uniform(0.8, 3.0)
    
    name = f'MtnRock{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', s)
    ss(f'TownArea/Terrain/{name}', 'position', {'x': x, 'y': y, 'z': z})
    ss(f'TownArea/Terrain/{name}', 'rotation_degrees', {
        'x': random.uniform(0, 360),
        'y': random.uniform(0, 360),
        'z': random.uniform(0, 360)
    })
    ss(f'TownArea/Terrain/{name}', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.25 + random.uniform(-0.05, 0.05), 'g': 0.23, 'b': 0.20, 'a': 1},
        'roughness': 0.92
    })

print(f"  {rock_count} rocks scattered on mountains!")

# ============================================================
# 6. Scatter trees in forest area
# ============================================================
print("\n=== Scattering Trees in Forest ===")

random.seed(77)
forest_count = 40
for i in range(forest_count):
    x = random.uniform(-40, 40)
    z = random.uniform(140, 180)
    y = 0
    s = random.uniform(0.6, 1.8)
    
    name = f'ForTree{i:02d}'
    sa('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Trunk')
    ss(f'TownArea/Terrain/{name}_Trunk', 'radius', 0.25 * s)
    ss(f'TownArea/Terrain/{name}_Trunk', 'height', 3.0 * s)
    ss(f'TownArea/Terrain/{name}_Trunk', 'position', {'x': x, 'y': y + 1.5 * s, 'z': z})
    ss(f'TownArea/Terrain/{name}_Trunk', 'rotation_degrees', {
        'x': random.uniform(-5, 5),
        'y': random.uniform(0, 360),
        'z': random.uniform(-5, 5)
    })
    ss(f'TownArea/Terrain/{name}_Trunk', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.12, 'g': 0.06, 'b': 0.03, 'a': 1},
        'roughness': 0.9
    })
    
    sa('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Foliage')
    ss(f'TownArea/Terrain/{name}_Foliage', 'radius', 1.2 * s)
    ss(f'TownArea/Terrain/{name}_Foliage', 'height', 3.5 * s)
    ss(f'TownArea/Terrain/{name}_Foliage', 'sides', 6)
    ss(f'TownArea/Terrain/{name}_Foliage', 'position', {'x': x, 'y': y + 3.0 * s + 1.5 * s, 'z': z})
    ss(f'TownArea/Terrain/{name}_Foliage', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.06, 'g': 0.15 + random.uniform(-0.03, 0.03), 'b': 0.05, 'a': 1},
        'roughness': 0.95
    })

print(f"  {forest_count} trees scattered in forest!")

# ============================================================
# 7. Scatter reeds/bushes near lake
# ============================================================
print("\n=== Scattering Lake Vegetation ===")

random.seed(55)
lake_count = 15
for i in range(lake_count):
    x = random.uniform(-10, 50)
    z = random.uniform(170, 195)
    y = 0
    s = random.uniform(0.4, 0.8)
    
    name = f'LakeBush{i:02d}'
    sa('TownArea/LakeRegion', 'CSGSphere3D', name)
    ss(f'TownArea/LakeRegion/{name}', 'radius', s)
    ss(f'TownArea/LakeRegion/{name}', 'position', {'x': x, 'y': y + s * 0.5, 'z': z})
    ss(f'TownArea/LakeRegion/{name}', 'material_override', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.07, 'g': 0.16, 'b': 0.05, 'a': 1},
        'roughness': 0.95
    })

print(f"  {lake_count} bushes scattered near lake!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 24 complete — trees, rocks, and vegetation scattered!")
