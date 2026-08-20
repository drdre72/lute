#!/usr/bin/env python3
"""
LLM Conversation-Mode World Builder

The LLM is the architect. It surveys available assets, receives instructions,
then builds the world through iterative tool calls — seeing results and adjusting
like a human level designer.

Usage:
  python3 llm_builder.py --api-key YOUR_MISTRAL_KEY
  python3 llm_builder.py --api-key YOUR_MISTRAL_KEY --vision
  python3 llm_builder.py --api-key YOUR_MISTRAL_KEY --instruction "Build a coastal fishing village"
"""

import requests
import json
import time
import sys
import os
import base64
import argparse

SERVER = "http://localhost:6410"

# Load API config from Foundry.txt
_FOUNDRY_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Novos", "Keys", "Foundry.txt")
with open(_FOUNDRY_PATH, "r") as _f:
    _lines = [l.strip() for l in _f.readlines() if l.strip()]
LLM_ENDPOINT = _lines[0]  # Azure OpenAI endpoint
LLM_API_KEY = _lines[1]   # API key
LLM_MODEL = "mistral-small-2503"  # Azure deployed model name
MAX_TURNS = 30

# ─── System Prompt ───────────────────────────────────────────────────────────

SYSTEM_PROMPT = """You are an expert world-building architect for Unreal Engine 5, specializing in survival game maps like Rust, Ark, and Conan Exiles.

## THE WORLD
There is already a landscape in the level, centered at origin (0,0,0). It is flat by default.
The landscape spans 3km x 3km (world coordinates -1500 to +1500 on X and Y axes).
Z is up (height). The ground surface is at Z=0.
All your tool calls operate on THIS landscape — terrain_sculpt modifies its height, terrain_paint paints its surface layers, place_prop and batch_place spawn props on it.
Props are placed as components on a single WorldBuilder actor at origin, so coordinates you specify are world coordinates on the landscape.

You have tools to:
- list_props: Survey available static mesh assets (trees, rocks, buildings, furniture, etc.)
- place_prop: Place a single unique prop at a world position
- batch_place: Place thousands of identical props via HISM (trees, rocks) — ONE draw call
- terrain_sculpt: Raise, lower, flatten, or smooth terrain
- terrain_paint: Paint terrain layers (Grass, Dirt, Rock, Sand)
- get_terrain_height: Query the terrain height at a position
- clear_world: Remove all placed props
- screenshot: Capture a viewport screenshot

## YOUR PROCESS

1. SURVEY: Call list_props with different filters to understand what assets you have.
   Try: "tree", "rock", "wall", "fence", "house", "cabin", "boat", "lamp", "bush", "barrel", "crate", "table", "chair", "bed", "chest", "anvil", "sword", "ruin", "pillar", "vase", "plant"

2. PLAN: Based on what you find, plan a coherent world. Think about:
   - Where will monuments/landmarks go? (spread them across the map)
   - Where will forests be? (perimeter, dense, natural clustering)
   - Where will roads connect things?
   - Where will water features be?
   - What terrain elevation changes make sense?

3. BUILD IN ORDER:
   a. Sculpt terrain FIRST (hills, valleys, water basins)
   b. Place monuments/structures at planned locations
   c. Batch-place forests (use batch_place for trees — hundreds at a time)
   d. Batch-place rocks and environmental details
   e. Place roadside details (lamps, fences, bushes)
   f. Paint terrain layers (dirt roads, grass areas, sand near water)

4. COORDINATES MATTER:
   - Monuments should be 400-800 units from center, spread in a ring
   - Forests should be 800-1400 units from center (perimeter)
   - Roads connect monuments in a ring
   - Use the FULL 3km canvas — don't clump everything at origin
   - X and Y are horizontal plane coordinates. Z=0 means ground level.

5. BATCH_PLACE IS CRITICAL for performance:
   - For trees: generate 50-200 placements per mesh type
   - For rocks: generate 20-50 placements per mesh type
   - Each placement is {x, y, z, yaw, scale}
   - Spread them across an area, not all at one point
   - Vary scale (0.8-1.3) and yaw (0-360) for natural look

6. After building, call screenshot to see your work. Evaluate and adjust.

## QUALITY BAR
Think like a Rust map: dense forests, clear landmarks, natural terrain variation, roads connecting points of interest, props spread across the entire map.

Be decisive. Place things with intention. Don't ask for permission — build.
"""

# ─── Tool Definitions (Mistral function calling format) ─────────────────────

TOOL_DEFINITIONS = [
    {
        "type": "function",
        "function": {
            "name": "list_props",
            "description": "List available static mesh assets with optional name filter",
            "parameters": {
                "type": "object",
                "properties": {
                    "filter": {"type": "string", "description": "Name filter (e.g. 'tree', 'wall', 'rock')"},
                    "max": {"type": "number", "description": "Max results (default 200)"}
                },
                "required": []
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "place_prop",
            "description": "Place a single static mesh prop at a world position (for unique structures)",
            "parameters": {
                "type": "object",
                "properties": {
                    "mesh_path": {"type": "string", "description": "Static mesh asset path"},
                    "x": {"type": "number", "description": "World X coordinate"},
                    "y": {"type": "number", "description": "World Y coordinate"},
                    "z": {"type": "number", "description": "World Z (default 0 = ground)"},
                    "yaw": {"type": "number", "description": "Rotation degrees (default 0)"},
                    "scale": {"type": "number", "description": "Scale (default 1.0)"}
                },
                "required": ["mesh_path", "x", "y"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "batch_place",
            "description": "Batch-place thousands of identical meshes as HISM instances (trees, rocks). Single draw call. Provide array of placements.",
            "parameters": {
                "type": "object",
                "properties": {
                    "mesh_path": {"type": "string", "description": "Static mesh asset path to instance"},
                    "placements": {
                        "type": "array",
                        "description": "Array of {x, y, z, yaw, scale} objects for each instance",
                        "items": {
                            "type": "object",
                            "properties": {
                                "x": {"type": "number"},
                                "y": {"type": "number"},
                                "z": {"type": "number"},
                                "yaw": {"type": "number"},
                                "scale": {"type": "number"}
                            }
                        }
                    }
                },
                "required": ["mesh_path", "placements"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "terrain_sculpt",
            "description": "Sculpt terrain height at a position",
            "parameters": {
                "type": "object",
                "properties": {
                    "x": {"type": "number", "description": "World X"},
                    "y": {"type": "number", "description": "World Y"},
                    "radius": {"type": "number", "description": "Brush radius"},
                    "strength": {"type": "number", "description": "Brush strength"},
                    "mode": {"type": "string", "description": "raise, lower, flatten, smooth"}
                },
                "required": ["x", "y"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "terrain_paint",
            "description": "Paint a terrain layer at a position",
            "parameters": {
                "type": "object",
                "properties": {
                    "x": {"type": "number", "description": "World X"},
                    "y": {"type": "number", "description": "World Y"},
                    "radius": {"type": "number", "description": "Brush radius"},
                    "strength": {"type": "number", "description": "Paint strength 0-1"},
                    "layer": {"type": "string", "description": "Layer: Grass, Dirt, Rock, Sand"}
                },
                "required": []
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_terrain_height",
            "description": "Query terrain height at a world position",
            "parameters": {
                "type": "object",
                "properties": {
                    "x": {"type": "number", "description": "World X"},
                    "y": {"type": "number", "description": "World Y"}
                },
                "required": []
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "clear_world",
            "description": "Remove all placed world builder props from the level",
            "parameters": {
                "type": "object",
                "properties": {},
                "required": []
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "screenshot",
            "description": "Capture a screenshot of the current viewport",
            "parameters": {
                "type": "object",
                "properties": {},
                "required": []
            }
        }
    }
]

# ─── Tool Execution ─────────────────────────────────────────────────────────

def execute_tool(tool_name, args):
    """Execute a tool call against the local Unreal HTTP server."""
    try:
        if tool_name == "list_props":
            r = requests.post(f"{SERVER}/list_props", json={
                "filter": args.get("filter", ""),
                "max": int(args.get("max", 200))
            }, timeout=30)
            return r.json()

        elif tool_name == "place_prop":
            r = requests.post(f"{SERVER}/place_prop", json={
                "mesh_path": args.get("mesh_path", ""),
                "x": args.get("x", 0),
                "y": args.get("y", 0),
                "z": args.get("z", 0),
                "yaw": args.get("yaw", 0),
                "scale": args.get("scale", 1.0)
            }, timeout=60)
            return r.json()

        elif tool_name == "batch_place":
            r = requests.post(f"{SERVER}/batch_place", json={
                "mesh_path": args.get("mesh_path", ""),
                "placements": args.get("placements", [])
            }, timeout=120)
            return r.json()

        elif tool_name == "terrain_sculpt":
            r = requests.post(f"{SERVER}/terrain_sculpt", json={
                "x": args.get("x", 0),
                "y": args.get("y", 0),
                "radius": args.get("radius", 500),
                "strength": args.get("strength", 100),
                "mode": args.get("mode", "raise")
            }, timeout=30)
            return r.json()

        elif tool_name == "terrain_paint":
            r = requests.post(f"{SERVER}/terrain_paint", json={
                "x": args.get("x", 0),
                "y": args.get("y", 0),
                "radius": args.get("radius", 500),
                "strength": args.get("strength", 0.5),
                "layer": args.get("layer", "Grass")
            }, timeout=30)
            return r.json()

        elif tool_name == "get_terrain_height":
            r = requests.get(f"{SERVER}/get_terrain_height", params={
                "x": args.get("x", 0),
                "y": args.get("y", 0)
            }, timeout=10)
            return r.json()

        elif tool_name == "clear_world":
            r = requests.post(f"{SERVER}/clear_world", json={}, timeout=30)
            return r.json()

        elif tool_name == "screenshot":
            r = requests.get(f"{SERVER}/screenshot", timeout=30)
            data = r.json()
            # Return a summary, not the full base64 (too large for LLM context)
            return {
                "success": True,
                "size": data.get("size", 0),
                "message": "Screenshot captured. Use vision mode to see it."
            }

        else:
            return {"error": f"Unknown tool: {tool_name}"}

    except Exception as e:
        return {"error": str(e)}

# ─── Screenshot Capture ──────────────────────────────────────────────────────

def capture_screenshot():
    """Capture screenshot and return base64 data URI for vision API."""
    try:
        r = requests.get(f"{SERVER}/screenshot", timeout=30)
        if r.status_code == 200:
            data = r.json()
            b64 = data.get("base64", "")
            if b64:
                return b64
            # Try reading from path
            path = data.get("path", "")
            if path and os.path.exists(path):
                with open(path, "rb") as f:
                    encoded = base64.b64encode(f.read()).decode()
                    return f"data:image/jpeg;base64,{encoded}"
    except Exception as e:
        print(f"  ! Screenshot error: {e}")
    return None

# ─── LLM Conversation ───────────────────────────────────────────────────────

def send_to_llm(api_key, messages, tools, api_endpoint=LLM_ENDPOINT, model=LLM_MODEL, vision_screenshot=None):
    """Send conversation to Mistral and get response."""
    # Build messages — if we have a screenshot, add it to the last user message
    payload_messages = []

    for i, msg in enumerate(messages):
        if i == len(messages) - 1 and msg["role"] == "user" and vision_screenshot:
            # Add vision to last user message
            content = [
                {"type": "text", "text": msg["content"]},
                {"type": "image_url", "image_url": {"url": vision_screenshot}}
            ]
            payload_messages.append({"role": "user", "content": content})
        else:
            payload_messages.append(msg)

    payload = {
        "model": model,
        "messages": payload_messages,
        "tools": tools,
        "temperature": 0.3,
        "max_tokens": 8000
    }

    max_retries = 5
    for attempt in range(max_retries):
        try:
            r = requests.post(
                api_endpoint,
                headers={
                    "api-key": api_key,
                    "Content-Type": "application/json"
                },
                json=payload,
                timeout=180
            )

            if r.status_code == 200:
                return r.json()
            elif r.status_code == 429:
                wait = min(10 * (attempt + 1), 60)
                print(f"  ! Rate limited (429). Waiting {wait}s before retry {attempt+1}/{max_retries}...")
                time.sleep(wait)
                continue
            else:
                print(f"  ! LLM API error: HTTP {r.status_code}")
                print(f"  Response: {r.text[:500]}")
                if attempt < max_retries - 1:
                    time.sleep(5)
                    continue
                return None
        except Exception as e:
            print(f"  ! LLM request error: {e}")
            if attempt < max_retries - 1:
                time.sleep(5)
                continue
            return None

    print("  ! LLM failed after all retries")
    return None

# ─── Main Conversation Loop ─────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="LLM Conversation-Mode World Builder")
    parser.add_argument("--api-key", default=LLM_API_KEY, help="LLM API key (defaults to Foundry.txt)")
    parser.add_argument("--endpoint", default=LLM_ENDPOINT, help="LLM API endpoint (defaults to Foundry.txt)")
    parser.add_argument("--model", default=LLM_MODEL, help="Model name")
    parser.add_argument("--instruction", default="Build a Rust-style survival map with monuments, forests, roads, and water features.", help="Build instruction")
    parser.add_argument("--max-turns", type=int, default=MAX_TURNS, help="Max conversation turns")
    parser.add_argument("--vision", action="store_true", help="Enable vision (screenshot after each turn)")
    args = parser.parse_args()

    print("=" * 70)
    print("  LLM CONVERSATION-MODE WORLD BUILDER")
    print(f"  Model: {args.model}")
    print(f"  Endpoint: {args.endpoint[:50]}...")
    print(f"  Vision: {'ON' if args.vision else 'OFF'}")
    print(f"  Max turns: {args.max_turns}")
    print("=" * 70)

    # Check server
    try:
        r = requests.get(f"{SERVER}/state", timeout=5)
        if r.status_code != 200:
            raise Exception("Bad status")
        print(f"\n  Server: {r.json().get('map_name', 'unknown')}")
    except:
        print("\n  ERROR: Server not responding on port 6410")
        sys.exit(1)

    # Initialize conversation
    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": args.instruction}
    ]

    print(f"\n  Instruction: {args.instruction}")
    print(f"\n{'─'*70}")
    print(f"  Starting conversation loop...")
    print(f"{'─'*70}\n")

    total_tool_calls = 0

    for turn in range(1, args.max_turns + 1):
        print(f"\n{'═'*70}")
        print(f"  TURN {turn}/{args.max_turns}")
        print(f"{'═'*70}")

        # Capture screenshot for vision if enabled
        vision_shot = None
        if args.vision and turn > 1:
            print("  Capturing screenshot for vision...")
            vision_shot = capture_screenshot()
            if vision_shot:
                print(f"  Screenshot ready ({len(vision_shot)} chars)")

        # Send to LLM
        print("  Sending to LLM...")
        response = send_to_llm(args.api_key, messages, TOOL_DEFINITIONS, args.endpoint, args.model, vision_shot)

        if not response:
            print("  ! LLM failed after all retries, ending conversation")
            break

        # Parse response
        choice = response.get("choices", [{}])[0]
        message = choice.get("message", {})

        # Print any text content
        content = message.get("content", "")
        if content:
            print(f"\n  LLM says: {content[:500]}")
            if len(content) > 500:
                print(f"  ... ({len(content)} chars total)")

        # Check for tool calls
        tool_calls = message.get("tool_calls", [])

        if not tool_calls:
            print(f"\n  LLM says: {content}")
            messages.append({"role": "assistant", "content": content})

            # Interactive mode — ask user for next instruction
            print(f"\n{'─'*70}")
            print(f"  Your turn. Guide the LLM's next move:")
            print(f"  (Enter instruction, or 'quit' to exit, or 'auto' to let it continue alone)")
            try:
                user_input = input("  > ").strip()
            except (EOFError, KeyboardInterrupt):
                break

            if user_input.lower() in ('quit', 'exit', 'q', ''):
                break
            elif user_input.lower() == 'auto':
                user_input = "Continue building. Execute your next steps with tool calls now."
            else:
                user_input = user_input + "\n\nExecute this now with tool calls."

            messages.append({"role": "user", "content": user_input})
            continue

        # Add assistant message to conversation
        messages.append(message)

        # Execute each tool call
        for i, tc in enumerate(tool_calls):
            func = tc.get("function", {})
            tool_name = func.get("name", "")
            args_str = func.get("arguments", "{}")
            tool_call_id = tc.get("id", f"call_{turn}_{i}")

            try:
                tool_args = json.loads(args_str) if isinstance(args_str, str) else args_str
            except:
                tool_args = {}

            # Print what the LLM is doing
            print(f"\n  🔧 Tool {i+1}/{len(tool_calls)}: {tool_name}")

            # Summarize the call
            if tool_name == "list_props":
                print(f"     filter='{tool_args.get('filter', '')}' max={tool_args.get('max', 200)}")
            elif tool_name == "place_prop":
                print(f"     mesh={tool_args.get('mesh_path', '?')[-40:]} at ({tool_args.get('x', 0)}, {tool_args.get('y', 0)})")
            elif tool_name == "batch_place":
                count = len(tool_args.get("placements", []))
                print(f"     mesh={tool_args.get('mesh_path', '?')[-40:]} x{count} instances")
            elif tool_name == "terrain_sculpt":
                print(f"     ({tool_args.get('x', 0)}, {tool_args.get('y', 0)}) r={tool_args.get('radius', 500)} s={tool_args.get('strength', 100)} {tool_args.get('mode', 'raise')}")
            elif tool_name == "terrain_paint":
                print(f"     ({tool_args.get('x', 0)}, {tool_args.get('y', 0)}) r={tool_args.get('radius', 500)} {tool_args.get('layer', 'Grass')}")
            elif tool_name == "clear_world":
                print(f"     Clearing all props...")
            elif tool_name == "screenshot":
                print(f"     Capturing viewport...")
            elif tool_name == "get_terrain_height":
                print(f"     Querying ({tool_args.get('x', 0)}, {tool_args.get('y', 0)})")

            # Execute
            result = execute_tool(tool_name, tool_args)
            total_tool_calls += 1

            # Summarize result
            if isinstance(result, dict):
                if result.get("success"):
                    if tool_name == "list_props":
                        count = result.get("count", 0)
                        props = result.get("props", [])
                        print(f"     → {count} props found")
                        if props:
                            # Show first few names
                            names = [p.get("name", "?") for p in props[:5]]
                            print(f"     → e.g: {', '.join(names)}{'...' if count > 5 else ''}")
                    elif tool_name == "batch_place":
                        print(f"     → {result.get('instance_count', 0)} instances placed via HISM")
                    elif tool_name == "place_prop":
                        print(f"     → Placed as {result.get('component', '?')}")
                    elif tool_name == "clear_world":
                        print(f"     → Removed {result.get('removed_count', 0)} actors")
                    elif tool_name == "screenshot":
                        print(f"     → {result.get('size', 0)} bytes captured")
                    else:
                        print(f"     → OK")
                else:
                    err = result.get("error", "unknown")
                    print(f"     → ERROR: {err}")
            else:
                print(f"     → {str(result)[:100]}")

            # Add tool result to conversation
            # For list_props, trim the result to avoid huge context
            if tool_name == "list_props" and isinstance(result, dict):
                props = result.get("props", [])
                # Keep full list but only name + path (no extra fields)
                trimmed = {
                    "success": True,
                    "count": result.get("count", len(props)),
                    "props": [{"name": p.get("name", ""), "path": p.get("path", "")} for p in props]
                }
                result_str = json.dumps(trimmed)
            else:
                result_str = json.dumps(result) if isinstance(result, dict) else str(result)

            messages.append({
                "role": "tool",
                "tool_call_id": tool_call_id,
                "name": tool_name,
                "content": result_str
            })

        # Small delay between turns to let engine settle
        time.sleep(1)

    # Final summary
    print(f"\n{'═'*70}")
    print(f"  CONVERSATION COMPLETE")
    print(f"  Total turns: {turn}")
    print(f"  Total tool calls: {total_tool_calls}")
    print(f"{'═'*70}")

    # Final screenshot
    if args.vision:
        print("\n  Capturing final screenshot...")
        shot = capture_screenshot()
        if shot:
            print(f"  Final screenshot: {len(shot)} chars")

if __name__ == "__main__":
    main()
