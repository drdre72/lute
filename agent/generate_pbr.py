"""
Generate 5-channel PBR textures + material variants + .vmat files for Lute.

Supersedes generate_textures.py. Outputs per material:
  - {name}_color.png     (albedo/diffuse)
  - {name}_normal.png    (tangent-space normal map)
  - {name}_rough.png     (roughness, white=rough, black=smooth)
  - {name}_metal.png     (metalness, white=metal, black=non-metal)
  - {name}_ao.png        (ambient occlusion, white=exposed, black=occluded)

Plus variants per material:
  - {name}_color_{variant}.png  (mossy, weathered, pristine)
  - {name}_rough_{variant}.png

And .vmat files with correct complex.shader PBR setup.

All textures are 512x512, tileable, saved as PNG.

Usage:
    python agent/generate_pbr.py                    # generate all
    python agent/generate_pbr.py --only stone       # only stone
    python agent/generate_pbr.py --variants          # also generate variants
    python agent/generate_pbr.py --vmat              # also generate .vmat files
    python agent/generate_pbr.py --variants --vmat   # full pipeline
    python agent/generate_pbr.py --size 1024         # higher resolution
"""
import os
import math
import random
import argparse
from PIL import Image, ImageFilter, ImageDraw, ImageChops

random.seed(42)

REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT_DIR = os.path.join(REPO_ROOT, "sbox", "Assets", "materials", "medieval")
VMAT_DIR = OUT_DIR  # same dir for .vmat files

SIZE = 512


def save(img, name):
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"  Saved {name} ({img.size}, {img.mode})")


def noise_image(size, scale, octaves=4, persistence=0.5):
    """Generate tileable value noise."""
    img = Image.new("L", (size, size), 128)
    pixels = img.load()
    for o in range(octaves):
        freq = scale * (2 ** o)
        amp = (255 // 4) * (persistence ** o)
        offset_x = random.randint(0, 9999)
        offset_y = random.randint(0, 9999)
        for y in range(size):
            for x in range(size):
                fx = (x / size) * freq * 2 * math.pi
                fy = (y / size) * freq * 2 * math.pi
                n = math.sin(fx + offset_x) * math.cos(fy + offset_y)
                n += math.sin(fx * 1.3 + offset_y) * math.cos(fy * 0.7 + offset_x)
                n = (n + 2) / 4
                pixels[x, y] = int(pixels[x, y] * 0.6 + n * amp * 2)
    return img


def normal_from_height(height_img, strength=2.0):
    """Generate a normal map from a height/grayscale image."""
    w, h = height_img.size
    height_pixels = height_img.load()
    normal = Image.new("RGB", (w, h), (128, 128, 255))
    npix = normal.load()
    for y in range(h):
        for x in range(w):
            xL = (x - 1) % w
            xR = (x + 1) % w
            yU = (y - 1) % h
            yD = (y + 1) % h
            dx = (height_pixels[xR, y] - height_pixels[xL, y]) / 255.255 * strength
            dy = (height_pixels[x, yD] - height_pixels[x, yU]) / 255.255 * strength
            nx = -dx
            ny = -dy
            nz = 1.0
            length = math.sqrt(nx*nx + ny*ny + nz*nz)
            nx /= length; ny /= length; nz /= length
            npix[x, y] = (
                int((nx * 0.5 + 0.5) * 255),
                int((ny * 0.5 + 0.5) * 255),
                int((nz * 0.5 + 0.5) * 255),
            )
    return normal


def ao_from_height(height_img, radius=8, strength=0.7):
    """
    Generate an ambient occlusion map from a height image.
    Approximates AO by darkening areas where the height is lower than
    the surrounding average (crevices, cracks, gaps).
    """
    w, h = height_img.size
    # Blur the height to get a local average
    blurred = height_img.filter(ImageFilter.GaussianBlur(radius))
    hp = height_img.load()
    bp = blurred.load()
    ao = Image.new("L", (w, h), 255)
    ap = ao.load()
    for y in range(h):
        for x in range(w):
            local_h = hp[x, y]
            avg_h = bp[x, y]
            # If this pixel is lower than average, it's occluded
            diff = avg_h - local_h  # positive = lower than surroundings
            occlusion = max(0, min(255, int(255 - diff * strength * 2)))
            ap[x, y] = occlusion
    return ao


# =============================================================================
# MATERIAL DEFINITIONS
# Each material generates: color, normal, rough, metal, ao
# =============================================================================

def gen_stone(size):
    """Grey castle stone with block pattern."""
    block_h, block_w = 48, 128
    color = Image.new("RGB", (size, size), (120, 115, 105))
    draw = ImageDraw.Draw(color)
    for row in range(size // block_h + 1):
        offset = (row % 2) * (block_w // 2)
        y0 = row * block_h
        for col in range(size // block_w + 2):
            x0 = col * block_w - offset
            base = random.randint(95, 140)
            r = base + random.randint(-10, 10)
            g = base - 5 + random.randint(-10, 10)
            b = base - 15 + random.randint(-10, 10)
            draw.rectangle([x0, y0, x0 + block_w, y0 + block_h], fill=(r, g, b))
            draw.line([x0, y0, x0 + block_w, y0], fill=(60, 55, 48), width=2)
            draw.line([x0, y0, x0, y0 + block_h], fill=(60, 55, 48), width=2)
    noise = noise_image(size, 8, octaves=3, persistence=0.3)
    color = Image.blend(color, Image.merge("RGB", [noise]*3).convert("RGB"), 0.15)

    height = Image.new("L", (size, size), 128)
    hdraw = ImageDraw.Draw(height)
    for row in range(size // block_h + 1):
        offset = (row % 2) * (block_w // 2)
        y0 = row * block_h
        for col in range(size // block_w + 2):
            x0 = col * block_w - offset
            hdraw.line([x0, y0, x0 + block_w, y0], fill=80, width=3)
            hdraw.line([x0, y0, x0, y0 + block_h], fill=80, width=3)
    height = height.filter(ImageFilter.GaussianBlur(1))

    normal = normal_from_height(height, strength=3.0)
    rough = Image.merge("L", [noise]).point(lambda v: 180 + (v - 128) // 4)
    metal = Image.new("L", (size, size), 0)  # stone is non-metal
    ao = ao_from_height(height, radius=8, strength=0.7)

    return color, normal, rough, metal, ao


def gen_wood(size):
    """Brown planks with grain."""
    plank_w = 64
    color = Image.new("RGB", (size, size), (90, 60, 35))
    draw = ImageDraw.Draw(color)
    for col in range(size // plank_w + 1):
        x0 = col * plank_w
        base_r = random.randint(75, 110)
        base_g = random.randint(45, 70)
        base_b = random.randint(25, 45)
        draw.rectangle([x0, 0, x0 + plank_w, size], fill=(base_r, base_g, base_b))
        draw.line([x0, 0, x0, size], fill=(40, 25, 15), width=3)
        for g in range(8):
            gy = random.randint(0, size)
            gh = random.randint(1, 3)
            shade = random.randint(-15, 15)
            draw.line([x0+2, gy, x0+plank_w-2, gy], fill=(
                max(0, base_r + shade), max(0, base_g + shade), max(0, base_b + shade)
            ), width=gh)
    noise = noise_image(size, 16, octaves=3, persistence=0.2)
    color = Image.blend(color, Image.merge("RGB", [noise]*3).convert("RGB"), 0.1)

    height = Image.new("L", (size, size), 130)
    hdraw = ImageDraw.Draw(height)
    for col in range(size // plank_w + 1):
        x0 = col * plank_w
        hdraw.line([x0, 0, x0, size], fill=60, width=3)
        for g in range(6):
            gy = random.randint(0, size)
            hdraw.line([x0+2, gy, x0+plank_w-2, gy], fill=110, width=1)

    normal = normal_from_height(height, strength=2.0)
    rough = Image.new("L", (size, size), 220).point(lambda v: 210 + random.randint(-20, 20))
    metal = Image.new("L", (size, size), 0)  # wood is non-metal
    ao = ao_from_height(height, radius=6, strength=0.6)

    return color, normal, rough, metal, ao


def gen_metal(size):
    """Dark iron with scratches."""
    color = Image.new("RGB", (size, size), (55, 55, 58))
    draw = ImageDraw.Draw(color)
    for y in range(size):
        shade = math.sin(y * 0.05) * 8 + random.randint(-5, 5)
        draw.line([0, y, size, y], fill=(
            max(0, min(255, 55 + int(shade))),
            max(0, min(255, 55 + int(shade))),
            max(0, min(255, 58 + int(shade))),
        ))
    for _ in range(30):
        x1 = random.randint(0, size)
        y1 = random.randint(0, size)
        x2 = x1 + random.randint(-50, 50)
        y2 = y1 + random.randint(-5, 5)
        shade = random.randint(-20, 20)
        draw.line([x1, y1, x2, y2], fill=(
            max(0, 55 + shade), max(0, 55 + shade), max(0, 58 + shade)
        ), width=1)

    height = Image.new("L", (size, size), 128)
    hdraw = ImageDraw.Draw(height)
    for _ in range(40):
        x1 = random.randint(0, size)
        y1 = random.randint(0, size)
        x2 = x1 + random.randint(-60, 60)
        y2 = y1 + random.randint(-5, 5)
        hdraw.line([x1, y1, x2, y2], fill=random.randint(100, 160), width=1)

    normal = normal_from_height(height, strength=1.5)
    rough = Image.new("L", (size, size), 100).point(lambda v: 90 + random.randint(-15, 15))
    # Metal is metallic — mostly white with some variation
    metal = Image.new("L", (size, size), 230).point(lambda v: 220 + random.randint(-20, 20))
    ao = ao_from_height(height, radius=4, strength=0.4)

    return color, normal, rough, metal, ao


def gen_roof(size):
    """Terracotta clay tiles."""
    tile_h, tile_w = 32, 64
    color = Image.new("RGB", (size, size), (140, 65, 40))
    draw = ImageDraw.Draw(color)
    for row in range(size // tile_h + 1):
        offset = (row % 2) * (tile_w // 2)
        y0 = row * tile_h
        for col in range(size // tile_w + 2):
            x0 = col * tile_w - offset
            base_r = 130 + random.randint(-15, 15)
            base_g = 55 + random.randint(-10, 10)
            base_b = 35 + random.randint(-8, 8)
            draw.rectangle([x0, y0, x0 + tile_w, y0 + tile_h], fill=(base_r, base_g, base_b))
            draw.arc([x0, y0, x0 + tile_w, y0 + tile_h * 2], 0, 180, fill=(
                min(255, base_r + 25), min(255, base_g + 15), min(255, base_b + 10)
            ), width=2)
    noise = noise_image(size, 8, octaves=2, persistence=0.2)
    color = Image.blend(color, Image.merge("RGB", [noise]*3).convert("RGB"), 0.08)

    height = Image.new("L", (size, size), 120)
    hdraw = ImageDraw.Draw(height)
    for row in range(size // tile_h + 1):
        offset = (row % 2) * (tile_w // 2)
        y0 = row * tile_h
        for col in range(size // tile_w + 2):
            x0 = col * tile_w - offset
            hdraw.arc([x0, y0, x0 + tile_w, y0 + tile_h * 2], 0, 180, fill=180, width=3)
            hdraw.line([x0, y0, x0 + tile_w, y0], fill=80, width=2)

    normal = normal_from_height(height, strength=3.0)
    rough = Image.new("L", (size, size), 180).point(lambda v: 170 + random.randint(-15, 15))
    metal = Image.new("L", (size, size), 0)  # terracotta is non-metal
    ao = ao_from_height(height, radius=6, strength=0.8)

    return color, normal, rough, metal, ao


def gen_plaza(size):
    """Lighter, smoother flagstone."""
    flag_h, flag_w = 128, 128
    color = Image.new("RGB", (size, size), (155, 150, 138))
    draw = ImageDraw.Draw(color)
    for row in range(size // flag_h + 1):
        offset = (row % 2) * (flag_w // 2)
        y0 = row * flag_h
        for col in range(size // flag_w + 2):
            x0 = col * flag_w - offset
            base = random.randint(140, 170)
            draw.rectangle([x0, y0, x0 + flag_w, y0 + flag_h], fill=(base, base - 5, base - 18))
            draw.line([x0, y0, x0 + flag_w, y0], fill=(90, 85, 75), width=2)
            draw.line([x0, y0, x0, y0 + flag_h], fill=(90, 85, 75), width=2)
    noise = noise_image(size, 6, octaves=3, persistence=0.2)
    color = Image.blend(color, Image.merge("RGB", [noise]*3).convert("RGB"), 0.12)

    height = Image.new("L", (size, size), 128)
    hdraw = ImageDraw.Draw(height)
    for row in range(size // flag_h + 1):
        offset = (row % 2) * (flag_w // 2)
        y0 = row * flag_h
        for col in range(size // flag_w + 2):
            x0 = col * flag_w - offset
            hdraw.line([x0, y0, x0 + flag_w, y0], fill=90, width=3)
            hdraw.line([x0, y0, x0, y0 + flag_h], fill=90, width=3)

    normal = normal_from_height(height, strength=2.0)
    rough = Image.new("L", (size, size), 170).point(lambda v: 160 + random.randint(-10, 10))
    metal = Image.new("L", (size, size), 0)  # stone is non-metal
    ao = ao_from_height(height, radius=10, strength=0.5)

    return color, normal, rough, metal, ao


# =============================================================================
# MATERIAL VARIANTS
# =============================================================================

def apply_variant_mossy(color, rough):
    """Add green moss patches to color, make rougher in mossy areas."""
    w, h = color.size
    moss = Image.new("RGB", (w, h), (0, 0, 0))
    mdraw = ImageDraw.Draw(moss)
    for _ in range(15):
        x = random.randint(0, w)
        y = random.randint(0, h)
        r = random.randint(30, 80)
        green = (random.randint(40, 70), random.randint(80, 120), random.randint(30, 50))
        mdraw.ellipse([x-r, y-r, x+r, y+r], fill=green)
    moss = moss.filter(ImageFilter.GaussianBlur(15))
    result = Image.blend(color, ImageChops.screen(color, moss), 0.4)
    # Moss is rougher
    rough_var = rough.point(lambda v: min(255, v + 20))
    return result, rough_var


def apply_variant_weathered(color, rough):
    """Desaturate and darken color, increase roughness."""
    gray = color.convert("L").convert("RGB")
    result = Image.blend(color, gray, 0.3)
    result = ImageChops.multiply(result, Image.new("RGB", color.size, (200, 200, 200)))
    rough_var = rough.point(lambda v: min(255, v + 30))
    return result, rough_var


def apply_variant_pristine(color, rough):
    """Brighten color, make smoother."""
    result = ImageChops.screen(color, Image.new("RGB", color.size, (20, 20, 20)))
    rough_var = rough.point(lambda v: max(0, v - 30))
    return result, rough_var


VARIANTS = {
    "mossy": apply_variant_mossy,
    "weathered": apply_variant_weathered,
    "pristine": apply_variant_pristine,
}


# =============================================================================
# .VMAT GENERATION
# =============================================================================

def generate_vmat(name, tex_scale=30.0, out_dir=None):
    """Generate a .vmat file with correct PBR shader setup for a material."""
    if out_dir is None:
        out_dir = VMAT_DIR

    vmat = f"""Layer0
{{
    shader "shaders/complex.shader"
    g_flAmbientOcclusionDirectDiffuse "0.100"
    g_flAmbientOcclusionDirectSpecular "0.000"
    TextureAmbientOcclusion "materials/medieval/{name}_ao.png"
    g_flModelTintAmount "1.000"
    g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
    TextureColor "materials/medieval/{name}_color.png"
    g_bFogEnabled "1"
    g_flMetalness "1.000"
    TextureMetalness "materials/medieval/{name}_metal.png"
    TextureNormal "materials/medieval/{name}_normal.png"
    g_flRoughnessScaleFactor "1.000"
    TextureRoughness "materials/medieval/{name}_rough.png"
    g_nScaleTexCoordUByModelScaleAxis "0"
    g_nScaleTexCoordVByModelScaleAxis "0"
    g_vTexCoordOffset "[0.000 0.000]"
    g_vTexCoordScale "[{tex_scale:.3f} {tex_scale:.3f}]"
    g_vTexCoordScrollSpeed "[0.000 0.000]"
}}
"""
    path = os.path.join(out_dir, f"{name}.vmat")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(vmat)
    print(f"  Saved {name}.vmat")
    return path


# =============================================================================
# MAIN
# =============================================================================

MATERIALS = {
    "stone": (gen_stone, 30.0),
    "wood": (gen_wood, 20.0),
    "metal": (gen_metal, 30.0),
    "roof": (gen_roof, 25.0),
    "plaza": (gen_plaza, 15.0),
}


def main():
    parser = argparse.ArgumentParser(description="Generate 5-channel PBR textures for Lute.")
    parser.add_argument("--only", default=None, help="Only generate the named material.")
    parser.add_argument("--variants", action="store_true", help="Also generate mossy/weathered/pristine variants.")
    parser.add_argument("--vmat", action="store_true", help="Also generate .vmat files.")
    parser.add_argument("--size", type=int, default=512, help="Texture resolution (default 512).")
    args = parser.parse_args()

    global SIZE
    SIZE = args.size
    os.makedirs(OUT_DIR, exist_ok=True)

    mats = MATERIALS.items()
    if args.only:
        if args.only not in MATERIALS:
            print(f"ERROR: Unknown material '{args.only}'. Available: {list(MATERIALS.keys())}")
            sys.exit(1)
        mats = [(args.only, MATERIALS[args.only])]

    for name, (gen_func, tex_scale) in mats:
        print(f"\nGenerating {name} (5-channel PBR)...")
        random.seed(42 + hash(name) % 1000)  # deterministic per material
        color, normal, rough, metal, ao = gen_func(SIZE)

        save(color, f"{name}_color.png")
        save(normal, f"{name}_normal.png")
        save(rough, f"{name}_rough.png")
        save(metal, f"{name}_metal.png")
        save(ao, f"{name}_ao.png")

        if args.variants:
            print(f"  Generating variants for {name}...")
            for vname, vfunc in VARIANTS.items():
                random.seed(42 + hash(name + vname) % 1000)
                v_color, v_rough = vfunc(color, rough)
                save(v_color, f"{name}_color_{vname}.png")
                save(v_rough, f"{name}_rough_{vname}.png")

        if args.vmat:
            print(f"  Generating .vmat for {name}...")
            generate_vmat(name, tex_scale)

            if args.variants:
                for vname in VARIANTS:
                    vmat_name = f"{name}_{vname}"
                    # Variant .vmat references the variant color/rough + base normal/metal/ao
                    vmat = f"""Layer0
{{
    shader "shaders/complex.shader"
    g_flAmbientOcclusionDirectDiffuse "0.100"
    g_flAmbientOcclusionDirectSpecular "0.000"
    TextureAmbientOcclusion "materials/medieval/{name}_ao.png"
    g_flModelTintAmount "1.000"
    g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
    TextureColor "materials/medieval/{name}_color_{vname}.png"
    g_bFogEnabled "1"
    g_flMetalness "1.000"
    TextureMetalness "materials/medieval/{name}_metal.png"
    TextureNormal "materials/medieval/{name}_normal.png"
    g_flRoughnessScaleFactor "1.000"
    TextureRoughness "materials/medieval/{name}_rough_{vname}.png"
    g_nScaleTexCoordUByModelScaleAxis "0"
    g_nScaleTexCoordVByModelScaleAxis "0"
    g_vTexCoordOffset "[0.000 0.000]"
    g_vTexCoordScale "[{tex_scale:.3f} {tex_scale:.3f}]"
    g_vTexCoordScrollSpeed "[0.000 0.000]"
}}
"""
                    path = os.path.join(VMAT_DIR, f"{vmat_name}.vmat")
                    with open(path, "w", encoding="utf-8", newline="\n") as f:
                        f.write(vmat)
                    print(f"  Saved {vmat_name}.vmat")

    print(f"\nAll PBR textures generated successfully!")


if __name__ == "__main__":
    import sys
    main()
