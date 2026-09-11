# Lute — Technical Architecture

## Design Philosophy

**AI proposes. Structured systems reason. Validators enforce reality.**

The LLM never directly places geometry. It produces structured JSON modifications (BuildingCritique). The deterministic C# engine validates and executes them. This boundary is the core design principle — it prevents an AI from having unrestricted authority over the simulation.

## System Pipeline

```
Natural Language
      ↓
    LLM / NLP
      ↓
Semantic Intent (BuildingCritique JSON)
      ↓
BlueprintModifier
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

## Core Systems

### Blueprint (Intermediate Representation)

The universal data format that all producers output and all executors consume. Inert data — a list of pieces (position, type, material, rotation, size) plus metadata (origin, dimensions, materials).

**Producers:**
- `BuildingGrammar` — BSP-style room-subdivided building layouts (grid → pieces)
- `StyleGrammar` — period-constrained architectural styles (symmetry, bay rhythm, roof pitch, colonnade)
- `MonumentBlueprintProducer` — massing-level monument generation (nave, dome, towers, colonnades)

**Consumers:**
- `VillageBuilder` — incremental village construction with save/resume
- `NPCBuilder` — single-structure NPC builder with collision inspection

**Serialization:**
- `Blueprint.SaveToFile()` / `LoadFromFile()` — JSON via FileSystem.Data
- `Blueprint.ToJsonString()` / `FromJsonString()` — string-based for console/MCP

### BlueprintValidator

Pre-execution validation that catches producer bugs before they become invisible in-world problems.

**Checks:**
- Piece bounds (NaN/infinity, distance from origin, below-ground)
- Overlapping pieces (duplicate position+type, capped at 10 warnings)
- Structural integrity (walls need adjacent floor support)
- Dimension constraints (footprint, height, wall/floor thickness, empty types)

**Result:** `BlueprintValidationResult` with IsValid, ErrorCount, WarningCount, and Issues list.

### VillageBuilder

Incremental procedural village construction:
- Generates a task list via `VillageGrammar` (walls, gates, roads, buildings)
- Builds each task piece-by-piece at ~6s/piece (8-hour pace)
- Saves progress every 10 minutes (non-redundant — skips if no progress)
- Reconstructs completed geometry on scene reload (runtime objects don't persist)
- Multi-builder mode: balanced task partitioning across N builders

### Multi-Builder Mode

- `BuilderId` / `TotalBuilders` on each `VillageBuilder` instance
- `PartitionTasks()`: estimates piece counts, sorts biggest-first, deals round-robin
- Each builder processes only its assigned tasks (`task.BuilderAssignment == BuilderId`)
- `SpatialBlackboard` claims prevent overlap at construction sites
- Only builder 0 reconstructs completed geometry on reload

### SpatialBlackboard

Shared static store for NPC coordination:
- **Positions:** each NPC registers its current position
- **Claims:** NPCs claim construction sites (position + radius + activity)
- **Messages:** NPC-to-NPC messages with type, content, timestamp

### NPCBrain

LLM/NLP bridge for building:
- Connects to local LLM server (LM Studio, Ollama) via OpenAI-compatible API
- LLM acts as Architect/Critic — emits `BuildingCritique` JSON
- `BlueprintModifier` applies critiques to pending tasks only
- Console commands: `village_status`, `village_blackboard`, `blueprint_export`, `blueprint_import`, `monument_export`

### BuildingCritique → BlueprintModifier

The LLM-to-engine bridge:
- `BuildingCritique`: structured JSON (wealth modifier, style tag, add/remove modules, position offset, material override)
- `BlueprintModifier.ApplyCritique()`: modifies pending tasks only (never in-progress or complete)
- Can add new modules (creates new tasks) or remove pending tasks by type

## File Organization

```
sbox/code/Building/
├── Blueprint.cs                  ← IR: piece list + serialization
├── BlueprintValidator.cs         ← pre-execution validation
├── BlueprintModifier.cs          ← applies LLM critiques to tasks
├── BuildingCritique.cs           ← structured critique JSON payload
├── BuildingGrammar.cs            ← BSP room subdivision producer
├── StyleGrammar.cs               ← architectural style constraints
├── MonumentBlueprintProducer.cs  ← massing-level monument producer
├── VillageBuilder.cs             ← village construction executor
├── VillageBuilderController.cs   ← NPC body + FSM for village builder
├── VillageGrammar.cs             ← village layout generator + task type
├── VillageSaveData.cs            ← persistence (save/load/apply progress)
├── SpatialBlackboard.cs          ← NPC coordination store
├── NPCConversation.cs            ← NPC-to-NPC message exchange
├── NPCBrain.cs                   ← LLM bridge + console commands
└── MultiBuilderProbe.cs          ← concurrency diagnostic
```

## Verification

Runtime verification is text-based (the agent has no vision):
- **MCP server** (`http://127.0.0.1:7269/mcp`): scene traces, object inspection, console commands, play mode control
- **Console commands**: `village_status`, `village_blackboard`, `blueprint_export`, `blueprint_import`, `monument_export`
- **Agent scripts**: `sbox_eyes.py` (spatial probing), `collision_probes.py` (collision checks), `scene_telemetry.py` (scene audit)

## Future Directions

- **Blueprint IR enrichment:** Version, ID, Hash, Constraints, Dependencies, Anchors, Provenance
- **Validator issue codes:** Machine-readable classifications (STRUCTURAL.FLOATING_WALL, GEOMETRY.OVERLAP, etc.)
- **ConstructionDirector:** Centralized task scheduling, reservations, and dependencies
- **Monument grammar hierarchy:** Massing → Architectural → Style → Detail → Blueprint
- **NLP NPC system:** Deterministic intent parsing, belief models, social reasoning — zero LLM calls
- **AI authority levels:** Observe → Propose → Validate → Approve → Execute with hard boundaries
