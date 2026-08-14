# Lute

A Godot 4.7 (C#/.NET) project (technical codename `Periphery`), with an
AI-driven development pipeline that lets a local LLM (Qwen 2.5 Coder 14B via
LM Studio) inspect and author scenes, nodes, scripts, and project settings
directly in the Godot editor. See `PRD.md` for the full game design.

## AI Pipeline overview

- `addons/ai_pipeline/` — a Godot `EditorPlugin` that runs a minimal HTTP/JSON
  tool server inside the editor (default port `6400`), with a dock panel
  ("AI Pipeline" tab in the bottom panel) showing status and a log of AI actions.
- `agent/` — two ways to drive the addon's tool server:
  - `agent.py` / `run.py` — a standalone loop that talks to LM Studio directly
    (OpenAI-compatible chat/tools API) until the task is complete.
  - `mcp_server.py` — a real [Model Context Protocol](https://modelcontextprotocol.io)
    server (stdio transport, via the official `mcp` SDK) exposing the same
    tools, so any MCP client (Claude Desktop, Windsurf, Cursor, etc.) can drive
    Godot too, not just the custom LM Studio loop.
- `PRD.md` — project requirements doc; loaded into the agent's context every
  wake (`agent.py` only). Edit it to steer what the agent builds.
- `AGENT_RULES.md` — coding/style/architecture rules for the agent (engine
  constraints, game systems reference, developer instructions). Also loaded
  into context every wake (`agent.py` only).
- `PROGRESS_LOG.md` — auto-generated append-only timeline log, written only
  after successful task/turns (`agent.py` only). Each line is capped at 150
  words (timestamp excluded) to avoid bloating the model's context; only the
  most recent entries are loaded into context every wake.

```
LM Studio (Qwen 2.5 14B)  <-- OpenAI-compatible chat/tools API -->  agent/agent.py
                                                                          |
                                                                    HTTP POST /rpc
                                                                          v
                                                        Godot Editor (addons/ai_pipeline)
```

## Setup

### 1. Enable the Godot plugin

The plugin is already registered in `project.godot` under `[editor_plugins]`.
Open the project in Godot 4.7 — the plugin auto-enables and starts its HTTP
server on port `6400`. Check the **AI Pipeline** tab in the bottom dock to
confirm "Server: running on port 6400".

### 2. Start LM Studio

Load a Qwen 2.5 14B model in LM Studio (a variant with tool/function-calling
support) and start the local server so it's reachable at
`http://127.0.0.1:1234/v1/chat/completions`. Note the exact model identifier
shown in LM Studio — you'll pass it via `--model`.

### 3. Install agent dependencies

```bash
cd agent
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
```

### 4. Run the agent

**Option A — custom LM Studio loop:**

```bash
# One-shot task
python run.py "Create a Node3D root with a Camera3D child in res://scenes/test.tscn"

# Open-ended chat (persists history across turns within the session)
python run.py --chat
```

The agent will call tools like `scene_create`, `node_add`, `get_scene_tree`, etc.
against the running Godot editor and print each tool call/result as it goes.

On every wake (one-shot or chat), the agent loads `PRD.md` (project requirements —
edit this to steer it) and `PROGRESS_LOG.md` (append-only; written automatically
after each successful task/turn, never on failure) into its system context. In
chat mode, type `/reset` to clear in-session history, `exit`/`quit` to leave.

**Using a cloud model instead of local LM Studio:** pass `--cloud` to route
through [OpenRouter](https://openrouter.ai) (one API key, OpenAI-compatible,
hosts most open-weight and proprietary models) with `deepseek/deepseek-chat`
(DeepSeek-V3) as the default model:

```bash
export LLM_API_KEY=sk-or-...           # from https://openrouter.ai/keys
python run.py --cloud "Add lighting and particle effects to the temple portal"

# Or override the model explicitly (see https://openrouter.ai/models):
python run.py --cloud --model deepseek/deepseek-r1 "..."

# Or point at any other OpenAI-compatible endpoint/model directly:
python run.py --lm-studio-url https://api.openai.com/v1/chat/completions \
              --model gpt-4o --api-key sk-... "..."
```

`--cloud` requires purchased OpenRouter credits (`deepseek/deepseek-chat`
returns HTTP 402 without them). For no-cost access, use `--cloud-free`
instead, which defaults to `nvidia/nemotron-3-ultra-550b-a55b:free`
(rate-limited but verified to support tool calling; no credits needed —
just the API key):

```bash
python run.py --cloud-free "Add lighting and particle effects to the temple portal"
```

`--api-key` also works instead of the env var. Local LM Studio (no `--cloud`
or `--cloud-free`, no key) remains the default and needs no auth header.

**Option B — standard MCP client (Claude Desktop, Windsurf, Cursor, ...):**

Add to your MCP client's config (e.g. `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "godot-ai-pipeline": {
      "command": "/Users/andrebaker/periphery/agent/.venv/bin/python",
      "args": ["/Users/andrebaker/periphery/agent/mcp_server.py"]
    }
  }
}
```

The client will launch `mcp_server.py` over stdio and see the same 17 tools
(`ping`, `get_scene_tree`, `scene_create`, `node_add`, ...), forwarding calls
to the Godot editor's HTTP server on port `6400`.

## Available tools

`get_scene_tree`, `scene_create`, `scene_open`, `scene_save`, `node_add`,
`node_delete`, `node_set_property`, `node_get_properties`, `script_write`,
`script_read`, `script_attach`, `project_setting_get`, `project_setting_set`,
`play_scene`, `stop_scene`, `list_dir`. Schemas live in `agent/tools.py`; handlers
live in `addons/ai_pipeline/plugin.gd`.

## Safety notes

- Node/property/script-attach mutations go through `EditorUndoRedoManager`, so
  they can be undone from the editor (Edit > Undo) — `node_delete`'s undo keeps
  the removed node alive but does not restore its exact previous position among
  siblings.
- Commit to git before running agent sessions so you have a clean rollback point.
- The HTTP server binds to localhost only and has no authentication — do not
  expose port `6400` beyond your machine.
