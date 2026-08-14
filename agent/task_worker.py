#!/usr/bin/env python3
"""Persistent task worker — watches task_queue.json and executes pending tasks.

Usage:
    python task_worker.py              # Run continuously (polls every 2s)
    python task_worker.py --once       # Process one task then exit
    python task_worker.py --dry-run    # Show what would be done without executing

Task types:
    delete_nodes       — Delete nodes matching name_pattern + optional type filter
    set_materials      — Apply material to nodes matching pattern
    add_grass          — Scatter grass tufts in a region
    screenshot         — Take screenshots from camera positions
    run_script         — Execute a build_phaseNN.py script
    scene_report       — Generate world report via scene_parser
    cleanup_duplicates — Find and remove duplicate CSG tree nodes
    batch_delete       — Delete nodes from a list of paths
"""
import json
import os
import sys
import time
import logging
from pathlib import Path
from typing import Any, Dict, List

sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool
from scene_parser import SceneParser

QUEUE_PATH = Path(__file__).parent / "task_queue.json"
SCENE_PATH = "/Users/andrebaker/periphery/scenes/main_nave.tscn"
LOG_PATH = Path(__file__).parent / "worker.log"

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(LOG_PATH),
        logging.StreamHandler(),
    ],
)
log = logging.getLogger("task_worker")


def load_queue() -> dict:
    if QUEUE_PATH.exists():
        return json.loads(QUEUE_PATH.read_text())
    return {"tasks": []}


def save_queue(queue: dict) -> None:
    QUEUE_PATH.write_text(json.dumps(queue, indent=2))


def get_next_task(queue: dict) -> dict | None:
    pending = [t for t in queue["tasks"] if t["status"] == "pending"]
    if not pending:
        return None
    # Sort by priority
    priority_order = {"high": 0, "medium": 1, "low": 2}
    pending.sort(key=lambda t: priority_order.get(t.get("priority", "medium"), 1))
    return pending[0]


# ============================================================
# Task handlers
# ============================================================

def handle_delete_nodes(spec: dict) -> dict:
    """Delete nodes matching name_pattern and optional type filter."""
    name_pattern = spec.get("name_pattern", "*")
    types_filter = spec.get("types", [])
    parent_prefix = spec.get("parent_prefix", "")
    
    parser = SceneParser(SCENE_PATH)
    parser.load()
    
    nodes = parser.find(
        name_pattern=name_pattern,
        parent_prefix=parent_prefix if parent_prefix else None,
        limit=10000,
    )
    
    if types_filter:
        nodes = [n for n in nodes if n.node_type in types_filter]
    
    log.info(f"delete_nodes: found {len(nodes)} nodes matching pattern='{name_pattern}' types={types_filter}")
    
    deleted = 0
    errors = 0
    for i, node in enumerate(nodes):
        r = call_tool('node_delete', {'node_path': node.full_path})
        if r.get('ok'):
            deleted += 1
        else:
            errors += 1
            if errors <= 5:
                log.warning(f"  Failed to delete {node.full_path}: {r.get('error', 'unknown')}")
        
        if (i + 1) % 50 == 0:
            log.info(f"  Progress: {i+1}/{len(nodes)} (deleted={deleted}, errors={errors})")
    
    # Save scene after batch delete
    call_tool('scene_save', {})
    
    return {"deleted": deleted, "errors": errors, "total_found": len(nodes)}


def handle_batch_delete(spec: dict) -> dict:
    """Delete nodes from a list of paths."""
    paths = spec.get("paths", [])
    log.info(f"batch_delete: {len(paths)} paths")
    
    deleted = 0
    errors = 0
    for i, path in enumerate(paths):
        r = call_tool('node_delete', {'node_path': path})
        if r.get('ok'):
            deleted += 1
        else:
            errors += 1
        
        if (i + 1) % 50 == 0:
            log.info(f"  Progress: {i+1}/{len(paths)}")
    
    call_tool('scene_save', {})
    return {"deleted": deleted, "errors": errors, "total": len(paths)}


def handle_set_materials(spec: dict) -> dict:
    """Apply material to nodes matching pattern."""
    name_pattern = spec.get("name_pattern", "*")
    material = spec.get("material", {})
    prop_name = spec.get("property", "surface_material_override/0")
    parent_prefix = spec.get("parent_prefix", "")
    
    parser = SceneParser(SCENE_PATH)
    parser.load()
    
    nodes = parser.find(
        name_pattern=name_pattern,
        parent_prefix=parent_prefix if parent_prefix else None,
        limit=10000,
    )
    
    log.info(f"set_materials: {len(nodes)} nodes, pattern='{name_pattern}', prop='{prop_name}'")
    
    updated = 0
    errors = 0
    for i, node in enumerate(nodes):
        r = call_tool('node_set_property', {
            'node_path': node.full_path,
            'property': prop_name,
            'value': material,
        })
        if r.get('ok'):
            updated += 1
        else:
            errors += 1
        
        if (i + 1) % 50 == 0:
            log.info(f"  Progress: {i+1}/{len(nodes)} (updated={updated})")
    
    call_tool('scene_save', {})
    return {"updated": updated, "errors": errors, "total_found": len(nodes)}


def handle_screenshot(spec: dict) -> dict:
    """Take screenshots from specified camera positions."""
    cameras = spec.get("cameras", [])
    results = []
    
    for cam in cameras:
        cam_path = cam.get("camera_path", "")
        if cam_path:
            call_tool('node_set_property', {'node_path': cam_path, 'property': 'current', 'value': True})
            time.sleep(1.5)
            r = call_tool('screenshot', {})
            call_tool('node_set_property', {'node_path': cam_path, 'property': 'current', 'value': False})
            results.append({"camera": cam_path, "path": r.get("path", "")})
    
    return {"screenshots": results}


def handle_run_script(spec: dict) -> dict:
    """Execute a build_phase script."""
    script = spec.get("script", "")
    if not script:
        return {"error": "no script specified"}
    
    script_path = os.path.join(os.path.dirname(__file__), script)
    if not os.path.exists(script_path):
        return {"error": f"script not found: {script_path}"}
    
    script_args = spec.get("args", [])
    cmd = [sys.executable, script_path] + [str(a) for a in script_args]
    
    log.info(f"run_script: {script} args={script_args}")
    
    import subprocess
    result = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        cwd=os.path.dirname(__file__),
        timeout=600,
    )
    
    return {
        "returncode": result.returncode,
        "stdout": result.stdout[-2000:] if result.stdout else "",
        "stderr": result.stderr[-1000:] if result.stderr else "",
    }


def handle_scene_report(spec: dict) -> dict:
    """Generate scene report using scene_parser."""
    parser = SceneParser(SCENE_PATH)
    parser.load()
    
    summary = parser.summary()
    mat_report = parser.material_report()
    tree_report = parser.tree_report()
    dup_report = parser.duplicate_report()
    
    return {
        "summary": summary,
        "material_report": {
            "with_material": mat_report["with_material"],
            "without_material": mat_report["without_material"],
            "coverage_pct": mat_report["coverage_pct"],
            "missing_by_parent": mat_report["missing_by_parent"],
            "missing_by_prefix": mat_report["missing_by_prefix"],
        },
        "tree_report": tree_report,
        "duplicates": dup_report["csg_and_mesh_trees"],
    }


def handle_cleanup_duplicates(spec: dict) -> dict:
    """Find and delete old CSG tree nodes that have MeshInstance3D replacements."""
    dry_run = spec.get("dry_run", False)
    
    parser = SceneParser(SCENE_PATH)
    parser.load()
    
    dup = parser.duplicate_report()
    csg_tree_ids = set(dup["csg_and_mesh_trees"]["csg_tree_ids"])
    
    if not csg_tree_ids:
        return {"message": "No duplicate trees found", "deleted": 0}
    
    # Find all CSG nodes belonging to these tree IDs
    nodes_to_delete = []
    for node in parser.nodes:
        if not node.is_csg:
            continue
        parts = node.name.split('_')
        if len(parts) >= 2:
            tree_id = f"{parts[0]}_{parts[1]}"
            if tree_id in csg_tree_ids:
                nodes_to_delete.append(node)
    
    log.info(f"cleanup_duplicates: {len(nodes_to_delete)} CSG nodes to delete (dry_run={dry_run})")
    
    if dry_run:
        return {
            "dry_run": True,
            "would_delete": len(nodes_to_delete),
            "sample_paths": [n.full_path for n in nodes_to_delete[:10]],
        }
    
    deleted = 0
    errors = 0
    for i, node in enumerate(nodes_to_delete):
        r = call_tool('node_delete', {'node_path': node.full_path})
        if r.get('ok'):
            deleted += 1
        else:
            errors += 1
        
        if (i + 1) % 50 == 0:
            log.info(f"  Progress: {i+1}/{len(nodes_to_delete)}")
    
    call_tool('scene_save', {})
    return {"deleted": deleted, "errors": errors, "total": len(nodes_to_delete)}


# ============================================================
# Task dispatch
# ============================================================

TASK_HANDLERS = {
    "delete_nodes": handle_delete_nodes,
    "batch_delete": handle_batch_delete,
    "set_materials": handle_set_materials,
    "screenshot": handle_screenshot,
    "run_script": handle_run_script,
    "scene_report": handle_scene_report,
    "cleanup_duplicates": handle_cleanup_duplicates,
}


def process_task(task: dict, dry_run: bool = False) -> dict:
    task_type = task.get("type", "")
    spec = task.get("spec", {})
    
    handler = TASK_HANDLERS.get(task_type)
    if not handler:
        return {"error": f"unknown task type: {task_type}"}
    
    if dry_run:
        return {"dry_run": True, "would_execute": task_type, "spec": spec}
    
    return handler(spec)


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Task queue worker")
    parser.add_argument("--once", action="store_true", help="Process one task then exit")
    parser.add_argument("--dry-run", action="store_true", help="Don't actually execute")
    parser.add_argument("--poll-interval", type=float, default=2.0, help="Seconds between polls")
    args = parser.parse_args()
    
    log.info(f"Task worker started (once={args.once}, dry_run={args.dry_run})")
    
    while True:
        queue = load_queue()
        task = get_next_task(queue)
        
        if not task:
            if args.once:
                log.info("No pending tasks. Exiting.")
                break
            time.sleep(args.poll_interval)
            continue
        
        log.info(f"Processing task: {task['id']} type={task['type']}")
        
        # Mark in progress
        for t in queue["tasks"]:
            if t["id"] == task["id"]:
                t["status"] = "in_progress"
                t["started_at"] = time.strftime("%Y-%m-%d %H:%M:%S")
                break
        save_queue(queue)
        
        try:
            result = process_task(task, dry_run=args.dry_run)
            log.info(f"Task {task['id']} completed: {json.dumps(result)[:200]}")
            
            # Update status
            queue = load_queue()
            for t in queue["tasks"]:
                if t["id"] == task["id"]:
                    t["status"] = "completed"
                    t["result"] = result
                    t["completed_at"] = time.strftime("%Y-%m-%d %H:%M:%S")
                    break
            save_queue(queue)
            
        except Exception as e:
            log.error(f"Task {task['id']} failed: {e}")
            queue = load_queue()
            for t in queue["tasks"]:
                if t["id"] == task["id"]:
                    t["status"] = "failed"
                    t["result"] = str(e)
                    t["completed_at"] = time.strftime("%Y-%m-%d %H:%M:%S")
                    break
            save_queue(queue)
        
        if args.once:
            break
        
        time.sleep(0.5)
    
    log.info("Task worker stopped.")


if __name__ == "__main__":
    main()
