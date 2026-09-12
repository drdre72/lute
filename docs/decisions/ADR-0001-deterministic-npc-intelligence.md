# ADR-0001 — Deterministic NPC Intelligence

- Status: Accepted
- Date: 2026-09-11

## Context

Lute needs NPCs that can converse, cooperate, negotiate construction tasks, use shared world knowledge, remember relevant events, and make decisions. Runtime LLM inference would introduce nondeterminism, latency, external model dependencies, and an unnecessarily weak authority boundary for gameplay state.

## Decision

Runtime NPC intelligence is deterministic. NPC communication is parsed into semantic intents and resolved against deterministic context: world state, goals, needs, beliefs, memory, social state, and blackboard state.

LLMs remain available only to development/offline content-generation tooling.

## Consequences

- NPC behavior is reproducible and testable without a model server.
- Construction coordination can be validated as ordinary game-state transitions.
- Personality can affect expression and policy selection without changing semantic truth.
- Runtime NPC systems must not acquire an LLM dependency as a shortcut for missing deterministic behavior.

## Construction-specific constraint

The current construction phase is cooperative. Deception is explicitly disabled. Future gameplay deception may be implemented behind a separate simulation/gameplay mode and feature gate.
