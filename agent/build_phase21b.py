#!/usr/bin/env python3
"""Phase 21b: Add TerrainGenerator3D nodes with noise configuration.
Requires the updated plugin.gd with custom type support."""
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

# Noise presets for different terrain types
def make_noise(seed, freq, noise_type=0):
    return {
        'class': 'FastNoiseLite',
        'seed': seed,
        'frequency': freq,
        'noise_type': noise_type,  # 0=Perlin, 1=Simplex, 2=Cellular
    }

# ============================================================
# 1. Town terrain — gentle rolling hills
# ============================================================
print("=== Town Terrain ===")
sa('TownArea/Terrain', 'TerrainGenerator3D', 'TownTerrain')
ss('TownArea/Terrain/TownTerrain', 'width', 128)
ss('TownArea/Terrain/TownTerrain', 'depth', 128)
ss('TownArea/Terrain/TownTerrain', 'height_scale', 3.0)
ss('TownArea/Terrain/TownTerrain', 'noise', make_noise(seed=42, freq=0.03))
ss('TownArea/Terrain/TownTerrain', 'auto_update', True)
ss('TownArea/Terrain/TownTerrain', 'position', {'x':0,'y':0,'z':110})
ss('TownArea/Terrain/TownTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# 2. Forest terrain — more varied, taller
# ============================================================
print("\n=== Forest Terrain ===")
sa('TownArea/Terrain', 'TerrainGenerator3D', 'ForestTerrain')
ss('TownArea/Terrain/ForestTerrain', 'width', 96)
ss('TownArea/Terrain/ForestTerrain', 'depth', 96)
ss('TownArea/Terrain/ForestTerrain', 'height_scale', 8.0)
ss('TownArea/Terrain/ForestTerrain', 'noise', make_noise(seed=77, freq=0.04, noise_type=1))
ss('TownArea/Terrain/ForestTerrain', 'auto_update', True)
ss('TownArea/Terrain/ForestTerrain', 'position', {'x':0,'y':0,'z':160})
ss('TownArea/Terrain/ForestTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# 3. Lake terrain — gentle near water
# ============================================================
print("\n=== Lake Terrain ===")
sa('TownArea/LakeRegion', 'TerrainGenerator3D', 'LakeTerrain')
ss('TownArea/LakeRegion/LakeTerrain', 'width', 96)
ss('TownArea/LakeRegion/LakeTerrain', 'depth', 96)
ss('TownArea/LakeRegion/LakeTerrain', 'height_scale', 5.0)
ss('TownArea/LakeRegion/LakeTerrain', 'noise', make_noise(seed=99, freq=0.025))
ss('TownArea/LakeRegion/LakeTerrain', 'auto_update', True)
ss('TownArea/LakeRegion/LakeTerrain', 'position', {'x':20,'y':0,'z':180})
ss('TownArea/LakeRegion/LakeTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# 4. Mountain terrain — large, tall
# ============================================================
print("\n=== Mountain Terrain ===")
sa('TownArea/Terrain', 'TerrainGenerator3D', 'MountainTerrain')
ss('TownArea/Terrain/MountainTerrain', 'width', 200)
ss('TownArea/Terrain/MountainTerrain', 'depth', 100)
ss('TownArea/Terrain/MountainTerrain', 'height_scale', 25.0)
ss('TownArea/Terrain/MountainTerrain', 'noise', make_noise(seed=13, freq=0.02, noise_type=1))
ss('TownArea/Terrain/MountainTerrain', 'auto_update', True)
ss('TownArea/Terrain/MountainTerrain', 'position', {'x':0,'y':0,'z':220})
ss('TownArea/Terrain/MountainTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# 5. Temple terrain — flat plateau
# ============================================================
print("\n=== Temple Terrain ===")
sa('Architecture/Exterior/ExteriorPlaza', 'TerrainGenerator3D', 'TempleTerrain')
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'width', 64)
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'depth', 64)
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'height_scale', 2.0)
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'noise', make_noise(seed=55, freq=0.02))
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'auto_update', True)
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'position', {'x':0,'y':0,'z':0})
ss('Architecture/Exterior/ExteriorPlaza/TempleTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# 6. Grove terrain — small, gentle
# ============================================================
print("\n=== Grove Terrain ===")
sa('TownArea/HiddenGrove', 'TerrainGenerator3D', 'GroveTerrain')
ss('TownArea/HiddenGrove/GroveTerrain', 'width', 32)
ss('TownArea/HiddenGrove/GroveTerrain', 'depth', 32)
ss('TownArea/HiddenGrove/GroveTerrain', 'height_scale', 1.5)
ss('TownArea/HiddenGrove/GroveTerrain', 'noise', make_noise(seed=333, freq=0.05))
ss('TownArea/HiddenGrove/GroveTerrain', 'auto_update', True)
ss('TownArea/HiddenGrove/GroveTerrain', 'position', {'x':-30,'y':0,'z':100})
ss('TownArea/HiddenGrove/GroveTerrain', 'material_override', 'res://addons/terrain_generator/Textures/Grass.tres')
print("  Done!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 21b complete!")
