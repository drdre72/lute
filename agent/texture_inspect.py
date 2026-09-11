"""
Texture inspection loop — systematically checks each market surface
against a medieval brick/stone rubric using Qwen vision.

Usage:
    python agent/texture_inspect.py          # inspect all surfaces
    python agent/texture_inspect.py --fix     # inspect + auto-fix tiling
"""
import sys, os, json, time, base64
sys.path.insert(0, os.path.dirname(__file__))
from vision_lib import call, ask_vision, pixel_check

# --- Rubric: appropriate block/brick sizes per surface type ---
RUBRIC = {
    "stone_wall": {
        "desc": "curtain wall stone blocks",
        "target_cm": "30-60cm wide, 15-25cm tall",
        "material": "materials/medieval/stone_wall.vmat",
        "current_tiling": 30,
    },
    "stone_tower": {
        "desc": "watchtower stone blocks",
        "target_cm": "40-80cm wide, 20-30cm tall",
        "material": "materials/medieval/stone_tower.vmat",
        "current_tiling": 6,
    },
    "stone_detail": {
        "desc": "detail stone (well, crafting stations)",
        "target_cm": "20-40cm wide, 10-20cm tall",
        "material": "materials/medieval/stone_detail.vmat",
        "current_tiling": 3,
    },
    "plaza": {
        "desc": "plaza floor stone tiles",
        "target_cm": "50-100cm wide, 50-100cm tall (square tiles)",
        "material": "materials/medieval/plaza.vmat",
        "current_tiling": 2,
    },
    "wood": {
        "desc": "wooden stall counters and structures",
        "target_cm": "15-30cm wide planks, 100-200cm long",
        "material": "materials/medieval/wood.vmat",
        "current_tiling": None,
    },
    "wood_house": {
        "desc": "NPC housing wood",
        "target_cm": "15-30cm wide planks",
        "material": "materials/medieval/wood_house.vmat",
        "current_tiling": None,
    },
    "roof": {
        "desc": "roof shingles/tiles",
        "target_cm": "20-40cm wide, 10-20cm tall",
        "material": "materials/medieval/roof.vmat",
        "current_tiling": None,
    },
    "foundation": {
        "desc": "foundation stone blocks",
        "target_cm": "40-80cm wide, 20-40cm tall",
        "material": "materials/medieval/stone_wall.vmat",  # shares wall material
        "current_tiling": 30,
    },
}

# --- Viewpoints for each surface ---
# Market center is at (15000, 15000, 0). WallOuterHalfWidth = 140m = 5512 units.
# MoatOuterHalfWidth = 170m = 6693 units.
VIEWPOINTS = [
    # (name, look_x, look_y, look_z, distance, height_offset, surface_key)
    ("wall_north_close", 15000, 15000, 200, 5800, 400, "stone_wall"),
    ("wall_east_close", 15000, 15000, 200, 5800, 400, "stone_wall"),
    ("tower_close", 15000, 15000, 200, 5500, 800, "stone_tower"),
    ("plaza_close", 15000, 15000, 200, 200, 800, "plaza"),
    ("foundation_close", 15000, 15000, 200, 5600, 100, "foundation"),
    ("stalls_close", 15000, 15000, 200, 500, 300, "wood"),
    ("housing_close", 15000, 15000, 200, 1000, 400, "wood_house"),
    ("roof_close", 15000, 15000, 200, 800, 1200, "roof"),
]


def inspect_surface(viewpoint, surface_key):
    """Capture screenshot and ask Qwen to assess texture scale."""
    name, lx, ly, lz, dist, hoff, skey = viewpoint
    rubric = RUBRIC.get(skey, {})

    # Capture screenshot via Merlyn
    out_name = f"tex_{name}"
    cmd = (
        f"python agent/sbox_vision.py "
        f"--look {lx},{ly},{lz} "
        f"--out {out_name} "
        f"--distance {dist} "
        f"--height-offset {hoff} "
        f"--via merlyn "
        f"--check "
        f"--ask \"Look at the {rubric.get('desc', 'surface')} in this image. "
        f"Estimate the size of individual blocks/bricks/tiles in centimeters. "
        f"The target size is {rubric.get('target_cm', 'unknown')}. "
        f"Is the texture scale appropriate, too small, or too large? "
        f"Reply with: SIZE=estimated_cm, VERDICT=appropriate|too_small|too_large, "
        f"RATIO=how_many_times_off\""
    )
    print(f"\n--- Inspecting {name} ({skey}) ---")
    print(f"Target: {rubric.get('target_cm', 'unknown')}")

    # Run via exec would be better but we're in python
    import subprocess
    result = subprocess.run(
        cmd, shell=True, capture_output=True, text=True,
        cwd=os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        timeout=120
    )
    print(result.stdout[-500:] if result.stdout else "No output")
    if result.stderr:
        print("STDERR:", result.stderr[-200:])
    return result.stdout


def main():
    auto_fix = "--fix" in sys.argv

    print("=== Lute Texture Inspection Loop ===")
    print(f"Surfaces to check: {len(VIEWPOINTS)}")
    print(f"Auto-fix: {auto_fix}")
    print()

    results = []
    for vp in VIEWPOINTS:
        name = vp[0]
        skey = vp[6]
        output = inspect_surface(vp, skey)
        results.append({"viewpoint": name, "surface": skey, "output": output})

    print("\n=== Summary ===")
    for r in results:
        print(f"{r['viewpoint']}: {r['surface']}")


if __name__ == "__main__":
    main()
