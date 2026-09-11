"""
context_manager.py — Track and budget context window usage for long sessions.

The agent's context window is finite. Long sessions accumulate tool output,
file reads, and conversation turns that eventually exceed the budget.
This module provides:

  - Context budget tracking: estimate tokens/chars per file read, tool
    output, and conversation turn
  - Context pruning: identify stale or low-value content that can be
    summarized or dropped
  - Context checkpointing: save key findings to persistent memory so they
    survive context compaction

Usage:
    python agent/context_manager.py --budget          # show current usage
    python agent/context_manager.py --track file=LuteMonumentBuilder.cs chars=15000
    python agent/context_manager.py --track tool=collision_probes chars=8000
    python agent/context_manager.py --track turn chars=2000
    python agent/context_manager.py --checkpoint "Gate colliders fixed, all 10 checks pass"
    python agent/context_manager.py --prune            # suggest what to drop
    python agent/context_manager.py --prune --apply     # apply pruning
    python agent/context_manager.py --reset            # clear tracking
"""
import sys, os, json, argparse, datetime

STATE_FILE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    ".devin-context", "context_state.json"
)

# Rough token estimate: ~4 chars per token for English/code
CHARS_PER_TOKEN = 4

# Context budget thresholds (in tokens)
# These are rough — the actual limit depends on the model.
BUDGET_SOFT = 80000     # start pruning suggestions
BUDGET_HARD = 120000    # must prune before continuing
BUDGET_MAX = 160000     # context compaction territory


def load_state():
    """Load context tracking state from disk."""
    if not os.path.exists(STATE_FILE):
        return {
            "entries": [],
            "checkpoints": [],
            "total_chars": 0,
            "session_start": datetime.datetime.now().isoformat(timespec="seconds"),
        }
    with open(STATE_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def save_state(state):
    """Save context tracking state to disk."""
    os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2, ensure_ascii=False)


def estimate_tokens(chars):
    """Rough token estimate from character count."""
    return max(1, chars // CHARS_PER_TOKEN)


def add_entry(state, entry_type, label, chars, details=""):
    """Add a tracked context entry."""
    entry = {
        "type": entry_type,      # file, tool, turn, conversation
        "label": label,           # filename, tool name, turn number
        "chars": chars,
        "tokens": estimate_tokens(chars),
        "details": details,
        "timestamp": datetime.datetime.now().isoformat(timespec="seconds"),
    }
    state["entries"].append(entry)
    state["total_chars"] += chars
    save_state(state)
    return entry


def add_checkpoint(state, summary, category="general"):
    """Save a key finding as a checkpoint. These survive pruning and
    should be re-injected after context compaction."""
    checkpoint = {
        "summary": summary,
        "category": category,  # general, finding, decision, bug, todo
        "timestamp": datetime.datetime.now().isoformat(timespec="seconds"),
    }
    state["checkpoints"].append(checkpoint)
    save_state(state)
    return checkpoint


def get_budget_status(state):
    """Return budget status dict."""
    total_tokens = estimate_tokens(state["total_chars"])
    return {
        "total_chars": state["total_chars"],
        "total_tokens": total_tokens,
        "soft_limit": BUDGET_SOFT,
        "hard_limit": BUDGET_HARD,
        "max_limit": BUDGET_MAX,
        "pct": (total_tokens / BUDGET_MAX) * 100,
        "status": ("ok" if total_tokens < BUDGET_SOFT
                   else "prune" if total_tokens < BUDGET_HARD
                   else "must_prune" if total_tokens < BUDGET_MAX
                   else "critical"),
        "entries": len(state["entries"]),
        "checkpoints": len(state["checkpoints"]),
    }


def suggest_pruning(state):
    """Analyze entries and suggest what to prune. Returns list of
    (entry_index, reason, estimated_savings_tokens)."""
    suggestions = []
    now = datetime.datetime.now()

    # Group entries by type and label to find duplicates/stale reads
    seen_files = {}
    for i, entry in enumerate(state["entries"]):
        age_str = entry.get("timestamp", "")
        label = entry.get("label", "")
        etype = entry.get("type", "")
        tokens = entry.get("tokens", 0)

        # File reads: if the same file was read multiple times, suggest
        # pruning all but the most recent
        if etype == "file":
            if label not in seen_files:
                seen_files[label] = []
            seen_files[label].append(i)

        # Tool outputs older than 30 minutes are likely stale
        if age_str:
            try:
                age = (now - datetime.datetime.fromisoformat(age_str)).total_seconds()
                if age > 1800:  # 30 minutes
                    suggestions.append((i, f"stale ({age/60:.0f}min old)", tokens))
            except (ValueError, TypeError):
                pass

        # Large tool outputs (>2000 tokens) are candidates for summarization
        if etype == "tool" and tokens > 2000:
            suggestions.append((i, f"large tool output ({tokens} tokens)", tokens))

    # Suggest pruning duplicate file reads (keep most recent)
    for label, indices in seen_files.items():
        if len(indices) > 1:
            for idx in indices[:-1]:  # all but the last
                tokens = state["entries"][idx].get("tokens", 0)
                suggestions.append((idx, f"superseded by later read of {label}", tokens))

    # Sort by savings (largest first)
    suggestions.sort(key=lambda x: x[2], reverse=True)
    return suggestions


def apply_pruning(state, suggestions, max_entries=None):
    """Apply pruning suggestions. Removes entries from the tracking list.
    Does NOT actually remove content from the live context — it just updates
    the tracking so the agent knows that content has been 'accounted for'
    and can be safely dropped if the context is compacted."""
    # Deduplicate suggestion indices
    indices_to_prune = sorted(set(s[0] for s in suggestions), reverse=True)
    if max_entries:
        indices_to_prune = indices_to_prune[:max_entries]

    pruned_chars = 0
    pruned_tokens = 0
    for idx in indices_to_prune:
        entry = state["entries"][idx]
        pruned_chars += entry.get("chars", 0)
        pruned_tokens += entry.get("tokens", 0)
        entry["pruned"] = True

    # Recalculate total (exclude pruned entries)
    state["total_chars"] = sum(e.get("chars", 0) for e in state["entries"] if not e.get("pruned"))
    save_state(state)
    return pruned_chars, pruned_tokens, len(indices_to_prune)


def format_budget(budget):
    """Format budget status for display."""
    status_emoji = {
        "ok": "OK", "prune": "PRUNE", "must_prune": "MUST PRUNE",
        "critical": "CRITICAL"
    }
    status = status_emoji.get(budget["status"], "?")
    lines = [
        f"Context Budget: {budget['total_tokens']:,} / {budget['max_limit']:,} tokens ({budget['pct']:.1f}%) — {status}",
        f"  Chars:    {budget['total_chars']:,}",
        f"  Entries:  {budget['entries']} tracked",
        f"  Checkpoints: {budget['checkpoints']} saved",
        f"  Limits:  soft={budget['soft_limit']:,}  hard={budget['hard_limit']:,}  max={budget['max_limit']:,}",
    ]
    return "\n".join(lines)


def format_checkpoints(state):
    """Format checkpoints for display."""
    if not state["checkpoints"]:
        return "  No checkpoints saved."
    lines = []
    for i, cp in enumerate(state["checkpoints"]):
        lines.append(f"  [{i+1}] ({cp['category']}) {cp['summary']}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(
        description="Lute context manager — track and budget context usage")
    parser.add_argument("--budget", action="store_true",
                        help="Show current context budget status")
    parser.add_argument("--track", nargs="*", metavar="KEY=VAL",
                        help="Track a context entry: type=file label=name chars=N")
    parser.add_argument("--checkpoint", metavar="SUMMARY",
                        help="Save a key finding as a checkpoint")
    parser.add_argument("--category", default="general",
                        choices=["general", "finding", "decision", "bug", "todo"],
                        help="Checkpoint category")
    parser.add_argument("--prune", action="store_true",
                        help="Suggest entries to prune")
    parser.add_argument("--apply", action="store_true",
                        help="With --prune: apply the pruning suggestions")
    parser.add_argument("--max-prune", type=int, default=None,
                        help="Max entries to prune in one pass")
    parser.add_argument("--checkpoints", action="store_true",
                        help="List all saved checkpoints")
    parser.add_argument("--reset", action="store_true",
                        help="Clear all tracking (keeps checkpoints)")
    args = parser.parse_args()

    state = load_state()

    if args.reset:
        checkpoints = state.get("checkpoints", [])
        state = {
            "entries": [],
            "checkpoints": checkpoints,
            "total_chars": 0,
            "session_start": datetime.datetime.now().isoformat(timespec="seconds"),
        }
        save_state(state)
        print("Context tracking reset (checkpoints preserved).")
        return

    if args.track:
        # Parse key=value pairs
        kv = {}
        for item in args.track:
            if "=" in item:
                k, v = item.split("=", 1)
                kv[k] = v
        etype = kv.get("type", "unknown")
        label = kv.get("label", kv.get("file", kv.get("tool", kv.get("turn", ""))))
        try:
            chars = int(kv.get("chars", 0))
        except ValueError:
            chars = 0
        details = kv.get("details", "")
        entry = add_entry(state, etype, label, chars, details)
        budget = get_budget_status(state)
        print(f"Tracked: {etype}/{label} — {entry['tokens']:,} tokens ({chars:,} chars)")
        print(format_budget(budget))
        return

    if args.checkpoint:
        cp = add_checkpoint(state, args.checkpoint, category=args.category)
        print(f"Checkpoint saved: [{cp['category']}] {cp['summary']}")
        return

    if args.checkpoints:
        print("=== Saved Checkpoints ===")
        print(format_checkpoints(state))
        return

    if args.prune:
        suggestions = suggest_pruning(state)
        if not suggestions:
            print("No pruning suggestions. Context is clean.")
            return
        print(f"=== Pruning Suggestions ({len(suggestions)} entries) ===")
        total_savings = 0
        for idx, reason, tokens in suggestions:
            entry = state["entries"][idx]
            print(f"  #{idx} [{entry['type']}] {entry['label']} — {reason} ({tokens:,} tokens)")
            total_savings += tokens
        print(f"\nTotal potential savings: {total_savings:,} tokens")

        if args.apply:
            chars, tokens, count = apply_pruning(state, suggestions, args.max_prune)
            print(f"\nPruned {count} entries: freed {tokens:,} tokens ({chars:,} chars)")
            budget = get_budget_status(state)
            print(format_budget(budget))
        return

    if args.budget:
        budget = get_budget_status(state)
        print(format_budget(budget))
        print(f"\nCheckpoints: {budget['checkpoints']} saved")
        if budget["status"] in ("prune", "must_prune", "critical"):
            suggestions = suggest_pruning(state)
            print(f"\nPruning available: {len(suggestions)} entries "
                  f"({sum(s[2] for s in suggestions):,} tokens)")
            print("Run with --prune to see details, --prune --apply to free space.")
        return

    # Default: show budget
    budget = get_budget_status(state)
    print(format_budget(budget))


if __name__ == "__main__":
    main()
