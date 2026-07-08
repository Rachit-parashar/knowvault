# ADR-003: Monorepo — one solution, seven deployables

**Status:** Accepted · **Date:** 2026-07-08

## Context

KnowVault has seven deployable services plus shared contracts and domain logic, built by a single engineer.

## Decision

One repository, one solution. Services live under `src/Services/`, shared code under `src/Shared/`, infrastructure under `infra/`, evals under `evals/`.

## Rationale

- Atomic changes across service boundaries (e.g., a message-contract change plus both its producer and consumer) land in one reviewable PR.
- One CI pipeline, one versioning scheme, one place for ADRs and eval baselines.
- `KnowVault.Contracts` and `KnowVault.Domain` are project references — no internal package feed to run.

## Consequences

- CI must stay fast as the solution grows: path filters decide when the (expensive) eval suite runs.
- Message contracts are versioned explicitly (`*.v1` names in application properties) so the monorepo doesn't hide accidental breaking changes behind simultaneous deploys.
- If the team grows, per-service repos remain possible — the folder layout maps one-to-one.
