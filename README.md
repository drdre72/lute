# Lute

A deterministic autonomous medieval-village simulation for S&Box (Source 2), built in C#.

Lute simulates a self-sustaining settlement: NPCs gather resources, craft materials, haul goods, and construct structures brick-by-brick through a deterministic, validated pipeline. The architecture separates **authoritative simulation logic** (deterministic C#) from **authoring tooling** (LLMs may propose blueprints offline, but never drive runtime NPC behavior).

## Architecture

```text
World → SpatialRegistry / ResourceRegistry → SettlementNeedBoard
     → Surveyor → ConstructionDirector (authoritative task DAG)
     → LogisticsPlanner / ProductionPlanner / Professions
     → VillageBuilder (village orchestration)
     → StructureExecutor (single-structure realization)
     → physical NPC execution → world mutation
```

### Core design rules

- **Runtime NPC intelligence is deterministic.** NPCs never call LLMs for gameplay behavior or conversation. They act on goals, needs, beliefs, memory, blackboard state, intents, construction tasks, reservations, and world state.
- **AI proposes; structured systems reason; validators enforce reality.** LLMs may be used by development tooling or offline content generation, but never by an NPC at runtime.
- **ConstructionDirector is the task authority.** It owns the task DAG, reservations, and completion transitions.
- **StructureExecutor realizes one authorized plan; it does not know what a "village" is.** Village-level orchestration stays in VillageBuilder.

## Key Systems

- **Blueprint IR** — universal piece-list intermediate representation produced by all generators and consumed by all executors. Supports JSON export/import.
- **BlueprintValidator** — pre-execution validation: piece bounds, collision overlaps, structural integrity, dimension constraints.
- **ConstructionDirector** — authoritative task scheduling, dependency DAG, reservations, and completion transitions.
- **ReservationManager** — task-tied spatial reservations, occupancy ledger, conflict detection.
- **SettlementNeedBoard** — demand-driven need registration and reconciliation against the director's task catalog.
- **Surveyor** — surveys the world and proposes construction candidates from settlement needs.
- **LogisticsPlanner / ProductionPlanner** — haul jobs to supply build sites; demand-driven crafting orders when stockpiles lack crafted materials.
- **CompletionEffectsManager** — completed structures activate production capacity (sawmill → +workstation) and housing capacity (cottage → +housing).
- **RepresentationCollapser** — collapses completed per-brick GameObjects into a single static representation for performance.
- **VillageBuilder** — village-level orchestration: build loop, multi-builder partitioning, save/resume, director registration. Delegates structure realization to StructureExecutor.
- **StructureExecutor** — single-structure realization authority (walls, gates, roads, wells, markets, buildings).
- **SpatialRegistry / ResourceRegistry** — authoritative spatial and resource state for the simulation.
- **ConstructionEventBus** — pub/sub event system for construction state transitions.
- **Deterministic NLP pipeline** — tokenizer, grammar parser, entity resolver, speech templates for NPC-to-NPC structured coordination (no LLM).
- **ConversationManager** — async message delivery, exactly-once consumption for agent coordination.
- **BeliefModel** — per-NPC self-beliefs, task history, reputation, trust, conversation state.

## Tech Stack

- **Engine:** S&Box (Source 2)
- **Language:** C# (.NET 10 / LangVersion 14)
- **Build:** `dotnet build sbox/code/lute.csproj`
- **MCP:** S&Box editor MCP server at `http://127.0.0.1:7269/mcp` for runtime inspection and control

## Project Layout

```
lute/
├── README.md                  ← this file (current product overview)
├── PRD.md                     ← product requirements (game design)
├── AGENTS.md                  ← rules and knowledge for AI agents (read before writing S&Box code)
├── ARCHITECTURE.md            ← technical architecture
├── LUTE_STATE.md              ← single-page source of truth for current implementation state
├── PROGRESS_LOG.md            ← chronological development history
├── docs/decisions/            ← ADRs (immutable architectural decisions)
├── docs/history/              ← historical project phases (Godot, Unreal)
├── sbox/
│   └── code/                  ← all C# gameplay code
│       ├── Building/          ← Blueprint, validators, grammars, director, executors, planners
│       ├── NPC/               ← NPC spawner, spawn markers, controllers
│       ├── Items/             ← inventory, resources
│       ├── NLP/               ← deterministic NLP pipeline
│       └── LuteWorld.cs        ← world setup (terrain, monuments, village)
├── agent/                     ← verification scripts (MCP probing, collision, telemetry)
└── docs_cache/                ← auto-generated S&Box API reference
```

## Documentation

| File | Purpose |
|------|---------|
| `README.md` | Current product overview (this file) |
| `PRD.md` | Product requirements and game design |
| `AGENTS.md` | Rules and knowledge for AI agents working on this repo |
| `ARCHITECTURE.md` | Technical architecture and system design |
| `LUTE_STATE.md` | Single-page source of truth for current implementation state |
| `PROGRESS_LOG.md` | Chronological development log |
| `docs/decisions/` | ADRs — immutable architectural decisions |
| `docs/history/` | Historical project phases (Godot, Unreal prototypes) |

## Building

```powershell
dotnet build sbox/code/lute.csproj
```

Sync changed files to the live S&Box addon:
```powershell
Copy-Item sbox/code/Building/*.cs C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute\code\Building/ -Force
```

## Verification

Runtime verification uses the S&Box MCP server (text-based, not visual):
- `agent/sbox_eyes.py` — spatial probing via raycasts and object inspection
- `agent/collision_probes.py` — automated collision and traversal checks
- `agent/scene_telemetry.py` — scene graph audit (counts, materials, bounds)
- `agent/sbox_verify.ps1` — combined screenshot/log/scene report
- `agent/gpt_eyes.py` — GPT-5 vision bridge (screenshot → text description)

## License

Private project. See repository for details.
