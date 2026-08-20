#!/usr/bin/env python3
"""Phase 37: Add stone bridge over stream, town well, banner flags on
buildings, signposts at path intersections, and training dummies."""
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

def banner_mat(color):
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': color,
        'roughness': 0.7,
        'emission': color,
        'emission_energy_multiplier': 0.05
    }

def water_mat():
    return {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.3, 'g': 0.4, 'b': 0.5, 'a': 0.8},
        'transparency': 1,
        'roughness': 0.1,
        'metallic': 0.2
    }

# ============================================================
# 1. Stone bridge over stream (between town and forest, z~140)
# ============================================================
print("=== Adding Stone Bridge ===")

# Bridge deck
sa('TownArea/Terrain', 'MeshInstance3D', 'BridgeDeck')
ss('TownArea/Terrain/BridgeDeck', 'mesh', {
    'class': 'BoxMesh',
    'size': {'x': 8, 'y': 0.4, 'z': 4}
})
ss('TownArea/Terrain/BridgeDeck', 'position', {'x': 30, 'y': 0.8, 'z': 140})
ss('TownArea/Terrain/BridgeDeck', 'surface_material_override/0', stone_mat())

# Bridge arch (curved underside)
sa('TownArea/Terrain', 'MeshInstance3D', 'BridgeArch')
ss('TownArea/Terrain/BridgeArch', 'mesh', {
    'class': 'CylinderMesh',
    'top_radius': 2.0,
    'bottom_radius': 2.0,
    'height': 4,
    'radial_segments': 12,
    'rings': 0
})
ss('TownArea/Terrain/BridgeArch', 'position', {'x': 30, 'y': 0.3, 'z': 140})
ss('TownArea/Terrain/BridgeArch', 'rotation_degrees', {'x': 0, 'y': 0, 'z': 90})
ss('TownArea/Terrain/BridgeArch', 'scale', {'x': 1, 'y': 0.3, 'z': 1})
ss('TownArea/Terrain/BridgeArch', 'surface_material_override/0', stone_mat())

# Bridge railings
for side in [-1, 1]:
    for x in range(27, 34, 1):
        sa('TownArea/Terrain', 'MeshInstance3D', f'BridgeRail_{x}_{"L" if side < 0 else "R"}')
        ss(f'TownArea/Terrain/BridgeRail_{x}_{"L" if side < 0 else "R"}', 'mesh', {
            'class': 'BoxMesh',
            'size': {'x': 0.15, 'y': 0.6, 'z': 0.15}
        })
        ss(f'TownArea/Terrain/BridgeRail_{x}_{"L" if side < 0 else "R"}', 'position', {
            'x': x, 'y': 1.3, 'z': 140 + side * 1.8
        })
        ss(f'TownArea/Terrain/BridgeRail_{x}_{"L" if side < 0 else "R"}', 'surface_material_override/0', stone_mat())

# Stream under bridge
sa('TownArea/Terrain', 'MeshInstance3D', 'StreamWater')
ss('TownArea/Terrain/StreamWater', 'mesh', {
    'class': 'PlaneMesh',
    'size': {'x': 6, 'y': 30}
})
ss('TownArea/Terrain/StreamWater', 'position', {'x': 30, 'y': 0.15, 'z': 140})
ss('TownArea/Terrain/StreamWater', 'rotation_degrees', {'x': 0, 'y': 0, 'z': 0})
ss('TownArea/Terrain/StreamWater', 'surface_material_override/0', water_mat())

print("  Stone bridge with stream added!")

# ============================================================
# 2. Town well in square center
# ============================================================
print("\n=== Adding Town Well ===")

# Well base (stone ring)
sa('TownArea/TownSquare', 'MeshInstance3D', 'WellBase')
ss('TownArea/TownSquare/WellBase', 'mesh', {
    'class': 'CylinderMesh',
    'top_radius': 1.2,
    'bottom_radius': 1.3,
    'height': 1.0,
    'radial_segments': 12,
    'rings': 0
})
ss('TownArea/TownSquare/WellBase', 'position', {'x': 30, 'y': 0.5, 'z': 119})
ss('TownArea/TownSquare/WellBase', 'surface_material_override/0', stone_mat())

# Well water
sa('TownArea/TownSquare', 'MeshInstance3D', 'WellWater')
ss('TownArea/TownSquare/WellWater', 'mesh', {
    'class': 'CylinderMesh',
    'top_radius': 1.0,
    'bottom_radius': 1.0,
    'height': 0.8,
    'radial_segments': 12,
    'rings': 0
})
ss('TownArea/TownSquare/WellWater', 'position', {'x': 30, 'y': 0.3, 'z': 119})
ss('TownArea/TownSquare/WellWater', 'surface_material_override/0', water_mat())

# Well roof supports (4 wooden posts)
for i in range(4):
    angle = i * 90 + 45
    px = 30 + math.cos(math.radians(angle)) * 1.2
    pz = 119 + math.sin(math.radians(angle)) * 1.2
    sa('TownArea/TownSquare', 'MeshInstance3D', f'WellPost_{i}')
    ss(f'TownArea/TownSquare/WellPost_{i}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.06,
        'bottom_radius': 0.08,
        'height': 2.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/TownSquare/WellPost_{i}', 'position', {'x': px, 'y': 1.75, 'z': pz})
    ss(f'TownArea/TownSquare/WellPost_{i}', 'surface_material_override/0', wood_mat())

# Well roof (pyramid)
sa('TownArea/TownSquare', 'MeshInstance3D', 'WellRoof')
ss('TownArea/TownSquare/WellRoof', 'mesh', {
    'class': 'CylinderMesh',
    'top_radius': 0.0,
    'bottom_radius': 1.8,
    'height': 1.0,
    'radial_segments': 4,
    'rings': 0
})
ss('TownArea/TownSquare/WellRoof', 'position', {'x': 30, 'y': 3.5, 'z': 119})
ss('TownArea/TownSquare/WellRoof', 'rotation_degrees', {'x': 0, 'y': 45, 'z': 0})
ss('TownArea/TownSquare/WellRoof', 'surface_material_override/0', {
    'class': 'StandardMaterial3D',
    'albedo_color': {'r': 0.2, 'g': 0.1, 'b': 0.05, 'a': 1},
    'roughness': 0.85
})

# Cross beam
sa('TownArea/TownSquare', 'MeshInstance3D', 'WellBeam1')
ss('TownArea/TownSquare/WellBeam1', 'mesh', {'class': 'BoxMesh', 'size': {'x': 3, 'y': 0.1, 'z': 0.1}})
ss('TownArea/TownSquare/WellBeam1', 'position', {'x': 30, 'y': 2.8, 'z': 119})
ss('TownArea/TownSquare/WellBeam1', 'surface_material_override/0', wood_mat())

sa('TownArea/TownSquare', 'MeshInstance3D', 'WellBeam2')
ss('TownArea/TownSquare/WellBeam2', 'mesh', {'class': 'BoxMesh', 'size': {'x': 0.1, 'y': 0.1, 'z': 3}})
ss('TownArea/TownSquare/WellBeam2', 'position', {'x': 30, 'y': 2.8, 'z': 119})
ss('TownArea/TownSquare/WellBeam2', 'surface_material_override/0', wood_mat())

# Bucket on rope
sa('TownArea/TownSquare', 'MeshInstance3D', 'WellBucket')
ss('TownArea/TownSquare/WellBucket', 'mesh', {
    'class': 'CylinderMesh',
    'top_radius': 0.15,
    'bottom_radius': 0.12,
    'height': 0.3,
    'radial_segments': 6,
    'rings': 0
})
ss('TownArea/TownSquare/WellBucket', 'position', {'x': 30, 'y': 1.5, 'z': 119})
ss('TownArea/TownSquare/WellBucket', 'surface_material_override/0', dark_wood_mat())

print("  Town well with roof and bucket added!")

# ============================================================
# 3. Banner flags on key buildings
# ============================================================
print("\n=== Adding Banner Flags ===")

banner_colors = [
    {'r': 0.7, 'g': 0.1, 'b': 0.1, 'a': 1},  # Red
    {'r': 0.1, 'g': 0.3, 'b': 0.7, 'a': 1},  # Blue
    {'r': 0.7, 'g': 0.6, 'b': 0.1, 'a': 1},  # Gold
]

banner_seed = 13000
# Banners near town gate
for i in range(4):
    bx = 58 + i * 0.5
    bz = 112 + i * 2
    color = banner_colors[i % 3]
    
    # Pole
    sa('TownArea', 'MeshInstance3D', f'BannerPole_{banner_seed}')
    ss(f'TownArea/BannerPole_{banner_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.03,
        'bottom_radius': 0.04,
        'height': 4.0,
        'radial_segments': 5,
        'rings': 0
    })
    ss(f'TownArea/BannerPole_{banner_seed}', 'position', {'x': bx, 'y': 4.0, 'z': bz})
    ss(f'TownArea/BannerPole_{banner_seed}', 'surface_material_override/0', dark_wood_mat())
    
    # Flag
    sa('TownArea', 'MeshInstance3D', f'BannerFlag_{banner_seed}')
    ss(f'TownArea/BannerFlag_{banner_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.8, 'y': 1.2, 'z': 0.02}
    })
    ss(f'TownArea/BannerFlag_{banner_seed}', 'position', {'x': bx + 0.4, 'y': 6.5, 'z': bz})
    ss(f'TownArea/BannerFlag_{banner_seed}', 'surface_material_override/0', banner_mat(color))
    banner_seed += 1

# Temple banners
for i in range(2):
    color = banner_colors[2]  # Gold for temple
    bx = -5 + i * 10
    bz = 5
    
    sa('Architecture/Exterior/ExteriorPlaza', 'MeshInstance3D', f'TempleBannerPole_{banner_seed}')
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerPole_{banner_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.04,
        'bottom_radius': 0.05,
        'height': 5.0,
        'radial_segments': 5,
        'rings': 0
    })
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerPole_{banner_seed}', 'position', {'x': bx, 'y': 5.0, 'z': bz})
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerPole_{banner_seed}', 'surface_material_override/0', dark_wood_mat())
    
    sa('Architecture/Exterior/ExteriorPlaza', 'MeshInstance3D', f'TempleBannerFlag_{banner_seed}')
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerFlag_{banner_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 1.0, 'y': 1.5, 'z': 0.02}
    })
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerFlag_{banner_seed}', 'position', {'x': bx + 0.5, 'y': 8.0, 'z': pz})
    ss(f'Architecture/Exterior/ExteriorPlaza/TempleBannerFlag_{banner_seed}', 'surface_material_override/0', banner_mat(color))
    banner_seed += 1

print(f"  {banner_seed - 13000} banner flags added!")

# ============================================================
# 4. Signposts at path intersections
# ============================================================
print("\n=== Adding Signposts ===")

sign_seed = 14000
sign_locations = [
    (30, 0, 50, 'PathJunction_Temple'),   # Temple to town junction
    (30, 0, 95, 'PathJunction_TownGate'),  # Town gate
    (30, 0, 145, 'PathJunction_Forest'),   # Forest junction
]

for sx, sy, sz, name in sign_locations:
    # Post
    sa('TownArea/Terrain', 'MeshInstance3D', f'SignPost_{sign_seed}')
    ss(f'TownArea/Terrain/SignPost_{sign_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.05,
        'bottom_radius': 0.07,
        'height': 2.5,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/Terrain/SignPost_{sign_seed}', 'position', {'x': sx + 3, 'y': sy + 1.25, 'z': sz})
    ss(f'TownArea/Terrain/SignPost_{sign_seed}', 'surface_material_override/0', wood_mat())
    
    # Sign board
    sa('TownArea/Terrain', 'MeshInstance3D', f'SignBoard_{sign_seed}')
    ss(f'TownArea/Terrain/SignBoard_{sign_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.8, 'y': 0.25, 'z': 0.05}
    })
    ss(f'TownArea/Terrain/SignBoard_{sign_seed}', 'position', {'x': sx + 3, 'y': sy + 2.0, 'z': sz})
    ss(f'TownArea/Terrain/SignBoard_{sign_seed}', 'rotation_degrees', {'x': 0, 'y': 0, 'z': -10})
    ss(f'TownArea/Terrain/SignBoard_{sign_seed}', 'surface_material_override/0', wood_mat())
    
    # Second sign board (different direction)
    sa('TownArea/Terrain', 'MeshInstance3D', f'SignBoard2_{sign_seed}')
    ss(f'TownArea/Terrain/SignBoard2_{sign_seed}', 'mesh', {
        'class': 'BoxMesh',
        'size': {'x': 0.8, 'y': 0.25, 'z': 0.05}
    })
    ss(f'TownArea/Terrain/SignBoard2_{sign_seed}', 'position', {'x': sx + 3, 'y': sy + 1.6, 'z': sz})
    ss(f'TownArea/Terrain/SignBoard2_{sign_seed}', 'rotation_degrees', {'x': 0, 'y': 30, 'z': 5})
    ss(f'TownArea/Terrain/SignBoard2_{sign_seed}', 'surface_material_override/0', wood_mat())
    
    sign_seed += 1

print(f"  {len(sign_locations)} signposts added!")

# ============================================================
# 5. Training dummies in training ground
# ============================================================
print("\n=== Adding Training Dummies ===")

dummy_seed = 15000
for i in range(4):
    dx = -45 + i * 3
    dz = 165
    
    # Post
    sa('TownArea/TrainingGround', 'MeshInstance3D', f'DummyPost_{dummy_seed}')
    ss(f'TownArea/TrainingGround/DummyPost_{dummy_seed}', 'mesh', {
        'class': 'CylinderMesh',
        'top_radius': 0.06,
        'bottom_radius': 0.08,
        'height': 2.0,
        'radial_segments': 6,
        'rings': 0
    })
    ss(f'TownArea/TrainingGround/DummyPost_{dummy_seed}', 'position', {'x': dx, 'y': 1.0, 'z': dz})
    ss(f'TownArea/TrainingGround/DummyPost_{dummy_seed}', 'surface_material_override/0', dark_wood_mat())
    
    # Dummy body
    sa('TownArea/TrainingGround', 'MeshInstance3D', f'DummyBody_{dummy_seed}')
    ss(f'TownArea/TrainingGround/DummyBody_{dummy_seed}', 'mesh', {
        'class': 'CapsuleMesh',
        'radius': 0.3,
        'height': 1.2,
        'radial_segments': 6,
        'rings': 2
    })
    ss(f'TownArea/TrainingGround/DummyBody_{dummy_seed}', 'position', {'x': dx, 'y': 1.8, 'z': dz})
    ss(f'TownArea/TrainingGround/DummyBody_{dummy_seed}', 'surface_material_override/0', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.5, 'g': 0.4, 'b': 0.25, 'a': 1},
        'roughness': 0.9
    })
    
    # Dummy head
    sa('TownArea/TrainingGround', 'MeshInstance3D', f'DummyHead_{dummy_seed}')
    ss(f'TownArea/TrainingGround/DummyHead_{dummy_seed}', 'mesh', {
        'class': 'SphereMesh',
        'radius': 0.2,
        'height': 0.4,
        'radial_segments': 6,
        'rings': 3
    })
    ss(f'TownArea/TrainingGround/DummyHead_{dummy_seed}', 'position', {'x': dx, 'y': 2.6, 'z': dz})
    ss(f'TownArea/TrainingGround/DummyHead_{dummy_seed}', 'surface_material_override/0', {
        'class': 'StandardMaterial3D',
        'albedo_color': {'r': 0.5, 'g': 0.4, 'b': 0.25, 'a': 1},
        'roughness': 0.9
    })
    
    dummy_seed += 1

print(f"  4 training dummies added!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 37 complete — bridge, well, banners, signposts, and training dummies!")
