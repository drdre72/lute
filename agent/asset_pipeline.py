"""
End-to-end asset pipeline orchestration for Lute (S&Box).

Combines FBX ingestion (ingest_asset.py), PBR texture generation
(generate_pbr.py), and texture inspection (texture_inspect.py) into a
single pipeline:

  FBX -> .vmdl -> .vmat -> verify tiling -> auto-fix if needed

Steps:
  1. Ingest FBX: auto-detect scale, generate .vmdl with collision
  2. Generate PBR: 5-channel textures + variants + .vmat files
  3. Match materials: auto-assign model material slots to PBR materials
     by name heuristic (e.g. "stone" in FBX material name -> stone.vmat)
  4. Verify: run texture_inspect.py --diff on new assets
  5. Auto-fix: if tiling issues detected, adjust g_vTexCoordScale in .vmat

Usage:
    python agent/asset_pipeline.py path/to/mesh.fbx
    python agent/asset_pipeline.py path/to/mesh.fbx --material stone
    python agent/asset_pipeline.py path/to/mesh.fbx --material stone --variant mossy
    python agent/asset_pipeline.py path/to/mesh.fbx --variants --vmat --verify
    python agent/asset_pipeline.py path/to/mesh.fbx --skip-pbr  # skip texture gen
    python agent/asset_pipeline.py path/to/mesh.fbx --dry-run    # plan only
"""
import os
import sys
import re
import argparse
import subprocess

REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
AGENT_DIR = os.path.join(REPO_ROOT, "agent")
SBOX_ASSETS = os.path.join(REPO_ROOT, "sbox", "Assets")

# Material name -> .vmat path mapping for auto-matching.
# The key is a substring to search for in FBX material names.
# The value is the .vmat path (relative to sbox root).
MATERIAL_MAP = {
    "stone": "materials/medieval/stone.vmat",
    "rock": "materials/medieval/stone.vmat",
    "wall": "materials/medieval/stone_wall.vmat",
    "brick": "materials/medieval/stone.vmat",
    "wood": "materials/medieval/wood.vmat",
    "plank": "materials/medieval/wood.vmat",
    "timber": "materials/medieval/wood.vmat",
    "metal": "materials/medieval/metal.vmat",
    "iron": "materials/medieval/metal.vmat",
    "steel": "materials/medieval/metal.vmat",
    "roof": "materials/medieval/roof.vmat",
    "tile": "materials/medieval/roof.vmat",
    "plaza": "materials/medieval/plaza.vmat",
    "flag": "materials/medieval/plaza.vmat",
    "ground": "materials/medieval/plaza.vmat",
}

# Variant suffixes
VARIANTS = ["mossy", "weathered", "pristine"]


def run_script(script_name, args, dry_run=False):
    """Run a Python script in the agent/ directory and return its output."""
    script_path = os.path.join(AGENT_DIR, script_name)
    cmd = [sys.executable, script_path] + args
    print(f"  Running: {' '.join(cmd)}")
    if dry_run:
        print(f"  [DRY RUN] Skipped execution.")
        return ""
    result = subprocess.run(cmd, capture_output=True, text=True, cwd=REPO_ROOT)
    if result.stdout:
        print(result.stdout.rstrip())
    if result.returncode != 0:
        print(f"  WARNING: {script_name} exited with code {result.returncode}")
        if result.stderr:
            print(f"  stderr: {result.stderr.rstrip()}")
    return result.stdout


def match_material(fbx_material_name):
    """
    Auto-match an FBX material name to a generated PBR .vmat path.
    Uses substring matching against MATERIAL_MAP keys.
    Returns the .vmat path or None if no match.
    """
    name_lower = fbx_material_name.lower()
    for key, vmat_path in MATERIAL_MAP.items():
        if key in name_lower:
            return vmat_path
    return None


def apply_variant_to_path(vmat_path, variant):
    """Insert a variant suffix into a .vmat path."""
    if variant is None:
        return vmat_path
    base, ext = os.path.splitext(vmat_path)
    return f"{base}_{variant}{ext}"


def parse_fbx_material_names(fbx_path):
    """
    Extract material names from an FBX file.
    For binary FBX, searches for material name strings.
    For ASCII FBX, parses Material: lines.
    Returns a list of material name strings.
    """
    with open(fbx_path, "rb") as f:
        raw = f.read()

    is_binary = b"Kaydara FBX Binary" in raw[:50]
    names = []

    if is_binary:
        # Binary FBX: material names appear as quoted strings near
        # "Material" markers. Search for patterns like: Material::"Name"
        text = raw.decode("utf-8", errors="ignore")
        matches = re.findall(r'Material::"([^"]+)"', text)
        names = matches
    else:
        text = raw.decode("utf-8", errors="ignore")
        # ASCII FBX: Model::"Name", "Type" { Material: "MatName"
        matches = re.findall(r'Material\s*:\s*"([^"]+)"', text)
        names = matches

    # Filter out auto-generated names like root.0.0
    filtered = [n for n in names if not re.match(r'^root\.\d+', n)]
    return filtered if filtered else names


def update_vmdl_material(vmdl_path, material_path):
    """
    Update the global_default_material in a .vmdl file.
    """
    with open(vmdl_path, "r", encoding="utf-8") as f:
        content = f.read()

    content = re.sub(
        r'global_default_material = "[^"]*"',
        f'global_default_material = "{material_path}"',
        content
    )

    with open(vmdl_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)
    print(f"  Updated .vmdl material -> {material_path}")


def auto_fix_tiling(vmat_path, issue_text):
    """
    Auto-fix tiling issues by adjusting g_vTexCoordScale in a .vmat file.
    If the issue text suggests textures are too large (visible tiling),
    increase the scale. If too small (blurry), decrease it.
    """
    with open(vmat_path, "r", encoding="utf-8") as f:
        content = f.read()

    # Parse current scale
    match = re.search(r'g_vTexCoordScale "\[(\d+\.?\d*)\s+(\d+\.?\d*)\]"', content)
    if not match:
        print(f"  Could not parse g_vTexCoordScale from {vmat_path}")
        return False

    current_u = float(match.group(1))
    current_v = float(match.group(2))

    # Heuristic: if "tiling" or "repeating" mentioned, increase scale (more repeats)
    # if "blurry" or "large" mentioned, decrease scale (fewer repeats)
    issue_lower = issue_text.lower()
    if any(w in issue_lower for w in ["tiling", "repeating", "repetition", "too large", "visible pattern"]):
        new_u = current_u * 1.5
        new_v = current_v * 1.5
        action = "increased"
    elif any(w in issue_lower for w in ["blurry", "too small", "low detail", "stretched"]):
        new_u = current_u * 0.75
        new_v = current_v * 0.75
        action = "decreased"
    else:
        print(f"  Could not determine fix direction from issue text: {issue_text}")
        return False

    new_u = round(new_u, 3)
    new_v = round(new_v, 3)

    content = re.sub(
        r'g_vTexCoordScale "\[\d+\.?\d*\s+\d+\.?\d*\]"',
        f'g_vTexCoordScale "[{new_u:.3f} {new_v:.3f}]"',
        content
    )

    with open(vmat_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)

    print(f"  Auto-fixed tiling: {action} scale from [{current_u}, {current_v}] to [{new_u}, {new_v}]")
    return True


def main():
    parser = argparse.ArgumentParser(
        description="End-to-end asset pipeline: FBX -> .vmdl -> .vmat -> verify -> auto-fix."
    )
    parser.add_argument("fbx", help="Path to the .fbx file to process.")
    parser.add_argument("--material", default=None,
                        help="Override material auto-matching. Specify a material name (stone, wood, metal, roof, plaza) or a full .vmat path.")
    parser.add_argument("--variant", default=None, choices=VARIANTS,
                        help="Use a material variant (mossy, weathered, pristine).")
    parser.add_argument("--outdir", default=None,
                        help="Output directory for .vmdl (default: same as FBX).")
    parser.add_argument("--name", default=None,
                        help="Output .vmdl name (without extension).")
    parser.add_argument("--variants", action="store_true",
                        help="Generate all material variants during PBR step.")
    parser.add_argument("--vmat", action="store_true",
                        help="Generate .vmat files during PBR step.")
    parser.add_argument("--skip-pbr", action="store_true",
                        help="Skip PBR texture generation (use existing textures).")
    parser.add_argument("--verify", action="store_true",
                        help="Run texture_inspect.py --diff after pipeline.")
    parser.add_argument("--auto-fix", action="store_true",
                        help="Auto-fix tiling issues if detected by verification.")
    parser.add_argument("--dry-run", action="store_true",
                        help="Plan only, don't execute sub-scripts.")
    parser.add_argument("--max-fix-iter", type=int, default=3,
                        help="Max auto-fix iterations (default 3).")

    args = parser.parse_args()

    fbx_abs = os.path.abspath(args.fbx)
    if not os.path.isfile(fbx_abs):
        print(f"ERROR: File not found: {fbx_abs}")
        sys.exit(1)

    print(f"=== Lute Asset Pipeline ===")
    print(f"  Source: {fbx_abs}")
    print(f"  Steps: ingest -> PBR -> match -> verify -> fix")

    # ── Step 1: Ingest FBX ──
    print(f"\n  [1/5] Ingesting FBX...")
    ingest_args = [fbx_abs]
    if args.outdir:
        ingest_args += ["--outdir", args.outdir]
    if args.name:
        ingest_args += ["--name", args.name]
    ingest_args += ["--verify"]
    ingest_output = run_script("ingest_asset.py", ingest_args, args.dry_run)

    # Determine the generated .vmdl path
    out_dir = args.outdir if args.outdir else os.path.dirname(fbx_abs)
    out_name = args.name if args.name else os.path.splitext(os.path.basename(fbx_abs))[0]
    vmdl_path = os.path.join(out_dir, out_name + ".vmdl")

    if not os.path.isfile(vmdl_path) and not args.dry_run:
        print(f"  ERROR: .vmdl not found at {vmdl_path}")
        sys.exit(1)

    # ── Step 2: Generate PBR textures ──
    if not args.skip_pbr:
        print(f"\n  [2/5] Generating PBR textures...")
        pbr_args = []
        if args.variants:
            pbr_args.append("--variants")
        if args.vmat:
            pbr_args.append("--vmat")
        run_script("generate_pbr.py", pbr_args, args.dry_run)
    else:
        print(f"\n  [2/5] Skipping PBR texture generation (--skip-pbr)")

    # ── Step 3: Match materials ──
    print(f"\n  [3/5] Matching materials...")

    # Determine the material to use
    material_path = None

    if args.material:
        # User-specified material
        if args.material.endswith(".vmat"):
            material_path = args.material
        elif args.material in ("stone", "wood", "metal", "roof", "plaza"):
            variant_suffix = f"_{args.variant}" if args.variant else ""
            material_path = f"materials/medieval/{args.material}{variant_suffix}.vmat"
        else:
            # Try as a name match
            matched = match_material(args.material)
            if matched:
                material_path = matched
            else:
                print(f"  WARNING: Could not match material '{args.material}', using default.")

    if material_path is None:
        # Auto-match from FBX material names
        fbx_mats = parse_fbx_material_names(fbx_abs)
        if fbx_mats:
            print(f"  FBX material names: {fbx_mats}")
            for mname in fbx_mats:
                matched = match_material(mname)
                if matched:
                    material_path = matched
                    print(f"  Matched '{mname}' -> {matched}")
                    break

        if material_path is None:
            # No match found — use default
            material_path = "materials/dev/primary_white.vmat"
            print(f"  No material match found, using default: {material_path}")

    # Apply variant if specified
    if args.variant and "medieval" in material_path:
        base, ext = os.path.splitext(material_path)
        material_path = f"{base}_{args.variant}{ext}"

    # Update the .vmdl
    if not args.dry_run and os.path.isfile(vmdl_path):
        update_vmdl_material(vmdl_path, material_path)

    # ── Step 4: Verify tiling ──
    verify_output = ""
    if args.verify:
        print(f"\n  [4/5] Verifying tiling...")
        verify_args = ["--diff"]
        verify_output = run_script("texture_inspect.py", verify_args, args.dry_run)
    else:
        print(f"\n  [4/5] Skipping verification (use --verify)")

    # ── Step 5: Auto-fix tiling ──
    if args.auto_fix and args.verify and verify_output:
        print(f"\n  [5/5] Auto-fixing tiling issues...")

        # Parse verify output for tiling issues
        issues = []
        for line in verify_output.split("\n"):
            if any(w in line.lower() for w in ["tiling", "scale", "repeating", "blurry", "stretched"]):
                if "fail" in line.lower() or "issue" in line.lower() or "warning" in line.lower():
                    issues.append(line.strip())

        if not issues:
            print(f"  No tiling issues detected.")
        else:
            print(f"  Found {len(issues)} tiling issue(s):")
            for issue in issues:
                print(f"    - {issue}")

            # Find the .vmat to fix
            vmat_abs = os.path.join(SBOX_ASSETS, material_path) if not material_path.startswith("dev") else None
            if vmat_abs and os.path.isfile(vmat_abs):
                for iteration in range(args.max_fix_iter):
                    print(f"\n  Fix iteration {iteration + 1}/{args.max_fix_iter}")
                    fixed = auto_fix_tiling(vmat_abs, issues[0] if issues else "")
                    if not fixed:
                        break
                    # Re-verify
                    print(f"  Re-verifying...")
                    re_verify = run_script("texture_inspect.py", ["--diff"], args.dry_run)
                    # Check if issues resolved
                    new_issues = [l for l in re_verify.split("\n")
                                  if any(w in l.lower() for w in ["tiling", "scale"])
                                  and ("fail" in l.lower() or "issue" in l.lower())]
                    if not new_issues:
                        print(f"  Tiling issues resolved!")
                        break
                    issues = new_issues
            else:
                print(f"  Cannot auto-fix: .vmat not found at {vmat_abs}")
    elif args.auto_fix:
        print(f"\n  [5/5] Auto-fix skipped (requires --verify)")
    else:
        print(f"\n  [5/5] Auto-fix skipped (use --auto-fix)")

    # ── Summary ──
    print(f"\n=== Pipeline Summary ===")
    print(f"  FBX: {os.path.basename(fbx_abs)}")
    print(f"  .vmdl: {vmdl_path}")
    print(f"  Material: {material_path}")
    if args.variant:
        print(f"  Variant: {args.variant}")
    print(f"  PBR: {'skipped' if args.skip_pbr else 'generated'}")
    print(f"  Verify: {'run' if args.verify else 'skipped'}")
    print(f"  Auto-fix: {'run' if args.auto_fix and args.verify else 'skipped'}")
    print(f"\nDone!")


if __name__ == "__main__":
    main()
