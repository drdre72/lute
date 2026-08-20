#!/usr/bin/env python3
"""Phase 36: Add campfire props with glowing embers, lake dock/pier,
graveyard headstones with iron fence, and market stalls in town square."""
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

def dark_wood_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.15, 'g': 0.08, 'b': 0.04, 'a': 1},
        'roughness': 0.9
    }

def iron_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.12, 'g': 0.12, 'b': 0.12, 'a': 1},
        'roughness': 0.4,
        'metallic': 0.8
    }

def ember_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 1.0, 'g': 0.4, 'b': 0.1, 'a': 1},
        'emission': {'r': 1.0, 'g': 0.3, 'b': 0.05, 'a': 1},
        'emission_energy_multiplier': 3.0,
        'roughness': 0.3
    }

def cloth_mat(color):
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': color,
        'roughness': 0.8
    }

# ============================================================
# 1. Campfire near town square
# ============================================================
print("=== Adding Campfires ===")

def make_campfire(parent, x, y, z, name='Campfire'):
    base = f'{parent}/{name}'
    
    # Ring of stones
    for i in range(8):
        angle = (i / 8.0) * 6.28318
        sx = x + math.cos(angle) * 0.8
        sz = z + math.sin(angle) * 0.8
        sa(parent, 'MeshInstance3D', f'{name}_Stone{i}')
        ss(f'{base}_Stone{i}', 'mesh', {
            'class': 'CapsuleMesh',
            'radius': 0.25,
            'height': 0.4,
            'radial_segments': 5,
            'rings': 1
        })
        ss(f'{base}_Stone{i}', 'position', {'x': sx, 'y': y + 0.2, 'z': sz})
        ss(f'{base}_Stone{i}', 'rotation_degrees', {'x': 90, 'y': i * 45, 'z': 0})
        ss(f'{base}_Stone{i}', 'surface_material_override/0', stone_mat())
    
    # Log stack (3 crossed logs)
    for i in range(3):
        sa(parent, 'MeshInstance3D', f'{name}_Log{i}')
        ss(f'{base}_Log{i}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.08,
            'bottom_radius': 0.1,
            'height': 1.5,
            'radial_segments': 6,
            'rings': 0
        })
        ss(f'{base}_Log{i}', 'position', {'x': x, 'y': y + 0.3, 'z': z})
        ss(f'{base}_Log{i}', 'rotation_degrees', {'x': 90, 'y': i * 60, 'z': 0})
        ss(f'{base}_Log{i}', 'surface_material_override/0', dark_wood_mat())
    
    # Glowing ember center
    sa(parent, 'MeshInstance3D', f'{name}_Ember')
    ss(f'{base}_Ember', 'mesh', {
        'class': 'SphereMesh',
        'radius': 0.3,
        'height': 0.4,
        'radial_segments': 6,
        'rings': 3
    })
    ss(f'{base}_Ember', 'position', {'x': x, 'y': y + 0.25, 'z': z})
    ss(f'{base}_Ember', 'surface_material_override/0', ember_mat())
    
    # Fire light
    sa(parent, 'OmniLight3D', f'{name}_Light')
    ss(f'{base}_Light', 'position', {'x': x, 'y': y + 0.5, 'z': z})
    ss(f'{base}_Light', 'light_color', {'r': 1.0, 'g': 0.6, 'b': 0.2, 'a': 1})
    ss(f'{base}_Light', 'light_energy', 2.0)
    ss(f'{base}_Light', 'omni_range', 12)
    ss(f'{base}_Light', 'shadow_enabled', True)

# Town campfire
make_campfire('TownArea/TownSquare', 30, 0, 120, 'TownCampfire')
print("  Town campfire added!")

# Forest campfire (hunter's rest)
make_campfire('TownArea/Terrain', -20, 0, 155, 'ForestCampfire')
print("  Forest campfire added!")

# ============================================================
# 2. Lake dock/pier
# ============================================================
print("\n=== Adding Lake Dock ===")

# Dock platform
sa('TownArea/LakeRegion', 'MeshInstance3D', 'DockPlatform')
ss('TownArea/LakeRegion/DockPlatform', 'mesh', {
    'class': 'BoxMesh',
    'size': {'x': 4, 'y': 0.2, 'z': 12}
})
ss('TownArea/LakeRegion/DockPlatform', 'position', {'x': 20, 'y': 0.5, 'z': 178})
ss('TownArea/LakeRegion/DockPlatform', 'surface_material_override/0', wood_mat())

# Dock support posts
for i in range(6):
    pz = 174 + i * 2
    for side in [-1, 1]:
        sa('TownArea/LakeRegion', 'MeshInstance3D', f'DockPost_{i}_{"L" if side < 0 else "R"}')
        ss(f'TownArea/LakeRegion/DockPost_{i}_{"L" if side < 0 else "R"}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.08,
            'bottom_radius': 0.1,
            'height': 1.5,
            'radial_segments': 6,
            'rings': 0
        })
        ss(f'TownArea/LakeRegion/DockPost_{i}_{"L" if side < 0 else "R"}', 'position', {
            'x': 20 + side * 1.8, 'y': -0.25, 'z': pz
        })
        ss(f'TownArea/LakeRegion/DockPost_{i}_{"L" if side < 0 else "R"}', 'surface_material_override/0', dark_wood_mat())

# Dock planks (lines on top)
for i in range(10):
    sa('TownArea/LakeRegion', 'MeshInstance3D', f'DockPlank_{i}')
    ss(f'TownArea/LakeRegion/DockPlank_{i}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 3.8, 'y': 0.04, 'z': 0.3}
    })
    ss(f'TownArea/LakeRegion/DockPlank_{i}', 'position', {'x': 20, 'y': 0.62, 'z': 173 + i * 1.1})
    ss(f'TownArea/LakeRegion/DockPlank_{i}', 'surface_material_override/0', wood_mat())

# Small rowboat at dock end
sa('TownArea/LakeRegion', 'MeshInstance3D', 'RowBoat_Hull')
ss('TownArea/LakeRegion/RowBoat_Hull', 'mesh', {
    'class': 'CapsuleMesh',
    'radius': 0.6,
    'height': 2.5,
    'radial_segments': 6,
    'rings': 2
})
ss('TownArea/LakeRegion/RowBoat_Hull', 'position', {'x': 22, 'y': 0.4, 'z': 186})
ss('TownArea/LakeRegion/RowBoat_Hull', 'rotation_degrees', {'x': 90, 'y': 0, 'z': 90})
ss('TownArea/LakeRegion/RowBoat_Hull', 'scale', {'x': 1, 'y': 0.5, 'z': 1})
ss('TownArea/LakeRegion/RowBoat_Hull', 'surface_material_override/0', dark_wood_mat())

print("  Lake dock with rowboat added!")

# ============================================================
# 3. Graveyard headstones + iron fence
# ============================================================
print("\n=== Adding Graveyard Details ===")

grave_seed = 11000
random.seed(66)

# Headstones
for i in range(12):
    gx = random.uniform(-50, -30)
    gz = random.uniform(195, 215)
    gh = random.uniform(0.8, 1.4)
    
    # Stone slab
    sa('TownArea/Graveyard', 'MeshInstance3D', f'Headstone_{grave_seed}')
    ss(f'TownArea/Graveyard/Headstone_{grave_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.6, 'y': gh, 'z': 0.15}
    })
    ss(f'TownArea/Graveyard/Headstone_{grave_seed}', 'position', {'x': gx, 'y': gh / 2, 'z': gz})
    ss(f'TownArea/Graveyard/Headstone_{grave_seed}', 'rotation_degrees', {
        'x': random.uniform(-3, 3),
        'y': random.uniform(-15, 15),
        'z': random.uniform(-2, 2)
    })
    ss(f'TownArea/Graveyard/Headstone_{grave_seed}', 'surface_material_override/0', stone_mat())
    grave_seed += 1

# Cross headstones (2)
for i in range(2):
    gx = random.uniform(-45, -35)
    gz = random.uniform(200, 210)
    
    # Vertical
    sa('TownArea/Graveyard', 'MeshInstance3D', f'CrossV_{grave_seed}')
    ss(f'TownArea/Graveyard/CrossV_{grave_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.15, 'y': 1.5, 'z': 0.15}
    })
    ss(f'TownArea/Graveyard/CrossV_{grave_seed}', 'position', {'x': gx, 'y': 0.75, 'z': gz})
    ss(f'TownArea/Graveyard/CrossV_{grave_seed}', 'surface_material_override/0', stone_mat())
    
    # Horizontal
    sa('TownArea/Graveyard', 'MeshInstance3D', f'CrossH_{grave_seed}')
    ss(f'TownArea/Graveyard/CrossH_{grave_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.6, 'y': 0.15, 'z': 0.15}
    })
    ss(f'TownArea/Graveyard/CrossH_{grave_seed}', 'position', {'x': gx, 'y': 1.1, 'z': gz})
    ss(f'TownArea/Graveyard/CrossH_{grave_seed}', 'surface_material_override/0', stone_mat())
    grave_seed += 1

# Iron fence around graveyard
for x in range(-52, -28, 2):
    # Iron bars
    sa('TownArea/Graveyard', 'MeshInstance3D', f'IronFence_N_{grave_seed}')
    ss(f'TownArea/Graveyard/IronFence_N_{grave_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.03,
        'bottom_radius': 0.04,
        'height': 1.0,
        'radial_segments': 4,
        'rings': 0
    })
    ss(f'TownArea/Graveyard/IronFence_N_{grave_seed}', 'position', {'x': x, 'y': 0.5, 'z': 218})
    ss(f'TownArea/Graveyard/IronFence_N_{grave_seed}', 'surface_material_override/0', iron_mat())
    grave_seed += 1
    
    sa('TownArea/Graveyard', 'MeshInstance3D', f'IronFence_S_{grave_seed}')
    ss(f'TownArea/Graveyard/IronFence_S_{grave_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.03,
        'bottom_radius': 0.04,
        'height': 1.0,
        'radial_segments': 4,
        'rings': 0
    })
    ss(f'TownArea/Graveyard/IronFence_S_{grave_seed}', 'position', {'x': x, 'y': 0.5, 'z': 192})
    ss(f'TownArea/Graveyard/IronFence_S_{grave_seed}', 'surface_material_override/0', iron_mat())
    grave_seed += 1

for z in range(192, 219, 2):
    sa('TownArea/Graveyard', 'MeshInstance3D', f'IronFence_W_{grave_seed}')
    ss(f'TownArea/Graveyard/IronFence_W_{grave_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.03,
        'bottom_radius': 0.04,
        'height': 1.0,
        'radial_segments': 4,
        'rings': 0
    })
    ss(f'TownArea/Graveyard/IronFence_W_{grave_seed}', 'position', {'x': -52, 'y': 0.5, 'z': z})
    ss(f'TownArea/Graveyard/IronFence_W_{grave_seed}', 'surface_material_override/0', iron_mat())
    grave_seed += 1
    
    sa('TownArea/Graveyard', 'MeshInstance3D', f'IronFence_E_{grave_seed}')
    ss(f'TownArea/Graveyard/IronFence_E_{grave_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.03,
        'bottom_radius': 0.04,
        'height': 1.0,
        'radial_segments': 4,
        'rings': 0
    })
    ss(f'TownArea/Graveyard/IronFence_E_{grave_seed}', 'position', {'x': -28, 'y': 0.5, 'z': z})
    ss(f'TownArea/Graveyard/IronFence_E_{grave_seed}', 'surface_material_override/0', iron_mat())
    grave_seed += 1

print(f"  Graveyard: 14 headstones + iron fence ({grave_seed - 11000} parts)")

# ============================================================
# 4. Market stalls in town square
# ============================================================
print("\n=== Adding Market Stalls ===")

stall_colors = [
    {'r': 0.7, 'g': 0.3, 'b': 0.2, 'a': 1},  # Red
    {'r': 0.3, 'g': 0.5, 'b': 0.7, 'a': 1},  # Blue
    {'r': 0.6, 'g': 0.5, 'b': 0.2, 'a': 1},  # Yellow
    {'r': 0.3, 'g': 0.6, 'b': 0.3, 'a': 1},  # Green
]

stall_seed = 12000
stall_positions = [
    (18, 0, 110), (22, 0, 110), (38, 0, 110), (42, 0, 110),
    (18, 0, 128), (22, 0, 128), (38, 0, 128), (42, 0, 128),
]

for i, (sx, sy, sz) in enumerate(stall_positions):
    color = stall_colors[i % len(stall_colors)]
    name = f'Stall_{stall_seed}'
    base = f'TownArea/TownSquare/{name}'
    
    # Table top
    sa('TownArea/TownSquare', 'MeshInstance3D', f'{name}_Table')
    ss(f'{base}_Table', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 2.5, 'y': 0.1, 'z': 1.5}
    })
    ss(f'{base}_Table', 'position', {'x': sx, 'y': sy + 0.9, 'z': sz})
    ss(f'{base}_Table', 'surface_material_override/0', wood_mat())
    
    # Table legs
    for lx in [-1.0, 1.0]:
        for lz in [-0.5, 0.5]:
            sa('TownArea/TownSquare', 'MeshInstance3D', f'{name}_Leg_{lx}_{lz}')
            ss(f'{base}_Leg_{lx}_{lz}', 'mesh', {
                'class': 'BoxMesh',
                'size': {'x': 0.1, 'y': 0.9, 'z': 0.1}
            })
            ss(f'{base}_Leg_{lx}_{lz}', 'position', {'x': sx + lx, 'y': sy + 0.45, 'z': sz + lz})
            ss(f'{base}_Leg_{lx}_{lz}', 'surface_material_override/0', dark_wood_mat())
    
    # Canopy roof
    sa('TownArea/TownSquare', 'MeshInstance3D', f'{name}_Canopy')
    ss(f'{base}_Canopy', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 3.0, 'y': 0.08, 'z': 2.0}
    })
    ss(f'{base}_Canopy', 'position', {'x': sx, 'y': sy + 2.2, 'z': sz})
    ss(f'{base}_Canopy', 'rotation_degrees', {'x': -5, 'y': 0, 'z': 0})
    ss(f'{base}_Canopy', 'surface_material_override/0', cloth_mat(color))
    
    # Canopy support poles
    for px in [-1.2, 1.2]:
        sa('TownArea/TownSquare', 'MeshInstance3D', f'{name}_Pole_{px}')
        ss(f'{base}_Pole_{px}', 'mesh', {
            'class': 'CylinderMesh',
            'top_radius': 0.04,
            'bottom_radius': 0.05,
            'height': 2.3,
            'radial_segments': 5,
            'rings': 0
        })
        ss(f'{base}_Pole_{px}', 'position', {'x': sx + px, 'y': sy + 1.15, 'z': sz})
        ss(f'{base}_Pole_{px}', 'surface_material_override/0', dark_wood_mat())
    
    # Goods on table (small colored boxes)
    for g in range(3):
        gx = sx + random.uniform(-0.8, 0.8)
        gz = sz + random.uniform(-0.5, 0.5)
        sa('TownArea/TownSquare', 'MeshInstance3D', f'{name}_Goods_{g}')
        ss(f'{base}_Goods_{g}', 'mesh', {
            'class': 'BoxMesh',
            'size': {'x': random.uniform(0.2, 0.4), 'y': random.uniform(0.15, 0.3), 'z': random.uniform(0.2, 0.4)}
        })
        ss(f'{base}_Goods_{g}', 'position', {'x': gx, 'y': sy + 1.05, 'z': gz})
        ss(f'{base}_Goods_{g}', 'surface_material_override/0', cloth_mat(stall_colors[(i + g) % len(stall_colors)]))
    
    stall_seed += 1

print(f"  {len(stall_positions)} market stalls added!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 36 complete — campfires, dock, graveyard, and market stalls added!")
