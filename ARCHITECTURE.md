# Lute — Technical Architecture

## Design Philosophy

**AI proposes. Structured systems reason. Validators enforce reality.**

The LLM never directly places geometry and never drives runtime NPC behavior. LLMs may be used by development tooling or offline content generation. At runtime, NPC intelligence is fully deterministic: NPCs act on goals, needs, beliefs, memory, blackboard state, intents, construction tasks, reservations, and world state. This boundary is the core design principle — it prevents an AI from having unrestricted authority over the simulation, and it keeps the simulation reproducible.

If you can compile Lute with the LLM components completely absent and the NPC simulation still works, the architecture is correct.

## Authoritative Simulation Chain

```text
World → SpatialRegistry / ResourceRegistry → SettlementNeedBoard
     → Surveyor → ConstructionDirector (authoritative task DAG)
     → LogisticsPlanner / ProductionPlanner / Professions
     → VillageBuilder (village orchestration)
     → StructureExecutor (single-structure realization)
     → physical NPC execution → world mutation
```

Each link is production-owned (not test-harness-owned). Demand, production, logistics, construction, and completion form a closed loop.

## Core Systems

### Blueprint (Intermediate Representation)

The universal data format that all producers output and all executors consume. Inert data — a list of pieces (position, type, material, rotation, size) plus metadata (origin, dimensions, materials). The authoritative construction representation; NPC speech is never itself the authority for world mutation.

**Producers:**
- `BuildingGrammar` — BSP-style room-subdivided building layouts (grid → pieces)
- `StyleGrammar` — period-constrained architectural styles (symmetry, bay rhythm, roof pitch, colonnade)
- `MonumentBlueprintProducer` — massing-level monument generation (nave, dome, towers, colonnades)

**Consumers:**
- `VillageBuilder` — village-level orchestration (delegates realization to `StructureExecutor`)
- `StructureExecutor` — single-structure realization authority

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

### ConstructionDirector (task authority)

The authoritative task system. Owns the task DAG, reservations, and completion transitions. Builders register with the director and claim tasks; the director reconciles settlement needs against its task catalog and reassigns work on completion/failure.

- Task lifecycle: `Pending` → `MaterialsSatisfied` → `PendingExecution` → `InProgress` → `Completed` (with `Blocked`/`Failed` recovery paths)
- `CompleteTask`: marks complete, releases reservations, fires `ConstructionEventBus.TaskCompleted`, reconciles needs, reassigns tasks
- Multi-builder partitioning via `PartitionTasks()` (estimates piece counts, sorts biggest-first, deals round-robin)

### ReservationManager

Task-tied spatial reservations, occupancy ledger, and conflict detection. `CanPlace`/`CommitPlacement` gate every piece placement against the directed task id. Reservations are released on completion or failure.

### SettlementNeedBoard

Demand-driven need registration and reconciliation against the director's task catalog. Needs (shelter, food, defense, production) drive the Surveyor to propose construction candidates.

### Surveyor

Surveys the world and proposes construction candidates from settlement needs. The bridge between demand (SettlementNeedBoard) and supply (ConstructionDirector tasks).

### LogisticsPlanner / ProductionPlanner

- **LogisticsPlanner** — creates haul jobs to supply build sites with materials; `SupplyTaskMaterials` hauls crafted materials from stockpiles to build sites.
- **ProductionPlanner** — demand-driven: when a construction task needs a crafted material (Plank, Brick, Timber) and no stockpile has it, looks up the recipe, finds the workstation, and creates haul jobs to bring raw inputs to the crafter. Closes the production loop.

### CompletionEffectsManager

Subscribes to `ConstructionEventBus.TaskCompleted`. Completed structures activate gameplay capacity:
- Production structures (sawmill, forge, brick bench, smelter) → spawn `CraftingBench` + re-discover workstations
- Housing (cottage, house) → increment housing capacity

### RepresentationCollapser

Subscribes to `ConstructionEventBus.TaskCompleted`. Collapses a completed wall segment's per-brick GameObjects into a single static `ModelRenderer` + `BoxCollider` representation, preserving structural metadata (`PlacedBricks`, `PiecesPlaced`, `TotalPieces`). Reduces draw calls and component overhead for large settlements.

### VillageBuilder (village orchestration)

Village-level orchestration only. Owns the build loop, multi-builder partitioning, save/resume, director registration, and MCP request handling. Delegates single-structure realization to `StructureExecutor`:
- `BuildTask` → `_executor.Execute(task, token)`
- MCP `RequestFinalizeWall` / `RequestDeconstructWall` → `_executor.FinalizeWall` / `DeconstructWall`

### StructureExecutor (single-structure realization)

Realizes one authorized `VillageBuildTask` / structure plan. Knows how to build a structure (walls, gates, roads, wells, markets, buildings). Does not own village planning, task scheduling, multi-builder partitioning, or village persistence. Introduced in Move 10 as a delegating facade; future moves physically relocate the build methods into this class.

### SpatialRegistry / ResourceRegistry

Authoritative spatial and resource state for the simulation. `SpatialRegistry` registers structural metadata for completed pieces; `ResourceRegistry` tracks stockpiles, sources, and `NearestStockpileWithSpace` / `NearestSource` queries.

### ConstructionEventBus

Pub/sub event system for construction state transitions (`TaskCompleted`, `TaskBlocked`, `TaskFailed`). Consumed by `CompletionEffectsManager`, `RepresentationCollapser`, and `VillageBuilderController`.

### Deterministic NLP pipeline

NPC-to-NPC structured coordination (AgentCommunication domain). Fully deterministic — zero LLM calls:
- Tokenizer, grammar parser, entity resolver, speech templates
- `ConversationManager` — async message delivery, exactly-once consumption
- `BeliefModel` — per-NPC self-beliefs, task history, reputation, trust, conversation state
- `IntentEnvelope` / `NpcActionDispatcher` — structured intent transmission, typed action verbs → domain authorities

### NPC intelligence (deterministic)

NPCs act on goals, needs, beliefs, memory, blackboard state, intents, construction tasks, reservations, and world state. `VillageBuilderController` is wired to `ConversationManager`, `ConstructionDirector`, and `ConstructionEventBus`. `LuteBuilderNpc` supports autonomous and agent-driven modes.

> **Note:** `NPCBrain.cs` and `BuildingCritique`/`BlueprintModifier` are **authoring/development tooling**, not runtime NPC behavior. They are not part of the runtime NPC intelligence path. `NPCConversation.cs` has been moved to `docs/history/NPCConversation.cs.legacy` and is compile-gated — do not resurrect it.

## Three-Domain Separation

NPC systems are split into three domains with separate execution machinery. They may share knowledge, but not execution paths:

1. **Agent cognition** (authoritative behavior) — `Intent`, `BeliefModel`, `NpcState`, capabilities, `ConstructionDirector` DAG + reservations, `SpatialRegistry` / `ReservationManager`. This is what moves NPCs and mutates the world.
2. **AgentCommunication** — NPC-to-NPC structured coordination. Deliberately constrained, highly reliable. Speech does not mutate inventories or tasks directly; it submits validated requests to domain authorities.
3. **PlayerDialogue** — Player-to-NPC dialogue (future, not yet implemented). Rich, contextual natural-language conversation. Can request world actions but cannot perform them; submits a validated request across a narrow bridge (`PlayerActionBridge` → typed `AgentRequest`), and the NPC's normal cognition decides whether/how to act.

## File Organization

```
sbox/code/Building/
├── Blueprint.cs                  ← IR: piece list + serialization
├── BlueprintValidator.cs         ← pre-execution validation
├── BlueprintModifier.cs          ← applies critiques to tasks (authoring tool)
├── BuildingCritique.cs            ← structured critique JSON payload (authoring tool)
├── BuildingGrammar.cs             ← BSP room subdivision producer
├── StyleGrammar.cs                ← architectural style constraints
├── MonumentBlueprintProducer.cs   ← massing-level monument producer
├── VillageBuilder.cs              ← village orchestration (delegates to StructureExecutor)
├── StructureExecutor.cs           ← single-structure realization authority (Move 10)
├── VillageBuilderController.cs    ← NPC body + FSM for village builder
├── VillageGrammar.cs              ← village layout generator + task type
├── VillageSaveData.cs             ← persistence (save/load/apply progress)
├── ConstructionDirector.cs        ← authoritative task DAG + reservations
├── ReservationManager.cs          ← task-tied spatial reservations
├── ConstructionEventBus.cs        ← pub/sub event system
├── CompletionEffectsManager.cs    ← completion → capacity (Move 8)
├── RepresentationCollapser.cs     ← completed walls → static mesh (Move 9)
├── SettlementNeedBoard.cs         ← demand-driven needs
├── SurveyorController.cs          ← surveys world, proposes construction
├── LogisticsPlanner.cs            ← haul jobs to supply build sites
├── ProductionPlanner.cs           ← demand-driven crafting orders (Move 7)
├── SpatialRegistry.cs             ← structural metadata registry
├── ResourceRegistry.cs            ← stockpiles, sources, nearest queries
├── ConstructionSelfRepair.cs      ← placement recovery
├── WallSegmentFinalizer.cs        ← wall state transitions
├── StructureDefinition.cs         ← structure definitions
├── StructuralPlacement.cs         ← placement helpers
├── NPCBrain.cs                    ← LLM bridge (authoring tool, not runtime NPC)
└── MultiBuilderProbe.cs           ← concurrency diagnostic
```

## Verification

Runtime verification is text-based (the agent has no vision):
- **MCP server** (`http://127.0.0.1:7269/mcp`): scene traces, object inspection, console commands, play mode control
- **Agent scripts**: `sbox_eyes.py` (spatial probing), `collision_probes.py` (collision checks), `scene_telemetry.py` (scene audit), `sbox_verify.ps1` (combined report), `gpt_eyes.py` (GPT-5 vision bridge)
- **Editor log** (`sbox-public-clean/game/logs/sbox-dev.log`): every `Lute:` `Log.Info` call appears with a timestamp

## Future Directions

- **BuildPlan:** `Blueprint → BlueprintCompiler → BuildPlan → ConstructionDirector → StructureExecutor → WorldAdapter`. Make `TotalPieces` authoritative from the compiled plan instead of "unknown until build completes."
- **Lute.Core:** Extract engine-independent simulation logic (construction state machines, dependency graph, need calculation, task scheduling, Blueprint model, resource economy, deterministic RNG, save schema) into a headless-testable assembly.
- **Deterministic replay:** Append-only simulation journal + world hashes; replay any night-run failure in accelerated time.
- **Test suite:** S&Box `UnitTests` directory + property/invariant testing over generated seeds.
- **CI:** Hosted CI for `Lute.Core` (build/test/property tests); self-hosted S&Box integration runner for engine-specific verification.
- **Spatial debug APIs:** Occupancy/reservation grid queries + in-world debug overlays.
- **Performance budgets:** Repeatable benchmarks + automatic soak tests with regression thresholds.
- **Persistence discipline:** Versioned save schema + migrations + corruption/recovery tests.
- **Blueprint IR enrichment:** Version, ID, Hash, Constraints, Dependencies, Anchors, Provenance.
- **Player dialogue:** `PlayerDialogueManager` / `PlayerDialogueParser` / `DialogueState` (future, separate from AgentCommunication).
