#!/usr/bin/env python3
"""Phase 25b: Fix environment and particle materials — set as resource properties."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    print(f"  .{path.split('/')[-1]}.{prop}: {r}")
    return r

# ============================================================
# 1. Set WorldEnvironment.environment as a resource
# ============================================================
print("=== World Environment ===")

setprop('WorldEnv', 'environment', {
    'class': 'Environment',
    'background_mode': 1,
    'ambient_light_source': 2,
    'ambient_light_color': {'r': 0.3, 'g': 0.35, 'b': 0.4, 'a': 1},
    'ambient_light_energy': 0.5,
    'fog_enabled': True,
    'fog_light_color': {'r': 0.25, 'g': 0.28, 'b': 0.32, 'a': 1},
    'fog_light_energy': 0.3,
    'fog_density': 0.008,
    'glow_enabled': True,
    'glow_intensity': 0.8,
    'glow_strength': 1.0,
    'volumetric_fog_enabled': True,
    'volumetric_fog_density': 0.01,
    'volumetric_fog_albedo': {'r': 0.6, 'g': 0.65, 'b': 0.7, 'a': 1},
    'volumetric_fog_emission': {'r': 0.15, 'g': 0.12, 'b': 0.08, 'a': 1},
    'volumetric_fog_emission_energy': 0.2,
    'ssao_enabled': True,
    'ssao_intensity': 1.5,
    'ssao_radius': 1.0,
    'tonemap_mode': 2,
    'tonemap_white': 1.0,
})

print("  Environment resource set!")

# ============================================================
# 2. Set particle process materials as resource properties
# ============================================================
print("\n=== Particle Materials ===")

# Dust motes
setprop('Architecture/NaveCombiner/DustMotes', 'process_material', {
    'class': 'ParticleProcessMaterial',
    'emission_shape': 1,
    'emission_box_extents': {'x': 12, 'y': 4, 'z': 15},
    'gravity': {'x': 0, 'y': -0.1, 'z': 0},
    'turbulence_enabled': True,
    'turbulence_noise_scale': 2.0,
    'turbulence_influence': 0.3,
})
print("  Dust motes material set!")

# Grove pollen
setprop('TownArea/HiddenGrove/GrovePollen', 'process_material', {
    'class': 'ParticleProcessMaterial',
    'emission_shape': 1,
    'emission_box_extents': {'x': 10, 'y': 5, 'z': 10},
    'gravity': {'x': 0, 'y': 0.05, 'z': 0},
    'turbulence_enabled': True,
    'turbulence_noise_scale': 1.5,
    'turbulence_influence': 0.5,
})
print("  Grove pollen material set!")

# Waterfall mist
setprop('TownArea/Terrain/WaterfallMist', 'process_material', {
    'class': 'ParticleProcessMaterial',
    'emission_shape': 1,
    'emission_box_extents': {'x': 8, 'y': 3, 'z': 4},
    'gravity': {'x': 0, 'y': 0.5, 'z': 0},
    'turbulence_enabled': True,
    'turbulence_influence': 0.8,
})
print("  Waterfall mist material set!")

# Campfire embers
setprop('TownArea/Terrain/CampfireEmbers', 'process_material', {
    'class': 'ParticleProcessMaterial',
    'emission_shape': 1,
    'emission_box_extents': {'x': 0.5, 'y': 0.2, 'z': 0.5},
    'gravity': {'x': 0, 'y': 2.0, 'z': 0},
    'turbulence_enabled': True,
    'turbulence_influence': 0.3,
})
print("  Campfire embers material set!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 25b complete — environment and particles fixed!")
