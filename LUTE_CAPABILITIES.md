# Lute — Capability Registry

> Machine-readable-friendly capability inventory for coding agents. Status values:
> `implemented`, `partial`, `experimental`, `planned`, `disabled`.

| Capability | Status | Authority / entry point | Notes |
|---|---|---|---|
| Blueprint IR | implemented | `sbox/code/Building/Blueprint.cs` | Canonical construction representation |
| Blueprint validation | implemented | `sbox/code/Building/BlueprintValidator.cs` | Must run before execution/export |
| Building grammar | implemented | `sbox/code/Building/BuildingGrammar.cs` | Produces Blueprint-compatible construction data |
| Style grammar | implemented | `sbox/code/Building/StyleGrammar.cs` | Architectural constraints |
| Monument generation | implemented | `sbox/code/Building/MonumentBlueprintProducer.cs` | Includes massing-level monument presets |
| Village construction | implemented | `sbox/code/Building/VillageBuilder.cs` | Incremental/save-resume path |
| Multi-builder scheduling | implemented | `sbox/code/Building/ConstructionDirector.cs` | Balanced deal, work-stealing, dependency-aware |
| Spatial blackboard | implemented | `sbox/code/Building/SpatialBlackboard.cs` | Shared NPC positions/claims/messages, AABB box reservations |
| Runtime block construction | implemented | `sbox/code/LuteBuilderNpc.cs` | Runtime MeshComponent creation requires disabled→mesh→enabled sequence |
| From-scratch watchtower construction | implemented | `sbox/code/LuteWatchtower.cs` | First validated walkable structure |
| Deterministic NPC intent/NLP | implemented | `sbox/code/NLP/` | Tokenizer, grammar parser, entity resolver, speech templates, conversation manager |
| ConstructionDirector | implemented | `sbox/code/Building/ConstructionDirector.cs` | Central task scheduler, dependency authority, authoritative reservations |
| Reservation manager | implemented | `sbox/code/Building/ReservationManager.cs` | Task-tied reservations, occupancy ledger, prevents concurrent spatial conflicts |
| Construction event bus | implemented | `sbox/code/Building/ConstructionEventBus.cs` | Pub/sub event system for construction state transitions |
| Construction conflict resolver | partial | `sbox/code/Building/ConstructionDirector.cs` | Reservation conflicts detected; deterministic replanning needs further work |
| NPC goals/needs/beliefs/memory | implemented | `sbox/code/NLP/BeliefModel.cs` | Self-beliefs, task history, reputation, conversation state, trust |
| NPC-to-NPC negotiation | implemented | `sbox/code/NLP/ConversationManager.cs` + `CommunicationBus` | Deterministic NLP intents, async message delivery, exactly-once consumption |
| Runtime LLM NPC behavior | disabled | project rule | Must remain absent from gameplay path |
| Construction deception | disabled | project rule | Intentionally reserved for future gameplay mode |
| Gameplay deception | planned | future gameplay phase | Must be feature-gated and outside construction mode |
| S&Box MCP spatial probing | implemented | `agent/sbox_eyes.py` | Text-based scene verification |
| Collision probes | implemented | `agent/collision_probes.py` | Automated traversal/collision checks |
| Scene telemetry | implemented | `agent/scene_telemetry.py` | Counts, bounds, components, hierarchy |
| Devin rolling memory | implemented | `.devin-context/` | Hook-based session bootstrap |
| Devin structured project memory | experimental | `LUTE_STATE.md`, `LUTE_CAPABILITIES.md`, `docs/decisions/` | Use `lute-context` MCP to retrieve/update |
| Lute context MCP | experimental | `agent/lute_context_server.py` | Project-specific context/capability/decision tools |

## Agent operating rule

Before implementing a feature, agents should query this registry and `LUTE_STATE.md` rather than assuming a capability exists.

After significant implementation work, update the affected capability status and current state. Record durable architectural choices as ADRs.
