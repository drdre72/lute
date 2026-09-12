# PR Proposal: Construction Hardening V2

Status: **Draft / implementation guide**  
Target branch: `main`  
Scope: construction initialization, task-completion authority, piece-level occupancy, reservation diagnostics  
Out of scope: build-speed tuning, gameplay deception, social-system expansion

## Why this PR exists

The recent ConstructionDirector / ReservationManager work materially improved builder coordination, but the current runtime still has three correctness gaps that should be fixed before optimizing build speed:

1. Static occupancy is scanned successfully and then cleared by builder initialization.
2. `VillageBuilderController` still has a stale completion path that can report the wrong task complete after a fast re-claim.
3. Occupancy is committed at coarse task-reservation granularity rather than at the actual spawned-piece granularity.

The desired invariant is:

> **Only the ConstructionDirector/executor may mutate task completion, and no construction world mutation may occur without an active directed task, a valid reservation, and a fresh piece-level occupancy check.**

NPC speech and visible controller state are projections of simulation state; they are never construction authority.

---

## 1. Fix initialization order so static occupancy survives

### Current failure

`LuteWorld.Build()` currently:

1. builds static world geometry,
2. calls `OccupancyScanner.ScanScene(Scene)`,
3. spawns village builders.

Then `VillageBuilder.OnStart()` for builder 0 calls:

```csharp
ConstructionDirector.Reset();
```

`ConstructionDirector.Reset()` calls `ReservationManager.Reset()`, which clears `_occupied`. The scan log can therefore report 122 registered regions while the live construction run no longer has those regions in its occupancy ledger.

### Required change

Reset construction runtime state exactly once, **before** static-world occupancy is scanned.

#### `LuteWorld.Build()` draft

```csharp
public GameObject Build()
{
    // Static ConstructionDirector/ReservationManager state can survive a
    // previous play session. Reset once at world-build entry, before any
    // current-session occupancy is registered.
    ConstructionDirector.Reset();

    var root = Scene.CreateObject( true );
    root.Name = "Sanctuary";
    root.WorldPosition = Vector3.Zero;

    // ... build world geometry ...

    BuildVillage( root );

    // This scan must happen AFTER the reset and AFTER static geometry exists.
    OccupancyScanner.ScanScene( Scene );

    // Spawning builders must not reset construction state again.
    NPCSpawner.SpawnAll( Scene, root );
    return root;
}
```

#### `VillageBuilder.OnStart()` draft

Delete this block entirely:

```csharp
if ( BuilderId == 0 )
    ConstructionDirector.Reset();
```

### Acceptance criteria

- `ConstructionDirector.Reset()` occurs once per world-build/session initialization.
- `OccupancyScanner` runs after that reset.
- Spawning/starting builders does not clear occupancy.
- Immediately after all builders start, `ReservationManager.GetOccupiedRegions().Count` still includes the static scan regions.
- Update `LUTE_STATE.md`: occupancy auto-scan remains **PARTIAL** until this is verified in a live editor run.

---

## 2. Remove controller-side task completion authority

### Current failure

`VillageBuilder` already completes the exact directed task:

```csharp
ConstructionDirector.CompleteTask( directed.Id );
```

But `VillageBuilderController.HandleBuilding()` still detects task-index changes and calls `ReportTaskComplete()`. `ReportTaskComplete()` reads `Builder.CurrentTask.Name` at call time and sends a `BlackboardOperation.Complete` transaction.

If the executor has already claimed the next task before the controller's next tick, `Builder.CurrentTask` can point at the **new task**, causing the controller to report the wrong task as complete.

This path should be deleted for director-controlled construction rather than timing around it.

### Required authority boundary

- `VillageBuilder` / executor: may call `ConstructionDirector.CompleteTask(exactDirectedTaskId)`.
- `ConstructionDirector`: authoritative task state.
- `VillageBuilderController`: may authorize arrival/execution and react to construction events, but may not complete a director task.

### Controller state should track exact directed task id

Add:

```csharp
private string _activeDirectedTaskId;
```

When the NPC arrives:

```csharp
if ( UseDirector && !string.IsNullOrEmpty( Builder.CurrentDirectedTaskId ) )
{
    var taskId = Builder.CurrentDirectedTaskId;

    if ( !ConstructionDirector.AuthorizeExecution( taskId, _npcId ) )
    {
        Log.Warning( $"Lute: '{_npcId}' could not authorize {taskId}." );
        Controller.WishVelocity = Vector3.Zero;
        return;
    }

    _activeDirectedTaskId = taskId;
    State = NpcState.Building;
    _stateTimer = 0;
    Controller.WishVelocity = Vector3.Zero;
    return;
}
```

### Transition out of Building from the exact TaskCompleted event

`OnConstructionEvent` should own the director-mode visual transition:

```csharp
case ConstructionEventType.TaskCompleted:
{
    if ( UseDirector && evt.TaskId == _activeDirectedTaskId )
    {
        _activeDirectedTaskId = null;
        _buildingTaskIndex = -1;
        State = Builder.IsComplete
            ? NpcState.VillageComplete
            : NpcState.Idle;
        _stateTimer = 0;
        Controller.WishVelocity = Vector3.Zero;
    }

    // Existing observed-reputation handling may remain below this.
    break;
}
```

### `HandleBuilding()` director-mode behavior

For director-controlled builders, do **not** infer completion from `CurrentTaskIndex` or `CurrentTask` names:

```csharp
void HandleBuilding()
{
    Controller.WishVelocity = Vector3.Zero;

    if ( UseDirector )
    {
        // Exact TaskCompleted events drive the transition.
        // Do not call ReportTaskComplete() here.
        return;
    }

    // Legacy/non-director logic may remain below if still required.
}
```

### Delete/retire director use of `ReportTaskComplete()`

If `ReportTaskComplete()` remains for a non-director path, it must never send a director completion request when `UseDirector == true`.

### Harden the transaction API anyway

Even after removing the stale controller path, `ConstructionDirector.HandleComplete()` should reject unauthorized completion requests.

Draft:

```csharp
static BlackboardResult HandleComplete( BlackboardRequest req )
{
    var task = ResolveTask( req.Key );
    if ( task == null )
        return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );

    int builderId = FindBuilderByNpc( req.Actor );
    if ( builderId < 0 )
        return BlackboardResult.Fail( $"builder not registered: {req.Actor}" );

    if ( task.AssignedBuilder != builderId )
        return BlackboardResult.Fail( $"{req.Actor} does not own {task.Id}" );

    if ( !_builders.TryGetValue( builderId, out var state ) ||
         state.CurrentTaskId != task.Id )
        return BlackboardResult.Fail( $"{task.Id} is not {req.Actor}'s current task" );

    if ( task.Status != TaskStatus.InProgress )
        return BlackboardResult.Fail( $"{task.Id} is not in progress" );

    CompleteTask( task.Id );
    return BlackboardResult.Ok();
}
```

This transaction hardening is defense-in-depth. The executor's direct completion path remains the normal trusted path.

### Acceptance criteria

- No director-mode controller code calls `BlackboardOperation.Complete`.
- Completion uses exact `DirectedTask.Id`, never current name/index inference.
- A stale completion request for a newly claimed task is rejected.
- A controller can miss several frames without corrupting task completion.

---

## 3. Wire the piece-level placement gate into the executor

### Current failure

`ReservationManager.CanPlace(taskId, bounds, out reason)` and `CommitPlacement(taskId, bounds, source)` exist, but the village executor does not currently use them at the final `SpawnBox()` boundary.

Instead, completed tasks commit the whole coarse `ReservationBounds` as occupied. That makes empty interior/workspace look like solid geometry and creates false positives as construction density increases.

### Required placement invariant

For director-controlled live construction:

```text
active exact task
  + active reservation
  + piece bounds clear now
  -> spawn piece
  -> commit exact piece bounds
```

### Draft `SpawnBox()` shape

The occupancy bounds must be calculated **after** converting the requested base/top position into the actual box-center position.

```csharp
void SpawnBox( Vector3 worldPos, Vector3 size, string materialPath,
    bool collides, GameObject parent )
{
    // Walls: base at z=0 (lift center). Floors: top at z=0 (lower center).
    if ( size.z > FloorThickness * 1.5f )
        worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
    else
        worldPos = worldPos.WithZ( worldPos.z - size.z * 0.5f );

    var half = size * 0.5f;
    var pieceBounds = new BBox( worldPos - half, worldPos + half );

    if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
    {
        if ( !ReservationManager.CanPlace(
            CurrentDirectedTaskId, pieceBounds, out var reason ) )
        {
            throw new ConstructionPlacementBlockedException(
                $"{CurrentDirectedTaskId} placement blocked at {worldPos}: {reason}" );
        }
    }

    var go = Scene.CreateObject( false );
    go.Name = $"Village_{CurrentTask?.Name ?? "piece"}_{_totalPiecesPlaced}";
    go.SetParent( parent );
    go.WorldPosition = worldPos;

    // existing MeshComponent / PolygonMesh construction...

    go.Enabled = true;

    if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
    {
        ReservationManager.CommitPlacement(
            CurrentDirectedTaskId,
            pieceBounds,
            go.Name );
    }
}
```

### Avoid double-counting a finished task's coarse reservation as occupied

Once piece-level occupancy is live, `ReservationManager.Release()` should **not** commit the whole task `ReservationBounds` for tasks that already have exact piece occupancy.

One low-risk transition is to add:

```csharp
static bool HasPieceOccupancy( string taskId ) =>
    _occupied.Values.Any( o => o.TaskId == taskId &&
        !string.Equals( o.Source, $"task:{taskId}", StringComparison.Ordinal ) );
```

Then in `Release()`:

```csharp
if ( task.Status == TaskStatus.Complete &&
     task.ReservationBounds.HasValue &&
     !HasPieceOccupancy( task.Id ) &&
     !_occupied.Values.Any( o => o.TaskId == task.Id ) )
{
    // Compatibility fallback only for executors not yet committing pieces.
    CommitPlacement( task.Id, task.ReservationBounds.Value, $"task:{task.Id}" );
}
```

Once every construction executor uses exact piece occupancy, remove the coarse fallback entirely.

### Exception handling

`VillageBuilder.BuildLoop()` should catch `ConstructionPlacementBlockedException` around `BuildTask()` and call the director's failure/block path rather than marking `VillageBuildTask.Status = 2`.

Draft:

```csharp
try
{
    await BuildTask( task, token );
}
catch ( ConstructionPlacementBlockedException ex )
{
    Log.Warning( $"Lute: {directed.Id} blocked during placement: {ex.BlockReason}" );
    ConstructionDirector.FailTask( directed.Id, ex.BlockReason );

    CurrentDirectedTaskId = null;
    CurrentTask = null;
    CurrentTaskIndex = -1;
    continue;
}
```

If the current `FailTask` retry semantics are not appropriate for occupancy conflicts, add a dedicated `BlockTask(taskId, reason)` path instead of treating deterministic occupancy as an execution crash.

### Acceptance criteria

- `CanPlace()` is called immediately before every live directed `SpawnBox()`.
- Failed placement does not mutate the world and does not advance `PiecesPlaced`.
- Successful placement commits exact piece AABB occupancy.
- Same-task pieces remain allowed to join/interlock.
- Other-task/static occupied geometry blocks placement.

---

## 4. Instrument reservation conflicts before changing tolerances

Do **not** solve the persistent `task_1` conflict by blindly increasing `TouchTolerance`.

`TouchTolerance = 0.5` S&Box units is only about 0.013 m. The larger source of false positives is coarse task-level reservation geometry.

Add structured diagnostics whenever reservation acquisition fails.

Suggested data:

```csharp
public sealed class ReservationConflictInfo
{
    public string RequestTaskId { get; init; }
    public string RequestTaskName { get; init; }
    public string RequestTaskType { get; init; }
    public BBox RequestBounds { get; init; }

    public string BlockerTaskId { get; init; }
    public string BlockerOwner { get; init; }
    public string BlockerSource { get; init; }
    public BBox BlockerBounds { get; init; }

    public string ConflictKind { get; init; } // "reservation" or "occupied"
}
```

At minimum log:

```text
request task_1 Wall_E_? type=wall bounds=[...]
blocked by task_? owner=VillageBuilderNPC_? bounds=[...]
kind=reservation
intersection size=(x,y,z)
```

This tells us whether `task_1` is:

- genuinely colliding,
- an intended structural join,
- overlapping a static object,
- or a coarse-bounds false positive.

### Reservation geometry follow-up

Keep task reservations as **work coordination**, not permanent solidity.

Longer-term model:

- `BuildFootprint`: actual geometry footprint or conservative union of pieces.
- `AccessFootprint`: optional worker staging area.
- `AllowedJoinZones`: endpoints/support regions where structural tasks may intentionally touch/intersect.

For walls, prefer bounds derived from their actual generated pieces over a fixed `10m x 3m x 6m` task box.

---

## 5. Stop depending on `CurrentTaskIndex` for multi-builder correctness

Additional architectural observation: `NPCSpawner.SpawnVillageBuilder()` creates a separate `VillageBuilder` component for each NPC. Each component generates its own `Tasks` list, while builder 0 is the one that registers the director's `VillageBuildTask` instances.

That means a director-returned `BuildTask` is not guaranteed to be reference-equal to an entry in another builder component's local `Tasks` list. Therefore:

```csharp
int idx = Tasks.IndexOf( task );
```

must not be used as a correctness identity for director-controlled construction.

The exact `DirectedTask.Id` should be the sole runtime identity across:

- assignment,
- reservation,
- arrival authorization,
- placement,
- completion,
- controller visual state,
- deterministic speech/events.

`CurrentTaskIndex` may remain for UI/debugging or legacy mode only.

A later cleanup should consider replacing per-builder duplicated task lists with a shared director-owned task registry plus per-builder executor view.

---

## 6. Do not optimize build pace in this PR

The 1-second piece delay is visible but not currently the highest-risk defect.

Do not change `BuildInterval` or batch placement until the following are verified:

1. static occupancy survives initialization,
2. controller cannot complete tasks,
3. exact directed task identity drives state,
4. piece-level placement gate is active,
5. `task_1` diagnostics explain the remaining conflict.

After correctness is proven, build speed can safely become a tuning variable, e.g. 0.1-0.25 seconds/piece or controlled batching.

---

## Verification gate

Before marking this PR ready:

- [ ] `dotnet build sbox/code/lute.csproj`
- [ ] run repo S&Box verification script
- [ ] start clean play session with 3 builders
- [ ] confirm static scan count remains unchanged after builders start
- [ ] confirm each controller tracks exact directed task id
- [ ] confirm no director-mode `BlackboardOperation.Complete` originates from controller
- [ ] deliberately create stale completion request and verify rejection
- [ ] deliberately place a static blocker in a task footprint and verify no world mutation occurs
- [ ] confirm successful pieces populate occupancy individually
- [ ] capture structured diagnostics for persistent `task_1` conflict
- [ ] run MCP spatial probes/collision probes
- [ ] confirm no construction deception paths are enabled

## Expected final flow

```text
world session reset
    -> build static geometry
    -> scan static occupancy
    -> spawn/register builders
    -> director assigns exact task id
    -> reserve work footprint
    -> NPC moves/warps to staging point
    -> controller authorizes exact task id
    -> executor computes exact piece bounds
    -> placement gate checks occupancy
    -> world mutation
    -> exact piece occupancy committed
    -> executor completes exact task id
    -> director releases work reservation
    -> TaskCompleted event drives controller back to idle
```

**Authority:** Director/executor  
**Identity:** `DirectedTask.Id`  
**Specification:** Blueprint / deterministic task data  
**Ground truth:** live world + exact occupancy ledger  
**NPC conversation:** projection of state only
