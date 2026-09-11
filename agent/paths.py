"""
paths.py — Shared path configuration for Lute agent scripts.

Centralizes the S&Box editor's live addon directory so scripts don't
hardcode absolute Windows paths. Override via env vars for non-default
install locations.

Env vars:
  LUTE_REPO_ROOT   — repo root (default: parent of this file's dir)
  LUTE_ADDON_ROOT  — editor's live addon copy
                     (default: C:\\Users\\Shadow\\Documents\\sbox-public-clean\\game\\addons\\lute)
  LUTE_ENGINE_ROOT — engine source + game install
                     (default: C:\\Users\\Shadow\\Documents\\sbox-public-clean)
"""
import os

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))

REPO_ROOT = os.environ.get(
    'LUTE_REPO_ROOT',
    os.path.dirname(_THIS_DIR),
)

ADDON_ROOT = os.environ.get(
    'LUTE_ADDON_ROOT',
    r'C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute',
)

ENGINE_ROOT = os.environ.get(
    'LUTE_ENGINE_ROOT',
    r'C:\Users\Shadow\Documents\sbox-public-clean',
)

# Common sub-paths
REPO_SBOX = os.path.join(REPO_ROOT, 'sbox')
REPO_CODE = os.path.join(REPO_SBOX, 'code')
REPO_ASSETS = os.path.join(REPO_SBOX, 'Assets')
REPO_MATERIALS = os.path.join(REPO_ASSETS, 'materials')

ADDON_CODE = os.path.join(ADDON_ROOT, 'code')
ADDON_ASSETS = os.path.join(ADDON_ROOT, 'Assets')
ADDON_MATERIALS = os.path.join(ADDON_ASSETS, 'materials')

CSPROJ = os.path.join(REPO_CODE, 'lute.csproj')


def material_paths(vmat_rel):
    """Return (repo_full, addon_full) paths for a .vmat given relative
    to sbox/Assets/ (e.g. 'materials/medieval/stone_wall.vmat')."""
    repo_full = os.path.join(REPO_ASSETS, vmat_rel)
    addon_full = os.path.join(ADDON_ASSETS, vmat_rel)
    return repo_full, addon_full


def code_paths(cs_rel):
    """Return (repo_full, addon_full) paths for a .cs file given relative
    to sbox/code/ (e.g. 'LuteMonumentBuilder.cs')."""
    repo_full = os.path.join(REPO_CODE, cs_rel)
    addon_full = os.path.join(ADDON_CODE, cs_rel)
    return repo_full, addon_full


def sync_file(src_full, dst_full):
    """Copy src to dst, creating parent dirs. Returns True on success."""
    import shutil
    try:
        os.makedirs(os.path.dirname(dst_full), exist_ok=True)
        shutil.copy2(src_full, dst_full)
        return True
    except Exception as e:
        print(f"  sync failed {src_full} -> {dst_full}: {e}")
        return False
