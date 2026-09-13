# Proposal: Autonomous Town Cooperation Test

**Status:** Draft  
**Runtime constraint:** deterministic / NLP-only NPC cognition; no LLM in the simulation loop  
**Primary milestone:** multiple professions autonomously bootstrap and build a small town with zero manual task steering

## 1. Purpose

The next Lute milestone should test whether the existing construction, spatial, task, and NLP systems can support a **closed-loop cooperative settlement build** rather than only several generic builders consuming a pre-generated task list.

The test should force NPCs to:

- discover or receive work from authoritative world state;
- acquire tools and physically located resources;
- communicate shortages, blockers, completion, and availability;
- reserve workspaces, resources, tools, and build sites without double ownership;
- satisfy task dependencies;
- transport material between producers, stockpiles, workstations, and build sites;
- recover deterministically from blocked work;
- choose useful alternate work instead of standing idle;
- complete the settlement without a human assigning each task.

The important success criterion is not visual complexity. It is **cooperative causality**: every completed structure must be explainable as a chain of world facts, resource transfers, claims, tasks, and actions performed by autonomous NPCs.

---

## 2. Existing Lute systems to reuse

This proposal is intentionally built on the systems already present in `main`.

### Construction / world authority

Reuse:

- `ConstructionDirector`
- task dependency DAG (`DependsOn`, dependency blocking, cycle rejection)
- `StructuralPlacement`
- `SpatialRegistry`
- `ReservationManager`
- `SpatialBlackboard`
- `ConstructionSelfRepair`
- `BuilderLivenessRegistry`
- `JunctionResolverRegistry`
- `ConstructionEventBus`
- `WorldFactProvider`

Do **not** create a second construction scheduler or second spatial authority.

### NLP / social coordination

Reuse:

- `Intent`
- `NlpParser`
- `Tokenizer`
- `ContextResolver`
- `SpeechGenerator`
- `SocialRules`
- `BeliefModel`
- `CommunicationBus`
- `ConversationManager`
- `GoalActionEvaluator`
- `BlackboardTransaction`
- construction-mode behavior gate (`NpcSimulationMode.Construction`)

### Economy primitives already available

Reuse and extend:

- `LuteInventory`
- `ItemType` / `ItemDefs`
- `ResourceNode`
- `CraftingBench`
- existing recipes and tool durability

The first cooperation benchmark should use these components rather than building a new inventory/crafting system.

---

## 3. Architecture principle: one NPC kernel, professions as capabilities

Do not implement a separate AI class for every profession.

Every working NPC should use the same agent kernel:

```text
TownNpcAgent
├── Identity
├── Profession profile
├── Capability set
├── LuteInventory
├── BeliefModel
├── Perception / world-query adapter
├── Goal + action evaluator
├── CommunicationBus client
├── task / reservation client
├── action executor
├── liveness state
└── short-term work memory
```

A profession is a deterministic capability profile, not a different brain.

Proposed capability representation:

```csharp
public enum NpcCapability
{
    Survey,
    MarkPlot,
    GatherWood,
    GatherStone,
    GatherOre,
    Haul,
    OperateSawmill,
    CraftMasonry,
    Carpentry,
    Masonry,
    Smithing,
    BuildWood,
    BuildMasonry,
    RepairTool,
    ManageStockpile,
}

public sealed class ProfessionDefinition
{
    public string Id { get; init; }
    public IReadOnlyDictionary<NpcCapability, float> Skills { get; init; }
    public IReadOnlySet<ItemType> PreferredTools { get; init; }
}
```

Task eligibility must be derived from capabilities + tools + world state. Avoid logic such as:

```csharp
if (npc.Role == "carpenter")
```

The existing `BeliefModel.Role` may remain as descriptive/social information, but **capabilities are authoritative for work eligibility**.

---

## 4. Proposed first full-town roster

Target: **12 NPCs**.

| Profession | Count | Primary capabilities |
|---|---:|---|
| Surveyor | 1 | survey, plot marking, road/build anchors, clearance checks |
| Quartermaster | 1 | stockpile accounting, shortage detection, material allocation |
| Lumberjack / Forester | 2 | wood gathering, tree-source selection, tool use |
| Quarryman | 1 | stone/ore gathering |
| Carpenter | 2 | timber processing, wooden structures, repair support |
| Mason | 2 | brick/mortar/stone processing, foundations, masonry structures |
| Hauler / Laborer | 2 | pickup, delivery, site clearing, generic assistance |
| Blacksmith | 1 | tools, tool repair/replacement, metal/concrete recipes |

This roster is intentionally construction-focused. Farming, cooking, guards, merchants, clergy, politics, factions, and deception remain out of scope for this benchmark.

### Bootstrap variant

Before the 12-NPC town test, validate the architecture with a **one-house vertical slice** using 5–6 NPCs:

- 1 gatherer / lumberjack
- 1 quarryman
- 1 hauler
- 1 carpenter / general builder
- 1 mason
- 1 smith (optional for the first run if starting tools are supplied)

If these NPCs cannot complete one dependency-driven house without intervention, do not scale to the town.

---

## 5. World-side semantic contracts

NPCs must never need bespoke code for `Tree_17`, `Quarry_2`, `Forge_A`, etc. Interactable world objects should expose machine-readable contracts.

### 5.1 Resource sources

Extend `ResourceNode` with a semantic adapter or interface:

```csharp
public interface IResourceSource
{
    string EntityId { get; }
    ItemType ResourceType { get; }
    int QuantityRemaining { get; }
    bool IsAvailable { get; }
    ItemType? RequiredTool { get; }
    Vector3 InteractionPoint { get; }
    OBB Bounds { get; }
}
```

Required changes to current `ResourceNode`:

- remove `ItemType.Clay` as the sentinel for "no tool";
- use `ItemType? RequiredTool` or an explicit `ToolRequirement.None`;
- stop treating a tree as a pickaxe resource in the long term (add Axe when the item model is expanded);
- register source position/availability with a world resource registry.

### 5.2 Stockpiles

Add an authoritative physical inventory container:

```csharp
public interface IStockpile
{
    string EntityId { get; }
    LuteInventory Inventory { get; }
    IReadOnlySet<ItemType> AcceptedItems { get; }
    Vector3 PickupPoint { get; }
    Vector3 DropPoint { get; }
    int ReservedIncoming(ItemType type);
    int ReservedOutgoing(ItemType type);
}
```

Stockpiles must support reservations so two haulers cannot promise the same 20 bricks.

### 5.3 Workstations

Adapt `CraftingBench` into a reservable workstation contract:

```csharp
public interface IWorkstation
{
    string EntityId { get; }
    IReadOnlyCollection<string> Recipes { get; }
    Vector3 InteractionPoint { get; }
    string ReservedBy { get; }
}
```

A bench must enforce:

- one active user unless explicitly multi-slot;
- capability requirements;
- tool requirements where applicable;
- input ownership/reservation;
- deterministic output destination.

### 5.4 Build sites

Each structure or subassembly should expose:

```csharp
public interface IBuildSite
{
    string BuildSiteId { get; }
    string BlueprintId { get; }
    IReadOnlyDictionary<ItemType, int> RequiredResources { get; }
    IReadOnlyDictionary<ItemType, int> DeliveredResources { get; }
    IReadOnlyCollection<string> DependsOn { get; }
    float WorkRemaining { get; }
    IReadOnlyCollection<string> WorkerSlots { get; }
    IReadOnlyCollection<string> ReservedSpatialCells { get; }
}
```

The build site should connect to the existing `ConstructionDirector` DAG instead of maintaining a separate dependency engine.

---

## 6. World contents for the benchmark

Use a controlled test map first.

### Required terrain

- mostly flat town basin;
- several modest slopes for survey/placement rejection tests;
- a few deliberately placed static obstacles;
- navigable routes between all resource zones and the town center;
- enough clear area for alternate plot selection.

### Required resource zones

- 30–50 tree/resource nodes;
- at least one stone quarry;
- at least one ore source or a small starting ore reserve;
- clay source;
- straw source;
- water source.

Existing `ResourceNode` already supports these item categories and can be the first implementation.

### Bootstrap infrastructure supplied at start

The first benchmark should start with enough equipment to avoid a chicken-and-egg deadlock:

- central stockpile / storage crates;
- one brick bench;
- one forge;
- starting tools;
- small emergency reserves of Wood, Stone, Ore, Clay, Straw, and Water.

The reserve must be sufficient to bootstrap production, but insufficient to construct the whole town.

### Target town

Suggested first complete town:

```text
Town Center
├── central stockyard
├── production yard / brick bench
├── smithy / forge
├── well
├── 4 houses
├── town hall
├── roads / paths
└── perimeter wall + gate
```

The wall and gate should be late dependencies so they exercise the stabilized masonry and junction systems after the economic/logistics loop has already been proven.

---

## 7. Resource and production chains

### Minimal first-run chain using current item definitions

```text
Clay + Straw
    ↓ BrickBench
Brick
    ↓ Mason / Builder
Masonry structure
```

```text
Stone + Water
    ↓ Forge (current recipe implementation)
Concrete
    ↓ construction
Foundation / structural task
```

```text
Ore + Wood
    ↓ Forge
Tools
    ↓ professions
Resource extraction / construction
```

```text
Wood
    ↓ direct use initially
Wood structure / support
```

This allows the first integration test to use the existing `ItemType`, `ResourceNode`, and `CraftingBench` systems.

### Later material refinement

After the closed loop works, introduce clearer production semantics:

- Log
- Timber / Plank
- Iron Ingot / Fitting
- Axe
- Saw

Do not add these before the first autonomous resource-delivery/build loop is functioning.

---

## 8. Required coordination boards / registries

Do not overload `SpatialBlackboard` with economic state. Preserve the current separation between spatial state and the `CommunicationBus`.

Add domain authorities with narrow responsibilities:

```text
ConstructionDirector
    construction tasks + DAG + execution authority

SpatialRegistry / SpatialBlackboard
    geometry + positions + claims + occupancy

ResourceRegistry
    source and stockpile quantities / reservations

LogisticsBoard
    material delivery jobs

WorkstationRegistry
    workstations, recipes, worker reservations

CommunicationBus
    semantic/social messages
```

These may be implemented as registries/services rather than literal "blackboard" classes. The important requirement is one authority per domain.

---

## 9. NLP / logic prerequisites before the town test

The current NLP stack is strong enough to build on, but several issues should be corrected before profession-scale coordination.

### 9.1 Fix intent pattern precedence

`NlpParser` currently checks several broad patterns before their specific extended forms. Examples:

- `"i need"` can match `Request` before `RequestHelp`, `RequestResource`, or `RequestTask`;
- `Offer` can win before `OfferHelp` / `OfferResource`;
- generic `Accept`, `Claim`, `Release`, `Assign`, and `Report` can similarly collapse more precise intents.

Replace first-match ordering with one of:

1. specific patterns before generic patterns, **or preferably**
2. deterministic scoring: longest phrase + topic compatibility + parameter evidence.

Add round-trip tests:

```csharp
var text = SpeechGenerator.Generate(intent);
var parsed = NlpParser.Parse(text);
Assert.Equal(intent.Type, parsed.Type);
Assert.Equal(intent.Topic, parsed.Topic);
```

Critical parameters such as `task_id`, `amount`, `resource`, `destination`, and `request_id` must also survive when text round-trip is required.

### 9.2 Preserve structured intents on machine-generated communication

`WorldFactProvider` already creates a fully structured `Intent`, then converts it to text, broadcasts the text, and recipients parse it again.

That can discard authoritative parameters such as:

- `task_id`
- `waiting_on`
- `blocker`
- retry count
- reason codes

Add an `IntentEnvelope` or optional structured payload to `ComMessage`:

```csharp
public sealed class IntentEnvelope
{
    public Intent Intent { get; init; }
    public string DisplayText { get; init; }
}
```

Machine-generated facts should transmit the structured intent as authority. Natural language remains the human-readable presentation layer.

Free text from a player or unscripted text source may still go through `NlpParser`.

### 9.3 Add an action dispatcher

`ConversationManager` currently logs `DecisionAction.Act` / `SpeakAndAct` world actions, but does not execute them.

Add:

```text
ResponseDecision
    ↓
NpcActionDispatcher
    ↓
validated domain request
    ↓
ConstructionDirector / LogisticsBoard / ResourceRegistry / WorkstationRegistry
```

Examples:

```text
deliver:brick
accept_task:TASK_42
claim_resource:Stockpile_1:Brick:20
craft:brick:10
help:NPC_4:TASK_88
```

Do not execute arbitrary action strings directly. Parse them into typed action requests.

### 9.4 Expand BlackboardProtocol beyond site Claim/Release

The current protocol only makes the legacy `Claim`/`Release` site intents binding.

Add routing for:

- `ClaimResource`
- `ReleaseResource`
- `RequestResource`
- `OfferResource`
- `RequestTask`
- `AssignTask`
- delivery claims/completions
- tool requests / returns

Each route should produce a typed domain transaction and receive a success/failure result.

Speech does not mutate inventories or tasks directly.

### 9.5 Replace role-string work logic with capabilities and actual inventory

`GoalActionEvaluator.FindSupplier` currently infers suppliers from role-name strings, and `SocialRules.HasSpareMaterial` does not inspect a physical inventory.

For the town test:

- supplier selection must use known stock/inventory + capability data;
- spare-material decisions must check actual available minus reserved quantity;
- task selection must check capability, required tool, dependencies, and reachable workspace;
- beliefs may be stale, so authoritative registries must validate before committing.

### 9.6 Cooperative construction must not deadlock on social trust

Construction mode starts unknown NPCs at neutral trust. Current request/assignment handlers use positive trust thresholds.

For the cooperative town benchmark, legitimate work requests should be accepted based primarily on:

- capability;
- availability;
- resource ownership;
- recognized authority (e.g. Quartermaster / ConstructionDirector);
- no conflicting reservation.

Trust may influence preference/tie-breaking, but should not cause a new settlement of cooperative NPCs to reject necessary work merely because they have not accumulated social history yet.

### 9.7 Track conversation state per peer / thread

`ConversationManager` currently maintains one turn count per NPC. With broadcasts and many professions, unrelated conversations can consume the same turn budget.

Track conversation state by `(npc, peer/thread/request_id)` so a world-fact broadcast does not terminate a separate resource negotiation.

---

## 10. Intent vocabulary for the cooperation benchmark

The existing extended intents already cover much of the required vocabulary. Use them and add parameters rather than exploding the enum unnecessarily.

Minimum semantic operations:

```text
REQUEST_HELP
OFFER_HELP
ACCEPT_HELP
DECLINE_HELP

REQUEST_RESOURCE
OFFER_RESOURCE
CLAIM_RESOURCE
RELEASE_RESOURCE

REQUEST_TASK
OFFER_TASK
ASSIGN_TASK
ACCEPT_TASK

REPORT_PROBLEM
REPORT_COMPLETION
REPORT_LOCATION
REPORT_AVAILABILITY

CLAIM / RELEASE (spatial)
ACKNOWLEDGE
WARN
INFORM
```

Required parameters should include as appropriate:

```text
request_id
task_id
resource_type
amount
source_id
destination_id
workstation_id
tool_type
blocker_id
reason
position
reservation_id
```

The human sentence is presentation. The structured intent is the operational truth.

---

## 11. Deterministic work loop

Each NPC should run approximately this decision loop:

```text
Observe authoritative world state
        ↓
Process incoming intents / facts
        ↓
Continue committed work if still valid
        ↓
If blocked: classify blocker
        ↓
Can resolve locally?
    ├── yes → deterministic repair / alternate action
    └── no  → publish structured request
        ↓
Find highest-priority eligible work
        ↓
Reserve required task/resource/workspace/tool
        ↓
Execute typed action
        ↓
Report completion / failure
        ↓
Release reservations
```

An NPC blocked on its preferred profession should be allowed to perform a secondary capability where appropriate rather than waiting forever.

---

## 12. Profession behavior examples

### Surveyor

Reads `SpatialRegistry` / terrain data and emits approved build anchors. It does not freestyle geometry.

```text
inspect candidate plot
→ validate slope / occupancy / access
→ reserve survey region
→ publish PlotDefinition
→ release survey reservation
```

### Quartermaster

Maintains shortages using physical stockpile state:

```text
required - available - incoming + reserved = shortage
```

Publishes material work orders, but does not teleport items or directly alter inventories.

### Lumberjack / Quarryman

```text
find available compatible ResourceNode
→ reserve source interaction slot
→ verify tool
→ gather
→ inventory receives item
→ publish haul availability / deliver to stockpile
```

### Carpenter / Mason / Smith

```text
claim workstation or build task
→ verify inputs + tools
→ request missing inputs if needed
→ craft / construct
→ output enters physical inventory or build site
→ report completion
```

### Hauler

Haulers are first-class agents, not visual decoration.

```text
claim LogisticsJob
→ reserve source quantity
→ travel to pickup
→ atomic inventory transfer
→ travel to destination
→ atomic drop-off
→ complete job
```

A material request is not satisfied until the destination physically receives the item.

---

## 13. Liveness requirements

Extend the existing `BuilderLivenessRegistry` concept to all working professions.

Required states/reasons:

```text
WORKING
MOVING
SEEKING_TASK
WAITING_MATERIAL
WAITING_TOOL
WAITING_WORKSTATION
WAITING_DEPENDENCY
WAITING_RESERVATION
PATH_BLOCKED
PLACEMENT_BLOCKED
DELIVERING
CRAFTING
GATHERING
RECOVERING
IDLE_NO_ELIGIBLE_TASK
SETTLEMENT_COMPLETE
```

Every idle NPC must have a machine-readable explanation.

Recommended benchmark:

> no unexplained idle period longer than 10 seconds.

A longer delay is allowed only when a concrete reason and dependency/request ID are exposed.

---

## 14. Benchmark sequence

### Stage A — One-house vertical slice

World:

- wood source
- stone/clay/straw source
- central stockpile
- brick bench
- optional forge
- one house blueprint

Required cooperation:

```text
gatherer → raw material
hauler   → stockpile/workstation
crafter  → processed material
hauler   → build site
builder  → dependency-driven construction
```

Success condition: one completed house with no injected resources after the run begins and no manual task assignment.

### Stage B — Production bootstrap

NPCs establish or activate:

- stockyard
- brick production
- forge / tool support
- first surveyed plots

### Stage C — Settlement

Construct:

- 4 houses
- well
- town hall
- roads

Allow independent structures to progress concurrently.

### Stage D — Perimeter integration

Construct:

- gate
- perimeter wall
- junctions / corners

This is the end-to-end integration of resource economy + task DAG + spatial intelligence + stabilized masonry.

---

## 15. Controlled failure tests

After a clean run, deliberately inject failures.

| Failure | Expected recovery |
|---|---|
| remove/break a required tool | request replacement; smith produces or stockpile supplies it |
| deplete one resource source | select another known source or report shortage |
| block a planned build volume | spatial validation rejects; survey/replan or defer |
| two haulers request same stock | reservations prevent double allocation |
| two workers claim one workstation | one succeeds; other receives deterministic wait/reassignment |
| remove a worker mid-task | claims released/expire; eligible replacement can claim work |
| block a road/path | path failure exposed; alternate path/task chosen |
| dependency never completes | dependent task remains explicitly blocked, no false progress |
| crafting inputs disappear | craft transaction fails atomically; inputs are not partially consumed twice |

---

## 16. Required diagnostics / MCP exposure

Add or expose machine-readable commands/tools for:

```text
npc_status
npc_capabilities
npc_inventory
npc_beliefs
npc_conversations

resource_sources
stockpile_status
resource_reservations

logistics_jobs
workstations

construction_dag
builder_liveness
spatial_query
world_facts
```

For any NPC, debugging should answer:

```text
Who are you?
What profession/capabilities do you have?
What are you doing?
Why did you choose it?
What are you waiting for?
What request/reservation/task caused that state?
What do you believe exists nearby?
What authoritative world fact will validate your next action?
```

---

## 17. Acceptance criteria

A successful autonomous town run requires:

```text
manual task steering                = 0
manual resource injection mid-run  = 0
invalid structural overlaps        = 0
double resource allocations        = 0
duplicate exclusive claims         = 0
permanent unexplained deadlocks    = 0
unexplained idle > 10s             = 0
lost task/resource reservations    = 0
```

And:

```text
target structures complete          = 100%
resource transfers auditable        = 100%
NPC states machine-explainable      = 100%
construction DAG respected          = 100%
world mutations authority-validated = 100%
```

The benchmark should produce an event trace sufficient to reconstruct why every structure was built and where its material came from.

---

## 18. Implementation order

### Gate 0 — harden NLP/action bridge

1. fix specific-vs-generic intent parsing;
2. add structured intent payload support to `CommunicationBus`;
3. make `WorldFactProvider` send structured intents directly;
4. add typed `NpcActionDispatcher`;
5. expand `BlackboardProtocol` / transaction routing to resource/task actions;
6. move construction-mode request acceptance from raw trust thresholds to capability/authority checks;
7. make conversation turn state per peer/thread;
8. add intent round-trip tests.

### Gate 1 — profession/capability layer

1. add `NpcCapability` + `ProfessionDefinition`;
2. attach capabilities to NPCs;
3. integrate capability checks with task eligibility;
4. expose capability diagnostics.

### Gate 2 — physical resource logistics

1. ResourceRegistry over existing `ResourceNode`;
2. stockpile component + item reservations;
3. LogisticsJob + LogisticsBoard;
4. atomic item pickup/drop transfer;
5. workstation reservations.

### Gate 3 — one-house benchmark

Run until repeatable with zero intervention.

### Gate 4 — 12-NPC town benchmark

Add Surveyor and Quartermaster behavior, multiple structures, concurrency, and final perimeter construction.

---

## 19. Non-goals for this milestone

Do not add yet:

- LLM runtime cognition;
- deception or sabotage;
- faction politics;
- economy/pricing/currency;
- hunger/sleep simulation unless required for the build test;
- combat/guards;
- unrestricted free-form building creativity;
- large new item catalogs before the current resource loop works.

The objective is to prove **deterministic cooperative autonomy** first.

---

## 20. Architectural success state

At the end of this proposal, the desired loop is:

```text
WORLD STATE
   ↓
registries / facts
   ↓
BeliefModel + structured Intent
   ↓
SocialRules / GoalActionEvaluator
   ↓
typed NpcAction
   ↓
authoritative domain transaction
   ↓
physical world mutation
   ↓
WorldFactProvider
   ↓
NPC communication + new decisions
```

That loop should be sufficient for a settlement to build itself without an LLM and without a human acting as the hidden scheduler.
