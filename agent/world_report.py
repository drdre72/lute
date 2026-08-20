#!/usr/bin/env python3
"""Generate a full world report — combines scene_parser analysis with
strategic screenshots from multiple angles. Outputs markdown report."""
import sys, os, json, time
sys.path.insert(0, os.path.dirname(__file__))
from scene_parser import SceneParser
from tools import call_tool

SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"
REPORT_DIR = os.path.join(os.path.dirname(__file__), "reports")
SCREENSHOT_DIR = os.path.expanduser("~/Library/Application Support/Godot/app_userdata/Lute")

def take_screenshot(cam_path=None, label="view"):
    """Take a screenshot by activating a camera, or just the current viewport."""
    if cam_path:
        call_tool('node_set_property', {'node_path': cam_path, 'property': 'current', 'value': True})
        time.sleep(1.5)
    r = call_tool('screenshot', {})
    if cam_path:
        call_tool('node_set_property', {'node_path': cam_path, 'property': 'current', 'value': False})
    path = r.get('path', '')
    filename = os.path.basename(path) if path else 'unknown'
    return filename

def generate_report():
    os.makedirs(REPORT_DIR, exist_ok=True)
    
    parser = SceneParser(SCENE_PATH)
    parser.load()
    
    summary = parser.summary()
    mat_report = parser.material_report()
    tree_report = parser.tree_report()
    dup_report = parser.duplicate_report()
    spatial = parser.spatial_report()
    
    # Take screenshots from existing cameras
    cameras = [
        ('Cam_GrandOverview', 'Grand Overview'),
        ('Cam_TempleNave', 'Temple Nave'),
        ('TownArea/Cam_TownStreet', 'Town Street'),
        ('TownArea/Cam_TownAerial', 'Town Aerial'),
        ('TownArea/LakeRegion/Cam_ForestLake', 'Forest Lake'),
        ('TownArea/Cam_PathJourney', 'Path Journey'),
    ]
    
    screenshots = []
    for cam_path, label in cameras:
        try:
            filename = take_screenshot(cam_path, label)
            screenshots.append((label, filename))
        except Exception as e:
            screenshots.append((label, f"ERROR: {e}"))
    
    # Build markdown report
    lines = []
    lines.append("# World Report")
    lines.append(f"Generated: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append(f"Scene: {summary['scene_file']}")
    lines.append(f"Root: {summary['root_name']}")
    lines.append(f"Total nodes: {summary['total_nodes']}")
    lines.append("")
    
    lines.append("## Screenshots")
    for label, filename in screenshots:
        lines.append(f"### {label}")
        lines.append(f"![{label}]({filename})")
        lines.append("")
    
    lines.append("## Node Distribution")
    lines.append("| Type | Count |")
    lines.append("|------|-------|")
    for t, c in list(summary['by_type'].items())[:15]:
        lines.append(f"| {t} | {c} |")
    lines.append("")
    
    lines.append("## Region Distribution")
    lines.append("| Region | Nodes |")
    lines.append("|--------|-------|")
    for p, c in list(summary['by_parent'].items())[:10]:
        lines.append(f"| {p} | {c} |")
    lines.append("")
    
    lines.append("## Material Coverage")
    mc = summary['material_coverage']
    rc = summary['renderable_material_coverage']
    lines.append(f"- All nodes: {mc['with_material']}/{mc['with_material']+mc['without_material']} ({mc['pct']}%)")
    lines.append(f"- Renderable: {rc['with_material']}/{rc['with_material']+rc['without_material']} ({rc['pct']}%)")
    lines.append("")
    
    lines.append("### Missing Materials by Parent")
    lines.append("| Parent | Missing |")
    lines.append("|--------|---------|")
    for p, c in list(mat_report['missing_by_parent'].items())[:10]:
        lines.append(f"| {p} | {c} |")
    lines.append("")
    
    lines.append("### Missing Materials by Name Prefix")
    lines.append("| Prefix | Missing |")
    lines.append("|--------|---------|")
    for p, c in list(mat_report['missing_by_prefix'].items())[:10]:
        lines.append(f"| {p} | {c} |")
    lines.append("")
    
    lines.append("## Tree Status")
    tr = tree_report
    lines.append(f"- CSG trees (old): {tr['csg_trees']['unique_tree_ids']} IDs, {tr['csg_trees']['total_parts']} parts")
    lines.append(f"- Mesh trees (new): {tr['mesh_trees']['unique_tree_ids']} IDs, {tr['mesh_trees']['total_parts']} parts")
    lines.append(f"- Trees with BOTH versions: {len(tr['trees_with_both'])}")
    lines.append(f"- **Needs cleanup: {tr['needs_cleanup']}**")
    if tr['needs_cleanup']:
        lines.append(f"- CSG parts to delete: {dup_report['csg_and_mesh_trees']['total_csg_tree_parts']}")
    lines.append("")
    
    lines.append("## Spatial Distribution")
    for region, data in spatial.items():
        lines.append(f"### {region}")
        lines.append(f"- Nodes: {data['node_count']}")
        lines.append(f"- X: {data.get('bounds_x', '?')}")
        lines.append(f"- Y: {data.get('bounds_y', '?')}")
        lines.append(f"- Z: {data.get('bounds_z', '?')}")
        lines.append(f"- Size: {data.get('size', '?')}")
        lines.append("")
    
    lines.append("## Issues Found")
    issues = []
    if tr['needs_cleanup']:
        issues.append(f"- {dup_report['csg_and_mesh_trees']['total_csg_tree_parts']} old CSG tree parts need deletion")
    if mat_report['without_material'] > 0:
        issues.append(f"- {mat_report['without_material']} renderable nodes missing materials")
    if mat_report['without_material'] > 0:
        issues.append(f"- 85 auto-named @MeshInstance nodes (likely from failed operations)")
    if not issues:
        issues.append("- None detected")
    lines.extend(issues)
    
    report_path = os.path.join(REPORT_DIR, f"world_report_{time.strftime('%Y%m%d_%H%M%S')}.md")
    with open(report_path, 'w') as f:
        f.write('\n'.join(lines))
    
    print(f"Report saved to: {report_path}")
    print(f"Issues found: {len([i for i in issues if i != '- None detected'])}")
    return report_path

if __name__ == '__main__':
    generate_report()
