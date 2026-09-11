"""
task_queue.py — Persistent task queue for incremental checklist-driven work.

Survives session restarts via JSON state on disk. Designed for the
"work through one item at a time via ./checklist" workflow.

Features:
  - Task states: pending, in_progress, completed, blocked, skipped
  - Task priorities: critical, high, normal, low
  - Task dependencies: a task won't be selected by --next if its
    dependencies aren't completed
  - Checklist sync: read/write MetaList_Verification.txt checkboxes
  - Persistent state: .devin-context/task_queue.json

Usage:
    python agent/task_queue.py --add "Implement Fire spell" --priority high
    python agent/task_queue.py --add "Implement Ice spell" --priority normal --depends 1
    python agent/task_queue.py --next              # select next task to work on
    python agent/task_queue.py --complete           # mark current task done
    python agent/task_queue.py --block "MCP server down"
    python agent/task_queue.py --skip "Deferred to Pillar 4"
    python agent/task_queue.py --list               # show all tasks
    python agent/task_queue.py --list --pending     # only pending tasks
    python agent/task_queue.py --show 3             # show task #3
    python agent/task_queue.py --sync-checklist      # import MetaList checkboxes
    python agent/task_queue.py --export-checklist    # write task state to MetaList
    python agent/task_queue.py --current             # show current in_progress task
    python agent/task_queue.py --reset              # clear all tasks (use with care)
"""
import sys, os, json, argparse, re, datetime

STATE_FILE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    ".devin-context", "task_queue.json"
)

CHECKLIST_FILE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "MetaList_Verification.txt"
)

# Priority order: lower number = higher priority
PRIORITY_ORDER = {"critical": 0, "high": 1, "normal": 2, "low": 3}
VALID_STATES = {"pending", "in_progress", "completed", "blocked", "skipped"}
VALID_PRIORITIES = set(PRIORITY_ORDER.keys())


def load_state():
    """Load task queue state from disk. Returns dict with tasks list and
    current_task id."""
    if not os.path.exists(STATE_FILE):
        return {"tasks": [], "current_task": None, "next_id": 1}
    with open(STATE_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def save_state(state):
    """Save task queue state to disk."""
    os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2, ensure_ascii=False)


def add_task(state, description, priority="normal", depends=None, notes=""):
    """Add a new task to the queue. Returns the task dict."""
    task_id = state["next_id"]
    state["next_id"] = task_id + 1
    task = {
        "id": task_id,
        "description": description,
        "priority": priority,
        "state": "pending",
        "depends": depends or [],
        "notes": notes,
        "created": datetime.datetime.now().isoformat(timespec="seconds"),
        "updated": None,
        "block_reason": None,
    }
    state["tasks"].append(task)
    save_state(state)
    return task


def get_next_task(state):
    """Select the next task to work on. Considers priority and dependencies.
    Returns the task dict or None."""
    # Already have an in_progress task?
    for t in state["tasks"]:
        if t["state"] == "in_progress":
            return t

    # Find eligible pending tasks (dependencies satisfied)
    completed_ids = {t["id"] for t in state["tasks"] if t["state"] == "completed"}
    eligible = []
    for t in state["tasks"]:
        if t["state"] != "pending":
            continue
        deps_met = all(d in completed_ids for d in t["depends"])
        if deps_met:
            eligible.append(t)

    if not eligible:
        return None

    # Sort by priority (critical first), then by id (oldest first)
    eligible.sort(key=lambda t: (PRIORITY_ORDER.get(t["priority"], 2), t["id"]))
    return eligible[0]


def start_task(state, task):
    """Mark a task as in_progress and set it as current."""
    # Clear any other in_progress tasks (shouldn't happen, but safety)
    for t in state["tasks"]:
        if t["state"] == "in_progress" and t["id"] != task["id"]:
            t["state"] = "pending"
            t["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
    task["state"] = "in_progress"
    task["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
    state["current_task"] = task["id"]
    save_state(state)


def complete_task(state, task_id=None):
    """Mark a task as completed. If task_id is None, complete the current task."""
    if task_id is None:
        task_id = state.get("current_task")
    if task_id is None:
        return None
    for t in state["tasks"]:
        if t["id"] == task_id:
            t["state"] = "completed"
            t["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
            state["current_task"] = None
            save_state(state)
            return t
    return None


def block_task(state, reason, task_id=None):
    """Mark a task as blocked with a reason."""
    if task_id is None:
        task_id = state.get("current_task")
    if task_id is None:
        return None
    for t in state["tasks"]:
        if t["id"] == task_id:
            t["state"] = "blocked"
            t["block_reason"] = reason
            t["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
            state["current_task"] = None
            save_state(state)
            return t
    return None


def skip_task(state, reason, task_id=None):
    """Mark a task as skipped with a reason."""
    if task_id is None:
        task_id = state.get("current_task")
    if task_id is None:
        return None
    for t in state["tasks"]:
        if t["id"] == task_id:
            t["state"] = "skipped"
            t["block_reason"] = reason
            t["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
            state["current_task"] = None
            save_state(state)
            return t
    return None


def unblock_task(state, task_id):
    """Move a blocked task back to pending."""
    for t in state["tasks"]:
        if t["id"] == task_id:
            t["state"] = "pending"
            t["block_reason"] = None
            t["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
            save_state(state)
            return t
    return None


def format_task(task, compact=False):
    """Format a task for display. Returns a string."""
    # Sanitize non-ASCII for Windows console (cp1252)
    def safe(s):
        return s.encode("ascii", "replace").decode("ascii")
    if compact:
        return f"  [{task['state']:12s}] #{task['id']:3d} ({task['priority']:8s}) {safe(task['description'])}"
    lines = [
        f"Task #{task['id']}: {safe(task['description'])}",
        f"  State:    {task['state']}",
        f"  Priority: {task['priority']}",
    ]
    if task["depends"]:
        dep_descs = []
        for d in task["depends"]:
            dep_task = next((t for t in load_state()["tasks"] if t["id"] == d), None)
            if dep_task:
                dep_descs.append(f"#{d} ({dep_task['state']})")
            else:
                dep_descs.append(f"#{d} (missing)")
        lines.append(f"  Depends:  {', '.join(dep_descs)}")
    if task["notes"]:
        lines.append(f"  Notes:    {task['notes']}")
    if task["block_reason"]:
        lines.append(f"  Blocked:  {task['block_reason']}")
    lines.append(f"  Created:  {task['created']}")
    if task["updated"]:
        lines.append(f"  Updated:  {task['updated']}")
    return "\n".join(lines)


def sync_from_checklist(state):
    """Import tasks from MetaList_Verification.txt checkboxes.
    Lines like '- [ ] description' become pending tasks.
    Lines like '- [x] description' become completed tasks.
    Section headers (## ...) set the current section for task notes."""
    if not os.path.exists(CHECKLIST_FILE):
        print(f"Checklist not found: {CHECKLIST_FILE}")
        return 0

    with open(CHECKLIST_FILE, "r", encoding="utf-8") as f:
        lines = f.readlines()

    existing_descs = {t["description"] for t in state["tasks"]}
    imported = 0
    current_section = ""

    for line in lines:
        line = line.rstrip("\n")
        # Section header
        if line.startswith("## "):
            current_section = line[3:].strip()
            continue
        # Checkbox line
        m = re.match(r"^- \[([ x])\]\s+(.+)$", line)
        if not m:
            continue
        checked = m.group(1) == "x"
        desc = m.group(2).strip()
        # Skip if already imported
        if desc in existing_descs:
            continue
        notes = f"[{current_section}]" if current_section else ""
        task = add_task(state, desc, priority="normal", notes=notes)
        if checked:
            task["state"] = "completed"
            task["updated"] = datetime.datetime.now().isoformat(timespec="seconds")
        existing_descs.add(desc)
        imported += 1

    save_state(state)
    return imported


def export_to_checklist(state):
    """Write task states back to MetaList_Verification.txt checkboxes.
    Updates - [ ] and - [x] markers based on task completion state.
    Preserves section headers and non-checkbox lines."""
    if not os.path.exists(CHECKLIST_FILE):
        print(f"Checklist not found: {CHECKLIST_FILE}")
        return

    with open(CHECKLIST_FILE, "r", encoding="utf-8") as f:
        lines = f.readlines()

    # Build a lookup: description -> state
    task_states = {}
    for t in state["tasks"]:
        task_states[t["description"]] = t["state"]

    updated_lines = []
    for line in lines:
        m = re.match(r"^(- \[)([ x])(\]\s+)(.+)$", line.rstrip("\n"))
        if m:
            prefix, _, sep, desc = m.groups()
            desc = desc.strip()
            if desc in task_states:
                tstate = task_states[desc]
                if tstate == "completed":
                    marker = "x"
                elif tstate == "skipped":
                    marker = "-"  # skipped = dash
                else:
                    marker = " "  # pending/in_progress/blocked = unchecked
                updated_lines.append(f"{prefix}{marker}{sep}{desc}\n")
            else:
                updated_lines.append(line)
        else:
            updated_lines.append(line)

    with open(CHECKLIST_FILE, "w", encoding="utf-8") as f:
        f.writelines(updated_lines)


def print_summary(state):
    """Print a summary of the queue state."""
    counts = {}
    for t in state["tasks"]:
        counts[t["state"]] = counts.get(t["state"], 0) + 1
    total = len(state["tasks"])
    print(f"Tasks: {total} total")
    for s in ["pending", "in_progress", "completed", "blocked", "skipped"]:
        if s in counts:
            print(f"  {s:12s}: {counts[s]}")
    current = state.get("current_task")
    if current:
        t = next((t for t in state["tasks"] if t["id"] == current), None)
        if t:
            print(f"\nCurrent: #{t['id']} — {t['description']}")


def main():
    parser = argparse.ArgumentParser(
        description="Lute task queue — persistent checklist-driven task management")
    parser.add_argument("--add", metavar="DESC", help="Add a new task")
    parser.add_argument("--priority", default="normal",
                        choices=VALID_PRIORITIES, help="Task priority (default: normal)")
    parser.add_argument("--depends", type=int, nargs="*", default=[],
                        help="Task IDs this task depends on")
    parser.add_argument("--notes", default="", help="Additional notes for the task")
    parser.add_argument("--next", action="store_true",
                        help="Select and start the next eligible task")
    parser.add_argument("--complete", action="store_true",
                        help="Mark the current task as completed")
    parser.add_argument("--block", metavar="REASON",
                        help="Mark current task as blocked (with reason)")
    parser.add_argument("--skip", metavar="REASON",
                        help="Mark current task as skipped (with reason)")
    parser.add_argument("--unblock", type=int, metavar="ID",
                        help="Move a blocked task back to pending")
    parser.add_argument("--list", action="store_true", help="List all tasks")
    parser.add_argument("--pending", action="store_true",
                        help="With --list: only show pending/blocked tasks")
    parser.add_argument("--show", type=int, metavar="ID", help="Show details of a task")
    parser.add_argument("--current", action="store_true",
                        help="Show the current in_progress task")
    parser.add_argument("--sync-checklist", action="store_true",
                        help="Import tasks from MetaList_Verification.txt")
    parser.add_argument("--export-checklist", action="store_true",
                        help="Export task states to MetaList_Verification.txt")
    parser.add_argument("--reset", action="store_true",
                        help="Clear all tasks (use with care)")
    args = parser.parse_args()

    state = load_state()

    if args.reset:
        state = {"tasks": [], "current_task": None, "next_id": 1}
        save_state(state)
        print("Task queue reset.")
        return

    if args.sync_checklist:
        imported = sync_from_checklist(state)
        print(f"Imported {imported} tasks from MetaList_Verification.txt")
        print_summary(state)
        return

    if args.export_checklist:
        export_to_checklist(state)
        print("Exported task states to MetaList_Verification.txt")
        return

    if args.add:
        task = add_task(state, args.add, priority=args.priority,
                        depends=args.depends, notes=args.notes)
        print(f"Added task #{task['id']}: {args.add}")
        print(f"  Priority: {args.priority}")
        if args.depends:
            print(f"  Depends:  {args.depends}")
        return

    if args.next:
        task = get_next_task(state)
        if not task:
            print("No eligible tasks. All pending tasks have unmet dependencies, "
                  "or the queue is empty.")
            print_summary(state)
            return
        start_task(state, task)
        print("=== NEXT TASK ===")
        print(format_task(task))
        return

    if args.complete:
        task = complete_task(state)
        if task:
            print(f"Completed: #{task['id']} — {task['description']}")
            # Show next task
            next_task = get_next_task(state)
            if next_task:
                print(f"\nNext up: #{next_task['id']} — {next_task['description']} "
                      f"({next_task['priority']})")
        else:
            print("No current task to complete.")
        return

    if args.block:
        task = block_task(state, args.block)
        if task:
            print(f"Blocked: #{task['id']} — {task['description']}")
            print(f"  Reason: {args.block}")
        else:
            print("No current task to block.")
        return

    if args.skip:
        task = skip_task(state, args.skip)
        if task:
            print(f"Skipped: #{task['id']} — {task['description']}")
            print(f"  Reason: {args.skip}")
        else:
            print("No current task to skip.")
        return

    if args.unblock:
        task = unblock_task(state, args.unblock)
        if task:
            print(f"Unblocked: #{task['id']} — {task['description']}")
        else:
            print(f"Task #{args.unblock} not found or not blocked.")
        return

    if args.show:
        task = next((t for t in state["tasks"] if t["id"] == args.show), None)
        if task:
            print(format_task(task))
        else:
            print(f"Task #{args.show} not found.")
        return

    if args.current:
        current_id = state.get("current_task")
        if current_id is None:
            print("No task currently in progress.")
            print_summary(state)
            return
        task = next((t for t in state["tasks"] if t["id"] == current_id), None)
        if task:
            print("=== CURRENT TASK ===")
            print(format_task(task))
        else:
            print(f"Current task #{current_id} not found in queue.")
        return

    if args.list:
        if args.pending:
            tasks = [t for t in state["tasks"]
                     if t["state"] in ("pending", "blocked", "in_progress")]
        else:
            tasks = state["tasks"]
        if not tasks:
            print("No tasks in queue.")
            return
        print(f"{'State':14s} {'ID':>4s}  {'Priority':8s}  Description")
        print(f"{'-'*14} {'-'*4}  {'-'*8}  {'-'*40}")
        for t in tasks:
            print(format_task(t, compact=True))
        print()
        print_summary(state)
        return

    # Default: show summary
    print_summary(state)


if __name__ == "__main__":
    main()
