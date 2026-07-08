# KnowVault

**Enterprise knowledge platform with multi-tenant, permission-aware RAG.**

Organizations connect their document sources (SharePoint, Confluence, or direct file upload for v1), content is chunked and embedded with permission metadata, and users ask questions and get **cited answers that only draw from documents they're allowed to see**.

## Success criteria

These are the contract for v1. Every one of them is verified by an automated test or a visible dashboard before this project is called done.

1. **Tenant isolation** — a user in Tenant A can never retrieve a chunk from Tenant B.
2. **Security trimming** — a user without access to document X never sees content from X in an answer, even if X is the best match.
3. **Citations** — every answer carries citations that link to the exact source chunk.
4. **Quality gates** — a golden-question eval suite runs in CI; a PR that degrades retrieval hit-rate or groundedness fails the build.
5. **Cost visibility** — cost per query and token usage are visible on a dashboard.
6. **Reproducibility** — the whole environment rebuilds from Bicep with one command.

## Explicitly out of scope for v1

Scope discipline is a feature. The following are deliberately deferred:

- Fine-tuned models
- GraphRAG
- Agentic multi-hop retrieval
- More than two connector types (SharePoint + direct upload; Confluence is future work)
- Mobile clients

## Architecture

Seven deployables in one solution (see [ADR-003](docs/adr/ADR-003-monorepo.md)):

| Service | Responsibility |
|---|---|
| `KnowVault.Admin` | Tenants, sources, API keys, per-tenant config (chunk size, model tier), usage/cost ledger |
| `KnowVault.Connector` | Source sync (Graph/upload), change detection via content hash, ACL capture, `DocumentChanged`/`DocumentDeleted` events |
| `KnowVault.Ingestion` | Service Bus worker: extract → chunk → embed (batched) → upsert to AI Search + Cosmos; poison-message handling |
| `KnowVault.Query` | Hybrid retrieval (BM25 + vector) with mandatory tenant + ACL filters, semantic reranking |
| `KnowVault.Answer` | Grounded prompt assembly, SSE streaming, inline `[n]` citations, sampled groundedness checks |
| `KnowVault.Eval` | Golden-question runs: hit-rate, MRR, groundedness, citation accuracy, security metrics |
| `KnowVault.Gateway` | YARP BFF locally; APIM fronts it in Azure |

Key platform choices and their rationale live in [docs/adr](docs/adr/):

- [ADR-001 — Compute: Azure Container Apps](docs/adr/ADR-001-compute-choice.md)
- [ADR-002 — ACL strategy: denormalized ACLs + query-time trimming](docs/adr/ADR-002-acl-strategy.md)
- [ADR-003 — Monorepo](docs/adr/ADR-003-monorepo.md)

## Local development

Requires .NET 10 SDK.

```bash
dotnet run --project src/KnowVault.AppHost
```

The Aspire dashboard shows all services, traces, and logs.

```bash
dotnet test          # unit tests
dotnet format        # style — enforced in CI
```

## Deploying

```bash
az deployment sub create \
  --location <region> \
  --template-file infra/main.bicep \
  --parameters infra/main.dev.bicepparam
```

## Status

Phase 0 (foundations) in progress. See [the build plan](docs/build-plan.md) for the full roadmap.
