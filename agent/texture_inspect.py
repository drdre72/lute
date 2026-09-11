"""
Texture inspection loop — systematically checks each market surface
against a medieval brick/stone rubric using Qwen vision.

Two inspection scales:
  - Macro: aerial / wide overview to verify overall layout, ring alignment,
    moat, and major structural placement.
  - Micro: close-up surface inspection for texture scale rubric checks.

Drives Merlyn to representative viewpoints around the market perimeter,
captures screenshots, asks Qwen to estimate texture scale, compares
against a rubric, optionally auto-fixes material tiling, then re-inspects.
Also checks for structural discrepancy keywords (floating, missing, etc.).

Usage:
    python agent/texture_inspect.py              # inspect all surfaces, report
    python agent/texture_inspect.py --fix        # inspect + auto-fix tiling, reinspect
    python agent/texture_inspect.py --max-iter 3  # limit fix iterations
    python agent/texture_inspect.py --only wall   # only inspect surfaces matching "wall"
    python agent/texture_inspect.py --no-macro    # skip macro overview pass
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

# ── Macro viewpoints: aerial / wide overview for layout verification ──
# These don't check texture scale — they check structural integrity.
# Each has a free-text prompt and a set of discrepancy keywords to scan for.
# Note: vantage() computes pitch from height/distance ratio. For a steep
# top-down view, height must be much larger than distance.
MACRO_VIEWPOINTS = [
    {
        "name": "aerial_overview",
        # High above market center, looking down at steep angle.
        # Large distance keeps Merlyn's body small in frame.
        "target": (CX, CY, 0),
        "distance": 4000,   # 102m horizontal
        "height": 8000,    # 203m up — steep downward angle
        "yaw": 0,
        "prompt": (
            "You are looking down at a medieval marketplace from above. "
            "Verify you can see: a central plaza, an inner ring of buildings, "
            "an outer curtain wall (square), and a moat surrounding the walls. "
            "Report if any major section is missing, rotated, displaced, or "
            "if the overall layout is not concentric square rings. "
            "Light gray surfaces are stone, not snow."
        ),
        "discrepancy_keywords": ["missing", "rotated", "displaced", "not concentric", "gap"],
    },
    {
        "name": "south_gate_approach",
        # Outside the moat, looking north toward the south gate
        "target": (CX, CY - MOAT_HALF, FOUND_TOP),
        "distance": 1500,   # 38m from gate
        "height": 300,      # 7.6m up — eye-level approach
        "yaw": 0,
        "prompt": (
            "You are approaching the south gate of a medieval market from outside. "
            "Verify you can see: a bridge crossing the moat, two flanking watchtowers, "
            "and the main gate entrance in the curtain wall. "
            "Report if the bridge is missing, towers are floating or misplaced, "
            "or the gate entrance is blocked or absent."
        ),
        "discrepancy_keywords": ["missing", "floating", "misplaced", "blocked", "absent"],
    },
    {
        "name": "plaza_interior",
        # Elevated inside the market, looking down at the plaza
        "target": (CX, CY, FOUND_TOP + 200),
        "distance": 3000,   # 76m — keeps plaza in frame, Merlyn small
        "height": 4000,    # 102m up — elevated overview
        "yaw": 0,
        "prompt": (
            "You are looking down at the interior of a medieval market plaza from an elevated angle. "
            "Verify you can see: market stalls with awnings, a central well/fountain, "
            "NPC housing buildings in the inner ring, and the plaza floor. "
            "Report if any objects are floating, buried, missing, or if the well is absent. "
            "Light gray ground is stone flagstone, not snow or ice."
        ),
        "discrepancy_keywords": ["floating", "buried", "missing", "absent", "snow", "ice"],
    },
]


def make_prompt(rubric, surface_name, distance_units):
    """Build a structured Qwen prompt for texture scale assessment.
    Includes anti-hallucination conditioning: explicit negative cues to
    prevent common VLM misreads (snow/ice on light stone, etc.)."""
    dist_m = distance_units / M
    # Anti-hallucination cues based on material type
    skey = rubric.get("desc", "")
    anti_halluc = ""
    if "stone" in skey.lower() or "plaza" in skey.lower() or "foundation" in skey.lower():
        anti_halluc = " Light gray surfaces are stone, not snow or ice."
    if "wood" in skey.lower():
        anti_halluc = " Brown surfaces are wood planks, not dirt or soil."
    return (
        f"You are inspecting a medieval market surface: {rubric['desc']}.\n"
        f"The camera is approximately {dist_m:.0f} meters from the surface.\n"
        f"Target texture scale: {rubric['target']}.{anti_halluc}\n\n"
        f"Look at the stone/brick/wood texture on the main surface in this image. "
        f"Estimate the apparent physical size of individual blocks/tiles/planks in centimeters. "
        f"Compare to the target scale.\n\n"
        f"Respond EXACTLY in this format (no other text):\n"
        f"SIZE=<estimated width cm>x<estimated height cm>\n"
        f"VERDICT=appropriate|too_small|too_large|not_visible\n"
        f"RATIO_U=<width ratio vs target, e.g. 2.0 means 2x too wide; 1.0 if appropriate>\n"
        f"RATIO_V=<height ratio vs target; 1.0 if appropriate>\n"
        f"NOTES=<one short sentence>"
    )


def parse_verdict(text):
    """Parse Qwen's structured response into a dict.
    Extracts separate U (width) and V (height) ratios when available;
    falls back to a single RATIO for backward compatibility."""
    result = {
        "verdict": "not_visible", "size": "",
        "ratio_u": 1.0, "ratio_v": 1.0, "ratio": 1.0,
        "notes": "",
    }
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
        elif line.upper().startswith("RATIO_U="):
            try:
                result["ratio_u"] = float(line[8:].strip())
            except ValueError:
                pass
        elif line.upper().startswith("RATIO_V="):
            try:
                result["ratio_v"] = float(line[8:].strip())
            except ValueError:
                pass
        elif line.upper().startswith("RATIO="):
            try:
                r = float(line[6:].strip())
                result["ratio"] = r
            except ValueError:
                pass
        elif line.upper().startswith("NOTES="):
            result["notes"] = line[6:].strip()
    # Backward compat: if U/V not provided, fall back to single RATIO
    if result["ratio_u"] == 1.0 and result["ratio_v"] == 1.0 and result["ratio"] != 1.0:
        result["ratio_u"] = result["ratio"]
        result["ratio_v"] = result["ratio"]
    return result


def read_tiling(vmat_path):
    """Read current g_vTexCoordScale (U, V) from a .vmat file.
    Returns (u, v) tuple or None."""
    from paths import material_paths
    repo_full, _ = material_paths(vmat_path)
    if not os.path.exists(repo_full):
        return None
    with open(repo_full, "r") as f:
        for line in f:
            m = re.search(r'g_vTexCoordScale\s+"\[([\d.]+)\s+([\d.]+)\]"', line)
            if m:
                return float(m.group(1)), float(m.group(2))
    return None


def write_tiling(vmat_path, new_u, new_v):
    """Update g_vTexCoordScale in a .vmat file (repo + editor live addon copy).
    Returns True only if BOTH copies were written successfully."""
    from paths import material_paths
    repo_full, addon_full = material_paths(vmat_path)
    targets = [("repo", repo_full), ("addon", addon_full)]
    new_str = f'g_vTexCoordScale "[{new_u:.3f} {new_v:.3f}]"'
    wrote_repo = False
    wrote_addon = False
    for label, full in targets:
        if not os.path.exists(full):
            print(f"  ! Material file not found ({label}): {full}")
            continue
        with open(full, "r") as f:
            content = f.read()
        new_content = re.sub(
            r'g_vTexCoordScale\s+"\[[\d.]+\s+[\d.]+\]"',
            new_str,
            content
        )
        if new_content == content:
            print(f"  ! No g_vTexCoordScale found in {label}: {full}")
            continue
        with open(full, "w") as f:
            f.write(new_content)
        print(f"  >> {label}: {full}: tiling -> U={new_u:.3f} V={new_v:.3f}")
        if label == "repo":
            wrote_repo = True
        else:
            wrote_addon = True
    if wrote_repo and not wrote_addon:
        print(f"  !! WARNING: repo updated but addon copy did not — "
              f"the editor will not see this change. Check ADDON_ROOT in paths.py.")
    return wrote_repo and wrote_addon


# Damping factor: move only this fraction of the suggested correction per pass.
# 0.5 = halfway, prevents overshoot from noisy single-image estimates.
DAMPING = 0.5


def compute_new_tiling(current_u, current_v, verdict, ratio_u, ratio_v):
    """Compute new (U, V) tiling values based on Qwen verdict.
    Higher tiling = smaller blocks. Applies DAMPING to avoid overshoot.
    Returns (new_u, new_v) — unchanged if verdict is appropriate/not_visible."""
    if verdict in ("appropriate", "not_visible"):
        return current_u, current_v
    if ratio_u <= 0:
        ratio_u = 1.0
    if ratio_v <= 0:
        ratio_v = 1.0

    def damped(current, ratio, too_large):
        # too_large: blocks too big -> tiling too low -> increase tiling
        # too_small: blocks too small -> tiling too high -> decrease tiling
        if too_large:
            target = current * ratio
        else:
            target = current / ratio
        # Move only DAMPING fraction toward target
        return current + (target - current) * DAMPING

    new_u = current_u
    new_v = current_v
    if verdict == "too_large":
        new_u = damped(current_u, ratio_u, too_large=True)
        new_v = damped(current_v, ratio_v, too_large=True)
    elif verdict == "too_small":
        new_u = damped(current_u, ratio_u, too_large=False)
        new_v = damped(current_v, ratio_v, too_large=False)

    return max(1.0, min(200.0, new_u)), max(1.0, min(200.0, new_v))


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
    print(f"  >> {verdict['verdict']}  size={verdict['size']}  "
          f"U={verdict['ratio_u']:.2f} V={verdict['ratio_v']:.2f}  notes={verdict['notes']}")
    return verdict, path


def inspect_macro(vl, wp):
    """Run a macro (overview) inspection pass. Returns a report entry.
    Unlike micro passes, this doesn't check texture scale — it checks
    structural integrity by scanning Qwen's free-text response for
    discrepancy keywords (floating, missing, displaced, etc.)."""
    name = wp["name"]
    tx, ty, tz = wp["target"]
    dist = wp["distance"]
    hoff = wp["height"]
    yaw = wp["yaw"]
    prompt = wp["prompt"]
    keywords = wp.get("discrepancy_keywords", [])

    pos, angles = vantage(tx, ty, tz, dist, hoff, yaw)
    print(f"\n--- MACRO: {name} ---")
    print(f"  Target: ({tx:.0f},{ty:.0f},{tz:.0f})  dist={dist/M:.0f}m  yaw={yaw}")

    # Use Merlyn for macro views. Player camera shows the editor viewport,
    # not the game. Merlyn's over-the-shoulder camera shows his body at
    # close range, so macro viewpoints use large height-to-distance ratios
    # for steep downward angles (body drops below frame).
    vl.teleport(pos, angles=angles, via="merlyn")
    img = vl.capture(via="merlyn", flash=True, flash_threshold=0.40,
                     flash_radius=3000, flash_pos=(tx, ty, tz))
    if not img:
        print("  CAPTURE FAILED")
        return {"viewpoint": name, "scale": "macro", "verdict": "not_visible",
                "discrepancies": [], "notes": "capture failed", "image": None}

    path = save_img(img, f"macro_{name}")
    ok, stats = pixel_check(img)
    if not ok:
        print(f"  Frame rejected: {stats['dominant_pct']:.0%} single color")
        return {"viewpoint": name, "scale": "macro", "verdict": "not_visible",
                "discrepancies": [], "notes": "frame rejected", "image": path}
    print(f"  Pixel: bright={stats['bright_pct']:.0%} mid={stats['mid_pct']:.0%} dark={stats['dark_pct']:.0%}")

    answer = ask_vision(img, prompt)
    print(f"  Qwen: {answer[:300]}")

    # Scan for discrepancy keywords in Qwen's response.
    # Handle negation: "not missing", "no gaps", "nothing is displaced"
    # should NOT trigger a discrepancy.
    answer_lower = answer.lower()
    negations = ["not ", "no ", "nothing", "none", "without", "isn't", "aren't", "wasn't", "weren't"]
    found = []
    for kw in keywords:
        # Find all occurrences of the keyword
        idx = 0
        while True:
            pos = answer_lower.find(kw, idx)
            if pos == -1:
                break
            # Check if a negation word appears within 20 chars before the keyword
            context_before = answer_lower[max(0, pos - 25):pos]
            is_negated = any(neg in context_before for neg in negations)
            if not is_negated:
                found.append(kw)
                break  # one non-negated occurrence is enough
            idx = pos + len(kw)

    verdict = "ok" if not found else "discrepancy"
    print(f"  >> {verdict}  discrepancies: {found if found else 'none'}")

    return {
        "viewpoint": name, "scale": "macro", "verdict": verdict,
        "discrepancies": found, "vlm_response": answer,
        "notes": ", ".join(found) if found else "no discrepancies detected",
        "image": path,
    }


def main():
    parser = argparse.ArgumentParser(description="Lute texture inspection loop")
    parser.add_argument("--fix", action="store_true", help="Auto-fix tiling and reinspect")
    parser.add_argument("--max-iter", type=int, default=3, help="Max fix iterations")
    parser.add_argument("--only", default="", help="Only inspect surfaces matching this substring")
    parser.add_argument("--no-macro", action="store_true", help="Skip macro overview pass")
    args = parser.parse_args()

    viewpoints = VIEWPOINTS
    if args.only:
        viewpoints = [vp for vp in VIEWPOINTS if args.only.lower() in vp[0] or args.only.lower() in vp[7]]

    print("=== Lute Texture Inspection Loop ===")
    print(f"Surfaces: {len(viewpoints)}  Auto-fix: {args.fix}  Max iter: {args.max_iter}")
    print(f"Macro pass: {'disabled' if args.no_macro else 'enabled'}")

    vl = VisionLib()
    report = []

    # ── Macro pass: structural overview ──
    if not args.no_macro:
        print(f"\n{'='*60}")
        print("  MACRO PASS (structural overview)")
        print(f"{'='*60}")
        for wp in MACRO_VIEWPOINTS:
            entry = inspect_macro(vl, wp)
            report.append(entry)
            if entry["verdict"] == "discrepancy":
                print(f"  [!] Structural discrepancies found: {entry['discrepancies']}")
                # Note: macro discrepancies are reported but not auto-fixed.
                # They require C# source changes, not material tiling changes.

    # ── Micro pass: texture scale inspection ──
    for iteration in range(args.max_iter if args.fix else 1):
        print(f"\n{'='*60}")
        print(f"  MICRO PASS {iteration+1}" + (" (inspect only)" if not args.fix else " (inspect + fix)"))
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
                "ratio_u": verdict["ratio_u"], "ratio_v": verdict["ratio_v"],
                "notes": verdict["notes"], "image": img_path,
            }
            report.append(entry)

            if verdict["verdict"] in ("too_small", "too_large"):
                all_pass = False
                if args.fix:
                    mat_path = rubric.get("material")
                    if not mat_path:
                        continue
                    current = read_tiling(mat_path)
                    if current is None:
                        cur_u = rubric.get("tiling", 1)
                        cur_v = cur_u
                    else:
                        cur_u, cur_v = current
                    new_u, new_v = compute_new_tiling(
                        cur_u, cur_v, verdict["verdict"],
                        verdict["ratio_u"], verdict["ratio_v"])
                    if (new_u != cur_u or new_v != cur_v) and mat_path not in changed_materials:
                        changed_materials[mat_path] = (new_u, new_v)
                        print(f"  [FIX] {mat_path}: U {cur_u:.3f}->{new_u:.3f}  V {cur_v:.3f}->{new_v:.3f}")

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
        for mat_path, (new_u, new_v) in changed_materials.items():
            write_tiling(mat_path, new_u, new_v)
            # Update rubric cache so reinspection shows updated context
            for rk, rv in RUBRIC.items():
                if rv["material"] == mat_path:
                    rv["tiling"] = (new_u + new_v) * 0.5  # avg for display

        # Rebuild + restart
        print("\n--- Rebuilding S&Box project ---")
        import subprocess
        from paths import CSPROJ
        result = subprocess.run(["dotnet", "build", CSPROJ],
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
        scale = r.get("scale", "micro")
        verdict = r["verdict"]
        if scale == "macro":
            status = {"ok": "OK", "discrepancy": "DISCREPANCY",
                      "not_visible": "?"}.get(verdict, "?")
            print(f"  {status:12s} {r['viewpoint']:20s} {'(macro)':14s} {verdict:12s} {r.get('notes', '')}")
        else:
            status = {"appropriate": "OK", "too_small": "SMALL", "too_large": "LARGE",
                      "not_visible": "?"}.get(verdict, "?")
            print(f"  {status:12s} {r['viewpoint']:20s} {r.get('surface', ''):14s} {verdict:12s} {r.get('size', ''):20s} {r.get('notes', '')}")

    # Save report
    report_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                                "scrap", "texture_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"\nReport saved: {report_path}")


if __name__ == "__main__":
    main()
