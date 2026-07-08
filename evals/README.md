# Evals

Golden-question suite for retrieval and answer quality. This starts on day one — before there is a pipeline to evaluate — because the eval mindset shapes every design decision.

## Corpus

`azure-docs-fixture-v1` — a fixture corpus of public Azure documentation pages, so the repo is shareable. Fixture files live in `corpus/` (added in Phase 1 when ingestion exists):

| Document ID | Source |
|---|---|
| `well-architected-reliability` | Azure Well-Architected Framework — Reliability pillar |
| `container-apps-overview` | Azure Container Apps overview |
| `service-bus-overview` | Azure Service Bus messaging overview |
| `cosmos-db-partitioning` | Azure Cosmos DB partitioning and horizontal scaling |
| `ai-search-vector-overview` | Vector search in Azure AI Search |
| `secret-capacity-plan` | **Planted fixture** (authored, not public) — access restricted to test user `alice`; used by the permission-restricted security eval |

## Question categories

- **factual** — answer lives in one document; measures retrieval hit-rate and groundedness.
- **multi-doc** — answer requires chunks from two or more documents.
- **unanswerable** — not in the corpus; the system must refuse. Measures refusal correctness.
- **permission-restricted** — asked as both an authorized and an unauthorized user. The unauthorized run must contain zero restricted content. **This metric gates CI at 100%, always.**

## Metrics per run (implemented in `KnowVault.Eval`, Phase 4)

- Retrieval hit-rate@10 and MRR
- Groundedness (LLM judge — GPT-4o, CI/sampled only)
- Citation accuracy
- Refusal correctness
- Security (permission-restricted leakage) — hard gate at 100%
- Latency p50/p95 and cost per question

Target size: 40–60 questions by Phase 4. Current: 10 starter questions.
