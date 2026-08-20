#!/usr/bin/env python3
"""
Noise Terrain Generator for Unreal Engine

Generates natural terrain using numpy-vectorized Perlin noise with domain warping
and ridge noise, renders a color-coded PNG preview, then pushes to UE via bulk
/set_heightmap (uint16 with min_z/max_z mapping).

Techniques borrowed from Rust's procedural generation:
- Domain warping with separate noise fields for organic distortion
- Ridge noise (1 - abs(noise)) blended with fBm for sharp ridgelines
- Radial basin mask for proper lake beds
- Feathered monument flattening with smoothstep falloff
- Whittaker biome model: temperature + moisture noise maps classify biomes
- Biome-aware terrain painting and scatter species selection
- Poisson-disk scatter with constraint masks (slope, height, water, monuments)

Usage:
  python3 noise_terrain.py --preview                          # Preview at default --size (no server needed)
  python3 noise_terrain.py --preview --target-size 204000      # Preview matching a real landscape footprint
  python3 noise_terrain.py --preview --seed 42                 # Different seed
  python3 noise_terrain.py --apply                             # Push to UE (queries live landscape size)
  python3 noise_terrain.py --apply --paint                     # Push + paint terrain layers
  python3 noise_terrain.py --apply --paint --water             # Push + paint + water plane
  python3 noise_terrain.py --scatter-preview --load-preset good_hills --seed 7  # Preview scatter placements
  python3 noise_terrain.py --apply --paint --water --scatter --seed 7  # Full pipeline: terrain + water + paint + scatter

Reproducibility (presets):
  python3 noise_terrain.py --apply --paint --seed 7 --save-preset good_hills
  python3 noise_terrain.py --preview --load-preset good_hills --seed 99   # iterate from a saved recipe
  python3 noise_terrain.py --list-presets
"""

import argparse
import base64
import json
import math
import os
import sys
import time

import numpy as np
import requests
from PIL import Image

SERVER = "http://localhost:6410"
PRESETS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "terrain_presets.json")


def load_presets():
    if not os.path.exists(PRESETS_FILE):
        return {}
    with open(PRESETS_FILE, "r") as f:
        return json.load(f)


def save_preset(name, params):
    presets = load_presets()
    presets[name] = params
    with open(PRESETS_FILE, "w") as f:
        json.dump(presets, f, indent=2)

# ─── Gradient (Perlin-style) noise, numpy-vectorized ─────────────────────────

def _fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)

def _make_permutation(seed):
    rng = np.random.default_rng(seed)
    p = np.arange(256, dtype=np.int32)
    rng.shuffle(p)
    return np.tile(p, 2)

class GradientNoise:
    """Minimal 2D Perlin noise, vectorized over numpy arrays."""
    def __init__(self, seed=0):
        self.perm = _make_permutation(seed)
        angles = np.linspace(0, 2 * np.pi, 256, endpoint=False)
        self.grad = np.stack([np.cos(angles), np.sin(angles)], axis=1)

    def sample(self, x, y):
        xi = np.floor(x).astype(np.int32) & 255
        yi = np.floor(y).astype(np.int32) & 255
        xf = x - np.floor(x)
        yf = y - np.floor(y)
        u = _fade(xf)
        v = _fade(yf)

        def grad_dot(hash_idx, gx, gy):
            g = self.grad[self.perm[hash_idx] & 255]
            return g[..., 0] * gx + g[..., 1] * gy

        aa = self.perm[self.perm[xi] + yi]
        ab = self.perm[self.perm[xi] + yi + 1]
        ba = self.perm[self.perm[xi + 1] + yi]
        bb = self.perm[self.perm[xi + 1] + yi + 1]

        n00 = grad_dot(aa, xf, yf)
        n10 = grad_dot(ba, xf - 1, yf)
        n01 = grad_dot(ab, xf, yf - 1)
        n11 = grad_dot(bb, xf - 1, yf - 1)

        nx0 = n00 + u * (n10 - n00)
        nx1 = n01 + u * (n11 - n01)
        return nx0 + v * (nx1 - nx0)

def fbm(noise, x, y, octaves, lacunarity, persistence):
    total = np.zeros_like(x)
    amplitude = 1.0
    freq = 1.0
    max_amp = 0.0
    for _ in range(octaves):
        total += amplitude * noise.sample(x * freq, y * freq)
        max_amp += amplitude
        amplitude *= persistence
        freq *= lacunarity
    return total / max_amp

def ridge(noise, x, y, octaves, lacunarity, persistence):
    total = np.zeros_like(x)
    amplitude = 1.0
    freq = 1.0
    max_amp = 0.0
    for _ in range(octaves):
        n = 1.0 - np.abs(noise.sample(x * freq, y * freq))
        total += amplitude * n
        max_amp += amplitude
        amplitude *= persistence
        freq *= lacunarity
    return (total / max_amp) * 2.0 - 1.0

def smoothstep_mask(distance, inner_radius, outer_radius):
    """1 inside inner_radius, 0 outside outer_radius, smooth blend between."""
    t = np.clip((outer_radius - distance) / max(outer_radius - inner_radius, 1e-6), 0, 1)
    return t * t * (3 - 2 * t)


# ─── Monument Sites (locked before terrain generation) ──────────────────────

MONUMENT_SITES = [
    {"name": "Town Market",    "x": 0,    "y": 0,    "radius": 735, "feather": 438},
    {"name": "Tavern & Inn",   "x": 520,  "y": -300, "radius": 250, "feather": 150},
    {"name": "Blacksmith",     "x": 520,  "y": 300,  "radius": 225, "feather": 125},
    {"name": "Ancient Ruins",  "x": 0,    "y": 600,  "radius": 275, "feather": 163},
    {"name": "Harbor Dock",    "x": -520, "y": 300,  "radius": 313, "feather": 188},
    {"name": "Watchtower",     "x": -520, "y": -300, "radius": 225, "feather": 125},
]


def randomize_monument_positions(sites, seed, ref_size):
    """Shuffle monument x/y positions using the seed, keeping them spread across the map.
    Returns new site dicts with updated x/y but original radius/feather."""
    import random
    rng = random.Random(seed * 31 + 17)
    half = ref_size / 2
    margin = ref_size * 0.1  # keep monuments away from map edges
    placed = []
    min_dist = ref_size * 0.15  # minimum distance between monument centers

    for site in sites:
        for _ in range(100):
            x = rng.uniform(-half + margin, half - margin)
            y = rng.uniform(-half + margin, half - margin)
            if all(math.hypot(x - p[0], y - p[1]) > min_dist for p in placed):
                break
        placed.append((x, y))
        new_site = dict(site)
        new_site["x"] = x
        new_site["y"] = y
        yield new_site


# ─── Heightmap Generation ───────────────────────────────────────────────────

def generate_heightmap(size, seed, hill_height, water_depth,
                       water_basin_center, water_basin_radius,
                       monument_sites, resolution=512,
                       ridge_weight=0.35, warp_strength=300.0,
                       frequency=1.0/900.0, octaves=5,
                       lacunarity=2.0, persistence=0.5,
                       min_z=-600.0, max_z=1400.0):
    """
    Generate a heightmap as a numpy array using vectorized noise.

    Returns:
        (resolution, resolution) numpy array of world Z heights (float32)
    """
    base_noise = GradientNoise(seed=seed)
    warp_noise_x = GradientNoise(seed=seed + 1000)
    warp_noise_y = GradientNoise(seed=seed + 2000)

    half = size // 2
    coords = np.linspace(-half, half, resolution)
    world_x, world_y = np.meshgrid(coords, coords)

    # Seed-based offset: shift the sampling window to a different region of the
    # Perlin lattice so different seeds produce visibly different terrain.
    # Without this, the sample range is tiny (e.g. [-6, 6]) and shuffling the
    # 256-entry permutation table barely changes the output.
    seed_offset_x = (seed * 137.5) % 256.0
    seed_offset_y = (seed * 271.3) % 256.0

    # Domain warp: displace sample coordinates before reading elevation
    warp_freq = 1.0 / 1400.0
    warp_x = warp_noise_x.sample(world_x * warp_freq, world_y * warp_freq) * warp_strength
    warp_y = warp_noise_y.sample(world_x * warp_freq, world_y * warp_freq) * warp_strength
    sample_x = (world_x + warp_x) * frequency + seed_offset_x
    sample_y = (world_y + warp_y) * frequency + seed_offset_y

    # Rolling hills (fBm) blended with ridged peaks
    fbm_layer = fbm(base_noise, sample_x, sample_y, octaves, lacunarity, persistence)
    ridge_layer = ridge(base_noise, sample_x, sample_y, octaves, lacunarity, persistence)
    combined = fbm_layer * (1 - ridge_weight) + ridge_layer * ridge_weight

    height = combined * hill_height

    # Water basin: radial mask with proper lowest point
    bx, by = water_basin_center
    dist_to_basin = np.sqrt((world_x - bx) ** 2 + (world_y - by) ** 2)
    basin_mask = smoothstep_mask(dist_to_basin, inner_radius=0, outer_radius=water_basin_radius)
    height -= basin_mask * abs(water_depth)

    # Beach transition: gentle slope at waterline
    beach_mask = (height > 0) & (height < 25)
    height = np.where(beach_mask, height * 0.4, height)

    # Feathered monument flattening + shallow berm at circumference
    for site in monument_sites:
        sx, sy, sr, sf = site["x"], site["y"], site["radius"], site["feather"]
        dist_to_site = np.sqrt((world_x - sx) ** 2 + (world_y - sy) ** 2)
        plains_mask = smoothstep_mask(dist_to_site, inner_radius=sr, outer_radius=sr + sf)
        plains_target = 5.0  # Slightly above sea level
        height = height * (1 - plains_mask) + plains_target * plains_mask

        # Berm: 2m tall raised ring with flat crater inside, gap for road entry
        berm_peak = sr * 0.9          # peak slightly inside the radius
        berm_width = sf * 0.4         # narrower berm for a cleaner ridge
        berm_height = 250.0           # ~2.5 meters tall (100 UE units = 1m)
        # Gaussian-like ring: high near berm_peak, falls off on both sides
        berm_mask = np.exp(-((dist_to_site - berm_peak) ** 2) / (2 * berm_width ** 2))

        # Only add berm where terrain is above water (don't berm into water basins)
        berm_mask = berm_mask * (height > -50).astype(np.float32)
        height = height + berm_mask * berm_height

    return np.clip(height, min_z, max_z).astype(np.float32)


# ─── PNG Preview ─────────────────────────────────────────────────────────────

def render_preview_png(heightmap, filepath, water_level=0, min_z=-600, max_z=1400):
    """Render heightmap as a color-coded PNG."""
    h, w = heightmap.shape
    img = Image.new('RGB', (w, h))
    pixels = img.load()

    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_range = max(h_max - h_min, 1.0)

    water_t = max(0, min(1, (water_level - h_min) / h_range))
    beach_t = min(1, water_t + 0.03)
    grass_t = min(1, water_t + 0.25)
    dirt_t = min(1, water_t + 0.50)

    for j in range(h):
        for i in range(w):
            val = float(heightmap[j, i])
            t = (val - h_min) / h_range

            if t < water_t:
                depth = (water_t - t) / max(water_t, 0.01)
                r = int(20 + (60 - 20) * (1 - depth))
                g = int(40 + (100 - 40) * (1 - depth))
                b = int(80 + (180 - 80) * (1 - depth))
                pixels[i, j] = (r, g, b)
            elif t < beach_t:
                pixels[i, j] = (200, 190, 140)
            elif t < grass_t:
                ft = (t - beach_t) / max(beach_t - water_t, 0.01)
                pixels[i, j] = (int(80 + 30 * ft), int(120 + 30 * ft), int(60 + 20 * ft))
            elif t < dirt_t:
                ft = (t - grass_t) / max(dirt_t - grass_t, 0.01)
                pixels[i, j] = (int(110 + 40 * ft), int(90 + 30 * ft), int(60 + 20 * ft))
            else:
                ft = min(1, (t - dirt_t) / max(1 - dirt_t, 0.01))
                pixels[i, j] = (int(150 + 100 * ft), int(140 + 100 * ft), int(130 + 110 * ft))

    img.save(filepath)
    print(f"  Preview saved: {filepath}")
    return True


# ─── Scatter Generation ──────────────────────────────────────────────────────

SCATTER_SPECIES = [
    {
        "name": "pine_tree",
        "mesh_variants": [
            "/Game/Fishermans_Cabin/Meshes/Foliage/Tree/SM_Fir_Tree_01.SM_Fir_Tree_01",
            "/Game/Fishermans_Cabin/Meshes/Foliage/Tree/SM_Fir_Tree_02.SM_Fir_Tree_02",
            "/Game/Fishermans_Cabin/Meshes/Foliage/Tree/SM_Fir_Tree_03.SM_Fir_Tree_03",
            "/Game/Fishermans_Cabin/Meshes/Foliage/Tree/SM_Fir_Tree_04.SM_Fir_Tree_04",
            "/Game/Fishermans_Cabin/Meshes/Foliage/Tree/SM_Fir_Tree_05.SM_Fir_Tree_05",
        ],
        "density": 0.0048,
        "min_scale": 0.8, "max_scale": 1.4,
        "slope_threshold": 0.55,
        "height_min": -200, "height_max": 1200,
        "forest_only": True,
        "water_exclusion": 300,
    },
    {
        "name": "broadleaf_tree",
        "mesh_variants": [
            "/Game/Stylized_Tree_Pack/Meshes/Beech/SM_Stylized_Tree_Beech_01.SM_Stylized_Tree_Beech_01",
            "/Game/Stylized_Tree_Pack/Meshes/Beech/SM_Stylized_Tree_Beech_02.SM_Stylized_Tree_Beech_02",
            "/Game/Stylized_Tree_Pack/Meshes/Beech/SM_Stylized_Tree_Beech_03.SM_Stylized_Tree_Beech_03",
            "/Game/Stylized_Tree_Pack/Meshes/Maple/SM_Stylized_Tree_Maple_01.SM_Stylized_Tree_Maple_01",
            "/Game/Stylized_Tree_Pack/Meshes/Maple/SM_Stylized_Tree_Maple_02.SM_Stylized_Tree_Maple_02",
            "/Game/Stylized_Tree_Pack/Meshes/Maple/SM_Stylized_Tree_Maple_03.SM_Stylized_Tree_Maple_03",
        ],
        "density": 0.003,
        "min_scale": 0.9, "max_scale": 1.6,
        "slope_threshold": 0.45,
        "height_min": -100, "height_max": 800,
        "forest_only": True,
        "water_exclusion": 200,
    },
    {
        "name": "rock",
        "mesh_variants": [
            "/Game/Light_Foliage/Meshes/SM_Rock_01.SM_Rock_01",
            "/Game/Light_Foliage/Meshes/SM_Rock_02.SM_Rock_02",
            "/Game/Light_Foliage/Meshes/SM_Rock_03.SM_Rock_03",
            "/Game/Light_Foliage/Meshes/SM_Rock_04.SM_Rock_04",
            "/Game/Light_Foliage/Meshes/SM_Rock_05.SM_Rock_05",
            "/Game/Light_Foliage/Meshes/SM_Rock_06.SM_Rock_06",
            "/Game/Light_Foliage/Meshes/SM_Rock_07.SM_Rock_07",
            "/Game/Light_Foliage/Meshes/SM_Rock_08.SM_Rock_08",
        ],
        "density": 0.0012,
        "min_scale": 0.7, "max_scale": 2.0,
        "slope_threshold": 0.95,
        "height_min": -500, "height_max": 5000,
        "forest_only": False,
        "water_exclusion": 100,
    },
    {
        "name": "bush",
        "mesh_variants": [
            "/Game/Light_Foliage/Meshes/SM_Bush_01.SM_Bush_01",
            "/Game/Light_Foliage/Meshes/SM_Bush_02.SM_Bush_02",
            "/Game/Light_Foliage/Meshes/SM_Bush_03.SM_Bush_03",
            "/Game/Light_Foliage/Meshes/SM_Bush_04.SM_Bush_04",
            "/Game/Light_Foliage/Meshes/SM_Bush_05.SM_Bush_05",
        ],
        "density": 0.0024,
        "min_scale": 0.6, "max_scale": 1.2,
        "slope_threshold": 0.60,
        "height_min": -50, "height_max": 1000,
        "forest_only": False,
        "water_exclusion": 150,
    },
    {
        "name": "grass",
        "mesh_variants": [
            "/Game/Light_Foliage/Meshes/SM_Grass_01.SM_Grass_01",
            "/Game/Light_Foliage/Meshes/SM_Grass_02.SM_Grass_02",
            "/Game/Light_Foliage/Meshes/SM_Grass_03.SM_Grass_03",
            "/Game/Light_Foliage/Meshes/SM_Grass_04.SM_Grass_04",
        ],
        "density": 0.006,
        "min_scale": 0.5, "max_scale": 1.0,
        "slope_threshold": 0.50,
        "height_min": -50, "height_max": 600,
        "forest_only": False,
        "water_exclusion": 100,
    },
    {
        "name": "arctic_tree",
        "mesh_variants": [
            "/Game/custom_trees/full_arctic_tree/model_LOD0.model_LOD0",
            "/Game/custom_trees/arctic_tallskinny_tree/model_LOD0.model_LOD0",
        ],
        "density": 0.003,
        "min_scale": 0.8, "max_scale": 1.4,
        "slope_threshold": 0.55,
        "height_min": -200, "height_max": 5000,
        "forest_only": False,
        "water_exclusion": 200,
        "z_offset": -50,
    },
    {
        "name": "desert_tree",
        "mesh_variants": [
            "/Game/custom_trees/des_oasis_tree/model_LOD0.model_LOD0",
            "/Game/custom_trees/des_skinny_tree/model_LOD0.model_LOD0",
        ],
        "density": 0.002,
        "min_scale": 0.7, "max_scale": 1.5,
        "slope_threshold": 0.50,
        "height_min": -200, "height_max": 3000,
        "forest_only": False,
        "water_exclusion": 150,
        "z_offset": -50,
    },
]

# Fallback mesh paths (first variant per species)
DEFAULT_SCATTER_MESHES = {
    name: sp["mesh_variants"][0]
    for name, sp in [(sp["name"], sp) for sp in SCATTER_SPECIES]
}


# ─── Biome System (Whittaker Model) ──────────────────────────────────────────

# Biome definitions keyed by biome name
# Each biome has: paint_layer, scatter_species (list of species names from SCATTER_SPECIES),
# and optional density_multiplier
BIOMES = {
    "snow": {
        "paint_layer": "Rock",   # snow uses rock layer (white material)
        "scatter_species": ["arctic_tree", "pine_tree", "rock"],
        "density_mult": 0.5,
        "color": (240, 245, 250),  # for preview
    },
    "tundra": {
        "paint_layer": "Dirt",
        "scatter_species": ["arctic_tree", "rock", "bush"],
        "density_mult": 0.6,
        "color": (130, 140, 120),
    },
    "plains": {
        "paint_layer": "Grass",
        "scatter_species": ["bush", "grass"],
        "density_mult": 1.2,
        "color": (90, 140, 70),
    },
    "forest": {
        "paint_layer": "Grass",
        "scatter_species": ["pine_tree", "broadleaf_tree", "bush", "grass"],
        "density_mult": 1.5,
        "color": (40, 90, 45),
    },
    "swamp": {
        "paint_layer": "Dirt",
        "scatter_species": ["broadleaf_tree", "bush", "grass"],
        "density_mult": 1.0,
        "color": (60, 70, 45),
    },
    "desert": {
        "paint_layer": "Sand",
        "scatter_species": ["desert_tree", "rock"],
        "density_mult": 0.5,
        "color": (210, 190, 130),
    },
}


def generate_biome_map(size, seed, heightmap, water_level, resolution=1024):
    """Generate temperature and moisture noise maps, then classify into biomes.
    Returns (biome_map, temp_map, moisture_map) as (resolution, resolution) arrays.
    biome_map contains biome name strings."""
    half = size / 2
    h, w = heightmap.shape

    coords = np.linspace(-half, half, w)
    world_x, world_y = np.meshgrid(coords, coords)

    # Temperature: latitude gradient (north=cold, south=warm) + noise + altitude cooling
    lat_gradient = (world_y + half) / size  # 0=south(warm), 1=north(cold)
    temp_noise = GradientNoise(seed=seed + 3000)
    temp_freq = 1.0 / (size / 3.0)  # ~3 large temperature zones
    temp_vals = fbm(temp_noise, world_x * temp_freq, world_y * temp_freq, 4, 2.0, 0.5)
    # Base temperature: 0=warm, 1=cold
    temp_map = 0.4 * lat_gradient + 0.3 * (temp_vals + 1.0) / 2.0
    # Altitude cooling: higher = colder
    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_range = max(h_max - h_min, 1.0)
    alt_factor = np.clip((heightmap - h_min) / h_range, 0, 1)
    temp_map += 0.3 * alt_factor
    temp_map = np.clip(temp_map, 0, 1)

    # Moisture: noise-based, lower near water
    moist_noise = GradientNoise(seed=seed + 4000)
    moist_freq = 1.0 / (size / 4.0)  # ~4 moisture zones
    moist_vals = fbm(moist_noise, world_x * moist_freq, world_y * moist_freq, 4, 2.0, 0.5)
    moisture_map = (moist_vals + 1.0) / 2.0  # 0=dry, 1=wet

    # Increase moisture near water (use origin as water center approximation)
    dist_to_water = np.sqrt(world_x ** 2 + world_y ** 2)
    water_proximity = np.clip(1.0 - dist_to_water / (size * 0.15), 0, 1)
    moisture_map = np.clip(moisture_map * 0.7 + water_proximity * 0.3, 0, 1)

    # Classify into biomes using Whittaker thresholds
    biome_map = np.empty((h, w), dtype=object)

    # Temperature: 0=warm, 1=cold
    # Moisture: 0=dry, 1=wet
    cold = temp_map > 0.65
    cool = (temp_map > 0.45) & (temp_map <= 0.65)
    warm = (temp_map > 0.25) & (temp_map <= 0.45)
    hot = temp_map <= 0.25

    dry = moisture_map < 0.3
    moist = (moisture_map >= 0.3) & (moisture_map < 0.6)
    wet = moisture_map >= 0.6

    # Below water level = swamp/water
    underwater = heightmap < water_level

    # Classification matrix:
    #         dry       moist     wet
    # hot     desert    plains    swamp
    # warm    plains    plains    forest
    # cool    tundra    forest    forest
    # cold    tundra    tundra    snow

    biome_map[hot & dry] = "desert"
    biome_map[hot & moist] = "plains"
    biome_map[hot & wet] = "swamp"
    biome_map[warm & dry] = "plains"
    biome_map[warm & moist] = "plains"
    biome_map[warm & wet] = "forest"
    biome_map[cool & dry] = "tundra"
    biome_map[cool & moist] = "forest"
    biome_map[cool & wet] = "forest"
    biome_map[cold & dry] = "tundra"
    biome_map[cold & moist] = "tundra"
    biome_map[cold & wet] = "snow"
    biome_map[underwater] = "swamp"

    return biome_map, temp_map, moisture_map


def biome_preview_colors(biome_map, heightmap, water_level):
    """Generate RGB colors for preview based on biome map + elevation shading."""
    h, w = heightmap.shape
    img = Image.new('RGB', (w, h))
    pixels = img.load()

    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_range = max(h_max - h_min, 1.0)

    for j in range(h):
        for i in range(w):
            biome = biome_map[j, i]
            base = BIOMES[biome]["color"]
            # Shading by elevation
            t = (float(heightmap[j, i]) - h_min) / h_range
            shade = 0.7 + 0.3 * t
            r = int(min(255, base[0] * shade))
            g = int(min(255, base[1] * shade))
            b = int(min(255, base[2] * shade))
            if heightmap[j, i] < water_level:
                depth = (water_level - float(heightmap[j, i])) / max(abs(water_level - h_min), 1.0)
                r = int(r * (1 - depth * 0.5))
                g = int(g * (1 - depth * 0.3))
                b = int(min(255, b + depth * 50))
            pixels[i, j] = (r, g, b)

    return img


def compute_slope(heightmap, size):
    """Compute slope (0-1) from heightmap gradient. 0=flat, 1=vertical."""
    h, w = heightmap.shape
    # World units per pixel
    unit_per_pixel = size / w
    # Gradient via numpy diff (with edge padding)
    dz_dx = np.gradient(heightmap, axis=1) / unit_per_pixel
    dz_dy = np.gradient(heightmap, axis=0) / unit_per_pixel
    slope = np.sqrt(dz_dx ** 2 + dz_dy ** 2)
    # Normalize: slope of 1.0 = 45 degrees, clamp to 0-1
    slope = np.clip(slope / 2.0, 0, 1)
    return slope


def poisson_disk_candidates(width, height, density, seed, k=30):
    """Generate blue-noise candidate points via Bridson Poisson-disk sampling.
    Returns list of (x, y) in world coordinates."""
    import random
    rng = random.Random(seed)

    # Cell size for the spatial grid
    min_dist = 1.0 / max(density, 1e-9)
    min_dist = min(min_dist, min(width, height) / 4)  # cap to avoid huge cells

    cell_size = min_dist / math.sqrt(2)
    cols = int(width / cell_size) + 1
    rows = int(height / cell_size) + 1
    grid = [[None] * cols for _ in range(rows)]

    def grid_idx(x, y):
        return int(x / cell_size), int(y / cell_size)

    def in_bounds(x, y):
        return 0 <= x < width and 0 <= y < height

    def fits(x, y):
        gx, gy = grid_idx(x, y)
        for dy in range(-2, 3):
            for dx in range(-2, 3):
                cx, cy = gx + dx, gy + dy
                if 0 <= cx < cols and 0 <= cy < rows and grid[cy][cx] is not None:
                    px, py = grid[cy][cx]
                    if (x - px) ** 2 + (y - py) ** 2 < min_dist ** 2:
                        return False
        return True

    # Start point
    start_x = rng.uniform(0, width)
    start_y = rng.uniform(0, height)
    active = [(start_x, start_y)]
    gx, gy = grid_idx(start_x, start_y)
    grid[gy][gx] = (start_x, start_y)

    points = [(start_x, start_y)]

    while active:
        idx = rng.randint(0, len(active) - 1)
        px, py = active[idx]
        found = False
        for _ in range(k):
            angle = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(min_dist, min_dist * 2)
            nx = px + r * math.cos(angle)
            ny = py + r * math.sin(angle)
            if in_bounds(nx, ny) and fits(nx, ny):
                active.append((nx, ny))
                points.append((nx, ny))
                gx, gy = grid_idx(nx, ny)
                grid[gy][gx] = (nx, ny)
                found = True
                break
        if not found:
            active.pop(idx)

    return points


def generate_scatter(heightmap, size, seed, monument_sites,
                     water_basin_center, water_basin_radius, water_level,
                     species_configs=None, resolution=1024, biome_map=None):
    """Generate scatter placements for all species.
    Returns dict: species_name -> list of {x, y, z, yaw, scale}.
    If biome_map is provided, species are filtered by biome."""
    if species_configs is None:
        species_configs = SCATTER_SPECIES

    half = size / 2
    h, w = heightmap.shape

    # Compute slope map
    print("  Computing slope map...")
    slope_map = compute_slope(heightmap, size)

    # Compute monument exclusion mask
    print("  Computing constraint masks...")
    coords = np.linspace(-half, half, w)
    world_x, world_y = np.meshgrid(coords, coords)

    monument_exclusion = np.ones((w, h), dtype=np.float32)
    for site in monument_sites:
        sx, sy, sr, sf = site["x"], site["y"], site["radius"], site["feather"]
        dist = np.sqrt((world_x - sx) ** 2 + (world_y - sy) ** 2)
        mask = 1.0 - smoothstep_mask(dist, inner_radius=sr, outer_radius=sr + sf)
        monument_exclusion *= mask

    # Water proximity mask
    bx, by = water_basin_center
    dist_to_water = np.sqrt((world_x - bx) ** 2 + (world_y - by) ** 2)

    # Forest mask: low-frequency noise decides where forests exist
    forest_noise = GradientNoise(seed=seed + 5000)
    forest_freq = 1.0 / (size / 4.0)  # ~4 forest zones across map
    forest_offset_x = (seed * 89.3) % 256.0
    forest_offset_y = (seed * 53.7) % 256.0
    forest_vals = fbm(forest_noise,
                      world_x * forest_freq + forest_offset_x,
                      world_y * forest_freq + forest_offset_y,
                      4, 2.0, 0.5)
    forest_mask = np.where(forest_vals > 0.15, 1.0, 0.0).astype(np.float32)

    # Density noise per species (gives clustering within allowed zones)
    density_noises = {}
    for sp in species_configs:
        density_noises[sp["name"]] = GradientNoise(seed=seed + hash(sp["name"]) % 10000)

    all_placements = {}

    # Build species-to-biome lookup for biome filtering
    species_biomes = {}  # species_name -> set of biome names where it appears
    if biome_map is not None:
        for biome_name, biome_def in BIOMES.items():
            for sp_name in biome_def["scatter_species"]:
                species_biomes.setdefault(sp_name, set()).add(biome_name)

    for sp in species_configs:
        name = sp["name"]
        print(f"  Generating {name}...")

        # Skip species that don't appear in any biome on this map
        if biome_map is not None and name in species_biomes:
            present_biomes = set(np.unique(biome_map))
            if not (species_biomes[name] & present_biomes):
                print(f"    {name}: skipped (not in any biome on this map)")
                all_placements[name] = []
                continue

        # Generate Poisson-disk candidates
        candidates = poisson_disk_candidates(size, size, sp["density"], seed + hash(name) % 100000)

        # Filter candidates against constraints
        accepted = []
        d_noise = density_noises[name]
        d_freq = 1.0 / (size / 20.0)  # moderate-scale clustering

        for cx, cy in candidates:
            # Convert to heightmap pixel coordinates
            px = int((cx / size) * w)
            py = int((cy / size) * h)
            if px < 0 or px >= w or py < 0 or py >= h:
                continue

            # Biome check: skip if this species isn't in the biome at this location
            if biome_map is not None and name in species_biomes:
                biome_here = biome_map[py, px]
                if biome_here not in species_biomes[name]:
                    continue

            # Sample constraints
            height_val = float(heightmap[py, px])
            slope_val = float(slope_map[py, px])
            mon_val = float(monument_exclusion[py, px])
            water_dist = float(dist_to_water[py, px])
            forest_val = float(forest_mask[py, px])

            # Height band check
            if height_val < sp["height_min"] or height_val > sp["height_max"]:
                continue

            # Slope check
            if slope_val > sp["slope_threshold"]:
                continue

            # Monument exclusion
            if mon_val < 0.5:
                continue

            # Water exclusion
            if water_dist < sp["water_exclusion"]:
                continue

            # Forest gating
            if sp["forest_only"] and forest_val < 0.5:
                continue

            # Density noise: probabilistic acceptance for natural clustering
            d_val = d_noise.sample(
                np.array([cx * d_freq]),
                np.array([cy * d_freq])
            )[0]
            d_prob = (d_val + 1.0) / 2.0  # map [-1,1] to [0,1]
            d_prob = 0.5 + 0.5 * d_prob   # baseline 50% + noise-modulated

            # Biome density multiplier
            if biome_map is not None:
                biome_here = biome_map[py, px]
                d_prob *= BIOMES.get(biome_here, {}).get("density_mult", 1.0)

            import random
            rng = random.Random(int(cx * 31 + cy * 17 + seed))
            if rng.random() > d_prob:
                continue

            # Accepted! Add per-instance variation
            scale = rng.uniform(sp["min_scale"], sp["max_scale"])
            yaw = rng.uniform(0, 360)
            mesh_variants = sp.get("mesh_variants", [DEFAULT_SCATTER_MESHES.get(name, "")])
            mesh_path = rng.choice(mesh_variants) if mesh_variants else ""

            z_off = sp.get("z_offset", 0)
            accepted.append({
                "x": round(cx - half, 1),
                "y": round(cy - half, 1),
                "z": round(height_val + z_off, 1),
                "yaw": round(yaw, 1),
                "scale": round(scale, 2),
                "mesh_path": mesh_path,
            })

        all_placements[name] = accepted
        print(f"    {name}: {len(accepted)} placements (from {len(candidates)} candidates)")

    return all_placements


def snap_to_terrain_local(points, heightmap, world_size, min_z, max_z):
    """Snap Z coordinates using local heightmap data instead of querying UE.
    Avoids crashing UE after heavy weightmap uploads."""
    h, w = heightmap.shape
    half = world_size / 2
    snapped = []
    for p in points:
        sp = dict(p)
        # Map world (x,y) to heightmap pixel
        nx = (p["x"] + half) / world_size
        ny = (p["y"] + half) / world_size
        ix = int(np.clip(nx * w, 0, w - 1))
        iy = int(np.clip(ny * h, 0, h - 1))
        # Heightmap is normalized [0,1] mapped to [min_z, max_z]
        h_val = float(heightmap[iy, ix])
        sp["z"] = round(h_val, 1)
        snapped.append(sp)
    return snapped


def snap_to_terrain(points, batch_size=500):
    """Query /batch_terrain_height for all points and replace Z with actual landscape height.
    Uses a single batch HTTP request instead of hundreds of individual queries."""
    import time
    snapped = []
    failed = 0

    for i in range(0, len(points), batch_size):
        batch = points[i:i + batch_size]
        query_points = [{"x": p["x"], "y": p["y"]} for p in batch]

        try:
            r = requests.post(f"{SERVER}/batch_terrain_height",
                              json={"points": query_points}, timeout=30)
            if r.status_code == 200:
                result = r.json()
                heights = result.get("heights", [])
                for j, p in enumerate(batch):
                    snapped_p = dict(p)
                    if j < len(heights) and heights[j].get("hit"):
                        snapped_p["z"] = round(float(heights[j]["z"]), 1)
                    else:
                        failed += 1
                    snapped.append(snapped_p)
            else:
                # Fallback to individual queries
                for p in batch:
                    snapped.append(dict(p))
                    failed += 1
        except Exception:
            for p in batch:
                snapped.append(dict(p))
                failed += 1

        print(f"    Snapped {min(i + batch_size, len(points))}/{len(points)} points...")

    if failed:
        print(f"    {failed} points couldn't be snapped (kept original Z)")
    return snapped


def upload_scatter(placements, mesh_overrides=None, cell_size=25600.0, snap_z=True,
                   heightmap=None, world_size=None, min_z=None, max_z=None):
    """Upload scatter placements to UE via /terrain/scatter endpoint.
    Falls back to /batch_place if the WP endpoint isn't available yet.
    Groups placements by mesh_path so each HISM gets all its instances.
    If snap_z is True, snaps Z to terrain height (locally if heightmap provided, else queries UE)."""
    if mesh_overrides is None:
        mesh_overrides = {}

    # Group all placements by mesh_path across all species
    by_mesh = {}
    for species_name, points in placements.items():
        for p in points:
            mesh_path = mesh_overrides.get(species_name, p.get("mesh_path", DEFAULT_SCATTER_MESHES.get(species_name, "")))
            if not mesh_path:
                continue
            clean_p = {k: v for k, v in p.items() if k != "mesh_path"}
            by_mesh.setdefault(mesh_path, []).append(clean_p)

    # Probe which endpoint is available (with retries — UE may be busy after weightmap upload)
    use_wp = False  # TODO: temporarily disabled /terrain/scatter to test /batch_place
    probe_ok = True
    for attempt in range(3):
        try:
            probe = requests.post(f"{SERVER}/terrain/scatter", json={"mesh_path": "", "placements": []}, timeout=30)
            if probe.status_code == 404:
                use_wp = False
                probe_ok = True
                print("  (WP endpoint not found, falling back to /batch_place)")
            elif probe.status_code in (200, 400):
                # 400 = endpoint exists but rejected empty mesh_path — that's fine
                probe_ok = True
            break
        except Exception as e:
            print(f"  (Probe attempt {attempt+1}/3 failed: {e})")
            if attempt < 2:
                time.sleep(3)
    if not probe_ok:
        use_wp = False
        print("  (Server unreachable after retries, falling back to /batch_place)")

    # Snap Z to terrain height — use local heightmap if available to avoid UE query
    if snap_z:
        if heightmap is not None and world_size is not None:
            print("\n  Snapping placements to terrain height (local heightmap)...")
            for mesh_path in by_mesh:
                by_mesh[mesh_path] = snap_to_terrain_local(
                    by_mesh[mesh_path], heightmap, world_size, min_z or 0, max_z or 0)
                print(f"    Snapped {len(by_mesh[mesh_path])} points...")
        else:
            print("\n  Snapping placements to terrain height (UE query)...")
            for mesh_path in by_mesh:
                by_mesh[mesh_path] = snap_to_terrain(by_mesh[mesh_path])

    endpoint = f"{SERVER}/terrain/scatter" if use_wp else f"{SERVER}/batch_place"
    batch_size = 2000 if use_wp else 500

    total_spawned = 0
    for mesh_path, points in by_mesh.items():
        if not points:
            continue

        mesh_name = mesh_path.split("/")[-1].split(".")[0]
        print(f"\n  Uploading {len(points)} instances of {mesh_name}...")

        for i in range(0, len(points), batch_size):
            batch = points[i:i + batch_size]
            payload = {"mesh_path": mesh_path, "placements": batch}
            if use_wp:
                payload["cell_size"] = cell_size
            try:
                r = requests.post(endpoint, json=payload, timeout=120)
                if r.status_code == 200:
                    result = r.json()
                    if result.get("success"):
                        spawned = result.get("instance_count", len(batch))
                        cells = result.get("cell_actors", 0)
                        total_spawned += spawned
                        if use_wp:
                            print(f"    Batch {i//batch_size + 1}: {spawned} instances across {cells} WP cells OK")
                        else:
                            print(f"    Batch {i//batch_size + 1}: {len(batch)} instances OK")
                    else:
                        print(f"    Batch {i//batch_size + 1}: ERROR: {result.get('error', 'unknown')}")
                else:
                    print(f"    Batch {i//batch_size + 1}: HTTP {r.status_code}")
            except Exception as e:
                print(f"    Batch {i//batch_size + 1}: Exception: {e}")
                # Retry once after 5s on connection failure
                if "Connection refused" in str(e) or "Max retries" in str(e):
                    print(f"    Retrying in 5s...")
                    time.sleep(5)
                    try:
                        r = requests.post(endpoint, json=payload, timeout=120)
                        if r.status_code == 200:
                            result = r.json()
                            if result.get("success"):
                                spawned = result.get("instance_count", len(batch))
                                total_spawned += spawned
                                print(f"    Batch {i//batch_size + 1}: {spawned} instances OK (retry)")
                    except Exception as e2:
                        print(f"    Retry failed: {e2}")
            time.sleep(1.0)  # delay between batches to avoid overwhelming UE

    print(f"\n  Total spawned: {total_spawned}")
    return total_spawned


def render_scatter_preview(heightmap, placements, filepath, size, water_level=0):
    """Render heightmap + scatter points overlay as a PNG."""
    h, w = heightmap.shape
    img = Image.new('RGB', (w, h))
    pixels = img.load()

    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_range = max(h_max - h_min, 1.0)

    water_t = max(0, min(1, (water_level - h_min) / h_range))
    beach_t = min(1, water_t + 0.03)
    grass_t = min(1, water_t + 0.25)
    dirt_t = min(1, water_t + 0.50)

    for j in range(h):
        for i in range(w):
            val = float(heightmap[j, i])
            t = (val - h_min) / h_range
            if t < water_t:
                depth = (water_t - t) / max(water_t, 0.01)
                pixels[i, j] = (int(20 + 40 * (1 - depth)), int(40 + 60 * (1 - depth)), int(80 + 100 * (1 - depth)))
            elif t < beach_t:
                pixels[i, j] = (200, 190, 140)
            elif t < grass_t:
                pixels[i, j] = (80, 110, 50)
            elif t < dirt_t:
                pixels[i, j] = (110, 90, 60)
            else:
                pixels[i, j] = (150, 140, 130)

    # Overlay scatter points with species-specific colors
    species_colors = {
        "pine_tree": (20, 80, 20),
        "broadleaf_tree": (40, 120, 30),
        "rock": (90, 90, 100),
        "bush": (60, 100, 40),
        "grass": (100, 140, 50),
    }

    half = size / 2
    for species_name, points in placements.items():
        color = species_colors.get(species_name, (255, 0, 255))
        for p in points:
            px = int((p["x"] / size + 0.5) * w)
            py = int((p["y"] / size + 0.5) * h)
            if 0 <= px < w and 0 <= py < h:
                pixels[px, py] = color
                # Draw a 2x2 block for visibility
                if px + 1 < w:
                    pixels[px + 1, py] = color
                if py + 1 < h:
                    pixels[px, py + 1] = color
                if px + 1 < w and py + 1 < h:
                    pixels[px + 1, py + 1] = color

    img.save(filepath)
    print(f"  Scatter preview saved: {filepath}")
    return True


# ─── Bulk Upload to UE ───────────────────────────────────────────────────────

def upload_heightmap(heightmap, world_x0, world_y0, world_x1, world_y1, min_z, max_z):
    """Upload heightmap to UE via /set_heightmap bulk endpoint (uint16 mode)."""
    h, w = heightmap.shape
    print(f"\n  Uploading heightmap ({w}x{h}) to UE...")

    # Normalize to uint16 [0..65535] mapped through min_z/max_z
    normalized = (heightmap - min_z) / (max_z - min_z)
    uint16_data = (np.clip(normalized, 0, 1) * 65535).astype(np.uint16)
    b64_data = base64.b64encode(uint16_data.tobytes()).decode('ascii')

    payload = {
        "data": b64_data,
        "width": w,
        "height": h,
        "x0": world_x0,
        "y0": world_y0,
        "x1": world_x1,
        "y1": world_y1,
        "min_z": min_z,
        "max_z": max_z,
    }

    print(f"  Payload size: {len(b64_data)} chars ({len(uint16_data.tobytes())} bytes raw)")

    try:
        r = requests.post(f"{SERVER}/set_heightmap", json=payload, timeout=120)
        if r.status_code == 200:
            result = r.json()
            if result.get("success"):
                print(f"  Written {result.get('written_verts', 0)} verts")
                print(f"  Landscape region: ({result.get('land_extent_x')}, {result.get('land_extent_y')}) "
                      f"{result.get('land_width')}x{result.get('land_height')}")
                return True
            else:
                print(f"  Error: {result.get('error', 'unknown')}")
                return False
        else:
            print(f"  HTTP {r.status_code}: {r.text[:200]}")
            return False
    except Exception as e:
        print(f"  Request error: {e}")
        return False


# ─── Water Plane ─────────────────────────────────────────────────────────────

def spawn_water(world_x, world_y, water_z, size_x, size_y, material_path=""):
    """Spawn a water plane in UE at the given position and height."""
    payload = {
        "x": world_x,
        "y": world_y,
        "z": water_z,
        "size_x": size_x,
        "size_y": size_y,
    }
    if material_path:
        payload["material"] = material_path

    try:
        r = requests.post(f"{SERVER}/spawn_water", json=payload, timeout=30)
        if r.status_code == 200:
            result = r.json()
            if result.get("success"):
                print(f"  Water plane spawned at ({world_x:.0f}, {world_y:.0f}, {water_z:.0f}) "
                      f"size {size_x:.0f}x{size_y:.0f}")
                return True
            else:
                print(f"  Water error: {result.get('error', 'unknown')}")
        else:
            print(f"  Water HTTP {r.status_code}")
    except Exception as e:
        print(f"  Water exception: {e}")
    return False


# ─── Terrain Painting (bulk weightmap upload) ────────────────────────────────

# Biome prototypes in normalized (T, M) space: (temperature, moisture)
# T: 0=hot, 1=cold  |  M: 0=dry, 1=wet
BIOME_PROTOTYPES = {
    "desert":  (0.10, 0.10),
    "plains":  (0.35, 0.35),
    "swamp":   (0.20, 0.80),
    "forest":  (0.55, 0.65),
    "tundra":  (0.75, 0.25),
    "snow":    (0.90, 0.75),
}

SOFTMAX_SHARPNESS = 8.0  # higher = crisper borders, lower = softer blending


def histogram_equalize(arr):
    """Histogram-equalize a 2D array to [0,1] for balanced distribution."""
    flat = arr.flatten()
    # Sort and get rank-based mapping
    sort_idx = np.argsort(flat)
    ranks = np.empty_like(sort_idx)
    ranks[sort_idx] = np.arange(len(flat))
    equalized = ranks / (len(flat) - 1)
    return equalized.reshape(arr.shape)


def compute_weightmaps_from_biome_map(biome_map, heightmap, water_level):
    """Compute per-layer weightmaps directly from a biome_map (for --force-biome).
    Each pixel maps to its biome's paint_layer at full weight."""
    h, w = heightmap.shape
    layer_names = ["Sand", "Grass", "Dirt", "Rock"]
    layer_weightmaps = {ln: np.zeros((h, w), dtype=np.float32) for ln in layer_names}

    for biome_name, biome_def in BIOMES.items():
        mask = biome_map == biome_name
        layer = biome_def["paint_layer"]
        layer_weightmaps[layer][mask] = 1.0

    # Water override: force Sand near/below water level
    water_depth_factor = np.clip((water_level - heightmap) / max(abs(water_level), 1.0), 0, 1)
    water_mask = water_depth_factor ** 2
    for ln in layer_names:
        layer_weightmaps[ln] *= (1.0 - water_mask)
    layer_weightmaps["Sand"] += water_mask

    result = {}
    for ln in layer_names:
        result[ln] = np.clip(layer_weightmaps[ln] * 255.0, 0, 255).astype(np.uint8)
    return result


def compute_layer_weightmaps(temp_map, moisture_map, heightmap, water_level):
    """Compute per-layer weightmaps (Sand, Grass, Dirt, Rock) using softmax over
    distance-to-biome-prototype in normalized (T, M) space.
    Returns dict: layer_name -> uint8 array (0-255)."""
    h, w = temp_map.shape

    # Histogram-equalize temp and moisture for balanced biome distribution
    t_eq = histogram_equalize(temp_map)
    m_eq = histogram_equalize(moisture_map)

    # Stack into (h, w, 2) TM field
    tm = np.stack([t_eq, m_eq], axis=-1)  # (h, w, 2)

    # Compute softmax weights for each biome
    biome_weights = {}
    for biome_name, (proto_t, proto_m) in BIOME_PROTOTYPES.items():
        proto = np.array([proto_t, proto_m])
        # Squared distance to prototype
        dist_sq = np.sum((tm - proto) ** 2, axis=-1)
        # Negative distance * sharpness → logits
        biome_weights[biome_name] = -SOFTMAX_SHARPNESS * dist_sq

    # Stack logits and softmax across biomes
    logit_stack = np.stack([biome_weights[b] for b in BIOME_PROTOTYPES.keys()], axis=-1)  # (h, w, 6)
    # Numerically stable softmax
    logit_max = np.max(logit_stack, axis=-1, keepdims=True)
    exp_logits = np.exp(logit_stack - logit_max)
    softmax_weights = exp_logits / np.sum(exp_logits, axis=-1, keepdims=True)  # (h, w, 6)

    # Map biome weights to layer weights
    biome_names = list(BIOME_PROTOTYPES.keys())
    layer_names = ["Sand", "Grass", "Dirt", "Rock"]
    layer_weightmaps = {ln: np.zeros((h, w), dtype=np.float32) for ln in layer_names}

    for i, biome_name in enumerate(biome_names):
        w_biome = softmax_weights[:, :, i]
        layer = BIOMES[biome_name]["paint_layer"]
        layer_weightmaps[layer] += w_biome

    # Water override: force Sand near/below water level
    water_depth_factor = np.clip((water_level - heightmap) / max(abs(water_level), 1.0), 0, 1)
    water_mask = water_depth_factor ** 2  # smooth falloff
    for ln in layer_names:
        layer_weightmaps[ln] *= (1.0 - water_mask)
    layer_weightmaps["Sand"] += water_mask

    # Convert to uint8 (0-255)
    result = {}
    for ln in layer_names:
        result[ln] = np.clip(layer_weightmaps[ln] * 255.0, 0, 255).astype(np.uint8)

    # Print distribution stats
    total_px = h * w
    print("  Layer weight distribution (mean weight %):")
    for ln in layer_names:
        mean_w = float(np.mean(layer_weightmaps[ln]))
        print(f"    {ln:8s}: {mean_w*100:.1f}%")

    return result


def upload_layer_weightmap(layer_name, weight_data, world_x0, world_y0, world_x1, world_y1):
    """Upload a single layer weightmap to UE via /set_layer_weightmap."""
    h, w = weight_data.shape
    b64_data = base64.b64encode(weight_data.tobytes()).decode("ascii")
    payload = {
        "layer": layer_name,
        "width": w,
        "height": h,
        "data": b64_data,
        "x0": world_x0,
        "y0": world_y0,
        "x1": world_x1,
        "y1": world_y1,
    }
    try:
        r = requests.post(f"{SERVER}/set_layer_weightmap", json=payload, timeout=60)
        if r.status_code == 200:
            result = r.json()
            if result.get("success"):
                print(f"    {layer_name}: {result.get('written_verts', 0)} verts OK")
                return True
            else:
                print(f"    {layer_name}: error - {result.get('error', 'unknown')}")
        else:
            print(f"    {layer_name}: HTTP {r.status_code}")
    except Exception as e:
        print(f"    {layer_name}: exception - {e}")
    return False


def paint_terrain_from_heightmap(heightmap, world_size, water_level=0,
                                  biome_map=None, temp_map=None, moisture_map=None,
                                  world_x0=None, world_y0=None, world_x1=None, world_y1=None):
    """Paint terrain layers using bulk weightmap upload (softmax from T/M fields)
    or elevation fallback if no temp/moisture maps provided."""
    print("\n  Setting up landscape material...")

    # Create and assign landscape material with 4 layer slots
    try:
        r = requests.post(f"{SERVER}/setup_landscape_material", json={}, timeout=30)
        if r.status_code == 200:
            result = r.json()
            print(f"  Material: {result.get('material', 'unknown')}")
            print(f"  Layers: {result.get('layers', [])}")
        else:
            print(f"  Warning: material setup returned {r.status_code}: {r.text[:200]}")
    except Exception as e:
        print(f"  Warning: could not setup material: {e}")

    print("  Setting up landscape layers...")

    # Ensure paint layers exist
    try:
        r = requests.post(f"{SERVER}/setup_landscape_layers", json={
            "layers": ["Sand", "Grass", "Dirt", "Rock"]
        }, timeout=30)
        if r.status_code == 200:
            result = r.json()
            if result.get("created_layers"):
                print(f"  Created layers: {result['created_layers']}")
            if result.get("existing_layers"):
                print(f"  Existing layers: {result['existing_layers']}")
    except Exception as e:
        print(f"  Warning: could not setup layers: {e}")

    # Default world bounds
    if world_x0 is None:
        half = world_size / 2
        world_x0, world_y0, world_x1, world_y1 = -half, -half, half, half

    if temp_map is not None and moisture_map is not None:
        print("  Computing layer weightmaps (softmax + histogram equalization)...")
        layer_weightmaps = compute_layer_weightmaps(temp_map, moisture_map, heightmap, water_level)
    elif biome_map is not None:
        print("  Computing layer weightmaps from biome map...")
        layer_weightmaps = compute_weightmaps_from_biome_map(biome_map, heightmap, water_level)
    else:
        print("  No biome data — using elevation fallback (brush strokes)...")
        _paint_elevation_fallback(heightmap, world_size, water_level)
        return

    print("  Uploading layer weightmaps...")
    for layer_name in ["Sand", "Grass", "Dirt", "Rock"]:
        upload_layer_weightmap(layer_name, layer_weightmaps[layer_name],
                               world_x0, world_y0, world_x1, world_y1)

    print("  Flushing landscape edits...")
    try:
        r = requests.post(f"{SERVER}/flush_landscape", json={}, timeout=60)
        if r.status_code == 200:
            print("  Flush OK")
        else:
            print(f"  Flush HTTP {r.status_code}")
    except Exception as e:
        print(f"  Flush exception: {e}")


def _paint_elevation_fallback(heightmap, world_size, water_level):
    """Elevation-based brush stroke painting (fallback when no biome data)."""
    h, w = heightmap.shape
    half = world_size // 2
    grid_steps = 50
    brush_radius = (world_size / grid_steps) * 0.5

    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_range = max(h_max - h_min, 1.0)
    sand_max = h_min + h_range * 0.15
    grass_max = h_min + h_range * 0.45
    dirt_max = h_min + h_range * 0.70

    categories = {"Sand": [], "Grass": [], "Dirt": [], "Rock": []}
    for j in range(grid_steps):
        for i in range(grid_steps):
            hm_j = int((j / grid_steps) * h)
            hm_i = int((i / grid_steps) * w)
            val = float(heightmap[hm_j, hm_i])
            wx = -half + ((i + 0.5) / grid_steps) * world_size
            wy = -half + ((j + 0.5) / grid_steps) * world_size
            if val < sand_max:
                categories["Sand"].append((wx, wy))
            elif val < grass_max:
                categories["Grass"].append((wx, wy))
            elif val < dirt_max:
                categories["Dirt"].append((wx, wy))
            else:
                categories["Rock"].append((wx, wy))

    for layer_name in ["Rock", "Dirt", "Grass", "Sand"]:
        points = categories.get(layer_name, [])
        if not points:
            print(f"    {layer_name}: no points")
            continue
        print(f"    {layer_name}: {len(points)} paint calls (r={brush_radius:.0f}u)...", end="", flush=True)
        ok = 0
        for (x, y) in points:
            try:
                r = requests.post(f"{SERVER}/terrain_paint", json={
                    "x": x, "y": y,
                    "radius": brush_radius,
                    "strength": 1.0,
                    "layer": layer_name
                }, timeout=30)
                if r.status_code == 200 and r.json().get("success"):
                    ok += 1
            except:
                pass
        print(f" {ok}/{len(points)} ok")


# ─── Main ───────────────────────────────────────────────────────────────────

PRESET_KEYS = [
    "seed", "resolution", "ridge_weight", "octaves",
    "hill_height", "water_depth", "water_radius_frac", "water_x_frac", "water_y_frac",
    "warp_frac", "freq_features", "min_z", "max_z", "target_size", "randomize_monuments",
]


def main():
    parser = argparse.ArgumentParser(description="Noise Terrain Generator for Unreal Engine")
    parser.add_argument("--seed", type=int, default=1337, help="Noise seed (default 1337)")
    parser.add_argument("--size", type=int, default=3000,
                         help="Reference size (UE units) that MONUMENT_SITES coordinates are authored in. "
                              "Not the world footprint size — see --target-size (default 3000)")
    parser.add_argument("--resolution", type=int, default=1024, help="Heightmap grid resolution (default 1024)")
    parser.add_argument("--ridge-weight", type=float, default=0.15, help="Ridge noise blend 0-1 (default 0.15)")
    parser.add_argument("--octaves", type=int, default=7, help="Noise octaves (default 7)")

    # Vertical (world Z) params — absolute world units, independent of footprint size
    parser.add_argument("--hill-height", type=float, default=800.0, help="Max hill elevation in world units (default 800)")
    parser.add_argument("--water-depth", type=float, default=-2000.0, help="Water basin depth in world units (default -2000)")
    parser.add_argument("--min-z", type=float, default=-3000.0, help="Min world Z for uint16 mapping (default -3000)")
    parser.add_argument("--max-z", type=float, default=6000.0, help="Max world Z for uint16 mapping (default 6000)")

    # Horizontal params — fractions of the footprint size, so they scale with the map
    parser.add_argument("--water-radius-frac", type=float, default=0.15, help="Water basin radius as fraction of footprint size (default 0.15)")
    parser.add_argument("--water-x-frac", type=float, default=-0.3, help="Water basin center X as fraction of footprint size (default -0.3)")
    parser.add_argument("--water-y-frac", type=float, default=0.0, help="Water basin center Y as fraction of footprint size (default 0.0)")
    parser.add_argument("--warp-frac", type=float, default=0.02, help="Domain warp displacement as fraction of footprint size (default 0.02)")
    parser.add_argument("--freq-features", type=float, default=12.0, help="Approx. number of major terrain features across the footprint (default 12)")

    # Footprint size for preview when not querying a live server
    parser.add_argument("--target-size", type=float, default=None,
                         help="Footprint size (world units) to simulate for --preview when the UE server "
                              "isn't reachable or you want to preview a hypothetical size. Ignored for "
                              "--apply (which always queries the live landscape size).")

    parser.add_argument("--preview", action="store_true", help="Generate PNG preview only")
    parser.add_argument("--apply", action="store_true", help="Push heightmap to UE via bulk endpoint")
    parser.add_argument("--paint", action="store_true", help="Paint terrain layers after applying")
    parser.add_argument("--scatter", action="store_true", help="Generate scatter (trees, rocks, bushes) and upload to UE")
    parser.add_argument("--scatter-preview", action="store_true", help="Generate scatter and render preview PNG (no upload)")
    parser.add_argument("--scatter-cell-size", type=float, default=25600.0,
                         help="World Partition grid cell size in world units (default 25600 = 256m)")
    parser.add_argument("--no-snap", action="store_true",
                         help="Skip snapping scatter Z to terrain height (faster but may float)")
    parser.add_argument("--no-biome", action="store_true",
                         help="Disable Whittaker biome model (use elevation-only painting and scatter)")
    parser.add_argument("--force-biome", default=None,
                         help="Force a single biome everywhere (e.g. desert, forest, snow). Overrides Whittaker model.")
    parser.add_argument("--monuments", type=int, default=None,
                         help="Number of monuments to place (default: all 6). Use 1 for single monument.")
    parser.add_argument("--water", action="store_true",
                         help="Spawn a water plane at the water level after applying heightmap")
    parser.add_argument("--water-material", default="",
                         help="Material path for water plane (e.g. /Game/Materials/M_Water)")
    parser.add_argument("--preview-path", default=None, help="Custom preview PNG path")

    parser.add_argument("--save-preset", default=None, help="Save the resolved parameters under this name for later reuse")
    parser.add_argument("--load-preset", default=None, help="Load parameters from a saved preset (CLI flags you pass explicitly still take priority)")
    parser.add_argument("--randomize-monuments", action="store_true",
                         help="Randomize monument site positions based on seed (default: fixed positions)")
    parser.add_argument("--list-presets", action="store_true", help="List saved presets and exit")

    args = parser.parse_args()

    if args.list_presets:
        presets = load_presets()
        if not presets:
            print("No saved presets.")
        else:
            print("Saved presets:")
            for name, params in presets.items():
                print(f"  {name}: {json.dumps(params)}")
        return

    # Merge in a saved preset for any flag the user did NOT explicitly pass on the CLI
    if args.load_preset:
        presets = load_presets()
        if args.load_preset not in presets:
            print(f"ERROR: preset '{args.load_preset}' not found. Use --list-presets to see available presets.")
            sys.exit(1)
        preset = presets[args.load_preset]
        explicit = set()
        for tok in sys.argv[1:]:
            if tok.startswith("--"):
                explicit.add(tok[2:].split("=")[0].replace("-", "_"))
        for key, value in preset.items():
            if key not in explicit and hasattr(args, key):
                setattr(args, key, value)
        print(f"  Loaded preset '{args.load_preset}'")

    if not args.preview and not args.apply:
        args.preview = True

    # Determine footprint size: live landscape (if --apply), else --target-size, else --size
    land_info = None
    if args.apply:
        try:
            r = requests.get(f"{SERVER}/state", timeout=5)
            if r.status_code != 200:
                raise Exception("Bad status")
            print(f"  Server: {r.json().get('map_name', 'unknown')}")
            r2 = requests.get(f"{SERVER}/landscape_info", timeout=10)
            if r2.status_code == 200 and r2.json().get("success"):
                land_info = r2.json()
        except Exception:
            print("\n  ERROR: Server not responding on port 6410")
            sys.exit(1)
        if not land_info:
            print("\n  ERROR: Could not query landscape info")
            sys.exit(1)
        wx0, wy0 = land_info["world_min_x"], land_info["world_min_y"]
        wx1, wy1 = land_info["world_max_x"], land_info["world_max_y"]
        size = max(wx1 - wx0, wy1 - wy0)
    else:
        size = args.target_size if args.target_size else args.size
        half = size / 2
        wx0, wy0, wx1, wy1 = -half, -half, half, half

    # Derive horizontal-scaled params from footprint size
    warp_strength = size * args.warp_frac
    frequency = 1.0 / (size / args.freq_features)
    water_radius = size * args.water_radius_frac
    water_x = size * args.water_x_frac
    water_y = size * args.water_y_frac

    if args.randomize_monuments:
        sites = MONUMENT_SITES
        if args.monuments is not None:
            sites = MONUMENT_SITES[:args.monuments]
        monuments = list(randomize_monument_positions(sites, args.seed, args.size))
    else:
        sites = MONUMENT_SITES
        if args.monuments is not None:
            sites = MONUMENT_SITES[:args.monuments]
        monuments = sites

    scaled_monuments = [
        {"name": s["name"],
         "x": s["x"] / args.size * size,
         "y": s["y"] / args.size * size,
         "radius": s["radius"] / args.size * size,
         "feather": s["feather"] / args.size * size}
        for s in monuments
    ]

    print("=" * 60)
    print("  NOISE TERRAIN GENERATOR")
    print(f"  Seed: {args.seed}")
    print(f"  Footprint size: {size:.0f}u" + (f"  ({land_info['grid_size']}x{land_info['grid_height']} grid, live)" if land_info else "  (simulated, no live server)"))
    print(f"  Resolution: {args.resolution}x{args.resolution}")
    print(f"  Hill height: {args.hill_height:.0f}u, warp: {warp_strength:.0f}u")
    print(f"  Ridge weight: {args.ridge_weight}")
    print(f"  Water: ({water_x:.0f}, {water_y:.0f}) r={water_radius:.0f}u depth={args.water_depth:.0f}u")
    print(f"  Frequency: {frequency:.6f} (~{1.0/frequency:.0f}u per feature)")
    print(f"  Monuments: {len(scaled_monuments)} sites, radius {scaled_monuments[0]['radius']:.0f}u" +
          (" (randomized)" if args.randomize_monuments else " (fixed)"))
    for s in scaled_monuments:
        print(f"    {s['name']:16s} at ({s['x']:.0f}, {s['y']:.0f})")
    print(f"  Z range: {args.min_z} to {args.max_z}")
    print("=" * 60)

    print("\n  Generating heightmap...")
    t0 = time.time()
    heightmap = generate_heightmap(
        size=int(size),
        seed=args.seed,
        hill_height=args.hill_height,
        water_depth=args.water_depth,
        water_basin_center=(water_x, water_y),
        water_basin_radius=water_radius,
        monument_sites=scaled_monuments,
        resolution=args.resolution,
        ridge_weight=args.ridge_weight,
        warp_strength=warp_strength,
        frequency=frequency,
        octaves=args.octaves,
        min_z=args.min_z,
        max_z=args.max_z,
    )
    t1 = time.time()
    print(f"  Generated {args.resolution}x{args.resolution} heightmap in {t1-t0:.2f}s")

    h_min = float(heightmap.min())
    h_max = float(heightmap.max())
    h_avg = float(heightmap.mean())
    print(f"  Height range: {h_min:.0f} to {h_max:.0f} (avg {h_avg:.0f})")

    # Generate biome map (Whittaker model: temperature + moisture)
    water_level = args.water_depth * 0.5
    biome_map = None
    temp_map = None
    moisture_map = None
    if not args.no_biome:
        if args.force_biome:
            print(f"\n  Forcing biome: {args.force_biome} (all terrain)")
            h, w = heightmap.shape
            biome_map = np.empty((h, w), dtype=object)
            biome_map[:] = args.force_biome
            biome_counts = {args.force_biome: h * w}
            total_px = args.resolution * args.resolution
            print("  Biome distribution:")
            print(f"    {args.force_biome:10s}: {h*w:6d} px (100.0%)")
        else:
            print("\n  Generating biome map (Whittaker model)...")
            biome_map, temp_map, moisture_map = generate_biome_map(
                size, args.seed, heightmap, water_level, resolution=args.resolution
            )
            biome_counts = {}
            for b in biome_map.flat:
                biome_counts[b] = biome_counts.get(b, 0) + 1
            total_px = args.resolution * args.resolution
            print("  Biome distribution:")
            for b, c in sorted(biome_counts.items(), key=lambda x: -x[1]):
                print(f"    {b:10s}: {c:6d} px ({c/total_px*100:.1f}%)")

    if args.save_preset:
        save_preset(args.save_preset, {k: getattr(args, k) for k in PRESET_KEYS})
        print(f"  Saved preset '{args.save_preset}'")

    if args.preview:
        preview_path = args.preview_path or os.path.join(
            os.path.dirname(os.path.abspath(__file__)),
            f"terrain_preview_s{args.seed}.png"
        )
        print(f"\n  Rendering preview...")
        render_preview_png(heightmap, preview_path,
                           water_level=water_level,
                           min_z=args.min_z, max_z=args.max_z)
        # Also render biome-colored preview if biome map exists
        if biome_map is not None:
            biome_path = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),
                f"biome_preview_s{args.seed}.png"
            )
            biome_img = biome_preview_colors(biome_map, heightmap, water_level)
            biome_img.save(biome_path)
            print(f"  Biome preview saved: {biome_path}")
        print(f"  Open the PNG to review terrain before applying.")
        print(f"  If it looks good, run with --apply to push to UE.")

    if args.apply:
        success = upload_heightmap(
            heightmap,
            wx0, wy0, wx1, wy1,
            args.min_z, args.max_z
        )

        # Give UE time to finish landscape rebuild after heightmap upload
        if success:
            print("  Waiting 5s for landscape rebuild...")
            time.sleep(5)

        if success and args.water:
            print("\n  Spawning water plane...")
            spawn_water(
                world_x=water_x,
                world_y=water_y,
                water_z=water_level,
                size_x=water_radius * 2.5,
                size_y=water_radius * 2.5,
                material_path=args.water_material,
            )

        if success and args.paint:
            paint_terrain_from_heightmap(
                heightmap, int(size),
                water_level=water_level,
                biome_map=biome_map,
                temp_map=temp_map,
                moisture_map=moisture_map,
                world_x0=wx0, world_y0=wy0,
                world_x1=wx1, world_y1=wy1,
            )

    if args.scatter or args.scatter_preview:
        if args.apply and args.paint:
            print("  Waiting 5s for UE to finish processing weightmaps...")
            time.sleep(5)
        elif args.apply:
            print("  Waiting 3s for UE to finish processing...")
            time.sleep(3)
        print(f"\n  Generating scatter...")
        t2 = time.time()
        placements = generate_scatter(
            heightmap, int(size), args.seed, scaled_monuments,
            water_basin_center=(water_x, water_y),
            water_basin_radius=water_radius,
            water_level=water_level,
            resolution=args.resolution,
            biome_map=biome_map,
        )
        t3 = time.time()
        total = sum(len(v) for v in placements.values())
        print(f"  Scatter generated {total} placements in {t3-t2:.2f}s")

        if args.scatter_preview:
            scatter_path = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),
                f"scatter_preview_s{args.seed}.png"
            )
            render_scatter_preview(heightmap, placements, scatter_path, int(size),
                                   water_level=water_level)
            print(f"  Open scatter PNG to review placements.")

        if args.scatter:
            upload_scatter(placements, cell_size=args.scatter_cell_size, snap_z=not args.no_snap,
                           heightmap=heightmap, world_size=int(size),
                           min_z=args.min_z, max_z=args.max_z)

    print(f"\n{'='*60}")
    print(f"  COMPLETE")
    if args.preview:
        print(f"  Preview PNG generated")
    if args.apply:
        print(f"  Heightmap pushed to UE")
        if args.water:
            print(f"  Water plane spawned")
        if args.paint:
            print(f"  Terrain painted")
    if args.scatter_preview:
        print(f"  Scatter preview PNG generated")
    if args.scatter:
        print(f"  Scatter uploaded to UE")
    print(f"{'='*60}")

if __name__ == "__main__":
    main()
