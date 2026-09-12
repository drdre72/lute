#!/usr/bin/env python3
"""GPT-powered vision bridge for the vision-less coding agent.

The coding agent (Devin/GLM) has no image-processing layer — it cannot
look at screenshots. This script closes that gap by:

  1. Capturing a screenshot from the S&Box editor via its MCP server
     (editor_camera_screenshot or camera_screenshot).
  2. Forwarding the base64 PNG to OpenAI's GPT-5 vision model.
  3. Printing the text description back, which the coding agent CAN read.

This is the "force multiplier": the agent gets real visual grounding by
delegating the image-understanding step to a vision-capable model and
consuming only the text result.

Usage:
    python agent/gpt_eyes.py                         # editor viewport, default prompt
    python agent/gpt_eyes.py "Is the terrain visible?"  # custom question
    python agent/gpt_eyes.py --camera <id>          # use a specific camera
    python agent/gpt_eyes.py --play                  # game camera (play mode)
    python agent/gpt_eyes.py --save scrap/shot.png  # also save the raw PNG
    python agent/gpt_eyes.py --width 1280 --height 720
    python agent/gpt_eyes.py --model gpt-5          # override model
    python agent/gpt_eyes.py --detail high          # vision detail level

Requires OPENAI_API_KEY in the environment (or Devin secrets manager).

The default prompt asks for a structured, spatially-aware description
suitable for a coding agent that needs to reason about a 3D game scene:
object positions, lighting, terrain, obvious problems, and anything that
looks broken or out of place.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import urllib.request

MCP_URL = "http://127.0.0.1:7269/mcp"
DEFAULT_MODEL = os.environ.get("GPT_EYES_MODEL", "gpt-5")
DEFAULT_DETAIL = "high"
DEFAULT_WIDTH = 1280
DEFAULT_HEIGHT = 720

DEFAULT_PROMPT = (
    "You are the vision layer for a coding agent that cannot see images. "
    "Describe this S&Box game editor screenshot precisely and spatially so "
    "the agent can reason about the 3D scene without seeing it.\n\n"
    "Report:\n"
    "1. SCENE OVERVIEW: What is visible? Terrain, sky, objects, UI overlays.\n"
    "2. SPATIAL LAYOUT: Where are things positioned (left/center/right, "
    "foreground/background, top/bottom of frame)? Relative sizes.\n"
    "3. TERRAIN & GEOMETRY: Is terrain present? Shape, elevation, color, "
    "any visible seams, holes, or artifacts.\n"
    "4. OBJECTS: Named or recognizable objects (monuments, buildings, "
    "player, props). Their placement, orientation, scale relative to ground.\n"
    "5. LIGHTING & ATMOSPHERE: Direction of light, shadows, fog, overall mood.\n"
    "6. PROBLEMS: Anything that looks broken, missing, floating, clipping, "
    "unlit, wrong-colored, or otherwise off. Be specific about location.\n"
    "7. ONE-LINE SUMMARY: A single sentence capturing the state of the scene.\n\n"
    "Be concrete and terse. No hedging. If something is wrong, say so "
    "and where. If the screen is black or empty, say that explicitly."
)


def mcp_call(method: str, params: dict | None = None) -> dict:
    """Send a JSON-RPC POST to the S&Box editor MCP server."""
    payload = {"jsonrpc": "2.0", "id": 1, "method": method}
    if params is not None:
        payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        MCP_URL,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        result = json.loads(resp.read().decode("utf-8"))
    if "error" in result:
        raise RuntimeError(f"MCP error: {result['error']}")
    return result.get("result", {})


def capture_screenshot(
    camera_id: str = "",
    width: int = DEFAULT_WIDTH,
    height: int = DEFAULT_HEIGHT,
    play_mode: bool = False,
) -> tuple[str, bytes]:
    """Capture a screenshot from the editor. Returns (base64_png, raw_bytes).

    If camera_id is given, uses camera_screenshot with that camera.
    Otherwise uses editor_camera_screenshot (the editor viewport).
    """
    if camera_id or play_mode:
        tool = "camera_screenshot"
        args = {"width": width, "height": height, "includeUi": True}
        if camera_id:
            args["camera"] = camera_id
    else:
        tool = "editor_camera_screenshot"
        args = {"width": width, "height": height}

    result = mcp_call("tools/call", {"name": tool, "arguments": args})
    content = result.get("content", [])
    for item in content:
        if item.get("type") == "image":
            b64 = item.get("data", "")
            raw = base64.b64decode(b64)
            mime = item.get("mimeType", "image/png")
            return b64, raw
    raise RuntimeError(
        f"No image returned by {tool}. Content: "
        f"{json.dumps(content)[:500]}"
    )


def describe_image(
    b64_png: str,
    prompt: str,
    model: str = DEFAULT_MODEL,
    detail: str = DEFAULT_DETAIL,
) -> str:
    """Send the image to OpenAI's vision model and return the text description."""
    try:
        from openai import OpenAI
    except ImportError:
        raise RuntimeError(
            "openai package not installed. Run: pip install openai"
        )

    api_key = os.environ.get("OPENAI_API_KEY")
    # Fallback: read from a local key file (gitignored, temp use only).
    if not api_key:
        for key_path in ("agent/.openai_key", ".openai_key"):
            if os.path.exists(key_path):
                with open(key_path, "r") as f:
                    api_key = f.read().strip()
                break
    if not api_key:
        raise RuntimeError(
            "OPENAI_API_KEY not set. Set it in the environment, or write it to "
            "agent/.openai_key (gitignored temp file)."
        )

    client = OpenAI(api_key=api_key)

    data_url = f"data:image/png;base64,{b64_png}"

    # Use the Responses API (newer, supports input_image content parts).
    response = client.responses.create(
        model=model,
        input=[
            {
                "role": "user",
                "content": [
                    {"type": "input_text", "text": prompt},
                    {
                        "type": "input_image",
                        "image_url": data_url,
                        "detail": detail,
                    },
                ],
            }
        ],
    )
    # Extract text from the response output items.
    text_parts = []
    for item in response.output:
        # message-type items have .content
        content = getattr(item, "content", None)
        if content is None and isinstance(item, dict):
            content = item.get("content")
        if content is None:
            continue
        for part in content:
            text = None
            if hasattr(part, "text"):
                text = part.text
            elif isinstance(part, dict):
                text = part.get("text")
            if text:
                text_parts.append(text)
    return "\n".join(text_parts) if text_parts else str(response)


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "GPT-powered vision bridge: capture an S&Box screenshot and "
            "describe it as text via OpenAI's vision model."
        )
    )
    parser.add_argument(
        "question",
        nargs="?",
        default="",
        help="Custom question/prompt for the vision model. "
        "Empty uses the default structured-scene description prompt.",
    )
    parser.add_argument(
        "--camera",
        default="",
        help="CameraComponent id or game object id for camera_screenshot. "
        "Empty uses the editor viewport.",
    )
    parser.add_argument(
        "--play",
        action="store_true",
        help="Use the game camera (camera_screenshot) instead of the editor "
        "viewport. Requires play mode to be running.",
    )
    parser.add_argument(
        "--width",
        type=int,
        default=DEFAULT_WIDTH,
        help="Screenshot width in pixels.",
    )
    parser.add_argument(
        "--height",
        type=int,
        default=DEFAULT_HEIGHT,
        help="Screenshot height in pixels.",
    )
    parser.add_argument(
        "--model",
        default=DEFAULT_MODEL,
        help=f"OpenAI model to use (default: {DEFAULT_MODEL}).",
    )
    parser.add_argument(
        "--detail",
        default=DEFAULT_DETAIL,
        choices=["auto", "low", "high", "original"],
        help=f"Vision detail level (default: {DEFAULT_DETAIL}).",
    )
    parser.add_argument(
        "--save",
        metavar="PATH",
        default="",
        help="Also save the raw screenshot PNG to this path.",
    )
    parser.add_argument(
        "--village",
        action="store_true",
        help="Move the editor camera to the village walls before capturing. "
        "Overrides --camera/--play.",
    )
    parser.add_argument(
        "--prompt-only",
        action="store_true",
        help="Print the prompt that would be sent and exit (no API call).",
    )
    args = parser.parse_args()

    prompt = args.question if args.question else DEFAULT_PROMPT

    if args.prompt_only:
        print(prompt)
        return 0

    try:
        # Move editor camera to village walls if requested.
        if args.village:
            mcp_call("tools/call", {"name": "set_editor_camera", "arguments": {
                "position": "-10000,-15800,400",
                "angles": "10,270,0",
            }})
            print("Editor camera moved to village walls.", file=sys.stderr)

        # 1. Capture
        print(f"Capturing screenshot ({args.width}x{args.height})...", file=sys.stderr)
        b64, raw = capture_screenshot(
            camera_id=args.camera if not args.village else "",
            width=args.width,
            height=args.height,
            play_mode=args.play if not args.village else False,
        )
        print(f"  got {len(raw)} bytes PNG", file=sys.stderr)

        if args.save:
            with open(args.save, "wb") as f:
                f.write(raw)
            print(f"  saved to {args.save}", file=sys.stderr)

        # 2. Describe via GPT vision
        print(f"Sending to {args.model} (detail={args.detail})...", file=sys.stderr)
        description = describe_image(b64, prompt, model=args.model, detail=args.detail)

        # 3. Print the text description to stdout (what the agent reads)
        print(description)
        return 0

    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
