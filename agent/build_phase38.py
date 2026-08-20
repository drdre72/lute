#!/usr/bin/env python3
"""Phase 38: Delete old CSG tree remnants, fix tree materials on remaining
MeshInstance3D trees, add more cameras for blind spots, and fill grass gaps.
Uses file-based node list to avoid slow get_scene_tree calls."""
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
GRASS_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass.jpg'
GRASS_NORMAL_TEX = 'res://addons/open-world-database/demo/resources/terrain/textures/grass_normal.jpg'

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

def grass_tuft_mat(green_var=0.0):
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

# ============================================================
# 1. Delete old CSG tree nodes (read from file)
# ============================================================
print("=== Deleting Old CSG Trees ===")

with open('/tmp/old_trees.txt') as f:
    old_paths = [line.strip() for line in f if line.strip()]

print(f"  Found {len(old_paths)} old CSG tree nodes to delete")

deleted = 0
for p in old_paths:
    r = call_tool('node_delete', {'node_path': p})
    if r.get('ok'):
        deleted += 1
    if deleted % 50 == 0 and deleted > 0:
        print(f"  ...deleted {deleted}/{len(old_paths)}")

print(f"  Deleted {deleted}/{len(old_paths)} old CSG tree nodes")

# ============================================================
# 2. Re-apply materials to remaining MeshInstance3D tree nodes
# ============================================================
print("\n=== Re-applying Tree Materials ===")

# Get tree node names from scene file (MeshInstance3D only)
import subprocess
result = subprocess.run(
    ['grep', '-n', 'type="MeshInstance3D"', '/Users/andrebaker/periphery/scenes/main_nave.tscn'],
    capture_output=True, text=True
)

# Extract node names and parents
mesh_tree_lines = []
for line in result.stdout.split('\n'):
    if 'RTree_' in line or 'PTree_' in line or 'DTree_' in line:
        # Extract name and parent
        import re
        name_match = re.search(r'name="([^"]*)"', line)
        parent_match = re.search(r'parent="([^"]*)"', line)
        if name_match and parent_match:
            name = name_match.group(1)
            parent = parent_match.group(1)
            full_path = f'{parent}/{name}'
            mesh_tree_lines.append((full_path, name))

print(f"  Found {len(mesh_tree_lines)} MeshInstance3D tree nodes")

# Apply materials based on node name
trunk_count = 0
leaf_count = 0
cone_count = 0
branch_count = 0

for full_path, name in mesh_tree_lines:
    if '_Trunk' in name:
        if name.startswith('DTree_'):
            ss(full_path, 'surface_material_override/0', dead_bark_mat())
        else:
            ss(full_path, 'surface_material_override/0', bark_mat())
        trunk_count += 1
    elif '_Leaf' in name:
        ss(full_path, 'surface_material_override/0', foliage_mat(random.uniform(-0.06, 0.06)))
        leaf_count += 1
    elif '_Cone' in name:
        ss(full_path, 'surface_material_override/0', pine_mat())
        cone_count += 1
    elif '_Branch' in name:
        ss(full_path, 'surface_material_override/0', dead_bark_mat())
        branch_count += 1

print(f"  Materials applied: {trunk_count} trunks, {leaf_count} leaves, {cone_count} cones, {branch_count} branches")

# ============================================================
# 3. Add more cameras for blind spots
# ============================================================
print("\n=== Adding Cameras ===")

# Camera: Graveyard view
sa('TownArea/Graveyard', 'Camera3D', 'Cam_Graveyard')
ss('TownArea/Graveyard/Cam_Graveyard', 'position', {'x': -25, 'y': 8, 'z': 210})
ss('TownArea/Graveyard/Cam_Graveyard', 'rotation_degrees', {'x': -25, 'y': -30, 'z': 0})
ss('TownArea/Graveyard/Cam_Graveyard', 'fov', 65)
print("  Graveyard camera added")

# Camera: Training ground
sa('TownArea/TrainingGround', 'Camera3D', 'Cam_TrainingGround')
ss('TownArea/TrainingGround/Cam_TrainingGround', 'position', {'x': -35, 'y': 6, 'z': 170})
ss('TownArea/TrainingGround/Cam_TrainingGround', 'rotation_degrees', {'x': -15, 'y': 20, 'z': 0})
ss('TownArea/TrainingGround/Cam_TrainingGround', 'fov', 60)
print("  Training ground camera added")

# Camera: Mountain pass
sa('TownArea/Terrain', 'Camera3D', 'Cam_MountainPass')
ss('TownArea/Terrain/Cam_MountainPass', 'position', {'x': 0, 'y': 25, 'z': 240})
ss('TownArea/Terrain/Cam_MountainPass', 'rotation_degrees', {'x': -40, 'y': 180, 'z': 0})
ss('TownArea/Terrain/Cam_MountainPass', 'fov', 70)
print("  Mountain pass camera added")

# Camera: Lake dock closeup
sa('TownArea/LakeRegion', 'Camera3D', 'Cam_LakeDock')
ss('TownArea/LakeRegion/Cam_LakeDock', 'position', {'x': 15, 'y': 4, 'z': 172})
ss('TownArea/LakeRegion/Cam_LakeDock', 'rotation_degrees', {'x': -10, 'y': 10, 'z': 0})
ss('TownArea/LakeRegion/Cam_LakeDock', 'fov', 55)
print("  Lake dock camera added")

# Camera: Town well closeup
sa('TownArea/TownSquare', 'Camera3D', 'Cam_TownWell')
ss('TownArea/TownSquare/Cam_TownWell', 'position', {'x': 25, 'y': 3, 'z': 115})
ss('TownArea/TownSquare/Cam_TownWell', 'rotation_degrees', {'x': -8, 'y': 25, 'z': 0})
ss('TownArea/TownSquare/Cam_TownWell', 'fov', 50)
print("  Town well camera added")

# Camera: Hidden grove
sa('TownArea/HiddenGrove', 'Camera3D', 'Cam_HiddenGrove')
ss('TownArea/HiddenGrove/Cam_HiddenGrove', 'position', {'x': -20, 'y': 5, 'z': 100})
ss('TownArea/HiddenGrove/Cam_HiddenGrove', 'rotation_degrees', {'x': -12, 'y': 35, 'z': 0})
ss('TownArea/HiddenGrove/Cam_HiddenGrove', 'fov', 60)
print("  Hidden grove camera added")

# Camera: Bridge closeup
sa('TownArea/Terrain', 'Camera3D', 'Cam_Bridge')
ss('TownArea/Terrain/Cam_Bridge', 'position', {'x': 35, 'y': 3, 'z': 135})
ss('TownArea/Terrain/Cam_Bridge', 'rotation_degrees', {'x': -5, 'y': -20, 'z': 0})
ss('TownArea/Terrain/Cam_Bridge', 'fov', 55)
print("  Bridge camera added")

# Camera: Market square closeup
sa('TownArea/TownSquare', 'Camera3D', 'Cam_Market')
ss('TownArea/TownSquare/Cam_Market', 'position', {'x': 30, 'y': 4, 'z': 105})
ss('TownArea/TownSquare/Cam_Market', 'rotation_degrees', {'x': -10, 'y': 0, 'z': 0})
ss('TownArea/TownSquare/Cam_Market', 'fov', 60)
print("  Market square camera added")

# ============================================================
# 4. Fill grass gaps — more tufts in sparse areas
# ============================================================
print("\n=== Filling Grass Gaps ===")

seed_counter = 20000

def make_grass_tuft(parent, x, y, z, scale=1.0, seed=0):
    random.seed(seed)
    name = f'Grass_{seed}'
    base = f'{parent}/{name}'
    blade_count = random.randint(3, 5)
    for i in range(blade_count):
        angle = random.uniform(0, 6.28318)
        r = random.uniform(0, 0.15) * scale
        bx = x + math.cos(angle) * r
        bz = z + math.sin(angle) * r
        bh = random.uniform(0.15, 0.35) * scale
        sa(parent, 'MeshInstance3D', f'{name}_Blade{i}')
        ss(f'{base}_Blade{i}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.0,
            'bottom_radius': 0.05 * scale,
            'height': bh,
            'radial_segments': 4,
            'rings': 0
        })
        ss(f'{base}_Blade{i}', 'position', {'x': bx, 'y': y + bh / 2, 'z': bz})
        ss(f'{base}_Blade{i}', 'rotation_degrees', {
            'x': random.uniform(-10, 10),
            'y': random.uniform(0, 360),
            'z': random.uniform(-10, 10)
        })
        ss(f'{base}_Blade{i}', 'surface_material_override/0', grass_tuft_mat(random.uniform(-0.08, 0.08)))

# Dense grass in town area
random.seed(101)
for i in range(40):
    x = random.uniform(-55, 65)
    z = random.uniform(50, 145)
    if 15 < x < 45 and 105 < z < 135:
        continue
    if 25 < x < 35 and 60 < z < 140:
        continue
    s = random.uniform(0.8, 1.4)
    make_grass_tuft('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Dense forest grass
random.seed(102)
for i in range(35):
    x = random.uniform(-38, 38)
    z = random.uniform(142, 178)
    s = random.uniform(0.7, 1.3)
    make_grass_tuft('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Lake area grass
random.seed(103)
for i in range(25):
    x = random.uniform(-15, 55)
    z = random.uniform(192, 212)
    s = random.uniform(0.8, 1.5)
    make_grass_tuft('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Temple area grass
random.seed(104)
for i in range(20):
    x = random.uniform(-35, 35)
    z = random.uniform(-15, 25)
    s = random.uniform(0.7, 1.2)
    make_grass_tuft('Architecture/Exterior/ExteriorPlaza', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Grove grass
random.seed(105)
for i in range(15):
    x = random.uniform(-38, -22)
    z = random.uniform(92, 108)
    s = random.uniform(0.8, 1.3)
    make_grass_tuft('TownArea/HiddenGrove', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Grass along path edges
random.seed(106)
for z in range(25, 140, 2):
    for side in [-1, 1]:
        x = 30 + side * random.uniform(3, 8)
        s = random.uniform(0.6, 1.0)
        make_grass_tuft('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
        seed_counter += 1

# Grass near graveyard
random.seed(107)
for i in range(15):
    x = random.uniform(-55, -25)
    z = random.uniform(190, 220)
    s = random.uniform(0.7, 1.2)
    make_grass_tuft('TownArea/Graveyard', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

print(f"  {seed_counter - 20000} grass tufts added to fill gaps!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 38 complete — old trees deleted, materials fixed, cameras added, grass filled!")
