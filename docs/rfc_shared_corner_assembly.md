# RFC: Shared Corner Assembly for Perimeter Masonry

## Why this change

The current corner implementation still treats each 90-degree junction as two independent wall segments that alternate ownership with `ShouldButt`, then tries to repair the joint with special header placement. The latest reproducibility run confirms that this produces a stable but visibly incorrect side profile: owner courses expose header ends while non-owner courses omit corner bricks, creating the repeating comb / shelf / gap pattern.

This RFC replaces the corner-specific `header + skip` behavior with a **shared corner masonry assembly** that owns the overlap region between two perpendicular wall segments. The wall centerlines remain canonical. Normal wall courses terminate at the shared assembly boundary. The shared assembly is tiled deterministically with ordinary masonry units and alternates orientation by course.

The goal is not another positional tweak. The goal is to make the corner a first-class topology object so rendering, validation, persistence, and MCP inspection all agree on the same geometry.

---

## Geometry model

The perimeter wall is 0.5 m thick and uses a 0.125 m depth module.

At a canonical 90-degree junction, each wall extends 0.25 m inward from its centerline. Therefore the geometric overlap of the two wall prisms is:

- `0.25 m x 0.25 m` in plan
- `2 x 2` depth-module cells at 0.125 m resolution

The previous draft proposal described this as a 0.5 m square / 4x4 core. That is too large for the actual intersection of two 0.5 m-thick perpendicular walls whose centerlines meet at the outside corner. The shared overlap core is **2 x 2 cells**.

The corner assembly should own this 2x2 cell core for every course.

---

## Architecture

Introduce one authoritative corner-placement representation consumed by both the builder and validator.

```csharp
public readonly struct CornerCell
{
    public readonly int U;
    public readonly int V;
    public readonly int Course;
}

public readonly struct CornerBrickPlacement
{
    public readonly BrickSlot Slot;
    public readonly Vector3 WorldCenter;
    public readonly float WorldYaw;
    public readonly Vector3 WorldSize;
    public readonly CornerCell[] OccupiedCells;
}

public sealed class CornerAssemblyPlan
{
    public string CornerId { get; init; }
    public VillageBuildTask WallA { get; init; }
    public VillageBuildTask WallB { get; init; }
    public Vector3 Junction { get; init; }
    public Vector3 InwardA { get; init; }
    public Vector3 InwardB { get; init; }
    public int Course { get; init; }
    public IReadOnlyList<CornerBrickPlacement> Placements { get; init; }
}
```

`BrickSlot` remains the logical masonry identity, but the corner placement record stores the actual world transform used by rendering and validation. This removes the current split where `PlacedBricks` records one thing while `PlaceCornerHeader` computes a different custom transform.

---

## Corner ownership rule

Remove corner ownership from the individual wall segments.

Current model:

```text
Wall A owns course -> Wall B skips -> A places header
Wall B owns next   -> Wall A skips -> B places header
```

Proposed model:

```text
Wall A terminates at corner-core boundary
Wall B terminates at corner-core boundary
CornerAssembly owns the 2x2 shared core on every course
```

This eliminates alternating empty rows by construction.

The normal walls still use running bond. Only the cells that geometrically belong to the shared overlap core are withheld from the two wall builders.

---

## Course tiling

The shared core is a 2x2 module grid. A standard full brick is approximately 2 cells x 1 cell in module-space. Therefore the 2x2 core can be tiled using two full bricks per course.

Even course:

```text
A A
A A
```

Use two full bricks aligned with Wall A.

Odd course:

```text
B B
B B
```

Use two full bricks aligned with Wall B.

In other words, each course contains exactly two full bricks occupying all four corner cells, and orientation alternates 90 degrees each course.

No special `BrickForm.Corner` model is required for the first implementation.

This is preferable because:

- it uses the existing brick asset;
- it preserves real brick proportions;
- it eliminates the need to scale a square custom brick;
- it creates an actual alternating masonry bond;
- it keeps all geometry on the existing module lattice.

`BrickForm.Corner` should only be introduced later if a specific visual quoin asset is desired.

---

## Exact world placement

For a corner with junction `J`, inward unit axes `A` and `B`, and module depth `D = BrickModuleY`:

The four corner-cell centers are:

```text
J + A*(D/2) + B*(D/2)
J + A*(3D/2) + B*(D/2)
J + A*(D/2) + B*(3D/2)
J + A*(3D/2) + B*(3D/2)
```

For an A-oriented course, combine cells along A into two full-brick placements:

```csharp
center0 = J + A * D + B * (D * 0.5f);
center1 = J + A * D + B * (D * 1.5f);
yaw = YawFrom(A);
```

For a B-oriented course:

```csharp
center0 = J + B * D + A * (D * 0.5f);
center1 = J + B * D + A * (D * 1.5f);
yaw = YawFrom(B);
```

Use the normal full brick body/model dimensions and the normal module-height anchor. Do not infer orientation from requested size.

---

## Changes to `VillageBuilder`

### 1. Stop using corner headers

Delete corner-specific execution paths based on:

- `IsCornerOwnerCourse`
- `PlaceCornerHeader`
- special `BrickOrientation.Header` placement from `BuildWallSegment`

`TryGetCornerFrame` can be retained and generalized because its corrected junction math is useful.

### 2. Replace `ShouldButt` with `ShouldReserveForCornerAssembly`

The wall builder should skip only brick slots whose module footprint intersects the shared 2x2 corner core.

This should be geometry-derived, not hard-coded by parity and edge side.

Conceptually:

```csharp
bool ShouldReserveForCornerAssembly(BrickSlot slot)
{
    var footprint = WallSlotToWorldFootprint(task, slot);
    return footprint overlaps CornerCore(task);
}
```

This removes the special-case asymmetry currently handled by `ShouldButtLeftFull`.

### 3. Build the corner once

Only one authoritative owner should instantiate the corner assembly, e.g. the lexically smaller wall name or a deterministic corner id.

```csharp
bool IsCornerAssemblyEmitter(VillageBuildTask a, VillageBuildTask b)
    => string.CompareOrdinal(a.Name, b.Name) < 0;
```

The other wall reserves the cells but does not spawn duplicates.

### 4. Preserve reconstruction determinism

Corner placements must be reconstructed from topology on reload exactly like normal wall bricks. Do not rely on scene-object discovery.

---

## `CornerBondResolver`

Refactor it from "choose special header slots" into "produce a shared corner assembly plan".

Suggested public API:

```csharp
public static bool TryResolveCorner(
    VillageBuildTask wallA,
    VillageBuildTask wallB,
    int course,
    out CornerAssemblyPlan plan);

public static IReadOnlyList<CornerBrickPlacement> PlacementsFor(
    Vector3 junction,
    Vector3 inwardA,
    Vector3 inwardB,
    int course);
```

The old `HeaderBond` behavior can remain as a legacy enum value temporarily, but runtime construction should stop using it once shared-corner assembly is enabled.

---

## Validator changes

The current validator reconstructs every `BrickSlot` using straight-wall grid rules, even for header slots that were placed with a custom world transform. That can report `IsValid=true` / `IsBonded=true` while the rendered geometry is visibly wrong.

Replace tag-based bond validation with cell/placement validation.

Add:

```csharp
public int ExpectedCornerCells { get; set; }   // 4 per course
public int OccupiedCornerCells { get; set; }
public int DuplicateCornerCells { get; set; }
public int ExteriorCornerCells { get; set; }
public int MissingCornerCells { get; set; }
public bool GeometryMatchesTopology { get; set; }
```

Per course, require:

```text
OccupiedCornerCells == 4
DuplicateCornerCells == 0
ExteriorCornerCells == 0
MissingCornerCells == 0
```

Across courses require orientation alternation:

```text
even -> A
odd  -> B
```

`BondDepth` may remain for compatibility, but it should be derived from actual corner-cell coverage / brick OBB placement, not from the mere presence of a slot tagged `Header`.

---

## MCP diagnostics

Extend `lute_get_corner_topology` with:

```text
cornerId
courseCount
missingCornerCells
duplicateCornerCells
exteriorCornerCells
geometryMatchesTopology
orientationAlternates
```

Add a detailed optional tool:

```text
lute_get_corner_course(cornerId, course)
```

returning:

```json
{
  "cornerId": "SW",
  "course": 12,
  "junction": [x,y,z],
  "orientation": "Wall_S",
  "cells": [
    {"u":0,"v":0,"occupied":true,"brickId":"..."},
    {"u":1,"v":0,"occupied":true,"brickId":"..."},
    {"u":0,"v":1,"occupied":true,"brickId":"..."},
    {"u":1,"v":1,"occupied":true,"brickId":"..."}
  ],
  "missing": 0,
  "duplicates": 0,
  "exterior": 0
}
```

This gives Devin an exact spatial proof rather than forcing visual inference.

---

## Migration / rollout

1. Keep canonical wall centerlines and the corrected `TryGetCornerFrame` math.
2. Add corner-core detection and cell representation without changing rendering.
3. Add deterministic `CornerAssemblyPlan` generation and unit-test it independently.
4. Change wall construction so core-intersecting wall slots are reserved instead of spawned.
5. Spawn the two-brick shared assembly per course from one deterministic emitter.
6. Update reconstruction/persistence.
7. Update `WallCornerTopologyProbe` to validate cells and actual placements.
8. Expose cell metrics over MCP.
9. Remove legacy runtime header placement only after all four corners pass.

---

## Acceptance criteria

All four perimeter corners must pass these checks for a full-height test wall:

- canonical wall endpoints meet within 2 cm;
- no wall object or corner object extends outside either exterior wall plane beyond 1 mm tolerance;
- every course has exactly 4/4 corner cells occupied;
- zero duplicate corner-cell claims;
- zero missing corner cells;
- no alternating horizontal slit / comb pattern in side views;
- course orientation alternates A/B/A/B;
- rebuild from persistence reproduces identical corner topology;
- `lute_get_corner_topology` reports `GeometryMatchesTopology=true` for all four corners;
- existing non-corner running bond is unchanged.

---

## Non-goals

Do not, in this PR:

- move West/East/North/South wall centerlines;
- loosen endpoint tolerance;
- add raycast-driven corner correction;
- introduce LLM reasoning into placement;
- add arbitrary NPC wall nudging;
- create a custom corner model unless the ordinary-brick 2x2 assembly proves visually inadequate.

The purpose is to make the corner deterministic, modular, and inspectable using the same no-LLM construction architecture as the rest of Lute.
