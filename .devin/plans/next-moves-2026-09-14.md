# Lute — Next Moves (post night_run #1)

Date: 2026-09-14
Based on: night_run.json (100-minute run, commit 3bb24c7)

## Night run #1 results

```
Total tasks:     148
Completed:        51  (34%)
In-progress:       4
Pending:         93
ElapsedTime:    6000s (100 min)
```

Completed: all 46 walls, both gates (in-progress), 2 guardhouses, roads.
Pending: 72 cottages, 8 shops, chapel, tavern, smithy, well, market, storage.

## Issues identified

### 1. Piece-counting bug (HIGH — corrupts save/resume)

Two tasks show `PiecesPlaced > TotalPieces`:
- Chapel: placed=5309, total=1383
- cottage_5: placed=2461, total=18

And several tasks have `TotalPieces=0` despite being in-progress:
- Gate_South: placed=4381, total=0
- Gate_North: placed=6589, total=0

This means `TotalPieces` is not being set correctly when the task starts,
or is being overwritten. On resume, the builder would think the task is
done (placed >= total) or has no work (total=0). This breaks save/resume
fidelity — the core persistence promise of the project.

**Fix**: Audit where `TotalPieces` is set in `VillageBuilder` and
`ConstructionDirector`. Ensure it's set once when construction begins and
never overwritten. Add a guard: if `PiecesPlaced > TotalPieces`, clamp
`TotalPieces = PiecesPlaced` on load.

### 2. Adaptive cottages stuck pending (MEDIUM)

The 2 Surveyor-dispatched adaptive cottages (cottage_9c672373e65c,
cottage_a33b0b259d87) are still pending after 100 minutes. They have
`TotalPieces=0` — the builder never started them. This is likely the
same piece-counting bug (TotalPieces=0 means the task never entered
construction), combined with material gating (93 tasks competing for
2 crafters' output).

**Fix**: Fix the piece-counting bug first, then verify adaptive cottages
get materials and a builder claim.

### 3. Material throughput bottleneck (MEDIUM — design)

93 pending tasks, 2 crafters producing Plank/Brick. At current rates,
each cottage needs ~20 Plank + 6 Timber + 15 Brick. With 2 crafters
producing ~2 Plank/2s and ~1 Brick/3s, a cottage's materials take
~30-40 seconds to produce. 72 cottages × 40s = ~48 minutes of pure
crafting time, but haulers and builders add overhead. The 100-minute
run completed 51/148 tasks — roughly on pace but cottages are starved.

**Options**:
- Add a 2nd sawmill + 2nd carpenter (parallel plank production)
- Add a 2nd brick bench + 2nd mason (parallel brick production)
- Increase `YieldPerGather` so gatherers fill stockpiles faster
- Reduce cottage material requirements (fewer bricks per cottage)

Recommendation: add 1 more carpenter + 1 more mason (4 crafters total)
and increase gatherer carry capacity to 50. This roughly doubles
throughput without changing the architecture.

### 4. Builder reservation conflict spam (LOW — pre-existing)

`VillageBuilderNPC_3` spams reservation conflicts on `task_3` (Wall_W_9)
blocked by `VillageBuilderNPC_0`. The fallback logic should pick another
task but instead retries the same blocked task every tick. This wastes
CPU and floods logs.

**Fix**: In `VillageBuilderController`, after a reservation conflict,
add the blocked task to a per-builder cooldown list (don't retry for
N seconds). Or check `AssignedBuilder` before claiming.

## Proposed next moves (priority order)

### Move 1: Fix piece-counting bug (HIGH)
- Audit `TotalPieces` assignment in `VillageBuilder` and
  `ConstructionDirector`
- Ensure it's set once when construction starts (first brick placed)
- Add a save-load clamp: `TotalPieces = max(TotalPieces, PiecesPlaced)`
- Verify with a short run + save + resume

### Move 2: Gate 3.4 — failure injection and recovery
- Run Gate 3.3 normally
- At ~5 minutes, call `gate3_fail` (depletes wood source)
- Verify:
  - Lumberjacks detect depletion, find alternative source or idle
  - Pending wood-dependent tasks stall (no planks/timber)
  - Haulers fail gracefully (no crash, no infinite loop)
  - If a second wood source exists, gatherers redirect
  - If no alternative, construction stalls but doesn't deadlock
  - On source respawn/replenish, the chain resumes
- This is the professor's Gate 3.4 requirement

### Move 3: Scale crafting throughput (MEDIUM)
- Add 1 more carpenter + sawmill bench
- Add 1 more mason + brick bench
- Increase gatherer CarryCapacity to 50
- Verify night_run #2 completes more tasks in same time

### Move 4: Builder reservation cooldown (LOW)
- Add per-builder blocked-task cooldown
- Stop the reservation conflict spam
- Improves log readability and CPU efficiency

### Move 5: Night run #2
- Run overnight again with fixes applied
- Compare task completion count vs night_run #1 (51 completed)
- Target: 80+ completed tasks, adaptive cottages built

## What we're NOT doing yet (deferred)

- Gate 3.4 failure injection — after piece-count fix (Move 1)
- Animation polish — still deferred per professor's advice
- VillageBuilder → StructureExecutor separation — deferred
- Quartermaster — deferred
- Full autonomous lifecycle (bootstrap/production/stable/civic/defense)
- Player dialogue system
