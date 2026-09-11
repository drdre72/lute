"""
Texture inspection loop — systematically checks each market surface
against a medieval brick/stone rubric using Qwen vision.

Drives Merlyn to representative viewpoints around the market perimeter,
captures screenshots, asks Qwen to estimate texture scale, compares
against a rubric, optionally auto-fixes material tiling, then re-inspects.

Usage:
    python agent/texture_inspect.py              # inspect all surfaces, report
    python agent/texture_inspect.py --fix        # inspect + auto-fix tiling, reinspect
    python agent/texture_inspect.py --max-iter 3  # limit fix iterations
    python agent/texture_inspect.py --only wall   # only inspect surfaces matching "wall"
"""
import sys, os, json, time, re, argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vision_lib import VisionLib, vantage, save_img, ask_vision, pixel_check

# ── Market geometry (must match LuteMonumentBuilder.cs) ──
M = 39.37
CX, CY = 15000.0, 15000.0
WALL_HALF = 140.0 * M      # 5511.8
MOAT_HALF = 170.0 * M      # 6692.9
WALL_H = 12.0 * M           # 472.4
FOUND_TOP = 2.5 * M         # 98.4 — foundation top
TOWER_H = 18.0 * M          # 708.7
TOWER_OFFSET = 10.0 * M     # 393.7 — tower offset from gate along wall
WALL_CENTER_Z = FOUND_TOP + WALL_H * 0.5   # ~335
TOWER_CENTER_Z = FOUND_TOP + TOWER_H * 0.5  # ~453

# ── Rubric: appropriate block/brick sizes per surface type ──
# tiling: higher = more repetitions = smaller blocks
RUBRIC = {
    "stone_wall": {
        "desc": "curtain wall stone blocks (walls, corners, gatehouse)",
        "target": "30-60cm wide, 15-25cm tall blocks",
        "material": "materials/medieval/stone_wall.vmat",
        "tiling": 30,
    },
    "stone_tower": {
        "desc": "watchtower stone blocks (8 towers flanking gates)",
        "target": "40-80cm wide, 20-30cm tall blocks",
        "material": "materials/medieval/stone_tower.vmat",
        "tiling": 6,
    },
    "stone_detail": {
        "desc": "detail stone (merlons, well rim, jambs)",
        "target": "20-40cm wide, 10-20cm tall blocks",
        "material": "materials/medieval/stone_detail.vmat",
        "tiling": 3,
    },
    "plaza": {
        "desc": "plaza floor stone tiles (plaza, inner ring, bridges)",
        "target": "50-100cm square tiles",
        "material": "materials/medieval/plaza.vmat",
        "tiling": 2,
    },
    "wood": {
        "desc": "wooden stall counters, workbenches, well posts",
        "target": "15-30cm wide planks, 100-200cm long",
        "material": "materials/medieval/wood.vmat",
        "tiling": 6,
    },
    "wood_house": {
        "desc": "NPC housing wood planks",
        "target": "15-30cm wide planks",
        "material": "materials/medieval/wood_house.vmat",
        "tiling": 12,
    },
    "roof": {
        "desc": "roof shingles/tiles (awnings, well roof)",
        "target": "20-40cm wide, 10-20cm tall shingles",
        "material": "materials/medieval/roof.vmat",
        "tiling": 8,
    },
    "foundation": {
        "desc": "foundation stone blocks (shares stone_wall material)",
        "target": "40-80cm wide, 20-40cm tall blocks",
        "material": "materials/medieval/stone_wall.vmat",
        "tiling": 30,
    },
}

# ── Viewpoints: (name, target_x, target_y, target_z, distance, height, yaw, surface_key)
# yaw: 0=North looking South, 90=East looking West, 180=South looking North, 270=West looking East
# Geometry notes:
#   PlazaHalfWidth = 60m, InnerRingOuter = 110m, midRadius = 85m (housing/crafting ring)
#   WallOuterHalfWidth = 140m, MoatOuterHalfWidth = 170m
#   Stalls at 15m + col*12m from center (quadrants), awnings on stall posts
#   Houses at midRadius=85m from center, 4m tall, FOUND_TOP + 2m
VIEWPOINTS = [
    # Walls — 4 cardinal sides, camera outside looking at wall face
    ("wall_north", CX, CY + WALL_HALF, WALL_CENTER_Z, 1500, 200, 0, "stone_wall"),
    ("wall_south", CX, CY - WALL_HALF, WALL_CENTER_Z, 1500, 200, 180, "stone_wall"),
    ("wall_east", CX + WALL_HALF, CY, WALL_CENTER_Z, 1500, 200, 90, "stone_wall"),
    ("wall_west", CX - WALL_HALF, CY, WALL_CENTER_Z, 1500, 200, 270, "stone_wall"),
    # Corners (use stone_wall material)
    ("corner_ne", CX + WALL_HALF, CY + WALL_HALF, WALL_CENTER_Z, 1500, 200, 45, "stone_wall"),
    ("corner_sw", CX - WALL_HALF, CY - WALL_HALF, WALL_CENTER_Z, 1500, 200, 225, "stone_wall"),
    # Towers — flanking north gate, camera outside
    ("tower_n", CX + TOWER_OFFSET, CY + WALL_HALF, TOWER_CENTER_Z, 1200, 300, 0, "stone_tower"),
    ("tower_s", CX - TOWER_OFFSET, CY - WALL_HALF, TOWER_CENTER_Z, 1200, 300, 180, "stone_tower"),
    # Foundation — look at base of north wall from far, low angle
    ("foundation_n", CX, CY + WALL_HALF, FOUND_TOP * 0.5, 2500, 50, 0, "foundation"),
    # Plaza floor — camera high inside market looking down
    ("plaza_floor", CX, CY, FOUND_TOP, 300, 1000, 0, "plaza"),
    # Stalls — inside market at plaza level (stalls at ~15m from center)
    ("stalls", CX + 15.0 * M, CY, FOUND_TOP + 60, 600, 200, 90, "wood"),
    # Housing — at midRadius=85m south of center, 4m tall
    ("housing", CX, CY - 85.0 * M, FOUND_TOP + 2.0 * M, 800, 200, 180, "wood_house"),
    # Roof/awning — above stalls (awnings are thin, at stall post height ~2.5m)
    ("roof_awning", CX + 15.0 * M, CY, FOUND_TOP + 2.5 * M, 600, 200, 45, "roof"),
    # Stone detail — merlons on top of north wall
    ("merlon_n", CX, CY + WALL_HALF, FOUND_TOP + WALL_H + 50, 1500, 100, 0, "stone_detail"),
]


def make_prompt(rubric, surface_name, distance_units):
    """Build a structured Qwen prompt for texture scale assessment."""
    dist_m = distance_units / M
    return (
        f"You are inspecting a medieval market surface: {rubric['desc']}.\n"
        f"The camera is approximately {dist_m:.0f} meters from the surface.\n"
        f"Target texture scale: {rubric['target']}.\n\n"
        f"Look at the stone/brick/wood texture on the main surface in this image. "
        f"Estimate the apparent physical size of individual blocks/tiles/planks in centimeters. "
        f"Compare to the target scale.\n\n"
        f"Respond EXACTLY in this format (no other text):\n"
        f"SIZE=<estimated width cm>x<estimated height cm>\n"
        f"VERDICT=appropriate|too_small|too_large|not_visible\n"
        f"RATIO=<how many times off, e.g. 2.0 or 0.5; 1.0 if appropriate>\n"
        f"NOTES=<one short sentence>"
    )


def parse_verdict(text):
    """Parse Qwen's structured response into a dict."""
    result = {"verdict": "not_visible", "size": "", "ratio": 1.0, "notes": ""}
    if not text:
        return result
    for line in text.strip().split("\n"):
        line = line.strip()
        if line.upper().startswith("SIZE="):
            result["size"] = line[5:].strip()
        elif line.upper().startswith("VERDICT="):
            v = line[8:].strip().lower()
            if v in ("appropriate", "too_small", "too_large", "not_visible"):
                result["verdict"] = v
        elif line.upper().startswith("RATIO="):
            try:
                result["ratio"] = float(line[6:].strip())
            except ValueError:
                pass
        elif line.upper().startswith("NOTES="):
            result["notes"] = line[6:].strip()
    return result


def read_tiling(vmat_path):
    """Read current g_vTexCoordScale from a .vmat file."""
    full = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "sbox", "Assets", vmat_path)
    if not os.path.exists(full):
        return None
    with open(full, "r") as f:
        for line in f:
            m = re.search(r'g_vTexCoordScale\s+"\[([\d.]+)\s+([\d.]+)\]"', line)
            if m:
                return float(m.group(1))
    return None


def write_tiling(vmat_path, new_tiling):
    """Update g_vTexCoordScale in a .vmat file (repo + editor live addon copy)."""
    repo_full = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "sbox", "Assets", vmat_path)
    addon_full = os.path.join(r"C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute\Assets", vmat_path)
    targets = [repo_full, addon_full]
    wrote_any = False
    for full in targets:
        if not os.path.exists(full):
            print(f"  ! Material file not found: {full}")
            continue
        with open(full, "r") as f:
            content = f.read()
        new_content = re.sub(
            r'g_vTexCoordScale\s+"\[[\d.]+\s+[\d.]+\]"',
            f'g_vTexCoordScale "[{new_tiling:.3f} {new_tiling:.3f}]"',
            content
        )
        if new_content == content:
            print(f"  ! No g_vTexCoordScale found in {full}")
            continue
        with open(full, "w") as f:
            f.write(new_content)
        print(f"  >> {full}: tiling -> {new_tiling:.3f}")
        wrote_any = True
    return wrote_any


def compute_new_tiling(current, verdict, ratio):
    """Compute new tiling value based on Qwen verdict.
    Higher tiling = smaller blocks. If blocks too small, decrease tiling."""
    if verdict == "appropriate" or verdict == "not_visible":
        return current
    if ratio <= 0 or ratio == 1.0:
        return current
    if verdict == "too_small":
        # blocks appear too small → tiling too high → reduce
        return max(1.0, current / ratio)
    if verdict == "too_large":
        # blocks appear too large → tiling too low → increase
        return min(200.0, current * ratio)
    return current


def inspect_one(vl, vp, rubric):
    """Capture and inspect one viewpoint. Returns (verdict_dict, img_path)."""
    name, tx, ty, tz, dist, hoff, yaw, skey = vp
    r = RUBRIC.get(skey, {})
    pos, angles = vantage(tx, ty, tz, dist, hoff, yaw)
    print(f"\n--- {name} ({skey}) ---")
    print(f"  Target: ({tx:.0f},{ty:.0f},{tz:.0f})  dist={dist/M:.0f}m  yaw={yaw}")
    print(f"  Camera: {pos}  angles: {angles}")
    print(f"  Rubric: {r.get('target', '?')}")

    vl.teleport(pos, angles=angles, via="merlyn")
    img = vl.capture(via="merlyn", flash=True, flash_threshold=0.40,
                     flash_radius=2000, flash_pos=(tx, ty, tz))
    if not img:
        print("  CAPTURE FAILED")
        return {"verdict": "not_visible", "size": "", "ratio": 1.0, "notes": "capture failed"}, None

    path = save_img(img, f"tex_{name}")
    ok, stats = pixel_check(img)
    if not ok:
        print(f"  Frame rejected: {stats['dominant_pct']:.0%} single color")
        return {"verdict": "not_visible", "size": "", "ratio": 1.0, "notes": "frame rejected"}, path
    print(f"  Pixel: bright={stats['bright_pct']:.0%} mid={stats['mid_pct']:.0%} dark={stats['dark_pct']:.0%}")

    prompt = make_prompt(r, name, dist)
    answer = ask_vision(img, prompt)
    print(f"  Qwen: {answer[:200]}")
    verdict = parse_verdict(answer)
    print(f"  >> {verdict['verdict']}  size={verdict['size']}  ratio={verdict['ratio']}  notes={verdict['notes']}")
    return verdict, path


def main():
    parser = argparse.ArgumentParser(description="Lute texture inspection loop")
    parser.add_argument("--fix", action="store_true", help="Auto-fix tiling and reinspect")
    parser.add_argument("--max-iter", type=int, default=3, help="Max fix iterations")
    parser.add_argument("--only", default="", help="Only inspect surfaces matching this substring")
    args = parser.parse_args()

    viewpoints = VIEWPOINTS
    if args.only:
        viewpoints = [vp for vp in VIEWPOINTS if args.only.lower() in vp[0] or args.only.lower() in vp[7]]

    print("=== Lute Texture Inspection Loop ===")
    print(f"Surfaces: {len(viewpoints)}  Auto-fix: {args.fix}  Max iter: {args.max_iter}")

    vl = VisionLib()
    report = []

    for iteration in range(args.max_iter if args.fix else 1):
        print(f"\n{'='*60}")
        print(f"  PASS {iteration+1}" + (" (inspect only)" if not args.fix else " (inspect + fix)"))
        print(f"{'='*60}")

        changed_materials = {}
        all_pass = True

        for vp in viewpoints:
            name, *_, skey = vp
            rubric = RUBRIC.get(skey, {})
            verdict, img_path = inspect_one(vl, vp, rubric)

            entry = {
                "viewpoint": name, "surface": skey,
                "verdict": verdict["verdict"], "size": verdict["size"],
                "ratio": verdict["ratio"], "notes": verdict["notes"],
                "image": img_path,
            }
            report.append(entry)

            if verdict["verdict"] in ("too_small", "too_large"):
                all_pass = False
                if args.fix:
                    mat_path = rubric.get("material")
                    if not mat_path:
                        continue
                    current = read_tiling(mat_path) or rubric.get("tiling", 1)
                    new_t = compute_new_tiling(current, verdict["verdict"], verdict["ratio"])
                    if new_t != current and mat_path not in changed_materials:
                        changed_materials[mat_path] = new_t
                        print(f"  [FIX] {mat_path}: {current:.3f} -> {new_t:.3f}")

        if not args.fix:
            break

        if not changed_materials:
            if all_pass:
                print("\n[OK] All inspectable surfaces pass the rubric.")
            else:
                print("\n[FAIL] Surfaces failed but no material changes computed.")
            break

        # Apply fixes
        print(f"\n--- Applying {len(changed_materials)} material fixes ---")
        for mat_path, new_t in changed_materials.items():
            write_tiling(mat_path, new_t)
            # Update rubric cache so reinspection shows updated context
            for rk, rv in RUBRIC.items():
                if rv["material"] == mat_path:
                    rv["tiling"] = new_t

        # Rebuild + restart
        print("\n--- Rebuilding S&Box project ---")
        import subprocess
        csproj = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                              "sbox", "code", "lute.csproj")
        result = subprocess.run(["dotnet", "build", csproj],
                                capture_output=True, text=True, timeout=120)
        if result.returncode != 0:
            print(f"  BUILD FAILED:\n{result.stderr[-500:]}")
            break
        print("  Build OK")

        # Restart play mode
        print("--- Restarting play mode ---")
        from vision_lib import call
        try:
            call("play_stop")
            time.sleep(3)
        except Exception:
            pass
        try:
            call("play_start")
            print("  Waiting 15s for world generation...")
            time.sleep(15)  # wait for world generation
        except Exception as e:
            print(f"  play_start failed: {e}")
            break

        # Track whether any surface was inconclusive (capture failed)
        any_inconclusive = any(r.get("verdict") == "not_visible" for r in report[-len(viewpoints):])
        if all_pass and not any_inconclusive:
            print("\n[OK] All inspectable surfaces pass the rubric.")
            break
        if all_pass and any_inconclusive:
            print("\n[?] Some surfaces inconclusive (capture failed). Continuing.")
        # else: loop continues to next iteration

    # Final report
    print(f"\n{'='*60}")
    print("  INSPECTION REPORT")
    print(f"{'='*60}")
    for r in report:
        status = {"appropriate": "OK", "too_small": "SMALL", "too_large": "LARGE",
                  "not_visible": "?"}.get(r["verdict"], "?")
        print(f"  {status} {r['viewpoint']:20s} {r['surface']:14s} {r['verdict']:12s} {r['size']:20s} {r['notes']}")

    # Save report
    report_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                                "scrap", "texture_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"\nReport saved: {report_path}")


if __name__ == "__main__":
    main()
