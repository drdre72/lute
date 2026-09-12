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

1. ~~NPC/executor synchronization~~ (RESOLVED): VillageBuilder waits for NPC arrival before placing geometry. Flow: Claim (PendingExecution) → teleport → AuthorizeExecution → build.
2. ~~Occupancy auto-scan~~ (RESOLVED): OccupancyScanner scans static colliders at startup and reset occurs before the scan so occupancy survives builder startup.
3. Builder NPC autonomous test placement can still interfere with agent-driven behavior.
4. Acropolis collision currently behaves as one imported collision hull and is not yet ideal for terrace traversal.
5. Agent movement can be affected by `PlayerController` friction/collision.
6. ~~Legacy fallback contention~~ (RESOLVED): director path is authoritative and multi-candidate reservation can try alternate work.
7. ~~Walk time bottleneck~~ (RESOLVED): builders teleport to build sites with per-builder warp effects.
8. ~~Task completion transition race~~ (RESOLVED): controller uses exact `DirectedTask.Id` event-driven completion; `TaskBlocked` and `TaskFailed` also clear the active directed task and return to Idle.
9. **Build pace / object-count bottleneck** (ACTIVE): realistic brick walls now use ~5,151 placements per 10 m wall segment at 0.5 s per brick. The old ~25-minute full-village estimate from the 0.1 s / low-piece-count implementation is no longer valid. Final performance architecture should batch/instance completed brick courses or segments while preserving brick-by-brick construction progress.
10. ~~Reservation conflicts on task_1~~ (RESOLVED): per-axis structural join tolerance handles wall corners and other intended joins without globally weakening collision checks.
11. ~~HandleComplete hardening~~ (RESOLVED): stale, foreign, unregistered, and non-in-progress completion requests are rejected.
12. ~~Piece-level occupancy gate~~ (RESOLVED): `SpawnBox()` calls `CanPlace()` before world mutation and commits exact occupancy after successful placement.
13. ~~Rotation-aware wall geometry~~ (RESOLVED): yaw is applied to render geometry and piece AABBs.
14. **Road direction correction** (FIX APPLIED ON `brick-wall-hardening`, PENDING RUNTIME VERIFY): road tiles now advance perpendicular to the configured width rotation so main roads extend N/S and cross streets E/W.
15. ~~AuthorizeExecution validation~~ (RESOLVED): reservation ownership is revalidated before switching to InProgress.
16. ~~Road↔cottage layout conflicts~~ (RESOLVED): grammar clearance rules eliminate the previously observed road/cottage conflicts.
17. ~~Building-to-building overlap~~ (RESOLVED for known cases): building spacing and special-building clearance were increased; Chapel was relocated.
18. ~~Deterministic replanning~~ (RESOLVED): active-task blockers defer work instead of consuming retries; genuine failures still use bounded retries.
19. **Remaining layout conflicts** (ACTIVE): `task_235` and `task_199` have been observed failing because actual spawned building footprints can exceed declared `BaseWidth`/`BaseHeight`. Deeper fix requires matching grammar clearance to real blueprint footprints.
20. **Brick renderer scale mismatch** (FIX APPLIED ON `brick-wall-hardening`, PENDING RUNTIME VERIFY): `models/dev/box.vmdl` is treated as a 50-unit cube. `SpawnBox()` now uses `WorldScale = requestedSize / 50`, so renderer, collider, and occupancy dimensions share one world-size contract.
21. **Brick vertical anchoring** (FIX APPLIED ON `brick-wall-hardening`, PENDING RUNTIME VERIFY): realistic short bricks no longer fall through the old height-based wall/floor heuristic; wall bricks use explicit base anchoring.
22. **Brick workload estimation** (FIX APPLIED ON `brick-wall-hardening`): VillageBuilder estimates ~5k placements per wall segment, uses that for partitioning, and propagates the corrected value into registered `DirectedTask.EstimatedPieces`.
23. **Builder eye-camera diagnostics** (PARTIAL): citizen child bodies now use local zero instead of world zero after parenting. Camera local position remains `(0,0,64)` until live inspection confirms whether a forward offset is still needed.
24. **Brick material identity** (ACTIVE): `brick_wall.vmat` still references the stone texture set. Do not treat the reported `stone_wall` name as proven engine deduplication until the override/resource path is visually verified.

## Next architectural target

The deterministic construction-coordination layer is largely implemented and hardened. Current priorities are:

1. Runtime-verify the `brick-wall-hardening` scale/anchor/road fixes.
2. Verify rendered brick bounds, collider bounds, and occupancy bounds all match.
3. Verify corrected ~5k wall estimates distribute work sensibly across three builders.
4. Design brick rendering/collision batching so brick-by-brick progress does not require hundreds of thousands of permanent GameObjects/colliders/occupancy records.
5. Resolve remaining blueprint-footprint layout conflicts (`task_199`, `task_235`).
6. Verify/fix brick material resources after geometry scale is proven.
7. Phase 6 social gameplay only after construction correctness/performance is stable; deception remains disabled during construction validation.

See `BRICK_WALL_ISSUES.md` and `BRICK_WALL_FIX_PASS.md` for the detailed brick-wall diagnosis, applied repair checklist, and verification steps.

## Rules of interpretation

- Prefer repository state over model memory.
- Prefer `AGENTS.md` for stable S&Box/API facts.
- Prefer this file for current implementation status.
- Prefer ADRs for the reason a design decision was made.
- Never infer a capability merely because a file/type exists; verify implementation and runtime behavior.
- A source-level fix marked `PENDING RUNTIME VERIFY` is not considered resolved until compile/play/spatial checks pass.
- Do not reintroduce runtime LLM inference into NPC behavior.
