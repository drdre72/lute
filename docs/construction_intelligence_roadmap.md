# Construction Intelligence Roadmap

Post-corner-stabilization priorities, per architectural review.

## 1. Freeze and certify the corner system
- All four perimeter corners pass the same deterministic test
- Test save/reload reconstruction, stop/start cycles, multiple build seeds
- Keep `CornerTestBuilder` permanently as a regression harness
- Treat corner geometry as locked unless a regression test fails

## 2. Create one authoritative placement representation
```csharp
StructuralPlacement
{
    EntityId
    Position
    Rotation
    Size
    OBB
    SemanticType
    ParentAssembly
    GridCells
}
```
- Renderer, validator, persistence, MCP tools, collision registry all consume this
- Prevents "topology says correct, scene looks wrong" situations

## 3. Finish the spatial registry / occupancy system
- Every built object and important static world object registers exact occupied cells and OBB bounds
- NPCs can ask: Is this location free? What is occupying it? What clearance exists? What cells would this structure reserve?
- Eliminates NPCs building through props, walls, furniture, or each other without raycasts

## 4. Add construction self-repair
```
proposed placement → validate → FAIL → classify failure → try approved corrections → revalidate
```
- Approved corrections: snap to grid, rotate 90°, shift one cell, nearest valid attachment point, defer task
- No free-form AI movement — bounded deterministic recovery loop

## 5. Fix builder liveness and coordination
- Every idle NPC has a machine-readable reason:
  - IDLE_NO_TASK
  - WAITING_DEPENDENCY
  - WAITING_RESERVATION
  - PATH_BLOCKED
  - MATERIAL_UNAVAILABLE
  - CONSTRUCTION_CONFLICT
  - YIELDING_TO_NPC
- If an NPC sits in one state beyond a threshold, director reassigns or explains why

## 6. Generalize corner solution into junction assemblies
- `JunctionResolver` abstraction for:
  - wall ↔ wall
  - wall ↔ gate
  - wall ↔ doorway
  - wall ↔ floor
  - wall ↔ roof
  - wall ↔ tower
  - foundation ↔ terrain
- Avoid special-case code per structure type

## 7. Build structural dependency graphs
- Tasks know what must exist before they can start:
  - foundation → wall → lintel → roof
- ConstructionDirector gets a proper DAG instead of just priorities
- Multiple builders work simultaneously without stepping on each other

## 8. NPC communication and blackboard collaboration
- NLP messages refer to concrete world facts:
  - "South wall section 4 is blocked by StorageRack_17."
  - "I reserved the western foundation cells."
  - "Gate support is incomplete; waiting for Builder_2."
- Makes NLP/blackboard feel intelligent without an LLM

## NOT next
- Do NOT add more building types immediately
- Extending content before consolidating placement/spatial architecture will multiply bugs

## Single next milestone after corners
> NPC can propose any structure placement, the spatial system can deterministically accept/reject it with an exact reason, and the NPC can recover from rejection without human intervention.
