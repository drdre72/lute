#!/usr/bin/env python3
"""Phase 34: Add path stepping stones, worn path edges, wooden fences
around town perimeter, and lantern posts along the main path."""
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

def stone_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_texture': ROCK_TEX,
        'roughness': 0.9,
        'uv1_triplanar': True,
        'uv1_triplanar_sharpness': 40.0
    }

def wood_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.25, 'g': 0.15, 'b': 0.08, 'a': 1},
        'roughness': 0.9
    }

def metal_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.15, 'g': 0.15, 'b': 0.15, 'a': 1},
        'roughness': 0.4,
        'metallic': 0.8
    }

def lantern_glow_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 1.0, 'g': 0.85, 'b': 0.4, 'a': 1},
        'emission': {'r': 1.0, 'g': 0.8, 'b': 0.3, 'a': 1},
        'emission_energy_multiplier': 2.0,
        'roughness': 0.3
    }

# ============================================================
# 1. Stepping stones along the main path (z: 25 to 140)
# ============================================================
print("=== Adding Stepping Stones ===")

seed_counter = 8000
random.seed(31)
for z in range(25, 140, 3):
    # 2-3 stones per step
    for s_i in range(random.randint(2, 3)):
        offset_x = random.uniform(-2, 2)
        sx = 30 + offset_x
        sz = z + random.uniform(-1, 1)
        sr = random.uniform(0.3, 0.6)
        
        sa('TownArea/Terrain', 'MeshInstance3D', f'StepStone_{seed_counter}')
        ss(f'TownArea/Terrain/StepStone_{seed_counter}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': sr * 0.8,
            'bottom_radius': sr,
            'height': 0.15,
            'radial_segments': 6,
            'rings': 0
        })
        ss(f'TownArea/Terrain/StepStone_{seed_counter}', 'position', {'x': sx, 'y': 0.08, 'z': sz})
        ss(f'TownArea/Terrain/StepStone_{seed_counter}', 'rotation_degrees', {
            'x': random.uniform(-5, 5),
            'y': random.uniform(0, 360),
            'z': random.uniform(-3, 3)
        })
        ss(f'TownArea/Terrain/StepStone_{seed_counter}', 'surface_material_override/0', stone_mat())
        seed_counter += 1

print(f"  {seed_counter - 8000} stepping stones placed!")

# ============================================================
# 2. Wooden fence around town perimeter
# ============================================================
print("\n=== Adding Town Fence ===")

fence_seed = 9000

# Fence posts + rails along north side (z=145, x: -10 to 60)
for x in range(-10, 62, 4):
    # Post
    sa('TownArea', 'MeshInstance3D', f'FencePost_N_{fence_seed}')
    ss(f'TownArea/FencePost_N_{fence_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08,
        'bottom_radius': 0.1,
        'height': 1.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/FencePost_N_{fence_seed}', 'position', {'x': x, 'y': 0.75, 'z': 145})
    ss(f'TownArea/FencePost_N_{fence_seed}', 'surface_material_override/0', wood_mat())
    fence_seed += 1

# Horizontal rail (north)
sa('TownArea', 'MeshInstance3D', 'FenceRail_N1')
ss('TownArea/FenceRail_N1', 'mesh', {
    'class': 'BoxMesh',
    'size': {'x': 72, 'y': 0.08, 'z': 0.06}
})
ss('TownArea/FenceRail_N1', 'position', {'x': 26, 'y': 1.0, 'z': 145})
ss('TownArea/FenceRail_N1', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_N2')
ss('TownArea/FenceRail_N2', 'mesh', {
    'class': 'BoxMesh',
    'size': {'x': 72, 'y': 0.08, 'z': 0.06}
})
ss('TownArea/FenceRail_N2', 'position', {'x': 26, 'y': 0.4, 'z': 145})
ss('TownArea/FenceRail_N2', 'surface_material_override/0', wood_mat())

# Fence posts along south side (z=95, x: -10 to 60)
for x in range(-10, 62, 4):
    sa('TownArea', 'MeshInstance3D', f'FencePost_S_{fence_seed}')
    ss(f'TownArea/FencePost_S_{fence_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08,
        'bottom_radius': 0.1,
        'height': 1.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/FencePost_S_{fence_seed}', 'position', {'x': x, 'y': 0.75, 'z': 95})
    ss(f'TownArea/FencePost_S_{fence_seed}', 'surface_material_override/0', wood_mat())
    fence_seed += 1

# Rails (south)
sa('TownArea', 'MeshInstance3D', 'FenceRail_S1')
ss('TownArea/FenceRail_S1', 'mesh', {'class': 'BoxMesh', 'size': {'x': 72, 'y': 0.08, 'z': 0.06}})
ss('TownArea/FenceRail_S1', 'position', {'x': 26, 'y': 1.0, 'z': 95})
ss('TownArea/FenceRail_S1', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_S2')
ss('TownArea/FenceRail_S2', 'mesh', {'class': 'BoxMesh', 'size': {'x': 72, 'y': 0.08, 'z': 0.06}})
ss('TownArea/FenceRail_S2', 'position', {'x': 26, 'y': 0.4, 'z': 95})
ss('TownArea/FenceRail_S2', 'surface_material_override/0', wood_mat())

# West side (x=-10, z: 95 to 145)
for z in range(95, 147, 4):
    sa('TownArea', 'MeshInstance3D', f'FencePost_W_{fence_seed}')
    ss(f'TownArea/FencePost_W_{fence_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08,
        'bottom_radius': 0.1,
        'height': 1.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/FencePost_W_{fence_seed}', 'position', {'x': -10, 'y': 0.75, 'z': z})
    ss(f'TownArea/FencePost_W_{fence_seed}', 'surface_material_override/0', wood_mat())
    fence_seed += 1

sa('TownArea', 'MeshInstance3D', 'FenceRail_W1')
ss('TownArea/FenceRail_W1', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 52}})
ss('TownArea/FenceRail_W1', 'position', {'x': -10, 'y': 1.0, 'z': 120})
ss('TownArea/FenceRail_W1', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_W2')
ss('TownArea/FenceRail_W2', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 52}})
ss('TownArea/FenceRail_W2', 'position', {'x': -10, 'y': 0.4, 'z': 120})
ss('TownArea/FenceRail_W2', 'surface_material_override/0', wood_mat())

# East side (x=60, z: 95 to 145) — leave gap for gate at z=115
for z in range(95, 147, 4):
    if 110 < z < 120:
        continue  # Gate gap
    sa('TownArea', 'MeshInstance3D', f'FencePost_E_{fence_seed}')
    ss(f'TownArea/FencePost_E_{fence_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.08,
        'bottom_radius': 0.1,
        'height': 1.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/FencePost_E_{fence_seed}', 'position', {'x': 60, 'y': 0.75, 'z': z})
    ss(f'TownArea/FencePost_E_{fence_seed}', 'surface_material_override/0', wood_mat())
    fence_seed += 1

sa('TownArea', 'MeshInstance3D', 'FenceRail_E1')
ss('TownArea/FenceRail_E1', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 16}})
ss('TownArea/FenceRail_E1', 'position', {'x': 60, 'y': 1.0, 'z': 103})
ss('TownArea/FenceRail_E1', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_E2')
ss('TownArea/FenceRail_E2', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 16}})
ss('TownArea/FenceRail_E2', 'position', {'x': 60, 'y': 0.4, 'z': 103})
ss('TownArea/FenceRail_E2', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_E3')
ss('TownArea/FenceRail_E3', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 27}})
ss('TownArea/FenceRail_E3', 'position', {'x': 60, 'y': 1.0, 'z': 133})
ss('TownArea/FenceRail_E3', 'surface_material_override/0', wood_mat())

sa('TownArea', 'MeshInstance3D', 'FenceRail_E4')
ss('TownArea/FenceRail_E4', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.06, 'y': 0.08, 'z': 27}})
ss('TownArea/FenceRail_E4', 'position', {'x': 60, 'y': 0.4, 'z': 133})
ss('TownArea/FenceRail_E4', 'surface_material_override/0', wood_mat())

print(f"  Town fence with gate gap placed! ({fence_seed - 9000} posts + rails)")

# ============================================================
# 3. Lantern posts along the path
# ============================================================
print("\n=== Adding Lantern Posts ===")

lantern_seed = 10000
for z in range(30, 140, 15):
    for side in [-1, 1]:
        lx = 30 + side * 4
        lz = z
        
        # Post
        sa('TownArea/Terrain', 'MeshInstance3D', f'LanternPost_{lantern_seed}')
        ss(f'TownArea/Terrain/LanternPost_{lantern_seed}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.05,
            'bottom_radius': 0.08,
            'height': 3.0,
            'radial_segments': 6,
            'rings': 0
        })
        ss(f'TownArea/Terrain/LanternPost_{lantern_seed}', 'position', {'x': lx, 'y': 1.5, 'z': lz})
        ss(f'TownArea/Terrain/LanternPost_{lantern_seed}', 'surface_material_override/0', metal_mat())
        
        # Lantern body
        sa('TownArea/Terrain', 'MeshInstance3D', f'LanternBody_{lantern_seed}')
        ss(f'TownArea/Terrain/LanternBody_{lantern_seed}', 'mesh', {
            'class': 'BoxMesh',
            'size': {'x': 0.3, 'y': 0.4, 'z': 0.3}
        })
        ss(f'TownArea/Terrain/LanternBody_{lantern_seed}', 'position', {'x': lx, 'y': 3.2, 'z': lz})
        ss(f'TownArea/Terrain/LanternBody_{lantern_seed}', 'surface_material_override/0', metal_mat())
        
        # Glow
        sa('TownArea/Terrain', 'MeshInstance3D', f'LanternGlow_{lantern_seed}')
        ss(f'TownArea/Terrain/LanternGlow_{lantern_seed}', 'mesh', {
            'class': 'SphereMesh',
            'radius': 0.12,
            'height': 0.24,
            'radial_segments': 6,
            'rings': 3
        })
        ss(f'TownArea/Terrain/LanternGlow_{lantern_seed}', 'position', {'x': lx, 'y': 3.2, 'z': lz})
        ss(f'TownArea/Terrain/LanternGlow_{lantern_seed}', 'surface_material_override/0', lantern_glow_mat())
        
        # Light
        sa('TownArea/Terrain', 'OmniLight3D', f'LanternLight_{lantern_seed}')
        ss(f'TownArea/Terrain/LanternLight_{lantern_seed}', 'position', {'x': lx, 'y': 3.2, 'z': lz})
        ss(f'TownArea/Terrain/LanternLight_{lantern_seed}', 'light_color', {'r': 1.0, 'g': 0.8, 'b': 0.4, 'a': 1})
        ss(f'TownArea/Terrain/LanternLight_{lantern_seed}', 'light_energy', 0.8)
        ss(f'TownArea/Terrain/LanternLight_{lantern_seed}', 'omni_range', 8)
        ss(f'TownArea/Terrain/LanternLight_{lantern_seed}', 'shadow_enabled', False)
        
        lantern_seed += 1

print(f"  {(lantern_seed - 10000) // 4} lantern posts with lights placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 34 complete — stepping stones, fences, and lanterns added!")
