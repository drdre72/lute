# Lute — Agent Memory (rolling state)

> This file is the agent's **persistent working memory**. It survives context
> compaction and session restarts (re-injected by `.devin/hooks.v1.json`).
> Keep it concise and current — append to "Recent session activity", prune
> stale TODOs, and update "Live runtime ids" each session. For **stable
> engine API knowledge** see `AGENTS.md` (don't duplicate it here); this file
> is for the **dynamic** state (what we're doing right now, what just
> changed, live object ids, current TODOs).

## Active TODOs

- [ ] Decide: keep BuilderNpc autonomous build-test sequence, or make it
      purely agent-driven? (Currently both run; AgentControlled takes
      priority but the state machine still places test blocks on fresh play
      and they can deflect the velocity-driven body.)
- [ ] Add Acropolis walkable collision (concave `PhysicsMeshFromRender` or
      multi-hull) so the NPC can climb terraces — current single convex hull
      makes the base a ~5m wall.
- [ ] Next from-scratch build target (watchtower done) — pick: bridge,
      guild hall shell, or more towers.
- [ ] Tighten NPC agent-drive: velocity is sometimes fought by
      PlayerController friction / deflected by collision. Consider driving
      via `Input.AnalogMove`-equivalent or a kinematic mode instead of raw
      Rigidbody velocity.

## Recent session activity (newest last; prune when >~15 entries)

- [2026-09-09] Acropolis lowered flush to floor (world z 1332.59 → 1135.74,
  base now at z=0). Collision intact (ModelCollider, hull top z≈3837).
- [2026-09-09] **CRITICAL FIX**: MeshComponent runtime collision/render bug.
  `RebuildMesh()` bails when `!Scene.IsEditor`, so setting `.Mesh` at runtime
  left `Model` null → invisible + non-solid blocks. Builder NPC's earlier
  "PASSED" test was a false positive (NPC grounded on WorldGround below).
  Fix: `Scene.CreateObject(false)` → set Mesh → `go.Enabled = true` triggers
  `OnEnabledInternal` → `RebuildRenderMesh` (no !IsEditor guard). Applied to
  `LuteBuilderNpc.PlaceBlock` and `LuteWatchtower.PlaceBox`. (See AGENTS.md
  "MeshComponent runtime gotcha".)
- [2026-09-09] Built first from-scratch structure: **Watchtower**
  (`LuteWatchtower.cs`) 120m east of sanctuary. 31 blocks (foundation,
  2 hollow wall tiers w/ doorway, parapet floor, parapet walls w/ stair gap,
  15-step external stair, 0.44m risers). Drove NPC up all 15 steps onto the
  parapet floor at z=255.95 (6.5m), grounded=True. First fully walkable
  from-scratch structure — validates the block-building system.
- [2026-09-09] Added live agent-control tunnel on `LuteBuilderNpc`
  (`AgentControlled`/`AgentTarget`/`AgentSpeed`/`AgentStopRadius` [Property]
  fields + `AgentTick()`). Driven via MCP `set_component`. Ran a full
  sanctuary waypoint circuit, all grounded. See AGENTS.md "Live agent
  control via MCP".
- [2026-09-09] In-game agent chat panel (`LuteAgentChat.razor`, key J not F6).
  `[AgentChat]` log prefix, no disk writes (whitelist). Verified: user sent
  "Ahoy! Welcome to Lute" + "hello!".
- [2026-09-09] Acropolis collision fixed via `ModelCollider` (option 1).
  `Model.Physics` = 1 hull. Confirms imported STL/OBJ→.vmdl assets with
  `PhysicsHullFromRender` produce working collision with one line.
- [2026-09-09] Set up this memory + hooks system (`.devin/hooks.v1.json`,
  `.devin-context/`). **Complete and verified**: inject-memory.ps1 (full +
  compact modes, UTF-8 read), byte-tracker.ps1 (PostToolUse tally, ~500KB
  threshold, gitignored tally_*.txt), hooks.v1.json wires SessionStart +
  PostCompaction (full), UserPromptSubmit (compact), PostToolUse (tracker).
  All scripts tested with sample stdin, emit valid JSON, exit 0.
- [2026-09-09] **Neutral Market whitebox detail pass**. Enhanced
  `LuteMonumentBuilder.cs` with: material differentiation (stone for walls/
  towers, wood for stalls/benches/housing, metal for portcullis bars),
  battlements/merlons on curtain wall (404 merlons) + tower tops (32
  merlons), gatehouse detail (jambs, lintels, portcullis bar grids — 36
  bars across 4 gates), tilted awnings with support posts, well detail
  (stone rim, water surface, 4 wooden roof posts, flat roof). Build
  verified: 0 errors, wall collision intact (trace hit Wall_0_0 at z=472).
  All new elements confirmed via `find_game_objects` at runtime.

## File map (gameplay code — `sbox/code/`)

- `LuteGame.cs` — entry; spawns LuteWorld + LutePlayer.
- `LuteWorld.cs` — builds sanctuary: world ground, monument, temple floor,
  hex walls, time portal, archway, **Acropolis** (ModelCollider), builder
  NPC, **watchtower**.
- `LutePlayer.cs` — player extension on PlayerController (noclip etc).
- `LuteBuilderNpc.cs` — citizen body NPC; autonomous build-test sequence +
  **agent-drive mode** (`AgentControlled`/`AgentTick`). Block placer.
- `LuteWatchtower.cs` — from-scratch watchtower builder (MeshComponent +
  PolygonMesh blocks). Reusable `PlaceBox` helper.
- `LuteMonumentBuilder.cs` — Neutral Market monument whitebox.
- `LuteTerrainGenerator.cs` — procedural terrain (currently disabled during
  monument whitebox work).
- `LuteAgentChat.razor` / `.scss` — in-game chat panel (key J).
- `LuteCompass.razor` / `.scss` — compass HUD.
- `lute.csproj` — build; references `sbox/base/code/Base Library.csproj`.

## File map (agent tooling — `agent/`)

- `sbox_verify.ps1` — text verification report (pixel grid + log tail + scene dump).
- `sbox_eyes.py` — MCP spatial probing (raycasts, object inspection).
- `scrape_apis.py` — regenerates `docs_cache/api_signatures.md` from engine source.
- `cleanup_screenshots.py` — screenshot retention (deletion disabled).
- `agent.py` / `run.py` / `tools.py` / `mcp_server.py` — Godot-era agent loop
  (legacy; S&Box native MCP server now used instead).

## Key repo paths

- Repo root: `C:\Users\Shadow\Documents\lute`
- S&Box project: `sbox/`  •  Gameplay code: `sbox/code/`
- Live addon copy (keep in sync): `C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute`
- Engine source: `C:\Users\Shadow\Documents\sbox-public-clean`
- API cache: `docs_cache/api_signatures.md` (773 types, 387KB)
- Stable knowledge: `AGENTS.md`  •  Full history: `PROGRESS_LOG.md`

## Live runtime (S&Box native MCP)

- MCP endpoint: `http://127.0.0.1:7269/mcp` (JSON-RPC, on by default).
- Meta-tools: `search_tools`, `call_tool`, `editor_status`, `read_console`,
  `list_toolsets`/`describe_toolset`. Scene tools: `find_game_objects`,
  `get_game_object` (use `includeComponentProperties:true` for prop values +
  component ids), `set_component` (live property writes during play),
  `set_game_object` (transform), `scene_trace` (raycast), `play_start`/
  `play_stop`.
- `read_console` returns a `since` cursor — pass it back to get only new
  entries. Filter by prefix to grab `[AgentChat]`, `[AgentDrive]`,
  `Lute:`, etc.
- **Runtime objects only exist while play mode is running** — call
  `play_start` before `find_game_objects` for anything spawned by
  `LuteWorld.Build()`.
- Conversion: `1 meter = 39.37 S&Box units`.

## Live object ids (this session — INVALID after play restart; re-query)

Re-query with `find_game_objects` at the start of each work segment. Don't
trust stale ids across `play_stop`/`play_start`. Pattern:
`find_game_objects {name:"Builder", component:"LuteBuilderNpc"}` → get
GameObject id → `get_game_object` with `includeComponentProperties:true` →
grab the `LuteBuilderNpc` component id → `set_component` to drive it.

## Decisions (locked in)

- Engine: S&Box / Source 2, C# .NET 10, `Sandbox` namespace. No Godot/Unreal.
- Block-building: `MeshComponent` + `PolygonMesh`, `CollisionType.Mesh`.
  Create blocks **disabled**, set mesh, then enable (runtime rebuild fix).
- Monument collision: `ModelCollider` for imported `.vmdl` with
  `PhysicsHullFromRender`.
- Agent embodiment: `LuteBuilderNpc` driven via MCP `set_component` on
  `AgentControlled`/`AgentTarget` props. No LLM vision — text probes only.
- Chat: in-game `LuteAgentChat.razor` (key J) → `[AgentChat]` log lines →
  agent reads via `read_console`. No disk writes (whitelist).
- Verification: `sbox_verify.ps1` (text) + MCP `scene_trace`/`read_console`,
  never rely on screenshot interpretation.

## Gotchas (quick reference — full detail in AGENTS.md)

- **MeshComponent at runtime**: must create disabled, set Mesh, then enable.
  Otherwise invisible + non-solid.
- **`BoxCollider.Scale`**: absolute world units, not multipliers of 50u box.
- **Scene JSON `"__type"`**: `ConvertFrom-Json` silently drops it — rename
  to `_ObjType` before parsing.
- **FBX material names with dots** (`root.0.0`): engine rejects; use
  `DefaultMaterialGroup` `use_global_default=true` + `MaterialOverride`.
- **F6**: S&Box record shortcut — use J for chat.
- **Stair walkability**: solid blocks from ground up, risers ≤0.46m
  (18u StepUpHeight), ascend toward the destination.
- **`File.*` from game code**: blocked by whitelist — use `Log.Info` only.
