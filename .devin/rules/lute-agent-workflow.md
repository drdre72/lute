# Lute Agent Workflow

This workflow governs substantive Devin coding sessions on Lute.

## Before coding

For any task involving architecture, NPC behavior, construction, persistence, world generation, or S&Box integration:

1. Read `AGENTS.md` for stable S&Box/API knowledge.
2. Read `LUTE_STATE.md` for current truth.
3. Read `LUTE_CAPABILITIES.md` before assuming a capability exists.
4. Query the `lute-context` MCP when the task depends on project history, current capability, prior decisions, or a symbol/bug that may already be documented.
5. Inspect the actual implementation and tests before changing it.

Do not rely on model memory when repository state can answer the question.

## During coding

- Make the smallest coherent change that advances the active architectural target.
- Preserve the separation between development AI and deterministic runtime NPC AI.
- Never use an LLM call as a shortcut for a runtime NPC behavior problem.
- Treat construction conflicts as deterministic scheduling, dependency, reservation, collision, or validation problems.
- Do not introduce gameplay deception into the current construction phase.
- Do not duplicate knowledge into rolling memory when it belongs in `AGENTS.md`, `LUTE_STATE.md`, capabilities, or an ADR.

## Verification gate

Before reporting work as complete:

1. Compile the affected project.
2. Run relevant automated/text verification.
3. If runtime behavior changed, run the S&Box MCP spatial probes and inspect the current console output.
4. If collision/traversal changed, run collision probes.
5. Confirm the result came from the current run rather than stale play-mode state.
6. If verification fails, report the failure and fix it rather than marking the task complete.

## After coding

Update the durable project knowledge only when it changed:

- `LUTE_STATE.md` — current implementation status, blockers, next target.
- `LUTE_CAPABILITIES.md` — capability status changes.
- `docs/decisions/ADR-*.md` — durable architectural decisions and rationale.
- `.devin-context/memory.md` — short-lived session details and live runtime notes.

Prefer canonical files over memory append operations for durable facts.

## Task handoff format

When a task reaches a stable stopping point, leave enough state for another agent to continue:

- what changed;
- what was verified;
- what remains broken;
- exact next architectural target;
- any new decision or constraint.

## Priority order

When sources disagree, use this order and reconcile conflicts instead of guessing:

`AGENTS.md` → `LUTE_STATE.md` → `LUTE_CAPABILITIES.md` → ADRs → rolling memory → chat/model recollection.
