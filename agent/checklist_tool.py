"""A simple on-disk checklist tool for breaking large multi-step build tasks
into single-item turns. Intended for local models with small context windows
that struggle to hold an entire multi-part spec in their reasoning at once:
the model calls checklist_next() to get exactly one small step, executes it,
then checklist_complete() before asking for the next one.

Checklist format on disk is a plain markdown checkbox list:
    - [ ] incomplete item text
    - [x] completed item text
Blank lines and any leading '#' comment/heading lines are preserved as-is
and ignored by the parser.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List

PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CHECKLIST_PATH = PROJECT_ROOT / "MetaList.txt"


def _resolve_path(args: Dict[str, Any]) -> Path:
    path = args.get("path")
    if path:
        p = Path(path)
        return p if p.is_absolute() else PROJECT_ROOT / path
    return DEFAULT_CHECKLIST_PATH


def _read_lines(path: Path) -> List[str]:
    if not path.exists():
        return []
    return path.read_text(encoding="utf-8").splitlines()


def _write_lines(path: Path, lines: List[str]) -> None:
    path.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8")


def _is_item_line(line: str, checked: bool) -> bool:
    marker = "- [x] " if checked else "- [ ] "
    return line.strip().startswith(marker) or line.startswith(marker)


def checklist_next(args: Dict[str, Any] = None) -> Dict[str, Any]:
    """Return the first unchecked ('- [ ] ...') item in the checklist file,
    along with its line-relative index, or {"done": true} if none remain.

    If completed_index is provided, mark that item as done before searching for
    the next one. This allows a single call to replace the previous two-call
    pattern (checklist_complete then checklist_next)."""
    args = args or {}
    path = _resolve_path(args)
    completed_index = args.get("completed_index")

    # Optional: mark a just-finished item in the same call.
    if completed_index is not None:
        _mark_index_complete(path, completed_index)

    lines = _read_lines(path)
    if not lines:
        return {"error": f"checklist file not found or empty: {path}"}
    for i, line in enumerate(lines):
        if _is_item_line(line, checked=False):
            return {"index": i, "item": line.strip()[6:].strip(), "done": False}
    return {"done": True, "message": "All checklist items complete."}


def _mark_index_complete(path: Path, index: int) -> None:
    """Mark the item at index as done without returning a result."""
    lines = _read_lines(path)
    if 0 <= index < len(lines):
        line = lines[index]
        if _is_item_line(line, checked=False):
            lines[index] = line.replace("- [ ] ", "- [x] ", 1)
            _write_lines(path, lines)


def checklist_complete(args: Dict[str, Any]) -> Dict[str, Any]:
    """Mark the item at the given index (from checklist_next's response) as
    done ('- [x] ...'). Returns an error if the index is out of range or
    already completed."""
    args = args or {}
    index = args.get("index")
    if index is None:
        return {"error": "index is required (from checklist_next's response)"}
    path = _resolve_path(args)
    lines = _read_lines(path)
    if index < 0 or index >= len(lines):
        return {"error": f"index {index} out of range (file has {len(lines)} lines)"}
    line = lines[index]
    if _is_item_line(line, checked=True):
        return {"error": f"item at index {index} is already marked complete"}
    if not _is_item_line(line, checked=False):
        return {"error": f"line at index {index} is not an unchecked checklist item: {line!r}"}
    lines[index] = line.replace("- [ ] ", "- [x] ", 1)
    _write_lines(path, lines)
    return {"ok": True}


def checklist_status(args: Dict[str, Any] = None) -> Dict[str, Any]:
    """Return the full checklist with completion counts."""
    args = args or {}
    path = _resolve_path(args)
    lines = _read_lines(path)
    items = [ln.strip() for ln in lines if _is_item_line(ln, True) or _is_item_line(ln, False)]
    done = sum(1 for ln in lines if _is_item_line(ln, checked=True))
    return {"total": len(items), "done": done, "remaining": len(items) - done, "items": items}


CHECKLIST_TOOL_NAMES = {"checklist_next", "checklist_complete", "checklist_status"}


def dispatch(name: str, args: Dict[str, Any]) -> Dict[str, Any]:
    if name == "checklist_next":
        return checklist_next(args)
    if name == "checklist_complete":
        return checklist_complete(args)
    if name == "checklist_status":
        return checklist_status(args)
    return {"error": f"unknown checklist tool: {name}"}
