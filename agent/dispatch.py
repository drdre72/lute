#!/usr/bin/env python3
"""Task dispatcher CLI — push tasks to the task queue, check status, view results.

Usage:
    python dispatch.py add --type delete_nodes --spec '{"name_pattern":"RTree_*","types":["CSGCylinder3D"]}' --priority high
    python dispatch.py add --type set_materials --spec '{"name_pattern":"*Trunk","material":{...}}'
    python dispatch.py status
    python dispatch.py results
    python dispatch.py clear
"""
import argparse
import json
import os
import sys
import time
from pathlib import Path

QUEUE_PATH = Path(__file__).parent / "task_queue.json"


def load_queue() -> dict:
    if QUEUE_PATH.exists():
        return json.loads(QUEUE_PATH.read_text())
    return {"tasks": []}


def save_queue(queue: dict) -> None:
    QUEUE_PATH.write_text(json.dumps(queue, indent=2))


def cmd_add(args):
    queue = load_queue()
    spec = json.loads(args.spec) if args.spec else {}
    task = {
        "id": f"task_{int(time.time())}_{len(queue['tasks'])}",
        "type": args.type,
        "spec": spec,
        "status": "pending",
        "priority": args.priority,
        "created_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "result": None,
    }
    queue["tasks"].append(task)
    save_queue(queue)
    print(f"Added task: {task['id']}")
    print(f"  Type: {task['type']}")
    print(f"  Priority: {task['priority']}")
    print(f"  Spec: {json.dumps(spec)[:200]}")


def cmd_status(args):
    queue = load_queue()
    tasks = queue["tasks"]
    if not tasks:
        print("Queue is empty.")
        return
    
    pending = [t for t in tasks if t["status"] == "pending"]
    in_progress = [t for t in tasks if t["status"] == "in_progress"]
    completed = [t for t in tasks if t["status"] == "completed"]
    failed = [t for t in tasks if t["status"] == "failed"]
    
    print(f"Task Queue: {len(tasks)} total")
    print(f"  Pending: {len(pending)}")
    print(f"  In Progress: {len(in_progress)}")
    print(f"  Completed: {len(completed)}")
    print(f"  Failed: {len(failed)}")
    print()
    
    if pending:
        print("Pending tasks:")
        for t in pending:
            print(f"  [{t['priority']}] {t['id']}: {t['type']} — {json.dumps(t['spec'])[:80]}")
    
    if in_progress:
        print("\nIn progress:")
        for t in in_progress:
            print(f"  {t['id']}: {t['type']}")
    
    if failed:
        print("\nFailed:")
        for t in failed:
            print(f"  {t['id']}: {t['type']} — {t.get('result', 'no result')}")


def cmd_results(args):
    queue = load_queue()
    completed = [t for t in queue["tasks"] if t["status"] in ("completed", "failed")]
    if not completed:
        print("No completed tasks.")
        return
    
    for t in completed[-10:]:  # Last 10
        print(f"\n{t['id']} [{t['status']}] {t['type']}")
        result = t.get("result", "")
        if isinstance(result, str):
            print(f"  Result: {result[:200]}")
        else:
            print(f"  Result: {json.dumps(result)[:200]}")


def cmd_clear(args):
    queue = load_queue()
    before = len(queue["tasks"])
    queue["tasks"] = [t for t in queue["tasks"] if t["status"] not in ("completed", "failed")]
    after = len(queue["tasks"])
    save_queue(queue)
    print(f"Cleared {before - after} completed/failed tasks. {after} remaining.")


def cmd_remove(args):
    queue = load_queue()
    before = len(queue["tasks"])
    queue["tasks"] = [t for t in queue["tasks"] if t["id"] != args.task_id]
    after = len(queue["tasks"])
    save_queue(queue)
    if before == after:
        print(f"Task {args.task_id} not found.")
    else:
        print(f"Removed task {args.task_id}")


def main():
    parser = argparse.ArgumentParser(description="Task queue dispatcher")
    sub = parser.add_subparsers(dest="command")
    
    add_p = sub.add_parser("add", help="Add a task to the queue")
    add_p.add_argument("--type", required=True, help="Task type: delete_nodes, set_materials, add_grass, screenshot, run_script, scene_report, cleanup_duplicates")
    add_p.add_argument("--spec", default="{}", help="JSON spec for the task")
    add_p.add_argument("--priority", default="medium", choices=["high", "medium", "low"])
    add_p.set_defaults(func=cmd_add)
    
    status_p = sub.add_parser("status", help="Show queue status")
    status_p.set_defaults(func=cmd_status)
    
    results_p = sub.add_parser("results", help="Show completed task results")
    results_p.set_defaults(func=cmd_results)
    
    clear_p = sub.add_parser("clear", help="Clear completed/failed tasks")
    clear_p.set_defaults(func=cmd_clear)
    
    remove_p = sub.add_parser("remove", help="Remove a specific task")
    remove_p.add_argument("task_id", help="Task ID to remove")
    remove_p.set_defaults(func=cmd_remove)
    
    args = parser.parse_args()
    if not args.command:
        parser.print_help()
        sys.exit(1)
    
    args.func(args)


if __name__ == "__main__":
    main()
