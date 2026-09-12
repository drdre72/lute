# Lute — Current State

> This is the single-page source of truth for what is true **right now**.
> Keep it current. Stable engine/API knowledge belongs in `AGENTS.md`.
> Historical reasoning belongs in `docs/decisions/`.

## Project

- Engine: S&Box / Source 2
- Language: C# / .NET 10
- Primary gameplay namespace: `Sandbox`
- Runtime NPC intelligence: deterministic only
- Runtime NPC LLM calls: prohibited
- Current development phase: cooperative construction
- Deception during construction: disabled

## Architecture

```text
Development AI / authoring tools
        |
        v
Structured proposals / Blueprint data
        |
        v
BlueprintValidator
        |
        v
ConstructionDirector / task scheduling
        |
        v
NPC construction agents
        |
        +--> deterministic NLP
        +--> goals / needs / beliefs / memory
        +--> SpatialBlackboard
        +--> reservations / dependencies
        |
        v
World executor
```

The authoritative construction representation is `Blueprint`. NPC speech is never itself the authority for world mutation.

## Current working systems

- Blueprint IR and serialization
- BlueprintValidator
- BuildingGrammar / StyleGrammar
- MonumentBlueprintProducer
- VillageBuilder and save/resume path
- SpatialBlackboard (positions, claims, AABB box reservations, messages)
- ConstructionDirector (authoritative task scheduling, dependencies, reservations)
- ReservationManager (task-tied reservations, occupancy ledger, conflict detection)
- ConstructionEventBus (pub/sub event system for construction state transitions)
- Deterministic NLP pipeline (tokenizer, grammar parser, entity resolver, speech templates)
- ConversationManager (async message delivery, exactly-once consumption)
- BeliefModel (self-beliefs, task history, reputation, trust, conversation state)
- VillageBuilderController (wired to ConversationManager, ConstructionDirector, ConstructionEventBus)
- LuteBuilderNpc autonomous and agent-drive modes
- LuteWatchtower from-scratch construction
- Neutral Market monument whitebox/detail pass
- Runtime MeshComponent construction fix
- In-game agent chat panel on `J`
- S&Box native MCP spatial probing through the existing `agent/sbox_eyes.py` workflow
- `.devin-context` session memory and hooks
- Lute context MCP server (`agent/lute_context_server.py`)
- Structured project memory (`LUTE_STATE.md`, `LUTE_CAPABILITIES.md`, ADRs)

## Current construction problems / risks

1. ~~NPC/executor synchronization~~ (RESOLVED): VillageBuilder now waits for NPC arrival before placing geometry. Flow: Claim (PendingExecution) → walk → AuthorizeExecution → build.
2. ~~Occupancy auto-scan~~ (RESOLVED): OccupancyScanner scans all static colliders at startup and registers them in the occupancy ledger (122 regions in current scene).
3. Builder NPC autonomous test placement can still interfere with agent-driven behavior.
4. Acropolis collision currently behaves as one imported collision hull and is not yet ideal for terrace traversal.
5. Agent movement can be affected by `PlayerController` friction/collision.
6. ~~Legacy fallback~~ (RESOLVED): All builders now consistently use the director path. Multi-candidate reservation tries alternate tasks when one has a spatial conflict.
7. ~~Walk time bottleneck~~ (RESOLVED): Builders now teleport to build sites instead of walking. Each builder has a distinct warp effect color (cyan/magenta/yellow).
8. **Task completion transition race** (ACTIVE): After a builder completes a director task, it now sets `CurrentTask = null` and `CurrentTaskIndex = -1` so the controller transitions to Idle and waits for the next claim. However, the controller's `HandleBuilding` state still relies on `CurrentTaskIndex` changing to detect task completion. If the builder claims the next task before the controller checks, the controller may miss the transition. The `CurrentTask = null` fix mitigates this, but the state machine should be made more robust.
9. **Build pace still bottlenecked** (ACTIVE): Even with teleportation, tasks complete in ~12 seconds each. The bottleneck is now the `BuildTask` async loop which places pieces at 1s intervals. With 273 tasks averaging ~16 pieces each, the full build takes ~73 minutes. Further acceleration would require reducing the per-piece interval or batching piece placement.
10. **Reservation conflicts on task_1** (PERSISTENT): Builder 2 consistently fails to reserve task_1 with a "reservation conflict". The multi-candidate loop handles this by trying the next task, but the root cause (overlapping estimated bounds between adjacent tasks) should be investigated. The `TouchTolerance` of 0.5 units may be too aggressive for tasks with large estimated bounds.

## Next architectural target

The deterministic construction-coordination layer is now implemented (Phases 1-5). NPC/executor synchronization, legacy fallback elimination, occupancy auto-scan, and teleportation are all done. Remaining refinements:

1. ~~NPC/executor state synchronization (ReadyAtSite gate)~~ (DONE)
2. ~~Auto-scan static world geometry into occupancy ledger~~ (DONE)
3. ~~Teleportation with per-builder warp effects~~ (DONE)
4. Deterministic conflict replanning (when reservation conflicts occur, replan deterministically)
5. Investigate task_1 reservation conflict root cause (overlapping bounds estimation)
6. Phase 6: Gameplay social systems (deception enabled only after construction is proven)

## Rules of interpretation

- Prefer repository state over model memory.
- Prefer `AGENTS.md` for stable S&Box/API facts.
- Prefer this file for current implementation status.
- Prefer ADRs for the reason a design decision was made.
- Never infer a capability merely because a file/type exists; verify the implementation and its tests/logs.
- Do not reintroduce runtime LLM inference into NPC behavior.
