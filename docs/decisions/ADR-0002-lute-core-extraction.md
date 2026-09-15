# ADR-0002 — Lute.Core Extraction and Coordinate Contract

- Status: Accepted
- Date: 2026-09-14

## Context

Lute's deterministic simulation logic (ConstructionDirector, ReservationManager, SettlementNeedBoard, Blueprint) is currently coupled to S&Box engine types: `Sandbox.Vector3`, `Sandbox.BBox`, `Sandbox.Diagnostics.Log`, and static engine services. This coupling prevents:

- Running the pure simulation logic on vanilla .NET CI (no S&Box installation)
- Headless unit testing of the core without engine initialization
- Reasoning about the simulation as a library independent of its rendering/physics host

The professor's review identified this as the key blocker for portable CI and reproducible verification.

## Decision

Extract engine-independent deterministic simulation logic into a separate `Lute.Core` project that references no S&Box assemblies.

### Math types

- `Lute.Core` uses `System.Numerics.Vector3` (BCL) for 3D positions.
- `Lute.Core` defines its own `Aabb3` (axis-aligned bounding box) as a `readonly record struct` with explicit intersection semantics:
  - `IntersectsVolume(other, tolerance)` — actual penetration (faces touching does not count)
  - `TouchesOrIntersects(other)` — inclusive adjacency
  - `Contains(point)`, `ExpandedBy(amount)`, `TranslatedBy(delta)`
- `Lute.Core` defines `SpatialKey` (quantized integer grid cell) for deterministic ownership/indexing — continuous float positions are never used as authoritative simulation identity.
- Rotations: `float YawDegrees` for yaw-only cases; `System.Numerics.Quaternion` for full 3D rotation if needed later.

### Coordinate contract

All Lute.Core spatial coordinates are S&Box-compatible world units represented as floats (X/Y horizontal, Z up). Conversion to meters is deliberate via `LuteUnits.WorldUnitsPerMeter = 39.37f`.

### Engine adapter

The S&Box project (`lute.csproj`) contains `SandboxSpatialConversions` — the only place that sees both type systems. It converts:
- `System.Numerics.Vector3` <-> `Sandbox.Vector3`
- `Aabb3` <-> `Sandbox.BBox`

### Boundary rule

> Core computes what the world should be; the S&Box adapter determines how that state is represented in the engine.

Core never references `Scene`, `GameObject`, `Component`, `NavMesh`, `MeshComponent`, `Sandbox.BBox`, `Sandbox.Vector3`, or `Sandbox.Diagnostics.Log`.

### Extraction scope (first pass)

This first pass extracts only core models and the dependency graph:
- `Aabb3`, `LuteUnits`, `SpatialKey`
- `TaskStatus` enum
- `DirectedTask` (pure data model, dependency-satisfaction via resolver function)
- `BuilderState` (pure data model)
- `DependencyGraph` (cycle detection, blocking-dependency resolution, topological order)

Future passes migrate `ConstructionDirector`, `ReservationManager` (non-trace parts), `SettlementNeedBoard`, `Blueprint`, and `BlueprintValidator` to use core types.

## Consequences

- The pure core builds and tests on any .NET 10 machine with no S&Box installation.
- Hosted CI can run core unit tests on every push.
- The engine adapter is the single boundary where type conversion happens — no S&Box types leak into core.
- `DirectedTask.DependenciesSatisfied` takes a resolver function instead of a static `ConstructionDirector.GetTask` reference, breaking the static coupling.
- Future `BuildPlan` / `BlueprintCompiler` work lives in core.
- The existing engine-side `ConstructionDirector`/`ReservationManager`/`SettlementNeedBoard` continue to work unchanged until migrated.
