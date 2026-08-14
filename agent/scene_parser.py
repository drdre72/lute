"""Parse Godot .tscn scene files directly in Python — no Godot round-trip needed.
Provides structured scene inspection: node inventory, material coverage, spatial
distribution, duplicate detection, and filtering. Replaces the hanging get_scene_tree RPC.

Usage:
    from scene_parser import SceneParser
    parser = SceneParser("path/to/scene.tscn")
    parser.load()
    
    # Get summary stats
    print(parser.summary())
    
    # Find nodes by pattern
    trees = parser.find(name_pattern="RTree_*", node_type="CSGCylinder3D")
    
    # Material report
    report = parser.material_report()
    
    # Spatial distribution
    regions = parser.spatial_report()
"""
from __future__ import annotations

import re
import os
from typing import Any, Dict, List, Optional, Tuple
from dataclasses import dataclass, field
from fnmatch import fnmatch


@dataclass
class SceneNode:
    name: str
    node_type: str
    parent: str  # "." for root, or path like "TownArea/Terrain"
    line_number: int
    properties: Dict[str, str] = field(default_factory=dict)  # raw string values
    
    @property
    def full_path(self) -> str:
        if self.parent == "." or self.parent == "":
            return self.name
        return f"{self.parent}/{self.name}"
    
    @property
    def position(self) -> Optional[Tuple[float, float, float]]:
        """Extract position from transform property if present."""
        transform = self.properties.get("transform", "")
        if not transform:
            return None
        match = re.match(r'Transform3D\((.+)\)', transform)
        if not match:
            return None
        try:
            vals = [float(v.strip()) for v in match.group(1).split(',')]
        except ValueError:
            return None
        if len(vals) >= 12:
            return (vals[9], vals[10], vals[11])
        return None
    
    @property
    def has_material(self) -> bool:
        if "material_override" in self.properties:
            return True
        if "surface_material_override/0" in self.properties:
            return True
        return False
    
    @property
    def material_type(self) -> str:
        if "surface_material_override/0" in self.properties:
            return "surface_material_override"
        if "material_override" in self.properties:
            return "material_override"
        return "none"
    
    @property
    def material_ref(self) -> str:
        if "surface_material_override/0" in self.properties:
            return self.properties["surface_material_override/0"]
        if "material_override" in self.properties:
            return self.properties["material_override"]
        return ""
    
    @property
    def is_csg(self) -> bool:
        return self.node_type.startswith("CSG")
    
    @property
    def is_mesh_instance(self) -> bool:
        return self.node_type == "MeshInstance3D"


class SceneParser:
    def __init__(self, scene_path: str):
        self.scene_path = scene_path
        self.nodes: List[SceneNode] = []
        self.ext_resources: Dict[str, Dict[str, str]] = {}
        self.sub_resources: Dict[str, Dict[str, str]] = {}
        self.root_name: str = ""
        self._loaded: bool = False
    
    def load(self) -> None:
        """Parse the .tscn file into structured data."""
        if not os.path.exists(self.scene_path):
            raise FileNotFoundError(f"Scene file not found: {self.scene_path}")
        
        with open(self.scene_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        self.nodes.clear()
        self.ext_resources.clear()
        self.sub_resources.clear()
        
        i = 0
        while i < len(lines):
            line = lines[i].strip()
            
            if line.startswith('[ext_resource'):
                info = self._parse_header(line)
                if info:
                    self.ext_resources[info.get('id', '')] = info
                i += 1
                continue
            
            if line.startswith('[sub_resource'):
                info = self._parse_header(line)
                if info:
                    rid = info.get('id', '')
                    props: Dict[str, str] = {}
                    i += 1
                    while i < len(lines):
                        prop_line = lines[i].strip()
                        if prop_line.startswith('[') or prop_line == '':
                            break
                        if '=' in prop_line:
                            k, v = prop_line.split('=', 1)
                            props[k.strip()] = v.strip()
                        i += 1
                    self.sub_resources[rid] = {'type': info.get('type', ''), 'props': props}
                continue
            
            if line.startswith('[node '):
                info = self._parse_header(line)
                if info:
                    node = SceneNode(
                        name=info.get('name', ''),
                        node_type=info.get('type', ''),
                        parent=info.get('parent', '.'),
                        line_number=i + 1,
                    )
                    if node.parent == '.' or node.parent == '':
                        self.root_name = node.name
                    
                    i += 1
                    while i < len(lines):
                        prop_line = lines[i].strip()
                        if prop_line.startswith('[') or prop_line == '':
                            break
                        if '=' in prop_line:
                            k, v = prop_line.split('=', 1)
                            node.properties[k.strip()] = v.strip()
                        i += 1
                    
                    self.nodes.append(node)
                continue
            
            i += 1
        
        self._loaded = True
    
    def _parse_header(self, line: str) -> Dict[str, str]:
        result = {}
        for match in re.finditer(r'(\w+)="([^"]*)"', line):
            result[match.group(1)] = match.group(2)
        type_match = re.search(r'type=(\w+)', line)
        if type_match and 'type' not in result:
            result['type'] = type_match.group(1)
        return result
    
    def summary(self) -> Dict[str, Any]:
        by_type: Dict[str, int] = {}
        by_parent: Dict[str, int] = {}
        total_with_material = 0
        total_without_material = 0
        renderable_with_material = 0
        renderable_without_material = 0
        
        renderable_types = {'MeshInstance3D', 'CSGBox3D', 'CSGCylinder3D', 
                           'CSGSphere3D', 'CSGTorus3D', 'CSGPolygon3D',
                           'CSGCombiner3D'}
        
        for node in self.nodes:
            by_type[node.node_type] = by_type.get(node.node_type, 0) + 1
            top_parent = node.parent.split('/')[0] if node.parent != '.' else node.name
            by_parent[top_parent] = by_parent.get(top_parent, 0) + 1
            
            if node.node_type in renderable_types:
                if node.has_material:
                    renderable_with_material += 1
                else:
                    renderable_without_material += 1
            
            if node.has_material:
                total_with_material += 1
            else:
                total_without_material += 1
        
        return {
            'scene_file': self.scene_path,
            'root_name': self.root_name,
            'total_nodes': len(self.nodes),
            'by_type': dict(sorted(by_type.items(), key=lambda x: -x[1])),
            'by_parent': dict(sorted(by_parent.items(), key=lambda x: -x[1])),
            'material_coverage': {
                'with_material': total_with_material,
                'without_material': total_without_material,
                'pct': round(total_with_material / max(1, len(self.nodes)) * 100, 1),
            },
            'renderable_material_coverage': {
                'with_material': renderable_with_material,
                'without_material': renderable_without_material,
                'pct': round(renderable_with_material / max(1, renderable_with_material + renderable_without_material) * 100, 1),
            },
        }
    
    def find(
        self,
        name_pattern: Optional[str] = None,
        node_type: Optional[str] = None,
        parent_prefix: Optional[str] = None,
        has_material: Optional[bool] = None,
        limit: int = 100,
    ) -> List[SceneNode]:
        results = []
        for node in self.nodes:
            if name_pattern and not fnmatch(node.name, name_pattern):
                continue
            if node_type and node.node_type != node_type:
                continue
            if parent_prefix and not node.full_path.startswith(parent_prefix):
                continue
            if has_material is not None and node.has_material != has_material:
                continue
            results.append(node)
            if len(results) >= limit:
                break
        return results
    
    def material_report(self, limit: int = 50) -> Dict[str, Any]:
        renderable_types = {'MeshInstance3D', 'CSGBox3D', 'CSGCylinder3D', 
                           'CSGSphere3D', 'CSGTorus3D', 'CSGPolygon3D'}
        
        with_mat = []
        without_mat = []
        
        for node in self.nodes:
            if node.node_type not in renderable_types:
                continue
            if node.has_material:
                with_mat.append(node)
            else:
                without_mat.append(node)
        
        missing_by_parent: Dict[str, int] = {}
        for node in without_mat:
            top = node.parent.split('/')[0] if node.parent != '.' else 'root'
            missing_by_parent[top] = missing_by_parent.get(top, 0) + 1
        
        missing_by_prefix: Dict[str, int] = {}
        for node in without_mat:
            prefix = re.split(r'[_\d]', node.name)[0] if node.name else 'unknown'
            missing_by_prefix[prefix] = missing_by_prefix.get(prefix, 0) + 1
        
        return {
            'renderable_total': len(with_mat) + len(without_mat),
            'with_material': len(with_mat),
            'without_material': len(without_mat),
            'coverage_pct': round(len(with_mat) / max(1, len(with_mat) + len(without_mat)) * 100, 1),
            'missing_by_parent': dict(sorted(missing_by_parent.items(), key=lambda x: -x[1])),
            'missing_by_prefix': dict(sorted(missing_by_prefix.items(), key=lambda x: -x[1])),
            'missing_samples': [
                {'name': n.name, 'type': n.node_type, 'path': n.full_path, 'line': n.line_number}
                for n in without_mat[:limit]
            ],
        }
    
    def spatial_report(self) -> Dict[str, Any]:
        regions: Dict[str, Dict[str, Any]] = {}
        
        for node in self.nodes:
            pos = node.position
            if pos is None:
                continue
            
            top = node.parent.split('/')[0] if node.parent != '.' else self.root_name
            if top not in regions:
                regions[top] = {
                    'node_count': 0,
                    'min_x': float('inf'), 'max_x': float('-inf'),
                    'min_y': float('inf'), 'max_y': float('-inf'),
                    'min_z': float('inf'), 'max_z': float('-inf'),
                }
            
            r = regions[top]
            r['node_count'] += 1
            r['min_x'] = min(r['min_x'], pos[0])
            r['max_x'] = max(r['max_x'], pos[0])
            r['min_y'] = min(r['min_y'], pos[1])
            r['max_y'] = max(r['max_y'], pos[1])
            r['min_z'] = min(r['min_z'], pos[2])
            r['max_z'] = max(r['max_z'], pos[2])
        
        for r in regions.values():
            if r['node_count'] == 0:
                continue
            r['bounds_x'] = [round(r['min_x'], 1), round(r['max_x'], 1)]
            r['bounds_y'] = [round(r['min_y'], 1), round(r['max_y'], 1)]
            r['bounds_z'] = [round(r['min_z'], 1), round(r['max_z'], 1)]
            r['size'] = [
                round(r['max_x'] - r['min_x'], 1),
                round(r['max_y'] - r['min_y'], 1),
                round(r['max_z'] - r['min_z'], 1),
            ]
            del r['min_x'], r['max_x'], r['min_y'], r['max_y'], r['min_z'], r['max_z']
        
        return regions
    
    def duplicate_report(self) -> Dict[str, Any]:
        by_name_type: Dict[str, List[SceneNode]] = {}
        
        for node in self.nodes:
            key = f"{node.name}_{node.node_type}"
            if key not in by_name_type:
                by_name_type[key] = []
            by_name_type[key].append(node)
        
        duplicates = []
        for key, nodes in by_name_type.items():
            if len(nodes) > 1:
                duplicates.append({
                    'name': nodes[0].name,
                    'type': nodes[0].node_type,
                    'count': len(nodes),
                    'paths': [n.full_path for n in nodes[:5]],
                })
        
        csg_trees = {}
        mesh_trees = {}
        for node in self.nodes:
            if node.name.startswith(('RTree_', 'PTree_', 'DTree_')):
                parts = node.name.split('_')
                if len(parts) >= 2:
                    tree_id = f"{parts[0]}_{parts[1]}"
                    if node.is_csg:
                        csg_trees[tree_id] = csg_trees.get(tree_id, 0) + 1
                    elif node.is_mesh_instance:
                        mesh_trees[tree_id] = mesh_trees.get(tree_id, 0) + 1
        
        dual_trees = set(csg_trees.keys()) & set(mesh_trees.keys())
        
        return {
            'exact_duplicates': sorted(duplicates, key=lambda x: -x['count'])[:20],
            'csg_and_mesh_trees': {
                'trees_with_both_csg_and_mesh': len(dual_trees),
                'csg_tree_ids': sorted(list(dual_trees))[:20],
                'total_csg_tree_parts': sum(csg_trees.values()),
                'total_mesh_tree_parts': sum(mesh_trees.values()),
            },
        }
    
    def tree_report(self) -> Dict[str, Any]:
        csg_parts = []
        mesh_parts = []
        
        for node in self.nodes:
            if node.name.startswith(('RTree_', 'PTree_', 'DTree_')):
                if node.is_csg:
                    csg_parts.append(node)
                elif node.is_mesh_instance:
                    mesh_parts.append(node)
        
        csg_ids = set()
        mesh_ids = set()
        for n in csg_parts:
            parts = n.name.split('_')
            if len(parts) >= 2:
                csg_ids.add(f"{parts[0]}_{parts[1]}")
        for n in mesh_parts:
            parts = n.name.split('_')
            if len(parts) >= 2:
                mesh_ids.add(f"{parts[0]}_{parts[1]}")
        
        csg_with_mat = sum(1 for n in csg_parts if n.has_material)
        mesh_with_mat = sum(1 for n in mesh_parts if n.has_material)
        
        return {
            'csg_trees': {
                'unique_tree_ids': len(csg_ids),
                'total_parts': len(csg_parts),
                'with_material': csg_with_mat,
                'without_material': len(csg_parts) - csg_with_mat,
            },
            'mesh_trees': {
                'unique_tree_ids': len(mesh_ids),
                'total_parts': len(mesh_parts),
                'with_material': mesh_with_mat,
                'without_material': len(mesh_parts) - mesh_with_mat,
            },
            'trees_with_both': sorted(list(csg_ids & mesh_ids)),
            'needs_cleanup': len(csg_parts) > 0,
        }
    
    def get_node_paths(self, name_pattern: str, node_type: Optional[str] = None) -> List[str]:
        results = []
        for node in self.nodes:
            if not fnmatch(node.name, name_pattern):
                continue
            if node_type and node.node_type != node_type:
                continue
            results.append(node.full_path)
        return results
    
    def print_report(self) -> None:
        s = self.summary()
        print(f"=== Scene Report: {s['scene_file']} ===")
        print(f"Root: {s['root_name']}")
        print(f"Total nodes: {s['total_nodes']}")
        print()
        print("By type (top 15):")
        for t, c in list(s['by_type'].items())[:15]:
            print(f"  {t:30s} {c:5d}")
        print()
        print("By parent region (top 10):")
        for p, c in list(s['by_parent'].items())[:10]:
            print(f"  {p:30s} {c:5d}")
        print()
        mc = s['material_coverage']
        print(f"Material coverage: {mc['with_material']}/{mc['with_material']+mc['without_material']} ({mc['pct']}%)")
        rc = s['renderable_material_coverage']
        print(f"Renderable coverage: {rc['with_material']}/{rc['with_material']+rc['without_material']} ({rc['pct']}%)")
        print()
        
        mr = self.material_report()
        print("Missing materials by parent:")
        for p, c in list(mr['missing_by_parent'].items())[:10]:
            print(f"  {p:30s} {c:5d}")
        print()
        print("Missing materials by name prefix:")
        for p, c in list(mr['missing_by_prefix'].items())[:10]:
            print(f"  {p:30s} {c:5d}")
        print()
        
        tr = self.tree_report()
        print("Tree report:")
        print(f"  CSG trees:  {tr['csg_trees']['unique_tree_ids']} IDs, {tr['csg_trees']['total_parts']} parts, {tr['csg_trees']['with_material']} with material")
        print(f"  Mesh trees: {tr['mesh_trees']['unique_tree_ids']} IDs, {tr['mesh_trees']['total_parts']} parts, {tr['mesh_trees']['with_material']} with material")
        print(f"  Trees with both CSG+Mesh: {len(tr['trees_with_both'])}")
        print(f"  Needs cleanup: {tr['needs_cleanup']}")
        print()
        
        dr = self.duplicate_report()
        if dr['csg_and_mesh_trees']['trees_with_both_csg_and_mesh'] > 0:
            print(f"DUPLICATE: {dr['csg_and_mesh_trees']['trees_with_both_csg_and_mesh']} trees have both CSG and Mesh versions")
            print(f"  CSG parts to delete: {dr['csg_and_mesh_trees']['total_csg_tree_parts']}")
        
        sr = self.spatial_report()
        print()
        print("Spatial distribution:")
        for region, data in sr.items():
            print(f"  {region:30s} {data['node_count']:5d} nodes, bounds: x={data.get('bounds_x','?')} y={data.get('bounds_y','?')} z={data.get('bounds_z','?')}")


if __name__ == '__main__':
    import sys
    path = sys.argv[1] if len(sys.argv) > 1 else "/Users/andrebaker/periphery/scenes/main_nave.tscn"
    parser = SceneParser(path)
    parser.load()
    parser.print_report()
