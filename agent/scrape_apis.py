#!/usr/bin/env python3
"""Scrape S&Box engine source for API signatures and dump to a markdown reference.

Targets the components/systems Lute actually uses, so the agent stops
hallucinating Garry's Mod / early-S&Box APIs. Run after pulling a new
engine snapshot, or whenever you need to verify an exact signature.

Output: docs_cache/api_signatures.md

Usage:
    python agent/scrape_apis.py            # full scrape
    python agent/scrape_apis.py --check    # dry-run, just report what it'd scan
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

ENGINE_ROOT = Path(r"C:\Users\Shadow\Documents\sbox-public-clean\engine")
GAME_ROOT = Path(r"C:\Users\Shadow\Documents\sbox-public-clean\game")

# Dirs to scrape — focused on what Lute uses, not the whole engine.
TARGET_DIRS = [
    ("Scene/Components (colliders, lights, render, player, terrain)",
     ENGINE_ROOT / "Sandbox.Engine" / "Scene" / "Components"),
    ("Scene/Scene (CreateObject, Trace, Camera, SceneWorld)",
     ENGINE_ROOT / "Sandbox.Engine" / "Scene" / "Scene"),
    ("Scene/GameObjectSystems (trace providers, debug overlay)",
     ENGINE_ROOT / "Sandbox.Engine" / "Scene" / "GameObjectSystems"),
    ("System/Math (Noise, BBox, Vector, Angles, Rotation)",
     ENGINE_ROOT / "Sandbox.System" / "Math"),
    ("Resources (Model, Material, TerrainStorage, GameResource)",
     ENGINE_ROOT / "Sandbox.Engine" / "Resources"),
    ("Utility/RayTrace (MeshTraceRequest)",
     ENGINE_ROOT / "Sandbox.Engine" / "Utility" / "RayTrace"),
    ("Systems/Physics (PhysicsWorld, PhysicsTraceBuilder)",
     ENGINE_ROOT / "Sandbox.Engine" / "Systems" / "Physics"),
    ("MCP Tools (editor addon — Scene, Play, Editor, Asset, Log, UI)",
     GAME_ROOT / "addons" / "tools" / "Code" / "Mcp"),
]

# Files/symbols to definitely include even if the dir scan misses them
ALWAYS_INCLUDE_FILES = [
    ENGINE_ROOT / "Sandbox.Engine" / "Scene" / "Components" / "Render" / "ModelRenderer.cs",
    ENGINE_ROOT / "Sandbox.Engine" / "Scene" / "Components" / "Render" / "ModelRenderer.Bounds.cs",
    ENGINE_ROOT / "Sandbox.Engine" / "Resources" / "Model" / "Model.cs",
    ENGINE_ROOT / "Sandbox.System" / "Math" / "Noise.cs",
    ENGINE_ROOT / "Sandbox.System" / "Math" / "BBox.cs",
]

# Skip files matching these patterns (tests, generated, internal)
SKIP_PATTERNS = [
    r"\\Tests\\",
    r"\\obj\\",
    r"\\bin\\",
    r"\.Generated\.cs$",
    r"AssemblyInfo\.cs$",
]

# Only extract public surface — public classes, methods, properties, attributes
CLASS_RE = re.compile(
    r"^(?P<indent>\t*)\[(?P<attr>[^\]]+)\]\s*\n"  # attributes (multi-line possible)
    r"(?P=indent)?\s*(?:public|internal)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*"
    r"(?:class|struct)\s+(?P<name>\w+)"
    r"(?:\s*:\s*(?P<base>[\w<>,\s.]+?))?"
    r"\s*(?:where\s+.+?)?\s*\{?",
    re.MULTILINE,
)

# Simpler, more reliable line-based parser
PUBLIC_CLASS_RE = re.compile(
    r"^\s*(?:public|internal)\s+"
    r"(?:sealed\s+|abstract\s+|static\s+|partial\s+)*"
    r"(?:class|struct|interface)\s+(\w+)"
    r"(?:\s*:\s*([^\n{]+?))?\s*(?:\{|$)",
    re.MULTILINE,
)

PUBLIC_METHOD_RE = re.compile(
    r"^\s*public\s+(?:static\s+|override\s+|virtual\s+|async\s+|sealed\s+)*"
    r"(?!class\b|struct\b|interface\b|enum\b|namespace\b)"  # not a type decl
    r"(?:[A-Za-z_][\w<>\[\],\s\.\?\*]*?)\s+"
    r"([A-Za-z_]\w*)\s*\(([^)]*)\)"
    r"(?:\s*where\s+.+?)?\s*(?:\{|;|=>)",
    re.MULTILINE,
)

# Single-line property:  public T Name { get; set; }
PUBLIC_PROP_RE = re.compile(
    r"^\s*public\s+(?:static\s+|override\s+|virtual\s+|new\s+)*"
    r"(?:[A-Za-z_][\w<>\[\],\s\.\?\*]*?)\s+"
    r"([A-Za-z_]\w*)\s*\{\s*(?:get;|get;|init;|set;|get;\s*set;|get;\s*init;|get;|set;|init;)\s*\}",
    re.MULTILINE,
)

# Multi-line property:  public T Name\n{ ... get ... set ... }
PUBLIC_PROP_MULTILINE_RE = re.compile(
    r"^\s*public\s+(?:static\s+|override\s+|virtual\s+|new\s+)*"
    r"(?:[A-Za-z_][\w<>\[\],\s\.\?\*]*?)\s+"
    r"([A-Za-z_]\w*)\s*\n\s*\{",
    re.MULTILINE,
)

PROP_ATTRIBUTE_RE = re.compile(
    r"\[Property\b"
)

XML_SUMMARY_RE = re.compile(
    r"///\s*<summary>\s*\n(.*?)///\s*</summary>",
    re.DOTALL | re.MULTILINE,
)

ATTR_RE = re.compile(
    r"^\s*\[([A-Za-z_][\w.]*(?:\([^)]*\))?)"  # Simple single-line attribute capture
)


def should_skip(path: Path) -> bool:
    """Skip test/generated/internal files."""
    s = str(path)
    for pat in SKIP_PATTERNS:
        if re.search(pat, s):
            return True
    return False


def strip_xml_comment(text: str) -> str:
    """Clean up a /// XML comment block into plain text."""
    lines = []
    for ln in text.splitlines():
        s = ln.strip()
        if s.startswith("///"):
            s = s[3:].strip()
        if s:
            lines.append(s)
    return " ".join(lines).strip()


def parse_cs_file(path: Path) -> list[dict]:
    """Parse a C# file and return a list of type entries with their public surface."""
    try:
        text = path.read_text(encoding="utf-8-sig", errors="ignore")
    except Exception:
        return []

    entries = []

    # Find all public class/struct/interface declarations
    for m in PUBLIC_CLASS_RE.finditer(text):
        type_name = m.group(1)
        base_list = m.group(2) or ""

        # Find the line range of this class body — crude but works for our purpose
        start = m.end()
        # Find matching closing brace by counting
        depth = 1
        i = start
        while i < len(text) and depth > 0:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1
        body = text[start:i]

        # Collect public methods
        methods = []
        for mm in PUBLIC_METHOD_RE.finditer(body):
            method_name = mm.group(1)
            params = mm.group(2).strip()
            # Skip constructors with same name as class (they're obvious) and
            # property-style accessors
            if method_name in ("get", "set", "init", "add", "remove"):
                continue
            # Get the XML summary above this method if present
            pre = body[max(0, mm.start() - 400) : mm.start()]
            summary_match = list(XML_SUMMARY_RE.finditer(pre))
            summary = strip_xml_comment(summary_match[-1].group(1)) if summary_match else ""

            # Capture attributes on the line above
            attr_lines = []
            for ln in pre.splitlines()[-5:]:
                if ln.strip().startswith("[") and "]" in ln:
                    attr_lines.append(ln.strip())

            methods.append({
                "name": method_name,
                "params": params,
                "summary": summary,
                "attrs": attr_lines,
            })

        # Collect public properties (single-line form)
        props = []
        seen_prop_names = set()
        for pm in PUBLIC_PROP_RE.finditer(body):
            prop_name = pm.group(1)
            seen_prop_names.add(prop_name)
            # Check for [Property] attribute and XML summary above
            pre = body[max(0, pm.start() - 200) : pm.start()]
            has_property_attr = bool(PROP_ATTRIBUTE_RE.search(pre))
            summary_match = list(XML_SUMMARY_RE.finditer(pre))
            summary = strip_xml_comment(summary_match[-1].group(1)) if summary_match else ""
            props.append({
                "name": prop_name,
                "has_property_attr": has_property_attr,
                "summary": summary,
            })

        # Collect public properties (multiline form: public T Name\n{ get => ...; set { ... } })
        for pm in PUBLIC_PROP_MULTILINE_RE.finditer(body):
            prop_name = pm.group(1)
            if prop_name in seen_prop_names:
                continue  # Already captured as single-line
            # Verify this is actually a property, not a method — check that
            # the body has get/set/init keywords in the first ~200 chars
            body_start = pm.end()
            snippet = body[body_start:body_start + 400]
            if not re.search(r"\b(get|set|init)\b", snippet):
                continue
            seen_prop_names.add(prop_name)
            pre = body[max(0, pm.start() - 200) : pm.start()]
            has_property_attr = bool(PROP_ATTRIBUTE_RE.search(pre))
            summary_match = list(XML_SUMMARY_RE.finditer(pre))
            summary = strip_xml_comment(summary_match[-1].group(1)) if summary_match else ""
            props.append({
                "name": prop_name,
                "has_property_attr": has_property_attr,
                "summary": summary,
            })

        # XML summary for the class itself
        pre_class = body[:100]  # just the first 100 chars of body, before any members
        class_summary_match = list(XML_SUMMARY_RE.finditer(pre_class))
        class_summary = strip_xml_comment(class_summary_match[-1].group(1)) if class_summary_match else ""

        # Also check the text before the class declaration for the summary
        if not class_summary:
            pre_text = text[max(0, m.start() - 600) : m.start()]
            class_summary_match = list(XML_SUMMARY_RE.finditer(pre_text))
            class_summary = strip_xml_comment(class_summary_match[-1].group(1)) if class_summary_match else ""

        entries.append({
            "type": type_name,
            "kind": "class" if "class" in m.group(0) else ("struct" if "struct" in m.group(0) else "interface"),
            "bases": [b.strip() for b in base_list.split(",") if b.strip()],
            "summary": class_summary,
            "methods": methods,
            "props": props,
            "file": str(path.relative_to(ENGINE_ROOT if ENGINE_ROOT in path.parents else GAME_ROOT)),
        })

    return entries


def render_section(title: str, entries: list[dict]) -> str:
    """Render a section of API entries as markdown."""
    lines = [f"## {title}", ""]

    if not entries:
        lines.append("*(no public types found)*")
        lines.append("")
        return "\n".join(lines)

    # Deduplicate by type name (partial classes merge)
    by_name: dict[str, list[dict]] = {}
    for e in entries:
        by_name.setdefault(e["type"], []).append(e)

    for type_name, group in sorted(by_name.items()):
        # Merge partials: union of methods and props
        all_methods = {}
        all_props = {}
        bases = set()
        summary = ""
        files = []
        for e in group:
            for m in e["methods"]:
                all_methods.setdefault(m["name"], m)
            for p in e["props"]:
                all_props.setdefault(p["name"], p)
            bases.update(e["bases"])
            if e["summary"]:
                summary = e["summary"]
            files.append(e["file"])

        kind = group[0]["kind"]
        lines.append(f"### {type_name} ({kind})")
        if bases:
            lines.append(f"**Inherits:** {', '.join(sorted(bases))}")
        if summary:
            lines.append(f"\n> {summary}")
        if len(set(files)) == 1:
            lines.append(f"\n*File: `{files[0]}`*")
        else:
            lines.append(f"\n*Files: {len(set(files))} partial files*")
        lines.append("")

        # [Property]-tagged properties (these show in the inspector/scene file)
        prop_tagged = [p for p in all_props.values() if p["has_property_attr"]]
        if prop_tagged:
            lines.append("**Inspector properties (`[Property]`):**")
            for p in sorted(prop_tagged, key=lambda x: x["name"]):
                desc = f" — {p['summary']}" if p["summary"] else ""
                lines.append(f"- `{p['name']}`{desc}")
            lines.append("")

        # Other public properties
        prop_other = [p for p in all_props.values() if not p["has_property_attr"]]
        if prop_other:
            lines.append("**Public properties:**")
            for p in sorted(prop_other, key=lambda x: x["name"]):
                desc = f" — {p['summary']}" if p["summary"] else ""
                lines.append(f"- `{p['name']}`{desc}")
            lines.append("")

        # Public methods
        if all_methods:
            lines.append("**Public methods:**")
            for m in sorted(all_methods.values(), key=lambda x: x["name"]):
                attrs = f" `{', '.join(m['attrs'])}`" if m["attrs"] else ""
                desc = f"\n  > {m['summary']}" if m["summary"] else ""
                lines.append(f"- `{m['name']}({m['params']})`{attrs}{desc}")
            lines.append("")

    return "\n".join(lines)


def scan_dir(dir_path: Path) -> list[dict]:
    """Scan a directory recursively for .cs files and parse them."""
    entries = []
    if not dir_path.exists():
        return entries
    for path in sorted(dir_path.rglob("*.cs")):
        if should_skip(path):
            continue
        entries.extend(parse_cs_file(path))
    return entries


def main() -> int:
    parser = argparse.ArgumentParser(description="Scrape S&Box engine source for API signatures.")
    parser.add_argument("--check", action="store_true",
                        help="Dry-run: list what would be scanned, don't write output.")
    parser.add_argument("--out", default="docs_cache/api_signatures.md",
                        help="Output markdown file (default: docs_cache/api_signatures.md).")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    out_path = repo_root / args.out

    if args.check:
        print("Would scan:")
        for label, d in TARGET_DIRS:
            count = 0
            if d.exists():
                count = sum(1 for _ in d.rglob("*.cs") if not should_skip(_))
            print(f"  {label}: {count} files in {d}")
        print(f"\nAlways-include files: {len(ALWAYS_INCLUDE_FILES)}")
        return 0

    sections = []
    for label, d in TARGET_DIRS:
        print(f"Scanning {label}...", file=sys.stderr)
        entries = scan_dir(d)
        sections.append((label, entries))
        print(f"  {len(entries)} types found", file=sys.stderr)

    # Add always-include files that may have been missed
    print("Scanning always-include files...", file=sys.stderr)
    extra = []
    seen_files = set()
    for _, entries in sections:
        for e in entries:
            seen_files.add(e["file"])
    for f in ALWAYS_INCLUDE_FILES:
        if f.exists() and str(f) not in seen_files:
            extra.extend(parse_cs_file(f))
    if extra:
        sections.append(("Always-include (guaranteed coverage)", extra))

    # Build the markdown
    lines = [
        "# S&Box API Signatures — Auto-Scraped Reference",
        "",
        "This file is generated by `agent/scrape_apis.py` from the S&Box engine",
        "source at `C:\\Users\\Shadow\\Documents\\sbox-public-clean\\engine`.",
        "It captures the public API surface (classes, `[Property]` fields, public",
        "methods) of the engine components and systems Lute actually uses, so the",
        "agent doesn't have to re-discover them every session or hallucinate",
        "outdated Garry's Mod APIs.",
        "",
        "**Regenerate after pulling a new engine snapshot:**",
        "```powershell",
        "python agent/scrape_apis.py",
        "```",
        "",
        "**Don't edit this file by hand** — it will be overwritten. Add manual",
        "notes to `AGENTS.md` instead.",
        "",
        "---",
        "",
        "## Contents",
        "",
    ]

    for label, entries in sections:
        lines.append(f"- [{label}](#{label.lower().replace(' ', '-').replace('/', '').replace('(', '').replace(')', '').replace(',', '').replace('—', '').replace('--', '-')})")

    lines.append("")
    lines.append("---")
    lines.append("")

    for label, entries in sections:
        lines.append(render_section(label, entries))
        lines.append("---")
        lines.append("")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"\nWrote {out_path} ({out_path.stat().st_size // 1024} KB)", file=sys.stderr)
    print(f"Sections: {len(sections)}", file=sys.stderr)
    total_types = sum(len(e) for _, e in sections)
    print(f"Total types: {total_types}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
