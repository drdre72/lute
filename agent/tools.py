"""Tool schemas and dispatch for the AI Pipeline Godot addon.

Each tool mirrors an RPC handler exposed by addons/ai_pipeline/plugin.gd,
reachable over HTTP at http://127.0.0.1:<port>/rpc.
"""
from __future__ import annotations

import json
from typing import Any, Dict

import requests

from checklist_tool import CHECKLIST_TOOL_NAMES, dispatch as checklist_dispatch

GODOT_SERVER_URL = "http://127.0.0.1:6400/rpc"
REQUEST_TIMEOUT_SECONDS = 15

# OpenAI-style tool/function schemas passed to the model.
TOOL_SCHEMAS = [
    {
        "type": "function",
        "function": {
            "name": "ping",
            "description": "Check that the Godot editor AI Pipeline server is reachable.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_scene_tree",
            "description": "Get the full node tree of the currently open/edited scene as JSON.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "scene_create",
            "description": "Create a new .tscn scene file on disk with a root node of the given type, and open it in the editor (it becomes the currently edited scene).",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "res:// path to save the new scene, e.g. res://scenes/level.tscn"},
                    "root_type": {"type": "string", "description": "Godot class name for the root node, e.g. Node3D"},
                },
                "required": ["path", "root_type"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "scene_open",
            "description": "Open an existing scene file in the Godot editor.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string"}},
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "scene_save",
            "description": "Save the currently open scene in the editor.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "node_add",
            "description": "Add a new node as a child of an existing node in the currently open scene. Undoable.",
            "parameters": {
                "type": "object",
                "properties": {
                    "parent_path": {"type": "string", "description": "Node path relative to scene root, e.g. '.' for root or 'Root/Enemies'"},
                    "type": {"type": "string", "description": "Godot class name, e.g. Camera3D"},
                    "name": {"type": "string", "description": "Name for the new node"},
                },
                "required": ["parent_path", "type"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "node_delete",
            "description": "Delete a node from the currently open scene. Undoable.",
            "parameters": {
                "type": "object",
                "properties": {"node_path": {"type": "string"}},
                "required": ["node_path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "node_set_property",
            "description": "Set a property on a node in the currently open scene. Undoable.",
            "parameters": {
                "type": "object",
                "properties": {
                    "node_path": {"type": "string"},
                    "property": {"type": "string"},
                    "value": {"description": (
                        "New value. For Vector2/Vector3/Color use {x,y[,z]} or {r,g,b,a}. "
                        "To construct and assign a Resource (e.g. a Mesh for MeshInstance3D.mesh, "
                        "or a Shape3D for CollisionShape3D.shape), use {\"class\": \"BoxMesh\", "
                        "...other properties of that class, e.g. \"size\": {\"x\":1,\"y\":1,\"z\":1}}. "
                        "Any Godot class deriving Resource works this way (BoxMesh, CylinderMesh, "
                        "SphereMesh, BoxShape3D, SphereShape3D, StandardMaterial3D, etc.), and nested "
                        "dicts recurse (e.g. a StandardMaterial3D's albedo_color as {r,g,b,a})."
                    )},
                },
                "required": ["node_path", "property", "value"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "node_get_properties",
            "description": "Get all editor-visible properties and current values of a node.",
            "parameters": {
                "type": "object",
                "properties": {"node_path": {"type": "string"}},
                "required": ["node_path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "script_write",
            "description": "Write (create or overwrite) a script file's contents on disk.",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "res:// path, e.g. res://scripts/player.gd"},
                    "content": {"type": "string"},
                },
                "required": ["path", "content"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "script_read",
            "description": "Read a script file's contents from disk.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string"}},
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "script_attach",
            "description": "Attach an existing script file to a node in the currently open scene. Undoable.",
            "parameters": {
                "type": "object",
                "properties": {
                    "node_path": {"type": "string"},
                    "script_path": {"type": "string"},
                },
                "required": ["node_path", "script_path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "project_setting_get",
            "description": "Read a project setting value.",
            "parameters": {
                "type": "object",
                "properties": {"key": {"type": "string"}},
                "required": ["key"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "project_setting_set",
            "description": "Set and save a project setting value.",
            "parameters": {
                "type": "object",
                "properties": {"key": {"type": "string"}, "value": {}},
                "required": ["key", "value"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "play_scene",
            "description": "Run the main scene, or a specific scene if a path is given.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string", "description": "Optional res:// scene path"}},
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "stop_scene",
            "description": "Stop the currently running scene.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "list_dir",
            "description": "List files and directories at a res:// path.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string", "description": "Defaults to res://"}},
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "screenshot",
            "description": "Capture a screenshot of the Godot editor viewport and save it as a PNG.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string", "description": "Optional output path (res:// or absolute). Defaults to user:// with timestamp."}},
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "checklist_next",
            "description": (
                "For large multi-step build tasks: return the next incomplete item from the "
                "on-disk checklist (MetaList.txt by default), one small step at a "
                "time. Use this instead of trying to plan the whole task at once. Returns "
                "{'done': true} when there are no items left. If you have just completed an "
                "item, pass its index as completed_index to mark it done and fetch the next "
                "item in a single call."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Optional path override; defaults to MetaList.txt"},
                    "completed_index": {"type": "integer", "description": "Optional index of the item just completed (from the prior checklist_next). Mark it done and return the next item in one call."}
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "checklist_complete",
            "description": "Mark a checklist item as done, using the index returned by checklist_next.",
            "parameters": {
                "type": "object",
                "properties": {
                    "index": {"type": "integer", "description": "Index from checklist_next's response"},
                    "path": {"type": "string", "description": "Optional path override; defaults to MetaList.txt"},
                },
                "required": ["index"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "node_find",
            "description": "Find nodes by name pattern, type, or parent path prefix. Returns compact list of {name, type, path} — much faster than get_scene_tree for targeted searches.",
            "parameters": {
                "type": "object",
                "properties": {
                    "name_pattern": {"type": "string", "description": "Wildcard pattern, e.g. 'RTree_*' or '*Canopy*'"},
                    "type": {"type": "string", "description": "Filter by Godot class name, e.g. 'MeshInstance3D'"},
                    "parent_prefix": {"type": "string", "description": "Filter by parent path prefix, e.g. 'TownArea'"},
                    "limit": {"type": "integer", "description": "Max results (default 100)"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "scene_stats",
            "description": "Get summary statistics of the current scene: total node count, counts by type, counts by parent region. Compact alternative to get_scene_tree.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "material_report",
            "description": "Report material coverage: how many renderable nodes have/lack materials, with samples of missing ones.",
            "parameters": {
                "type": "object",
                "properties": {
                    "limit": {"type": "integer", "description": "Max missing samples to return (default 50)"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "screenshot_camera",
            "description": "Take a screenshot from an arbitrary camera position. Creates a temporary Camera3D, renders, saves screenshot, cleans up. Not limited to predefined camera nodes.",
            "parameters": {
                "type": "object",
                "properties": {
                    "position": {"type": "object", "description": "Camera position {x, y, z}"},
                    "look_at": {"type": "object", "description": "Look-at target {x, y, z}"},
                    "fov": {"type": "number", "description": "Field of view in degrees (default 60)"},
                },
                "required": ["position", "look_at"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "checklist_status",
            "description": "Get the full checklist with completion counts (how many done/remaining).",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string", "description": "Optional path override; defaults to MetaList.txt"}},
            },
        },
    },
]


def call_tool(name: str, args: Dict[str, Any], server_url: str = GODOT_SERVER_URL) -> Dict[str, Any]:
    """POST a tool call to the Godot editor AI Pipeline server and return its JSON result.
    Checklist tools are handled locally (no Godot round-trip needed)."""
    if name in CHECKLIST_TOOL_NAMES:
        return checklist_dispatch(name, args or {})
    payload = {"tool": name, "args": args or {}}
    try:
        resp = requests.post(server_url, data=json.dumps(payload), timeout=REQUEST_TIMEOUT_SECONDS,
                              headers={"Content-Type": "application/json"})
        resp.raise_for_status()
        return resp.json()
    except requests.RequestException as exc:
        return {"error": f"request to Godot server failed: {exc}"}
    except ValueError:
        return {"error": "Godot server returned invalid JSON"}
