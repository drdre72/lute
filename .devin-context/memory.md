# Lute — Agent Memory (rolling state)

> This file is the agent's **persistent working memory**. It survives context
> compaction and session restarts (re-injected by `.devin/hooks.v1.json`).
> Keep it concise and current — append to "Recent session activity", prune
> stale TODOs, and update "Live runtime ids" each session. For **stable
> engine API knowledge** see `AGENTS.md` (don't duplicate it here); this file
> is for the **dynamic** state (what we're doing right now, what just
> changed, live object ids, current TODOs).

## Active TODOs

- [~] Move 1: Unify task authority + fix piece accounting — PARTIAL
      ConstructionDirector authority substantially unified. Exact piece
      accounting still requires authoritative BuildPlan/Blueprint-derived
      totals. Current band-aid: TotalPieces=0 until build method sets
      actual, clamp grows TotalPieces to PiecesPlaced. Proper fix is
      Move 4. (commits ac74b49, 3323628)
- [x] Move 2: One global simulation clock — COMPLETE
      LuteSimulationTicker advances all global clocks once per frame.
      Per-builder clock advancement removed. (commit ac74b49)
- [~] Move 3: Correct SettlementNeed lifecycle — PARTIAL
      Planned/completed semantics corrected conceptually. Need counts
      now derived from ConstructionDirector via ReconcileFromDirector().
      But need board still stores its own count fields (not pure derived
      view). OnTaskCompleted/Failed/Cancelled are logging-only. The
      existing=0 planned=70 runtime result is a DIAGNOSTIC OBSERVATION,
      not proof of correctness. (commits ac74b49, 3323628)
- [ ] Move 4: Make Surveyor consume authoritative structure definitions
      (exact footprint, bounds, BOM, work estimate) — ALSO solves exact
      TotalPieces via Blueprint/BuildPlan compilation. NEXT.
- [ ] Move 5: Move benchmark orchestration into production services
- [ ] Move 6: Run Gate 3.4 failure injection (after hardening)
- [ ] Move 7: Demand-driven production
- [ ] Move 8: Completion effects (built sawmill → +plank capacity)
- [ ] Move 9: Representation collapse (bricks → static mesh when done)
- [ ] Move 10: Extract StructureExecutor from VillageBuilder

## Architecture (current state — post 3323628)

Professor's three prerequisites — STATUS:
- Move 1 (task authority + piece accounting): PARTIAL. Director is the
  task catalog. But TotalPieces is still a band-aid (0 until build sets
  actual, clamp grows to PiecesPlaced). Proper fix is Move 4: Blueprint
  compiles exact piece plan → TotalPieces = BuildPlan.Count (authoritative).
- Move 2 (global clock): COMPLETE. LuteSimulationTicker works.
- Move 3 (need lifecycle): PARTIAL. ReconcileFromDirector derives counts
  from the authoritative task catalog. But need board still stores its
  own count fields (not a pure derived view). The existing=0 planned=70
  result is a diagnostic, NOT proof of correctness.

Key authority chain (stable):
```
World → SpatialRegistry/ResourceRegistry → SettlementNeedBoard
  → Surveyor → ConstructionDirector → Logistics/professions
  → physical NPC execution → world mutation
```

Professor's guiding principle (applied):
> Don't synchronize copies of truth when Lute already has an authority
> that can derive the answer.
- Construction progress → authority is Blueprint/BuildPlan (future, Move 4)
- Task lifecycle → authority is ConstructionDirector (implemented)
- Physical existence → authority is world/spatial state (future)

## Night run #1 results (baseline — pre-hardening)

- 100 minutes, 148 tasks, 51 completed (34%), 93 pending
- Anomalies found: Chapel 5309/1383, cottage_5 2461/18, gates TotalPieces=0
- 2 adaptive Surveyor cottages stuck pending (material starvation)
- Root causes identified: piece accounting (PARTIAL fix — needs Move 4
  for proper authoritative totals), task authority split (fixed),
  clock scaling (fixed), need lifecycle (PARTIAL fix — needs derived
  view, not stored counts). The zero-total problem improved; exact
  piece accounting was NOT solved. Do NOT treat existing=0 planned=70
  as proof of correctness — it's a diagnostic observation.

## Recent session activity (newest last; prune when >~15 entries)

- [2026-09-14] Architecture hardening commit ac74b49: three professor
  prerequisites implemented. LuteSimulationTicker (one global clock),
  SettlementNeed lifecycle (planned vs completed), piece accounting fix
  (TotalPieces initialized before build + defensive clamp). Runtime
  verified: existing=0 planned=70 for shelter need (correctly NOT
  fulfilled). Full Gate 3.3 loop running: gatherers→crafters→haulers→
  builders.
- [2026-09-14] Night run #1 (100 min): 148 tasks, 51 completed, 93
  pending. Found piece-count corruption (PiecesPlaced > TotalPieces),
  adaptive cottages stuck, material bottleneck. Plan pushed to GitHub
  (commits 2fb2e0a, 76c2b14).
- [2026-09-14] Gate 3.3 full loop proven: GathererController (Lumberjack,
  Quarryman, Forager) → CrafterController → HaulerController →
  VillageBuilderController. No pre-stocked materials — all from source
  nodes. Commit ecabcea.
- [2026-09-14] Adaptive Surveyor site selection: SettlementNeedBoard →
  StructureRequest → SurveyorController scores candidate sites →
  ConstructionDirector.RegisterTask. Commit 220e0e0.
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
- [2026-09-09] **Fixed yaw-rotation placement bugs**. Merlons, portcullis
  bars, and gate jambs were all placed using `rot.ToRotation() * localYOffset`
  which put them perpendicular to walls instead of along them. Fixed with
  direct world-space offsets (X for N/S, Y for E/W). Gate jambs now flank
  the opening instead of sitting in front/behind it.
- [2026-09-09] **Fixed wall/corner overlap**. Wall segments extended to
  corner center, overlapping by ~3m. Shortened by cornerSize/2.
- [2026-09-09] **Merlyn teleport**. Added `AgentTeleportTo` property to
  `LuteBuilderNpc` — set via MCP to instantly reposition. Fires once,
  clears to zero. Verified at north wall top and watchtower.
- [2026-09-09] **Systematic monument inspection**. `inspect_monument2.py`
  verifies all 8 towers, 4 gates, 4 walls, 4 corners, 4 bridges, 36
  portcullis bars, 404 merlons, well, stalls, workbenches, houses.
  Only remaining issue: moat has no colliders (v1 dry-moat design).
- [2026-09-09] **Camera observation pipeline**. `sbox_camera.py` teleports
  the PLAYER (not the camera — PlayerController overrides it every frame)
  and captures via `camera_screenshot` MCP tool. 8-shot orbit of market
  captured and verified (real scenes, 3434-4582 unique colors). Deleted
  dead `cleanup_screenshots.py` (Godot-era path). Moondream2 download in
  progress for vision sub-agent.

## File map (gameplay code — `sbox/code/`)

### Construction simulation (current focus — `sbox/code/Building/`)
- `ConstructionDirector.cs` — sole authoritative task catalog + scheduler.
  DirectedTask, BuilderState, material requirements, dependency graph.
- `VillageBuilder.cs` — executor. Generates layout via VillageGrammar,
  builds brick-by-brick. Tasks is now an executor view, not authority.
- `VillageBuilderController.cs` — NPC body + FSM. Reads director tasks.
  No longer advances global clocks (ticker does that).
- `VillageGrammar.cs` — fixed village layout generator. VillageBuildTask
  definition lives here.
- `VillageSaveData.cs` — VillagePersistence + VillageSaveData + TaskProgress.
  Save/load with piece-count clamp.
- `LuteSimulationTicker.cs` — single global simulation clock (scene component).
- `SettlementNeed.cs` — SettlementNeed + SettlementNeedType. Lifecycle
  counts: ExistingCount (completed), PlannedCount, InProgressCount,
  FailedCount. Deterministic IDs.
- `SettlementNeedBoard.cs` — needs → StructureRequests → Surveyor dispatch.
  OnTaskCompleted/Failed/Cancelled reconciliation.
- `StructureRequest.cs` — StructureRequest + StructureRequestStatus +
  SiteRequirements. DirectedTaskId links to ConstructionDirector task.
- `SurveyorController.cs` — autonomous site selection. Scores candidates
  by road access, slope, clearance, proximity.
- `Gate3Benchmark.cs` — test harness. Night-run save at 100 min. Injects
  shelter need after 30s. Still creates logistics demand (Move 5 target).
- `GathererController.cs` — Lumberjack/Quarryman/Forager. Gathers from
  source nodes, hauls to stockpiles.
- `CrafterController.cs` — Carpenter/Mason. Fetches raw materials,
  crafts processed materials (Plank/Brick).
- `HaulerController.cs` — hauls materials from stockpiles to build sites.
- `ConstructionEventBus.cs` — TaskCompleted/TaskFailed/TaskCancelled events.
- `SpatialBlackboard.cs` — spatial position registry. Global clock ticked
  by LuteSimulationTicker only.
- `ReservationManager.cs` — spatial reservation for task bounds.
- `CapabilityRegistry.cs` — profession → capability mapping.

### Core game (`sbox/code/`)
- `LuteGame.cs` — entry. Spawns LuteWorld, HUD, LuteSimulationTicker.
  Initializes CapabilityRegistry, ResourceBootstrap.
- `LuteWorld.cs` — builds sanctuary: world ground, monument, temple.
- `LutePlayer.cs` — player extension on PlayerController.
- `LuteBuilderNpc.cs` — citizen body NPC + agent-drive mode.
- `LuteWatchtower.cs` — from-scratch watchtower builder.
- `LuteMonumentBuilder.cs` — Neutral Market monument whitebox.
- `LuteTerrainGenerator.cs` — procedural terrain (currently disabled).
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
- **`edit` tool whitespace**: must match tabs exactly. This repo uses
  tabs + CRLF. Diagnose with the PowerShell T-counter snippet (see
  AGENTS.md "Agent Workflow First Principles").
- **PowerShell from bash**: use `shell_flavor: "powershell"` for any
  command with backslashes, backticks, or `$` in C# strings. Never nest
  shells.
- **`read_console` MCP**: parameter is `limit` (not `lineCount`). Set
  `PYTHONIOENCODING=utf-8` when piping — console output has Unicode arrows.
- **`write` tool**: replaces ENTIRE file. Always verify nothing was
  dropped (grep for class/enum after writing).
- **Static state persists across sessions**: S&Box static fields survive
  play_stop/play_start. Call `Clear()` before `InitializeDefaults()` in
  `LuteGame.OnStart` (pattern: CapabilityRegistry, SettlementNeedBoard).
