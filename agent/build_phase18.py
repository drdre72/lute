#!/usr/bin/env python3
"""Phase 18: Rust-style weathered textures — rusted metal, mossy stone,
grungy wood, cracked concrete, oil stains, water damage, grime buildup."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r.get('ok')}")
    return r

# ============================================================
# Rust-style material palettes
# ============================================================

# Rusted iron — orange-brown with high roughness
RUST_METAL = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.18,'b':0.08,'a':1},
    'roughness':0.85,'metallic':0.3,
    'emission_enabled':False
}

# Heavy rust — deep orange with pitted look
HEAVY_RUST = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.12,'b':0.05,'a':1},
    'roughness':0.95,'metallic':0.1
}

# Mossy stone — green-tinged grey
MOSS_STONE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.22,'b':0.15,'a':1},
    'roughness':0.9,'metallic':0.0
}

# Weathered concrete — cracked grey
WEATHERED_CONCRETE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.22,'g':0.21,'b':0.19,'a':1},
    'roughness':0.92,'metallic':0.0
}

# Grungy wood — dark, water-stained
GRUNGY_WOOD = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.10,'b':0.05,'a':1},
    'roughness':0.88,'metallic':0.0
}

# Oil-stained stone
OIL_STONE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.07,'b':0.06,'a':1},
    'roughness':0.5,'metallic':0.1
}

# Verdigris copper — green patina
VERDIGRIS = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.30,'b':0.22,'a':1},
    'roughness':0.7,'metallic':0.4
}

# ============================================================
# 1. Apply rusted metal to hanging lamp chains and bodies
# ============================================================
print("=== Rusted Hanging Lamps ===")

for i in range(6):
    ss(f'Architecture/NaveCombiner/HangingLamp{i}_Chain', 'material_override', HEAVY_RUST)
    ss(f'Architecture/NaveCombiner/HangingLamp{i}_Body', 'material_override', RUST_METAL)

print("  6 lamps rusted!")

# ============================================================
# 2. Apply mossy stone to buttresses and lower walls
# ============================================================
print("\n=== Mossy Stone ===")

for side in ['L', 'R']:
    for i in range(4):
        ss(f'Architecture/NaveCombiner/Buttress_{side}{i}', 'material_override', MOSS_STONE)

# Weather strips → oil stains
ss('Architecture/NaveCombiner/WeatherStrip_L', 'material_override', OIL_STONE)
ss('Architecture/NaveCombiner/WeatherStrip_R', 'material_override', OIL_STONE)

print("  Buttresses mossy, wall bases oil-stained!")

# ============================================================
# 3. Apply verdigris to candle holders
# ============================================================
print("\n=== Verdigris Candle Holders ===")

for side in ['L', 'R']:
    for i in range(6):
        ss(f'Architecture/NaveCombiner/Pillar_{side}{i}/Candle_{side}{i}_Base', 'material_override', VERDIGRIS)

print("  12 candle holders patina'd!")

# ============================================================
# 4. Apply weathered concrete to exterior
# ============================================================
print("\n=== Weathered Exterior ===")

# Front wall
ss('Architecture/Exterior/ExteriorPlaza/FrontWall', 'material_override', WEATHERED_CONCRETE)

# Gargoyles → heavy rust
ss('Architecture/Exterior/ExteriorPlaza/Gargoyle_L', 'material_override', HEAVY_RUST)
ss('Architecture/Exterior/ExteriorPlaza/Gargoyle_R', 'material_override', HEAVY_RUST)

# Bird statues → verdigris
ss('Architecture/Exterior/ExteriorPlaza/BirdStatue_L_Body', 'material_override', VERDIGRIS)
ss('Architecture/Exterior/ExteriorPlaza/BirdStatue_L_Head', 'material_override', VERDIGRIS)
ss('Architecture/Exterior/ExteriorPlaza/BirdStatue_R_Body', 'material_override', VERDIGRIS)
ss('Architecture/Exterior/ExteriorPlaza/BirdStatue_R_Head', 'material_override', VERDIGRIS)

# Relief panels → mossy
for i in range(3):
    ss(f'Architecture/Exterior/ExteriorPlaza/FrontWall/ReliefPanel{i}', 'material_override', MOSS_STONE)

print("  Exterior weathered!")

# ============================================================
# 5. Apply grungy wood to town buildings
# ============================================================
print("\n=== Grungy Town Buildings ===")

building_names = ['House3', 'House4', 'Tavern', 'Blacksmith']
for name in building_names:
    ss(f'TownArea/TownSquare/{name}_Walls', 'material_override', GRUNGY_WOOD)

# Market stalls → grungy
for i in range(4):
    # Try to update stall canopies
    ss(f'TownArea/TownSquare/Stall{i}_Canopy', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.12,'b':0.06,'a':1},
        'roughness':0.9
    })

# Cart → grungy
ss('TownArea/TownSquare/CartBase', 'material_override', GRUNGY_WOOD)

# Windmill → grungy
ss('TownArea/TownSquare/WindmillBase', 'material_override', GRUNGY_WOOD)
for i in range(4):
    ss(f'TownArea/TownSquare/WindmillBlade{i}', 'material_override', GRUNGY_WOOD)

print("  Town buildings grunged!")

# ============================================================
# 6. Apply rusted metal to town props
# ============================================================
print("\n=== Rusted Town Props ===")

# Well posts → rusted
for i in range(4):
    ss(f'TownArea/TownSquare/WellPost{i}', 'material_override', RUST_METAL)

# Gate lanterns → rusted
for side in ['L', 'R']:
    ss(f'TownArea/TownGate/GateLantern_{side}_Post', 'material_override', HEAVY_RUST)

# Anvil → heavy rust
ss('TownArea/TownSquare/Anvil', 'material_override', HEAVY_RUST)

# Weapon rack → grungy
ss('TownArea/TrainingGround/TrainingGeometry/WeaponRack', 'material_override', GRUNGY_WOOD)

# Training dummies → grungy
for i in range(3):
    ss(f'TownArea/TrainingGround/TrainingGeometry/TrainDummy{i}_Post', 'material_override', GRUNGY_WOOD)

print("  Town props rusted!")

# ============================================================
# 7. Apply mossy stone to graveyard, ruins, shrine
# ============================================================
print("\n=== Mossy Ruins ===")

# Tombstones
for i in range(12):
    ss(f'TownArea/Graveyard/GraveyardTerrain/Tombstone{i:02d}', 'material_override', MOSS_STONE)

# Ruin columns
for i in range(6):
    ss(f'TownArea/Terrain/RuinColumn{i:02d}', 'material_override', MOSS_STONE)

# Ruin arch
ss('TownArea/Terrain/RuinArch1', 'material_override', MOSS_STONE)
ss('TownArea/Terrain/RuinArch2', 'material_override', MOSS_STONE)
ss('TownArea/Terrain/RuinLintel', 'material_override', MOSS_STONE)

# Shrine
ss('TownArea/LakeRegion/LakeGeometry/ShrinePlatform', 'material_override', MOSS_STONE)
for i in range(4):
    ss(f'TownArea/LakeRegion/LakeGeometry/ShrinePost{i}', 'material_override', MOSS_STONE)
ss('TownArea/LakeRegion/LakeGeometry/ShrineRoof', 'material_override', MOSS_STONE)

# Cave mound → weathered
ss('TownArea/LakeRegion/LakeGeometry/CaveMound', 'material_override', WEATHERED_CONCRETE)

# Castle → weathered
ss('TownArea/Terrain/CastleBase', 'material_override', WEATHERED_CONCRETE)
ss('TownArea/Terrain/CastleTower_20', 'material_override', WEATHERED_CONCRETE)
ss('TownArea/Terrain/CastleTower_40', 'material_override', WEATHERED_CONCRETE)

print("  Ruins and castle weathered!")

# ============================================================
# 8. Apply rusted metal to bridge and dock hardware
# ============================================================
print("\n=== Rusted Infrastructure ===")

# Bridge railings
ss('TownArea/LakeRegion/LakeGeometry/CastleBridgeRail_L', 'material_override', RUST_METAL)
ss('TownArea/LakeRegion/LakeGeometry/CastleBridgeRail_R', 'material_override', RUST_METAL)

# Dock posts
for i in range(5):
    ss(f'TownArea/LakeRegion/LakeGeometry/DockPost{i}', 'material_override', RUST_METAL)
    ss(f'TownArea/LakeRegion/LakeGeometry/DockPostR{i}', 'material_override', RUST_METAL)

# Overlook railings
ss('TownArea/Terrain/OverlookRail_F', 'material_override', RUST_METAL)
ss('TownArea/Terrain/OverlookRail_L', 'material_override', RUST_METAL)
ss('TownArea/Terrain/OverlookRail_R', 'material_override', RUST_METAL)

print("  Infrastructure rusted!")

# ============================================================
# 9. Apply oil stains and grime to nave floor
# ============================================================
print("\n=== Floor Grime ===")

# Apply darker patches near altar
for i in range(5):
    ss(f'Architecture/NaveCombiner/FloorCrack{i}', 'material_override', OIL_STONE)

# Altar railings → rusted
ss('Architecture/NaveCombiner/AltarRail_L', 'material_override', RUST_METAL)
ss('Architecture/NaveCombiner/AltarRail_R', 'material_override', RUST_METAL)

# Throne → rusted dark metal
ss('Architecture/NaveCombiner/ThroneSeat', 'material_override', HEAVY_RUST)
ss('Architecture/NaveCombiner/ThroneBack', 'material_override', HEAVY_RUST)
ss('Architecture/NaveCombiner/ThroneArm_L', 'material_override', HEAVY_RUST)
ss('Architecture/NaveCombiner/ThroneArm_R', 'material_override', HEAVY_RUST)

print("  Floor grime and throne rusted!")

# ============================================================
# 10. Apply verdigris to sconce brackets
# ============================================================
print("\n=== Verdigris Sconces ===")

for side in ['L', 'R']:
    for i in range(6):
        ss(f'Architecture/NaveCombiner/Sconce_{side}{i}_Bracket', 'material_override', VERDIGRIS)

print("  12 sconce brackets patina'd!")

# ============================================================
# 11. Apply weathered concrete to town walls
# ============================================================
print("\n=== Weathered Town Walls ===")

# Town walls — find them
for i in range(4):
    try:
        ss(f'TownArea/TownSquare/TownWall{i}', 'material_override', WEATHERED_CONCRETE)
    except:
        pass

# Corner towers
for i in range(2):
    try:
        ss(f'TownArea/TownSquare/CornerTower{i}', 'material_override', MOSS_STONE)
    except:
        pass

print("  Town walls weathered!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 18 complete — Rust-style weathered textures applied!")
