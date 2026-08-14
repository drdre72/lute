#!/usr/bin/env python3
"""Phase 25: Atmospheric polish — fog tuning, volumetric light shafts,
ambient particle systems (dust motes, pollen), and environment refinement
to complement the new terrain."""
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

# ============================================================
# 1. World Environment — fog and ambient
# ============================================================
print("=== World Environment ===")

sa('.', 'WorldEnvironment', 'WorldEnv')
sa('WorldEnv', 'Environment', 'EnvRes')

# Set environment properties via the resource
ss('WorldEnv/EnvRes', 'background_mode', 1)  # Sky
ss('WorldEnv/EnvRes', 'ambient_light_source', 2)  # Sky
ss('WorldEnv/EnvRes', 'ambient_light_color', {'r': 0.3, 'g': 0.35, 'b': 0.4, 'a': 1})
ss('WorldEnv/EnvRes', 'ambient_light_energy', 0.5)
ss('WorldEnv/EnvRes', 'fog_enabled', True)
ss('WorldEnv/EnvRes', 'fog_light_color', {'r': 0.25, 'g': 0.28, 'b': 0.32, 'a': 1})
ss('WorldEnv/EnvRes', 'fog_light_energy', 0.3)
ss('WorldEnv/EnvRes', 'fog_density', 0.008)
ss('WorldEnv/EnvRes', 'fog_aerial_perspective', 0.5)
ss('WorldEnv/EnvRes', 'glow_enabled', True)
ss('WorldEnv/EnvRes', 'glow_intensity', 0.8)
ss('WorldEnv/EnvRes', 'glow_strength', 1.0)
ss('WorldEnv/EnvRes', 'volumetric_fog_enabled', True)
ss('WorldEnv/EnvRes', 'volumetric_fog_density', 0.01)
ss('WorldEnv/EnvRes', 'volumetric_fog_albedo', {'r': 0.6, 'g': 0.65, 'b': 0.7, 'a': 1})
ss('WorldEnv/EnvRes', 'volumetric_fog_emission', {'r': 0.15, 'g': 0.12, 'b': 0.08, 'a': 1})
ss('WorldEnv/EnvRes', 'volumetric_fog_emission_energy', 0.2)
ss('WorldEnv/EnvRes', 'ssao_enabled', True)
ss('WorldEnv/EnvRes', 'ssao_intensity', 1.5)
ss('WorldEnv/EnvRes', 'ssao_radius', 1.0)
ss('WorldEnv/EnvRes', 'ssr_enabled', True)
ss('WorldEnv/EnvRes', 'tonemap_mode', 2)  # ACES
ss('WorldEnv/EnvRes', 'tonemap_white', 1.0)

print("  World environment configured!")

# ============================================================
# 2. Directional light (sun) with shadows
# ============================================================
print("\n=== Sun Light ===")

sa('.', 'DirectionalLight3D', 'SunLight')
ss('SunLight', 'position', {'x': 50, 'y': 80, 'z': -30})
ss('SunLight', 'rotation_degrees', {'x': -45, 'y': -30, 'z': 0})
ss('SunLight', 'light_color', {'r': 1.0, 'g': 0.92, 'b': 0.78, 'a': 1})
ss('SunLight', 'light_energy', 1.2)
ss('SunLight', 'shadow_enabled', True)
ss('SunLight', 'shadow_bias', 0.05)
ss('SunLight', 'directional_shadow_mode', 1)  # PSSM
ss('SunLight', 'directional_shadow_split_1', 0.1)
ss('SunLight', 'directional_shadow_split_2', 0.3)
ss('SunLight', 'directional_shadow_split_3', 0.6)
ss('SunLight', 'directional_shadow_max_distance', 200)

print("  Sun with shadows placed!")

# ============================================================
# 3. Dust mote particles in nave
# ============================================================
print("\n=== Dust Motes ===")

sa('Architecture/NaveCombiner', 'GPUParticles3D', 'DustMotes')
ss('Architecture/NaveCombiner/DustMotes', 'position', {'x': 0, 'y': 6, 'z': 10})
ss('Architecture/NaveCombiner/DustMotes', 'amount', 200)
ss('Architecture/NaveCombiner/DustMotes', 'lifetime', 8.0)
ss('Architecture/NaveCombiner/DustMotes', 'explosiveness', 0.0)
ss('Architecture/NaveCombiner/DustMotes', 'randomness', 1.0)

# Create particle material
sa('Architecture/NaveCombiner/DustMotes', 'ProcessMaterial', 'DustProcMat')
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'class', 'ParticleProcessMaterial')
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'emission_shape', 1)  # Box
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'emission_box_extents', {'x': 12, 'y': 4, 'z': 15})
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'gravity', {'x': 0, 'y': -0.1, 'z': 0})
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'turbulence_enabled', True)
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'turbulence_noise_scale', 2.0)
ss('Architecture/NaveCombiner/DustMotes/DustProcMat', 'turbulence_influence', 0.3)

print("  Dust motes in nave!")

# ============================================================
# 4. Pollen particles in hidden grove
# ============================================================
print("\n=== Grove Pollen ===")

sa('TownArea/HiddenGrove', 'GPUParticles3D', 'GrovePollen')
ss('TownArea/HiddenGrove/GrovePollen', 'position', {'x': -30, 'y': 3, 'z': 100})
ss('TownArea/HiddenGrove/GrovePollen', 'amount', 100)
ss('TownArea/HiddenGrove/GrovePollen', 'lifetime', 12.0)
ss('TownArea/HiddenGrove/GrovePollen', 'randomness', 1.0)

sa('TownArea/HiddenGrove/GrovePollen', 'ProcessMaterial', 'PollenMat')
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'class', 'ParticleProcessMaterial')
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'emission_shape', 1)
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'emission_box_extents', {'x': 10, 'y': 5, 'z': 10})
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'gravity', {'x': 0, 'y': 0.05, 'z': 0})
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'turbulence_enabled', True)
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'turbulence_noise_scale', 1.5)
ss('TownArea/HiddenGrove/GrovePollen/PollenMat', 'turbulence_influence', 0.5)

print("  Pollen in grove!")

# ============================================================
# 5. Mist particles near waterfall
# ============================================================
print("\n=== Waterfall Mist ===")

sa('TownArea/Terrain', 'GPUParticles3D', 'WaterfallMist')
ss('TownArea/Terrain/WaterfallMist', 'position', {'x': -70, 'y': 10, 'z': 210})
ss('TownArea/Terrain/WaterfallMist', 'amount', 150)
ss('TownArea/Terrain/WaterfallMist', 'lifetime', 5.0)
ss('TownArea/Terrain/WaterfallMist', 'randomness', 0.8)

sa('TownArea/Terrain/WaterfallMist', 'ProcessMaterial', 'MistMat')
ss('TownArea/Terrain/WaterfallMist/MistMat', 'class', 'ParticleProcessMaterial')
ss('TownArea/Terrain/WaterfallMist/MistMat', 'emission_shape', 1)
ss('TownArea/Terrain/WaterfallMist/MistMat', 'emission_box_extents', {'x': 8, 'y': 3, 'z': 4})
ss('TownArea/Terrain/WaterfallMist/MistMat', 'gravity', {'x': 0, 'y': 0.5, 'z': 0})
ss('TownArea/Terrain/WaterfallMist/MistMat', 'turbulence_enabled', True)
ss('TownArea/Terrain/WaterfallMist/MistMat', 'turbulence_influence', 0.8)

print("  Mist near waterfall!")

# ============================================================
# 6. Campfire embers near forest path
# ============================================================
print("\n=== Campfire Embers ===")

sa('TownArea/Terrain', 'GPUParticles3D', 'CampfireEmbers')
ss('TownArea/Terrain/CampfireEmbers', 'position', {'x': 28, 'y': 1, 'z': 155})
ss('TownArea/Terrain/CampfireEmbers', 'amount', 50)
ss('TownArea/Terrain/CampfireEmbers', 'lifetime', 3.0)
ss('TownArea/Terrain/CampfireEmbers', 'randomness', 0.5)

sa('TownArea/Terrain/CampfireEmbers', 'ProcessMaterial', 'EmberMat')
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'class', 'ParticleProcessMaterial')
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'emission_shape', 1)
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'emission_box_extents', {'x': 0.5, 'y': 0.2, 'z': 0.5})
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'gravity', {'x': 0, 'y': 2.0, 'z': 0})
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'turbulence_enabled', True)
ss('TownArea/Terrain/CampfireEmbers/EmberMat', 'turbulence_influence', 0.3)

print("  Campfire embers!")

# ============================================================
# 7. Fill lights for town area
# ============================================================
print("\n=== Town Fill Lights ===")

# Warm fill light over town square
sa('TownArea', 'OmniLight3D', 'TownFillLight')
ss('TownArea/TownFillLight', 'position', {'x': 30, 'y': 20, 'z': 115})
ss('TownArea/TownFillLight', 'light_color', {'r': 1.0, 'g': 0.85, 'b': 0.6, 'a': 1})
ss('TownArea/TownFillLight', 'light_energy', 0.4)
ss('TownArea/TownFillLight', 'omni_range', 60)
ss('TownArea/TownFillLight', 'omni_attenuation', 1.5)
ss('TownArea/TownFillLight', 'shadow_enabled', False)

# Cool fill light over lake
sa('TownArea/LakeRegion', 'OmniLight3D', 'LakeFillLight')
ss('TownArea/LakeRegion/LakeFillLight', 'position', {'x': 20, 'y': 15, 'z': 185})
ss('TownArea/LakeRegion/LakeFillLight', 'light_color', {'r': 0.4, 'g': 0.5, 'b': 0.7, 'a': 1})
ss('TownArea/LakeRegion/LakeFillLight', 'light_energy', 0.3)
ss('TownArea/LakeRegion/LakeFillLight', 'omni_range', 50)
ss('TownArea/LakeRegion/LakeFillLight', 'shadow_enabled', False)

print("  Fill lights placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 25 complete — atmospheric polish added!")
