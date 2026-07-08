# ADR-001: Compute — Azure Container Apps

**Status:** Accepted · **Date:** 2026-07-08

## Context

KnowVault has seven deployables with very different scaling profiles: request-serving APIs (Query, Answer, Gateway, Admin), queue-driven workers (Ingestion, Connector), and a batch-style Eval service. It is built and operated by one engineer.

## Decision

Deploy all services to **Azure Container Apps** in a single environment.

## Rationale

- **KEDA scaling on queue depth** — the Ingestion worker scales with Service Bus queue length, including scale-to-zero when idle. This is the single biggest cost lever for a part-time project.
- **Scale-to-zero** for Ingestion, Connector, and Eval; min-replica 1 only for Query/Answer during demo periods.
- **Revisions** give blue-green / traffic-splitting deploys (10% → 100% with a health gate) without any cluster machinery.
- Kubernetes benefits without cluster operations — no node pools, upgrades, or CNI decisions for a solo project.

## Alternatives considered

- **AKS** — full control, but cluster ops (upgrades, node management, ingress, secrets CSI) is unjustified overhead at this scale. Documented as the "at 10x scale" migration path: the services are plain containers, so the migration is Bicep + manifests, not code.
- **Azure Functions** — good fit for the workers, but splits the platform across two compute models and complicates local dev and tracing.
- **App Service** — no scale-to-zero, weaker fit for queue-driven workers.

## Consequences

- Local dev uses .NET Aspire; parity with ACA is good but not perfect (e.g., Dapr not used to keep the two aligned).
- Per-service Dockerfiles and an ACR are required from Phase 0.
