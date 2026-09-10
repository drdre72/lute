"""
Generate tileable PBR textures for the Neutral Market monument.
Outputs color, normal, and roughness maps for stone, wood, metal, and roof.
All textures are 512x512, tileable, saved as PNG.
"""
import os
import math
import random
from PIL import Image, ImageFilter, ImageDraw

random.seed(42)

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "sbox", "Assets", "materials", "medieval")
os.makedirs(OUT_DIR, exist_ok=True)

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
                # Tileable: wrap coordinates
                fx = (x / size) * freq * 2 * math.pi
                fy = (y / size) * freq * 2 * math.pi
                n = math.sin(fx + offset_x) * math.cos(fy + offset_y)
                n += math.sin(fx * 1.3 + offset_y) * math.cos(fy * 0.7 + offset_x)
                n = (n + 2) / 4  # normalize to 0-1
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
            # Sample neighbors with wrapping
            xL = (x - 1) % w
            xR = (x + 1) % w
            yU = (y - 1) % h
            yD = (y + 1) % h
            dx = (height_pixels[xR, y] - height_pixels[xL, y]) / 255.0 * strength
            dy = (height_pixels[x, yD] - height_pixels[x, yU]) / 255.0 * strength
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

# =============================================================================
# STONE — grey castle stone with block pattern and noise
# =============================================================================
print("Generating stone textures...")

# Color: grey with variation, subtle block lines
stone_color = Image.new("RGB", (SIZE, SIZE), (120, 115, 105))
draw = ImageDraw.Draw(stone_color)
# Add block pattern — bricks wider than tall (horizontal courses)
block_h = 48  # pixels per stone row (shorter = more horizontal look)
block_w = 128  # pixels per stone (wider = horizontal bricks)
for row in range(SIZE // block_h + 1):
    offset = (row % 2) * (block_w // 2)  # brick offset
    y0 = row * block_h
    y1 = y0 + block_h
    for col in range(SIZE // block_w + 2):
        x0 = col * block_w - offset
        x1 = x0 + block_w
        # Random grey variation per block
        base = random.randint(95, 140)
        r = base + random.randint(-10, 10)
        g = base - 5 + random.randint(-10, 10)
        b = base - 15 + random.randint(-10, 10)
        draw.rectangle([x0, y0, x1, y1], fill=(r, g, b))
        # Dark mortar lines
        draw.line([x0, y0, x1, y0], fill=(60, 55, 48), width=2)
        draw.line([x0, y0, x0, y1], fill=(60, 55, 48), width=2)
# Add noise overlay
stone_noise = noise_image(SIZE, 8, octaves=3, persistence=0.3)
stone_color = Image.blend(stone_color, Image.merge("RGB", [stone_noise]*3).convert("RGB"), 0.15)
save(stone_color, "stone_color.png")

# Height map for normal: block edges + noise
stone_height = Image.new("L", (SIZE, SIZE), 128)
hdraw = ImageDraw.Draw(stone_height)
for row in range(SIZE // block_h + 1):
    offset = (row % 2) * (block_w // 2)
    y0 = row * block_h
    for col in range(SIZE // block_w + 2):
        x0 = col * block_w - offset
        hdraw.line([x0, y0, x0 + block_w, y0], fill=80, width=3)
        hdraw.line([x0, y0, x0, y0 + block_h], fill=80, width=3)
# Add noise to height
stone_height = stone_height.filter(ImageFilter.GaussianBlur(1))
stone_normal = normal_from_height(stone_height, strength=3.0)
save(stone_normal, "stone_normal.png")

# Roughness: mostly rough with some variation
stone_rough = Image.new("L", (SIZE, SIZE), 200)
stone_rough = Image.merge("L", [stone_noise]).point(lambda v: 180 + (v - 128) // 4)
save(stone_rough, "stone_rough.png")

# =============================================================================
# WOOD — brown planks with grain
# =============================================================================
print("Generating wood textures...")

wood_color = Image.new("RGB", (SIZE, SIZE), (90, 60, 35))
wdraw = ImageDraw.Draw(wood_color)
plank_w = 64
for col in range(SIZE // plank_w + 1):
    x0 = col * plank_w
    base_r = random.randint(75, 110)
    base_g = random.randint(45, 70)
    base_b = random.randint(25, 45)
    wdraw.rectangle([x0, 0, x0 + plank_w, SIZE], fill=(base_r, base_g, base_b))
    # Dark gap between planks
    wdraw.line([x0, 0, x0, SIZE], fill=(40, 25, 15), width=3)
    # Grain lines
    for g in range(8):
        gy = random.randint(0, SIZE)
        gh = random.randint(1, 3)
        shade = random.randint(-15, 15)
        wdraw.line([x0+2, gy, x0+plank_w-2, gy], fill=(
            max(0, base_r + shade), max(0, base_g + shade), max(0, base_b + shade)
        ), width=gh)
# Add noise
wood_noise = noise_image(SIZE, 16, octaves=3, persistence=0.2)
wood_color = Image.blend(wood_color, Image.merge("RGB", [wood_noise]*3).convert("RGB"), 0.1)
save(wood_color, "wood_color.png")

# Wood height: plank gaps + grain
wood_height = Image.new("L", (SIZE, SIZE), 130)
hdraw = ImageDraw.Draw(wood_height)
for col in range(SIZE // plank_w + 1):
    x0 = col * plank_w
    hdraw.line([x0, 0, x0, SIZE], fill=60, width=3)
    for g in range(6):
        gy = random.randint(0, SIZE)
        hdraw.line([x0+2, gy, x0+plank_w-2, gy], fill=110, width=1)
wood_normal = normal_from_height(wood_height, strength=2.0)
save(wood_normal, "wood_normal.png")

wood_rough = Image.new("L", (SIZE, SIZE), 220)
wood_rough = wood_rough.point(lambda v: 210 + random.randint(-20, 20))
save(wood_rough, "wood_rough.png")

# =============================================================================
# METAL — dark iron with scratches
# =============================================================================
print("Generating metal textures...")

metal_color = Image.new("RGB", (SIZE, SIZE), (55, 55, 58))
mdraw = ImageDraw.Draw(metal_color)
# Add horizontal banding (forge marks)
for y in range(SIZE):
    shade = math.sin(y * 0.05) * 8 + random.randint(-5, 5)
    mdraw.line([0, y, SIZE, y], fill=(
        max(0, min(255, 55 + int(shade))),
        max(0, min(255, 55 + int(shade))),
        max(0, min(255, 58 + int(shade))),
    ))
# Scratches
for _ in range(30):
    x1 = random.randint(0, SIZE)
    y1 = random.randint(0, SIZE)
    x2 = x1 + random.randint(-50, 50)
    y2 = y1 + random.randint(-5, 5)
    shade = random.randint(-20, 20)
    mdraw.line([x1, y1, x2, y2], fill=(
        max(0, 55 + shade), max(0, 55 + shade), max(0, 58 + shade)
    ), width=1)
save(metal_color, "metal_color.png")

# Metal height: mostly flat with scratches
metal_height = Image.new("L", (SIZE, SIZE), 128)
hdraw = ImageDraw.Draw(metal_height)
for _ in range(40):
    x1 = random.randint(0, SIZE)
    y1 = random.randint(0, SIZE)
    x2 = x1 + random.randint(-60, 60)
    y2 = y1 + random.randint(-5, 5)
    hdraw.line([x1, y1, x2, y2], fill=random.randint(100, 160), width=1)
metal_normal = normal_from_height(metal_height, strength=1.5)
save(metal_normal, "metal_normal.png")

metal_rough = Image.new("L", (SIZE, SIZE), 100)
metal_rough = metal_rough.point(lambda v: 90 + random.randint(-15, 15))
save(metal_rough, "metal_rough.png")

# =============================================================================
# ROOF — terracotta clay tiles
# =============================================================================
print("Generating roof textures...")

roof_color = Image.new("RGB", (SIZE, SIZE), (140, 65, 40))
rdraw = ImageDraw.Draw(roof_color)
tile_h = 32
tile_w = 64
for row in range(SIZE // tile_h + 1):
    offset = (row % 2) * (tile_w // 2)
    y0 = row * tile_h
    for col in range(SIZE // tile_w + 2):
        x0 = col * tile_w - offset
        base_r = 130 + random.randint(-15, 15)
        base_g = 55 + random.randint(-10, 10)
        base_b = 35 + random.randint(-8, 8)
        rdraw.rectangle([x0, y0, x0 + tile_w, y0 + tile_h], fill=(base_r, base_g, base_b))
        # Tile curve highlight
        rdraw.arc([x0, y0, x0 + tile_w, y0 + tile_h * 2], 0, 180, fill=(
            min(255, base_r + 25), min(255, base_g + 15), min(255, base_b + 10)
        ), width=2)
# Add noise
roof_noise = noise_image(SIZE, 8, octaves=2, persistence=0.2)
roof_color = Image.blend(roof_color, Image.merge("RGB", [roof_noise]*3).convert("RGB"), 0.08)
save(roof_color, "roof_color.png")

# Roof height: tile curves
roof_height = Image.new("L", (SIZE, SIZE), 120)
hdraw = ImageDraw.Draw(roof_height)
for row in range(SIZE // tile_h + 1):
    offset = (row % 2) * (tile_w // 2)
    y0 = row * tile_h
    for col in range(SIZE // tile_w + 2):
        x0 = col * tile_w - offset
        hdraw.arc([x0, y0, x0 + tile_w, y0 + tile_h * 2], 0, 180, fill=180, width=3)
        hdraw.line([x0, y0, x0 + tile_w, y0], fill=80, width=2)
roof_normal = normal_from_height(roof_height, strength=3.0)
save(roof_normal, "roof_normal.png")

roof_rough = Image.new("L", (SIZE, SIZE), 180)
roof_rough = roof_rough.point(lambda v: 170 + random.randint(-15, 15))
save(roof_rough, "roof_rough.png")

# =============================================================================
# PLAZA STONE — lighter, smoother flagstone
# =============================================================================
print("Generating plaza stone textures...")

plaza_color = Image.new("RGB", (SIZE, SIZE), (155, 150, 138))
pdraw = ImageDraw.Draw(plaza_color)
# Large flagstone tiles
flag_h = 128
flag_w = 128
for row in range(SIZE // flag_h + 1):
    offset = (row % 2) * (flag_w // 2)
    y0 = row * flag_h
    for col in range(SIZE // flag_w + 2):
        x0 = col * flag_w - offset
        base = random.randint(140, 170)
        pdraw.rectangle([x0, y0, x0 + flag_w, y0 + flag_h], fill=(base, base - 5, base - 18))
        pdraw.line([x0, y0, x0 + flag_w, y0], fill=(90, 85, 75), width=2)
        pdraw.line([x0, y0, x0, y0 + flag_h], fill=(90, 85, 75), width=2)
plaza_noise = noise_image(SIZE, 6, octaves=3, persistence=0.2)
plaza_color = Image.blend(plaza_color, Image.merge("RGB", [plaza_noise]*3).convert("RGB"), 0.12)
save(plaza_color, "plaza_color.png")

plaza_height = Image.new("L", (SIZE, SIZE), 128)
hdraw = ImageDraw.Draw(plaza_height)
for row in range(SIZE // flag_h + 1):
    offset = (row % 2) * (flag_w // 2)
    y0 = row * flag_h
    for col in range(SIZE // flag_w + 2):
        x0 = col * flag_w - offset
        hdraw.line([x0, y0, x0 + flag_w, y0], fill=90, width=3)
        hdraw.line([x0, y0, x0, y0 + flag_h], fill=90, width=3)
plaza_normal = normal_from_height(plaza_height, strength=2.0)
save(plaza_normal, "plaza_normal.png")

plaza_rough = Image.new("L", (SIZE, SIZE), 170)
plaza_rough = plaza_rough.point(lambda v: 160 + random.randint(-10, 10))
save(plaza_rough, "plaza_rough.png")

print("\nAll textures generated successfully!")
