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
- SpatialBlackboard
- LuteBuilderNpc autonomous and agent-drive modes
- LuteWatchtower from-scratch construction
- Neutral Market monument whitebox/detail pass
- Runtime MeshComponent construction fix
- In-game agent chat panel on `J`
- S&Box native MCP spatial probing through the existing `agent/sbox_eyes.py` workflow
- `.devin-context` session memory and hooks

## Current construction problems / risks

1. NPCs can still attempt construction in occupied/invalid spatial regions. Treat this as a scheduling/reservation/dependency problem, not as a dialogue problem.
2. Builder NPC autonomous test placement can still interfere with agent-driven behavior.
3. Acropolis collision currently behaves as one imported collision hull and is not yet ideal for terrace traversal.
4. Agent movement can be affected by `PlayerController` friction/collision.

## Next architectural target

Implement the deterministic construction-coordination layer before adding gameplay deception:

1. ConstructionDirector
2. task dependency graph
3. spatial reservation manager
4. deterministic NPC intent/NLP layer
5. construction conflict resolver
6. truthful construction reporting and validation

## Rules of interpretation

- Prefer repository state over model memory.
- Prefer `AGENTS.md` for stable S&Box/API facts.
- Prefer this file for current implementation status.
- Prefer ADRs for the reason a design decision was made.
- Never infer a capability merely because a file/type exists; verify the implementation and its tests/logs.
- Do not reintroduce runtime LLM inference into NPC behavior.
