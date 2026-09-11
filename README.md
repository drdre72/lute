# Lute

An AI-directed persistent sandbox engine for S&Box (Source 2) in which language models propose world changes through a deterministic, validated construction pipeline.

Players, NPCs, and external agents can describe desired structures or world changes in natural language. Lute translates those requests into structured blueprints, validates them against architectural, geometric, and gameplay constraints, and executes them through deterministic S&Box systems.

## Architecture

```
Natural Language
      ↓
    LLM / NLP
      ↓
Semantic Intent
      ↓
BuildingGrammar / StyleGrammar / MonumentBlueprintProducer
      ↓
Blueprint (Intermediate Representation)
      ↓
BlueprintValidator
      ↓
Executor (VillageBuilder / NPCBuilder)
      ↓
Persistent World
```

The LLM doesn't directly place geometry. It proposes structured modifications (BuildingCritique JSON). The deterministic C# engine validates and executes them. This is the core design principle: **AI proposes, structured systems reason, validators enforce reality.**

## Key Systems

- **Blueprint IR** — universal piece-list format produced by all generators (BuildingGrammar, StyleGrammar, MonumentBlueprintProducer) and consumed by all executors. Supports JSON export/import for inspection and hand-authoring.
- **BlueprintValidator** — pre-execution validation: piece bounds, collision overlaps, structural integrity (floating walls), dimension constraints. Catches producer bugs before they become invisible in-world problems.
- **VillageBuilder** — incremental procedural village construction with save/resume, multi-builder parallelism, and balanced task partitioning.
- **MonumentBlueprintProducer** — massing-level monument generation (St. Peter's Basilica, etc.) with parametric volumes (nave, dome, towers, colonnades).
- **SpatialBlackboard** — shared NPC coordination: positions, claims, reservations, and messages.
- **NPCBrain** — LLM/NLP bridge for building critiques, console commands, and architect/critic interaction.

## Tech Stack

- **Engine:** S&Box (Source 2)
- **Language:** C# (.NET 10 / LangVersion 14)
- **Build:** `dotnet build sbox/code/lute.csproj`
- **MCP:** S&Box editor MCP server at `http://127.0.0.1:7269/mcp` for runtime inspection and control

## Project Layout

```
lute/
├── README.md                  ← this file (current product)
├── PRD.md                     ← product requirements (game design)
├── AGENTS.md                  ← rules for AI agents (read before writing S&Box code)
├── ARCHITECTURE.md            ← technical architecture
├── PROGRESS_LOG.md            ← chronological development history
├── sbox/
│   └── code/                  ← all C# gameplay code
│       ├── Building/          ← Blueprint, validators, grammars, builders
│       ├── NPC/               ← NPC spawner, spawn markers
│       ├── LuteWorld.cs       ← world setup (terrain, monuments, village)
│       └── LuteMonumentBuilder.cs ← hand-authored Neutral Market monument
├── agent/                     ← verification scripts (MCP probing, collision, telemetry)
├── docs/
│   └── history/               ← historical project phases (Godot, Unreal)
└── docs_cache/                ← auto-generated API reference
```

## Documentation

| File | Purpose |
|------|---------|
| `README.md` | Current product overview (this file) |
| `PRD.md` | Product requirements and game design |
| `AGENTS.md` | Rules and knowledge for AI agents working on this repo |
| `ARCHITECTURE.md` | Technical architecture and system design |
| `PROGRESS_LOG.md` | Chronological development log |
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
- Console commands: `village_status`, `village_blackboard`, `blueprint_export`, `blueprint_import`, `monument_export`

## License

Private project. See repository for details.
