# Lute Core Rules

These rules are project constraints, not suggestions.

## Source of truth

1. `AGENTS.md` is authoritative for stable S&Box/API knowledge and known engine gotchas.
2. `LUTE_STATE.md` is authoritative for current project state.
3. `LUTE_CAPABILITIES.md` is authoritative for capability status.
4. `docs/decisions/` is authoritative for durable architectural decisions and their rationale.
5. `.devin-context/memory.md` is rolling session memory, not a substitute for the above.

If sources disagree, stop and reconcile them rather than guessing.

## NPC intelligence boundary

- Runtime NPCs MUST NOT call an LLM.
- Runtime NPC conversation and coordination use deterministic NLP, semantic intents, world state, goals, needs, beliefs, memory, social state, blackboard state, and deterministic decision rules.
- LLMs may be used by development/offline content-generation tools only.
- Never add an LLM dependency to the NPC gameplay path to solve a behavior problem.

## Construction mode

Construction is currently cooperative and truthful.

- Deception is DISABLED.
- Do not implement, invoke, or simulate lying, concealment, sabotage, false reporting, manipulative persuasion, or fabricated world-state claims during construction.
- Deception interfaces may be designed behind a future gameplay feature flag, but they must not affect construction behavior.
- NPCs may negotiate task ownership, request help, offer alternatives, share resources, report blockers, and resolve conflicts deterministically.

## Construction authority

- Blueprint is the canonical construction intermediate representation.
- BlueprintValidator must validate before execution/export where applicable.
- NPC speech is never direct authority over world mutation.
- Construction state changes go through deterministic systems such as task scheduling, reservations, blackboard operations, validation, and execution.
- Treat NPCs building into each other's geometry as a coordination/reservation/dependency defect first, not as a language-model defect.

## Verification

Before declaring a construction change complete:

1. Build/compile the affected project.
2. Run the appropriate text-based S&Box verification.
3. Use native S&Box MCP spatial probes when runtime state matters.
4. Use collision probes for traversal/collision changes.
5. Check console logs for the latest run and distinguish current behavior from stale runtime state.
6. Do not claim visual verification merely from a screenshot; this agent's reliable workflow is text/probe based.

## S&Box discipline

- Read `docs_cache/api_signatures.md` before inventing an S&Box API.
- Search actual engine source when a signature is uncertain.
- Respect the known MeshComponent runtime sequence: create disabled, assign mesh, then enable.
- Respect S&Box coordinate conversion documented in `AGENTS.md`.

## Memory discipline

At session start, read the project rules and current state before making architectural changes.

After significant work:

- update `LUTE_STATE.md` if current truth changed;
- update `LUTE_CAPABILITIES.md` if capability status changed;
- add/update an ADR when a durable architectural decision is made;
- update `.devin-context/memory.md` for short-lived session state.

Do not pad memory with information that can be rediscovered cheaply.
