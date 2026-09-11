# Bug List: sbox_multiscale_inspection_v2.py

This is a detailed breakdown of the bugs in `sbox_multiscale_inspection_v2.py`,
organized by severity. Each bug explains what's wrong, why it matters, and
how to fix it. The goal is to help you learn the S&Box MCP API and avoid
these patterns in future work.

The good news: your conceptual architecture (multi-scale inspection,
anti-hallucination prompts, keyword discrepancy detection, closed-loop
correct-then-reverify) is sound. The three best ideas have been folded
into the working `agent/texture_inspect.py`. This file is about the
implementation bugs that prevented your script from running.

---

## Blocking Bugs (script will crash or produce no results)

### Bug 1: Wrong JSON-RPC method name

**What:** `send_mcp_request` sends `{"method": "set_component"}` directly.
The S&Box MCP server expects the JSON-RPC `tools/call` wrapper:

```python
# WRONG (your code):
payload = {"jsonrpc": "2.0", "id": 1, "method": "set_component", "params": {...}}

# CORRECT:
payload = {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
           "params": {"name": "set_component", "arguments": {...}}}
```

**Why it matters:** Every single MCP call returns an error. The script
can never communicate with S&Box at all.

**Fix:** See `agent/vision_lib.py` lines 25-30 for the working `call()`
function. The tool name goes inside `params.name`, and the tool's
arguments go inside `params.arguments`.

**Lesson:** MCP (Model Context Protocol) uses a two-level dispatch:
the JSON-RPC method is always `tools/call`, and the specific tool
(`set_component`, `find_game_objects`, etc.) is a parameter *within* that
call. This is standard MCP, not S&Box-specific.

---

### Bug 2: set_component takes a component GUID, not names

**What:** You pass `{"game_object": "Merlyn", "component": "LuteBuilderNpc"}`
to `set_component`. The actual tool requires `id` = a component GUID
(globally unique identifier), not a human-readable name.

**Why it matters:** `set_component` can't find the component by name.
The teleport never happens, so every screenshot is from wherever Merlyn
was last standing.

**Fix:** The lookup is a two-step process:
1. `find_game_objects(name='Merlyn')` → returns a list of matching
   GameObjects with their GUIDs.
2. `get_game_object(id=GUID)` → returns the full GameObject tree as JSON
   text. You parse this text with regex to find the `LuteBuilderNpc`
   component's GUID and the `CameraComponent` GUID.

See `agent/vision_lib.py` lines 148-170 (`_find_merlyn` method) for the
complete working lookup. The key insight: S&Box MCP tools work with
GUIDs, not names. Names are only for the initial `find_game_objects`
search.

**Lesson:** Game engines use GUIDs internally. Human-readable names are
a search convenience, not an address. Always resolve name → GUID before
operating on an object.

---

### Bug 3: camera_screenshot response parsing is wrong

**What:** You expect `res["result"]["image_base64"]`. The actual response
format is:

```json
{
  "result": {
    "content": [
      {"type": "image", "data": "<base64 string>", "mimeType": "image/png"}
    ]
  }
}
```

**Why it matters:** `capture_screenshot()` always returns `None`, so
`query_vlm` is never called. No vision analysis happens.

**Fix:** See `agent/vision_lib.py` lines 201-209. You must iterate the
`content` list, find the item where `type == "image"`, and base64-decode
the `data` field:

```python
for item in r['result'].get('content', []):
    if item.get('type') == 'image':
        return base64.b64decode(item['data'])
```

**Lesson:** Always check the actual response structure with a test call
before writing parsing code. Don't guess the response format from the
function name.

---

### Bug 4: Waypoint coordinates are in the wrong location and unit system

**What:** Your waypoints use coordinates like `{"x": 0, "y": 0, "z": 1800}`
and `{"x": 15, "y": 15, "z": 50}`. Two problems:

1. **Wrong location:** The market center is at `(15000, 15000, 0)`. A
   waypoint at `(0, 0, 1800)` is ~14km away from the market. Every
   screenshot would be empty terrain or sky.

2. **Wrong units:** S&Box uses inches (39.37 units per meter). A value
   of `z=1800` is only ~46m up, which is reasonable for an aerial view,
   but `x=15, y=15` is 0.4m from the world origin — not 15m from the
   market center.

**Why it matters:** Every screenshot captures the wrong thing. The VLM
sees empty ground or sky and reports "nothing visible."

**Fix:** All coordinates must be in S&Box world units, offset from the
market center `(15000, 15000, 0)`. For example, an aerial overview of
the market would be at approximately `(15000, 15000, 6000)` — 150m above
the market center. See the `MACRO_VIEWPOINTS` in the updated
`texture_inspect.py` for correct coordinates.

**Lesson:** Always check what coordinate system and unit scale the engine
uses before writing position values. In S&Box: 1 meter = 39.37 units,
and the world origin is `(0, 0, 0)`, not the market center.

---

### Bug 5: Output path is a Linux path on Windows

**What:** `out_path = "/workspace/scratch/sbox_closed_loop_report.json"`

**Why it matters:** `open()` will raise `FileNotFoundError` on Windows
because `/workspace/` doesn't exist. The script crashes at the end even
if everything else worked.

**Fix:** Use `os.path.join` with the script's directory:

```python
out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        '..', 'scrap', 'sbox_closed_loop_report.json')
os.makedirs(os.path.dirname(out_path), exist_ok=True)
```

**Lesson:** Never hardcode absolute paths. Use `os.path.join` relative
to `__file__` or an environment variable. This is also why we created
`agent/paths.py` — to centralize all path configuration.

---

### Bug 6: No Merlyn/Eyes camera lookup at all

**What:** The script assumes "Merlyn" can be passed as a string to MCP
tools. It can't — you need the GUID of the `CameraComponent` attached
to Merlyn's "Eyes" child GameObject.

**Why it matters:** `camera_screenshot` needs a camera component GUID.
Without it, no screenshot is captured.

**Fix:** See Bug 2. The full lookup chain is:
1. `find_game_objects(name='Merlyn')` → Merlyn's GameObject GUID
2. `get_game_object(id=merlyn_guid)` → parse for "Eyes" child GUID
3. `get_game_object(id=eyes_guid)` → parse for `CameraComponent` GUID
4. `camera_screenshot(camera=cam_comp_guid)`

See `agent/vision_lib.py` lines 148-170 and 201-209.

---

## Design Problems (would produce wrong results even if bugs were fixed)

### Bug 7: Runtime MCP corrections don't persist

**What:** `apply_corrective_transform` moves a runtime GameObject via
MCP `set_component`. But the market is procedurally generated by
`LuteMonumentBuilder.cs` on every `play_start`. Moving a runtime object
doesn't fix the source code — the next restart regenerates the same bug.

**Why it matters:** The "correct" step appears to work, but the fix is
lost on restart. The re-verify pass might see the fix, but the next
session won't.

**Fix:** For procedural geometry, corrections must be written to the C#
source file (`LuteMonumentBuilder.cs`), then the project rebuilt and
play mode restarted. See how `texture_inspect.py` handles this: it
writes new tiling values to `.vmat` files, runs `dotnet build`, stops
and restarts play mode, then re-inspects.

**Lesson:** Distinguish between runtime state (ephemeral, lost on
restart) and source state (persistent). Procedural generation means
the source code is the source of truth, not the runtime scene.

---

### Bug 8: Correction deltas are hardcoded magic numbers

**What:** `z: -10.0 if "floating"` and `yaw: 90.0 if "perpendicular"`
assume every floating object needs exactly 10 units down and every
perpendicular object needs exactly 90° rotation.

**Why it matters:** Real errors vary. An object might be floating 200
units (5m) above the ground, not 10. A rotation error might be 45°, not
90°. Hardcoded deltas will overcorrect or undercorrect.

**Fix:** Either:
- Query the object's actual position via `get_game_object` and compute
  the delta to the target position, or
- Ask the VLM to estimate the magnitude of the error (like
  `texture_inspect.py` does with `RATIO_U`/`RATIO_V`), and scale the
  correction proportionally with a damping factor.

**Lesson:** Avoid magic numbers in correction logic. Measure the actual
error, then compute a proportional correction. Damp the correction
(move partway, not all the way) to avoid overshoot from noisy estimates.

---

### Bug 9: No pixel_check or flash lighting

**What:** Dark or empty frames are sent directly to the VLM without
any sanity check.

**Why it matters:** The VLM will hallucinate when given a black frame —
it might describe "a dark dungeon" or "nighttime scene" that doesn't
exist. This wastes a VLM call and produces false data.

**Fix:** See `agent/vision_lib.py` `pixel_check()` (lines 108-133) and
the `capture()` method's flash parameter (lines 307-336). Take a test
shot, check brightness, and if too dark, spawn a temporary `PointLight`
at the target position before re-capturing.

**Lesson:** Always validate input before sending it to a model. A
garbage-in/garbage-out pipeline with a VLM is worse than no pipeline,
because the output looks plausible but is fabricated.

---

### Bug 10: Stale model default

**What:** `VLM_MODEL` defaults to `"moondream-2b-2025-04-14"`. The
project already upgraded to `qwen3-vl-4b-instruct`.

**Why it matters:** If the env var isn't set, the script queries a model
that may not be loaded in LM Studio, producing a connection error.

**Fix:** Default to `"qwen3-vl-4b-instruct"` or read from
`vision_lib.MODEL`.

---

## Factual Errors in the Analysis Text

These don't affect the script but would mislead anyone reading the
analysis:

1. **"MeshComponent and PolygonMesh primitives"** — The project uses
   `ModelRenderer` + pre-made `.vmdl` models (`box.vmdl`, `sphere.vmdl`,
   `plane_large.vmdl`), not `MeshComponent` or `PolygonMesh`. Always
   verify claims about the tech stack by reading the actual source code.

2. **"moondream-2b-2025-04-14"** — Already replaced with
   `qwen3-vl-4b-instruct`. The "recommended upgrade to Qwen2.5-VL" is
   based on stale information.

3. **"440 battlements"** — The actual count is generated procedurally
   and depends on wall length, gate gaps, and corner sizes. Don't state
   specific counts without verifying from the source.

---

## What You Got Right

These ideas were good and have been incorporated into the working
`agent/texture_inspect.py`:

1. **Multi-scale inspection (macro + micro)** — The updated script now
   runs a macro pass first (aerial overview, gate approach, plaza
   interior) to verify structural integrity, then a micro pass for
   texture scale. Use `--no-macro` to skip it.

2. **Anti-hallucination prompt conditioning** — The updated `make_prompt`
   now includes explicit negative cues: "Light gray surfaces are stone,
   not snow or ice" for stone/plaza surfaces, "Brown surfaces are wood
   planks, not dirt or soil" for wood surfaces.

3. **Keyword-based discrepancy detection** — The macro pass scans
   Qwen's free-text response for discrepancy keywords (`floating`,
   `missing`, `displaced`, `perpendicular`, etc.) and flags them in
   the report. Macro discrepancies are reported but not auto-fixed
   (they require C# source changes, not material tiling changes).

---

## Recommended Reading

To understand the working patterns:

- `agent/vision_lib.py` — The shared library. Read the entire file.
  Pay attention to `_find_merlyn()`, `teleport_merlyn()`,
  `capture_merlyn()`, and `capture()` (with flash).
- `agent/texture_inspect.py` — The working closed-loop inspector.
  Compare its MCP calls, coordinate system, and correction approach
  to your script.
- `agent/paths.py` — How path configuration is centralized.
- `AGENTS.md` — Project rules and S&Box API notes.
