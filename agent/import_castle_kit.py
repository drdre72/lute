#!/usr/bin/env python3
"""
Import Kenney Castle Kit FBX models into S&Box Source 2 format.

Creates .vmdl files (KV3 text) for each FBX, .vtex files for textures,
and .vmat material files. Copies everything to both the repo and the
editor addon directory.

Kenney assets are authored in meters, so import_scale = 39.37 (meters to inches).
Uses global_default_material to bypass the FBX material name gotcha.
"""

import os
import shutil
import sys
from pathlib import Path

# Paths
SRC = Path(r"C:\Users\Shadow\Downloads\kenney_castle-kit")
REPO = Path(r"C:\Users\Shadow\Documents\lute\sbox\Assets")
ADDON = Path(r"C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute\Assets")

# Asset subdirectories
MODEL_DIR = "models/castle_kit"
MAT_DIR = "materials/castle_kit"
TEX_DIR = "materials/castle_kit"

# Import scale: Kenney assets are in meters, S&Box uses inches
# 1 meter = 39.37 inches
IMPORT_SCALE = "39.37"

# Default material to use (bypasses FBX material name issues)
DEFAULT_MATERIAL = "materials/dev/primary_white.vmat"


def ensure_dir(path: Path):
    path.mkdir(parents=True, exist_ok=True)


def copy_fbx_files():
    """Copy FBX files to both repo and addon directories."""
    fbx_src = SRC / "Models" / "FBX format"
    if not fbx_src.exists():
        print(f"ERROR: FBX source not found: {fbx_src}")
        return []

    fbx_files = list(fbx_src.glob("*.fbx"))
    print(f"Found {len(fbx_files)} FBX files")

    repo_model_dir = REPO / MODEL_DIR
    addon_model_dir = ADDON / MODEL_DIR
    ensure_dir(repo_model_dir)
    ensure_dir(addon_model_dir)

    copied = []
    for fbx in fbx_files:
        # Copy to repo
        dst_repo = repo_model_dir / fbx.name
        shutil.copy2(fbx, dst_repo)
        # Copy to addon
        dst_addon = addon_model_dir / fbx.name
        shutil.copy2(fbx, dst_addon)
        copied.append(fbx.stem)

    print(f"Copied {len(copied)} FBX files to repo and addon")
    return copied


def copy_textures():
    """Copy texture PNGs to both directories."""
    tex_src = SRC / "Models" / "FBX format" / "Textures"
    if not tex_src.exists():
        tex_src = SRC / "Models" / "Textures"

    if not tex_src.exists():
        print("WARNING: No textures directory found")
        return []

    tex_files = list(tex_src.glob("*.png"))
    print(f"Found {len(tex_files)} texture files")

    repo_tex_dir = REPO / TEX_DIR
    addon_tex_dir = ADDON / TEX_DIR
    ensure_dir(repo_tex_dir)
    ensure_dir(addon_tex_dir)

    copied = []
    for tex in tex_files:
        dst_repo = repo_tex_dir / tex.name
        shutil.copy2(tex, dst_repo)
        dst_addon = addon_tex_dir / tex.name
        shutil.copy2(tex, dst_addon)
        copied.append(tex.stem)

    print(f"Copied {len(copied)} textures to repo and addon")
    return copied


def create_vtex(tex_name: str):
    """Create a .vtex file for a texture PNG."""
    vtex_content = f"""/* VTEX */
{{
	"version": 1,
	"filename": "materials/castle_kit/{tex_name}.png",
	"format": "DXT5",
	"type": "Texture2D",
	"filter": "Box",
	"mipmaps": true,
	"bumpmap": false,
	"srgbread": true,
	"srgbwrite": true,
}}
"""

    for base in [REPO, ADDON]:
        vtex_path = base / TEX_DIR / f"{tex_name}.vtex"
        ensure_dir(vtex_path.parent)
        vtex_path.write_text(vtex_content, encoding="utf-8")

    print(f"  Created {tex_name}.vtex")


def create_vmat(tex_name: str):
    """Create a .vmat material file referencing a colormap texture."""
    vmat_content = f"""/* VMAT */
{{
	"shader": "simple",
	"textureColor": "materials/castle_kit/{tex_name}.vtex",
	"textureNormal": "",
	"textureRough": "",
	"textureMetal": "",
	"colorTint": "[1 1 1]",
	"roughTint": "[0.5 0.5 0.5]",
	"metalTint": "[0 0 0]",
}}
"""

    for base in [REPO, ADDON]:
        vmat_path = base / MAT_DIR / f"{tex_name}.vmat"
        ensure_dir(vmat_path.parent)
        vmat_path.write_text(vmat_content, encoding="utf-8")

    print(f"  Created {tex_name}.vmat")


def create_vmdl(fbx_stem: str):
    """Create a .vmdl file for an FBX model.

    Uses MaterialGroupList with global_default_material to bypass
    the FBX material name gotcha (root.0.0, root.1, etc.).
    """
    vmdl_content = f"""/* VMDL */
{{
	"rootNode":
	{{
		"children":
		[
			{{
				"typeName": "RenderMeshFile",
				"fileName": "models/castle_kit/{fbx_stem}.fbx",
				"import_scale": {IMPORT_SCALE},
				"import_add_skin": false,
				"import_remove_degenerate_faces": true,
			}},
			{{
				"typeName": "MaterialGroupList",
				"children":
				[
					{{
						"typeName": "DefaultMaterialGroup",
						"use_global_default": true,
						"global_default_material": "{DEFAULT_MATERIAL}",
					}}
				]
			}}
		]
	}}
}}
"""

    for base in [REPO, ADDON]:
        vmdl_path = base / MODEL_DIR / f"{fbx_stem}.vmdl"
        ensure_dir(vmdl_path.parent)
        vmdl_path.write_text(vmdl_content, encoding="utf-8")

    print(f"  Created {fbx_stem}.vmdl")


def main():
    print("=== Kenney Castle Kit Import Pipeline ===")
    print()

    # Step 1: Copy FBX files
    fbx_stems = copy_fbx_files()
    if not fbx_stems:
        print("No FBX files found, aborting.")
        sys.exit(1)

    # Step 2: Copy textures
    tex_names = copy_textures()

    # Step 3: Create .vtex files for textures
    print("\nCreating .vtex files...")
    for tex in tex_names:
        create_vtex(tex)

    # Step 4: Create .vmat material files
    print("\nCreating .vmat files...")
    # Create a castle_kit material from the colormap
    if "colormap" in tex_names:
        create_vmat("castle_kit_colormap")
    for tex in tex_names:
        if tex != "colormap":
            create_vmat(f"castle_kit_{tex}")

    # Step 5: Create .vmdl files for each FBX
    print(f"\nCreating .vmdl files for {len(fbx_stems)} models...")
    for stem in fbx_stems:
        create_vmdl(stem)

    print(f"\n=== Import complete ===")
    print(f"  {len(fbx_stems)} models imported")
    print(f"  {len(tex_names)} textures imported")
    print(f"  Files in both repo and addon directories")
    print()
    print("Next: Restart S&Box editor to compile the .vmdl files.")
    print("The editor will auto-compile .vmdl → .vmdl_c on load.")


if __name__ == "__main__":
    main()
