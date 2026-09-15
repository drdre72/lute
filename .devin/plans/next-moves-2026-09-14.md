# Lute — Next Moves (post night_run #1, revised per professor review)

Date: 2026-09-14
Based on: night_run.json (100-minute run, commit 3bb24c7)
Revised: incorporates professor's structural review

## Night run #1 results

```
Total tasks:     148
Completed:        51  (34%)
In-progress:       4
Pending:         93
ElapsedTime:    6000s (100 min)
```

## Professor's assessment

The core authority chain is sound:

```
World → SpatialRegistry/ResourceRegistry → SettlementNeedBoard
     → Surveyor → ConstructionDirector → Logistics/Professions
     → physical NPC execution → world mutation
```

Preserve all of that. The problems are at the seams where the new
autonomous layer grew around older village-builder assumptions.

### Three prerequisites before autonomous expansion

> **one persistent task truth, one global simulation clock,
> and a correct planned-vs-completed settlement-need lifecycle.**

## Structural issues (professor's severity)

| Severity | Issue |
|----------|-------|
| Critical before expansion | Two task truths: `VillageBuilder.Tasks` vs `ConstructionDirector` |
| Critical before population scaling | Shared simulation clocks ticked by every builder NPC |
| High | Settlement need counts planned structures as existing |
| High | Planner validates approximate footprint, executor builds different one |
| High for true autonomy | Benchmark still creates needs and supply work |
| High for settlement growth | Building completion doesn't change simulation capabilities |
| Medium | Production is capability-driven, not demand-driven |
| Medium before larger towns | Brick-by-brick runtime representation will become expensive |
| Medium | Legacy scheduling remains alongside Director scheduling |

## Revised priority order (per professor)

### 1. Unify task authority + fix piece/persistence accounting

**Problem**: `VillageBuilder.Tasks` (fixed village, normal persistence,
reconstruction, IsComplete) vs `ConstructionDirector.AllTasks()` (fixed +
adaptive). The night-run save already works around this by reading from
`ConstructionDirector.AllTasks()`, but the two notions can diverge:
builder can think village is complete while Director still has adaptive
cottages.

**Also**: `PiecesPlaced > TotalPieces` on Chapel (5309/1383) and
cottage_5 (2461/18). Several in-progress tasks have `TotalPieces=0`.
This corrupts save/resume fidelity.

**Fix**:
- Make `ConstructionDirector` the task catalog of record, not merely
  the scheduler
- Persistence serializes Director's task catalog, including adaptive
  tasks
- `VillageBuilder.Tasks` becomes an executor-facing view, then loses
  authority
- Fix `TotalPieces` assignment — set once when construction starts,
  never overwritten
- Add save-load clamp: `TotalPieces = max(TotalPieces, PiecesPlaced)`
- Verify with short run + save + resume

### 2. Move global clocks to one simulation ticker

**Problem**: Every `VillageBuilderController.OnFixedUpdate()` calls:
```csharp
SpatialBlackboard.Update(Time.Delta);
ConstructionEventBus.Update(Time.Delta);
BuilderLivenessRegistry.TickAll(Time.Delta);
```
These are global static systems that do `CurrentTime += deltaTime`.
So: 1 builder → clock advances 1×, 4 builders → 4×, 8 builders → 8×.
Distorts claim expiry, message expiry, event timestamps, liveness
thresholds, cooldowns based on workforce size.

**Fix**: Create one scene-level component:
```csharp
LuteSimulationTicker : Component
{
    OnFixedUpdate()
    {
        SpatialBlackboard.Update(Time.Delta);
        ConstructionEventBus.Update(Time.Delta);
        BuilderLivenessRegistry.TickAll(Time.Delta);
    }
}
```
NPCs publish positions individually but do NOT advance global time.
Restores population-independent determinism.

### 3. Correct SettlementNeed lifecycle

**Problem**: `SettlementNeedBoard.DispatchRequest()` increments
`ExistingCount` immediately when Surveyor dispatches to construction.
So need is "fulfilled" before structures exist. If both fail, settlement
still believes it has housing.

**Fix**: Separate the counts:
```
ExistingCount     = completed functional structures
PlannedCount      = dispatched / pending construction
InProgressCount   = actually being built
FailedCount       = failed/cancelled
```
Duplicate suppression uses `Existing + Planned + InProgress`.
Need satisfaction comes from completed functional capacity.
Listen to `TaskCompleted`, `TaskFailed`, `TaskCancelled` and reconcile
the originating need.

**Also**: Replace `Guid.NewGuid()` in `SettlementNeed` and Surveyor
adaptive task names with deterministic counters if replay determinism
matters.

### 4. Make site planning consume authoritative structure definition

**Problem**: Need board tells Surveyor a cottage requires ~4×4 cells.
Surveyor creates adaptive building with `BaseWidth/BaseHeight/
WealthFactor` and the building grammar determines actual structure
afterward. Validator geometry and rendered geometry have two different
truths (same class as the corner bug fixed earlier).

**Fix**: Before Surveyor validates a site, compile an authoritative
local structure description:
```
StructureDefinition
    → Blueprint / BuildPlan
    → exact local footprint
    → exact bounds
    → exact material bill
    → estimated work
    → required capabilities
```
Then:
```
Surveyor validates BuildPlan.Bounds
ReservationManager reserves BuildPlan.Bounds
ConstructionDirector uses BuildPlan.Materials
StructureExecutor realizes BuildPlan geometry
```
One definition. Use existing `Blueprint` architecture, don't invent a
competing system.

### 5. Move benchmark orchestration into production services

**Problem**: `Gate3Benchmark` clears/updates `SettlementNeedBoard`,
manually injects shelter need after 30s, periodically calls
`SupplyUnsatisfiedTasks()`. Fine for Gate 3, but before calling the
settlement autonomous, those functions need production owners.

**Fix**: Desired architecture:
```
SettlementStateEvaluator → SettlementNeedBoard
Construction demand → LogisticsSupplyPlanner → LogisticsBoard
```
`Gate3Benchmark` only does: configure scenario, inject test failure,
assert outcomes, report results. No production simulation should stop
functioning if `Gate3Benchmark` is removed from the scene.

### 6. Run Gate 3.4 after those changes

Deplete wood, destroy a workstation, block a road, kill a worker.
Verify the system stalls explainably, replans when possible, and
resumes. Remains an excellent test — but run it AFTER the architecture
hardening, not before.

### 7. Make production demand-driven before multiplying workers

Gatherers currently select nearest gatherable source rather than
responding to settlement demand. Resource pressure should generate
production orders that lumberjacks/quarrymen/crafters/haulers claim.
Adding a second carpenter becomes a response to measured capacity,
not a benchmark constant.

### 8. Add completion effects + autonomous lifecycle

```
TaskCompleted → StructureActivation → semantic capability enters world
```
Examples:
- Sawmill → WorkstationRegistry gains sawmill (+worker slots, +plank/timber capacity)
- Storage → ResourceRegistry gains stockpile (+storage capacity)
- Cottage → SettlementPopulation gains housing capacity
- Smithy → forge/smelter workstation available
- Well → water capacity/source available

This closes the loop: WORLD STATE → NEED → BUILD → WORLD STATE CHANGES
→ NEED DISAPPEARS. Without it, adaptive planning is an open loop.

Abstraction:
```csharp
StructureDefinition
{
    Blueprint
    SiteRequirements
    MaterialRequirements
    CompletionEffects
}
```

### 9. Generalize representation collapse (before very large settlements)

Keep individual bricks while construction is visible. Collapse
completed buildings/roads into efficient static render/collision
representations while preserving structural metadata. Otherwise
brick-by-brick GameObject counts become the scale ceiling.

### 10. Extract StructureExecutor from VillageBuilder

Don't interrupt the current stable build to rewrite today, but don't
defer until far after autonomous growth. Once Director is the sole task
catalog, extraction is straightforward: `VillageGrammar` stays as the
fixed benchmark planner; `StructureExecutor` knows how to realize one
authorized plan; it no longer knows what a "village" is.

## What we're NOT changing

- Animation strategy — walking-only/bare-bones is appropriate.
  Establish the boundary now: simulation state → NpcActivity.Building
  → animation presentation. Animations observe logical actions, never
  authorize them.

## Deferred (not started)

- Quartermaster
- Full autonomous lifecycle (bootstrap/production/stable/civic/defense)
- Player dialogue system
- Deception (disabled during construction phase)

## Acceptance criterion

> No production simulation should stop functioning if
> `Gate3Benchmark` is removed from the scene.

## Implementation order (immediate)

1. **Unify task authority + fix piece accounting** (Move 1)
2. **One simulation ticker** (Move 2)
3. **SettlementNeed lifecycle** (Move 3)
4. **Authoritative structure definition for Surveyor** (Move 4)
5. **Move benchmark orchestration to production services** (Move 5)
6. **Gate 3.4** (Move 6)
7. **Demand-driven production** (Move 7)
8. **Completion effects** (Move 8)
9. **Representation collapse** (Move 9)
10. **StructureExecutor extraction** (Move 10)
