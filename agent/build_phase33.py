#!/usr/bin/env python3
"""Phase 33: Add grass tufts, flowers, and small vegetation details.
Uses small cone/sphere meshes with foliage texture for grass clumps,
and colored spheres for flowers. Scattered across grassy areas."""
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

FOLIAGE_TEX = 'res://addons/open-world-database/demo/resources/tree/foliage.png'

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

def flower_mat(color):
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': color,
        'roughness': 0.8,
        'emission': color,
        'emission_energy_multiplier': 0.15
    }

FLOWER_COLORS = [
    {'r': 0.9, 'g': 0.2, 'b': 0.2, 'a': 1},  # Red
    {'r': 0.9, 'g': 0.8, 'b': 0.2, 'a': 1},  # Yellow
    {'r': 0.8, 'g': 0.3, 'b': 0.9, 'a': 1},  # Purple
    {'r': 0.9, 'g': 0.9, 'b': 0.9, 'a': 1},  # White
    {'r': 0.3, 'g': 0.6, 'b': 0.9, 'a': 1},  # Blue
]

def make_grass_tuft(parent, x, y, z, scale=1.0, seed=0):
    """Small grass clump using a low cone mesh with foliage texture."""
    random.seed(seed)
    name = f'Grass_{seed}'
    base = f'{parent}/{name}'
    
    # 3-5 small blades (cones)
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

def make_flower(parent, x, y, z, scale=1.0, seed=0):
    """Small flower with stem + colored bloom."""
    random.seed(seed)
    name = f'Flower_{seed}'
    base = f'{parent}/{name}'
    color = random.choice(FLOWER_COLORS)
    
    # Stem
    sa(parent, 'MeshInstance3D', f'{name}_Stem')
    ss(f'{base}_Stem', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.01 * scale,
        'bottom_radius': 0.02 * scale,
        'height': 0.3 * scale,
        'radial_segments': 4,
        'rings': 0
    })
    ss(f'{base}_Stem', 'position', {'x': x, 'y': y + 0.15 * scale, 'z': z})
    ss(f'{base}_Stem', 'surface_material_override/0', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.1, 'g': 0.4, 'b': 0.1, 'a': 1},
        'roughness': 0.9
    })
    
    # Bloom
    sa(parent, 'MeshInstance3D', f'{name}_Bloom')
    ss(f'{base}_Bloom', 'mesh', {
        'class': 'SphereMesh',
        'radius': 0.06 * scale,
        'height': 0.12 * scale,
        'radial_segments': 5,
        'rings': 3
    })
    ss(f'{base}_Bloom', 'position', {'x': x, 'y': y + 0.32 * scale, 'z': z})
    ss(f'{base}_Bloom', 'surface_material_override/0', flower_mat(color))

# ============================================================
# Place grass tufts across all grassy areas
# ============================================================
print("=== Placing Grass Tufts ===")

seed_counter = 7000

# Town area grass tufts
random.seed(11)
for i in range(30):
    x = random.uniform(-50, 60)
    z = random.uniform(50, 140)
    if 15 < x < 45 and 105 < z < 135:
        continue
    if 25 < x < 35 and 60 < z < 140:
        continue
    s = random.uniform(0.7, 1.3)
    make_grass_tuft('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Forest grass tufts
random.seed(12)
for i in range(25):
    x = random.uniform(-35, 35)
    z = random.uniform(145, 175)
    s = random.uniform(0.6, 1.2)
    make_grass_tuft('TownArea/Terrain', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Lake grass tufts
random.seed(13)
for i in range(15):
    x = random.uniform(-10, 50)
    z = random.uniform(195, 210)
    s = random.uniform(0.8, 1.4)
    make_grass_tuft('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Temple grass tufts
random.seed(14)
for i in range(15):
    x = random.uniform(-40, 40)
    z = random.uniform(-15, 20)
    s = random.uniform(0.7, 1.2)
    make_grass_tuft('Architecture/Exterior/ExteriorPlaza', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Grove grass tufts
random.seed(15)
for i in range(10):
    x = random.uniform(-38, -22)
    z = random.uniform(92, 108)
    s = random.uniform(0.8, 1.3)
    make_grass_tuft('TownArea/HiddenGrove', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

print(f"  {seed_counter - 7000} grass tufts placed!")

# ============================================================
# Place flowers in selective areas
# ============================================================
print("\n=== Placing Flowers ===")

# Flowers near town square
random.seed(21)
for i in range(12):
    x = random.uniform(10, 50)
    z = random.uniform(100, 130)
    if 25 < x < 35:
        continue
    s = random.uniform(0.8, 1.2)
    make_flower('TownArea/TownSquare', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Flowers in grove
random.seed(22)
for i in range(8):
    x = random.uniform(-35, -25)
    z = random.uniform(95, 105)
    s = random.uniform(0.9, 1.3)
    make_flower('TownArea/HiddenGrove', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Flowers near lake
random.seed(23)
for i in range(10):
    x = random.uniform(0, 45)
    z = random.uniform(195, 205)
    s = random.uniform(0.8, 1.2)
    make_flower('TownArea/LakeRegion', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

# Flowers at temple
random.seed(24)
for i in range(8):
    x = random.uniform(-30, 30)
    z = random.uniform(-10, 15)
    s = random.uniform(0.8, 1.2)
    make_flower('Architecture/Exterior/ExteriorPlaza', x, 0, z, scale=s, seed=seed_counter)
    seed_counter += 1

print(f"  {seed_counter - 7000 - 95} flowers placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print(f"Phase 33 complete — grass tufts and flowers added!")
