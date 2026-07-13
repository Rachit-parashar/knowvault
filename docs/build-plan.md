# KnowVault — Build Plan
## Enterprise Knowledge Platform with Multi-Tenant, Permission-Aware RAG

**Target:** a deployed, demo-able platform in 10–12 weeks (part-time), with a README, architecture decision records, and an evaluation report that together read like a senior engineer's work.

---

## 1. What you're building (scope contract)

A platform where organizations connect their document sources (SharePoint, Confluence, or file upload for v1), content is chunked and embedded with permission metadata, and users ask questions and get **cited answers that only draw from documents they're allowed to see**.

**Success criteria (write these in the README on day one):**
1. A user in Tenant A can never retrieve a chunk from Tenant B (tenant isolation).
2. A user without access to document X never sees content from X in an answer, even if X is the best match (security trimming).
3. Every answer carries citations that link to the exact source chunk.
4. A golden-question eval suite runs in CI; a PR that degrades retrieval hit-rate or groundedness fails the build.
5. Cost per query and token usage are visible on a dashboard.
6. The whole environment rebuilds from Bicep with one command.

**Explicitly out of scope for v1** (write this down too — scope discipline is itself a portfolio signal): fine-tuned models, GraphRAG, agentic multi-hop retrieval, more than two connector types, mobile clients.

---

## 2. Architecture decisions (record these as ADRs)

| Decision | Choice | Rationale |
|---|---|---|
| Compute | Azure Container Apps | KEDA scaling on queue depth, scale-to-zero for ingestion workers, revisions for blue-green — Kubernetes benefits without cluster ops for a solo project. AKS is a documented "at 10x scale" migration path. |
| Vector store | Azure AI Search | Hybrid (BM25 + vector) in one query, semantic reranker built in, and `search.in()` filters give query-time security trimming. A dedicated vector DB adds ops burden without adding capability at this scale. |
| Chunk store | Cosmos DB | Chunks are schema-flexible JSON, write-heavy during ingestion, point-read by ID at answer time. Partition key `/tenantId` gives physical tenant separation. |
| Relational | Azure SQL (serverless tier) | Tenants, sources, sync state, eval runs, usage ledger — genuinely relational, low volume. |
| Messaging | Service Bus (queues + DLQ) | Ingestion is a classic competing-consumers pipeline needing retries, dead-lettering, and per-message TTL. Event Grid only for blob-created triggers on direct uploads. |
| Sync auth | App-only Graph permissions for connector sync; **OBO (On-Behalf-Of) is NOT used for retrieval** — instead ACLs are denormalized into the index at sync time and enforced via filters | Query-time OBO calls to source systems per chunk would be slow and rate-limited. Denormalized ACL + filter is how M365 Copilot-style systems actually work. Trade-off to discuss: permission changes propagate at sync latency, mitigated by short sync intervals + tombstoning. |
| API auth | Entra ID (OIDC), JWT validated at APIM edge + again in services; `tid`/`oid`/`groups` claims drive trimming | Defense in depth; never trust the gateway alone. |
| LLM | Azure OpenAI — `text-embedding-3-large` (embeddings), GPT-4o-mini (answers, query rewrite), GPT-4o (eval judge only) | Cost tiering: the judge model is expensive but runs only in CI/sampled production, not per query. |
| Orchestration lib | Microsoft.Extensions.AI + hand-rolled RAG pipeline (not a heavy framework) | You learn the actual mechanics — chunking, retrieval, prompting — which is the interview material. Semantic Kernel noted as an alternative in the ADR. |
| Local dev | .NET Aspire AppHost | Service discovery, dashboards, and container wiring locally; emulators for Service Bus/Cosmos where possible. |

---

## 3. Services and repo structure

Seven deployables in one solution (monorepo is correct for a solo project — record the ADR):

```
knowvault/
├── src/
│   ├── KnowVault.AppHost/              # Aspire orchestration (local dev)
│   ├── KnowVault.ServiceDefaults/      # OTel, health checks, resilience — shared
│   ├── Services/
│   │   ├── KnowVault.Admin/            # tenants, sources, API keys, usage analytics
│   │   ├── KnowVault.Connector/        # source sync (Graph/Confluence/upload), change detection, ACL capture
│   │   ├── KnowVault.Ingestion/        # parse → chunk → embed → index (Service Bus worker)
│   │   ├── KnowVault.Query/            # hybrid retrieval + security trimming + rerank
│   │   ├── KnowVault.Answer/           # prompt assembly, streaming generation, citations
│   │   ├── KnowVault.Eval/             # golden sets, metric computation, regression reports
│   │   └── KnowVault.Gateway/          # YARP BFF (local) — APIM fronts it in Azure
│   ├── Shared/
│   │   ├── KnowVault.Contracts/        # DTOs, Service Bus message contracts (versioned)
│   │   └── KnowVault.Domain/           # chunking logic, citation model — pure, unit-testable
├── infra/                              # Bicep modules per resource + main.bicep per env
├── evals/                              # golden-questions.json, corpora fixtures
├── docs/adr/                           # ADR-001-compute-choice.md, ADR-002-acl-strategy.md ...
└── .github/workflows/
```

**Service responsibilities in one line each:**
- **Admin** — CRUD for tenants/sources is the boring part; the interesting parts are per-tenant config (chunk size, model tier) and the usage/cost ledger.
- **Connector** — pulls documents + their ACLs (users/groups with read access), computes content hashes for change detection, emits `DocumentChanged` / `DocumentDeleted` messages.
- **Ingestion** — the worker: extract text (Document Intelligence for PDFs/scans, native parsers for HTML/MD/DOCX), chunk, embed in batches, upsert to AI Search + Cosmos, handle poison messages.
- **Query** — embeds the (rewritten) question, runs hybrid search with tenant + ACL filters, applies semantic reranking, returns top-k chunks with scores.
- **Answer** — assembles the grounded prompt, streams tokens over SSE, maps inline citation markers `[1]` to chunk IDs, runs a groundedness check on a sample.
- **Eval** — runs golden questions against the live pipeline, computes retrieval hit-rate, MRR, groundedness, citation accuracy; persists runs for trend charts.

---

## 4. Data design

**Azure AI Search index (one index, tenant-partitioned by filter — document the "index-per-tenant at scale" alternative):**

```json
{
  "name": "chunks",
  "fields": [
    { "name": "chunkId",     "type": "Edm.String", "key": true },
    { "name": "tenantId",    "type": "Edm.String", "filterable": true },
    { "name": "documentId",  "type": "Edm.String", "filterable": true },
    { "name": "sourceType",  "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "title",       "type": "Edm.String", "searchable": true },
    { "name": "content",     "type": "Edm.String", "searchable": true, "analyzer": "en.microsoft" },
    { "name": "contentVector", "type": "Collection(Edm.Single)",
      "dimensions": 3072, "vectorSearchProfile": "hnsw-default" },
    { "name": "allowedPrincipals", "type": "Collection(Edm.String)", "filterable": true },
    { "name": "sourceUrl",   "type": "Edm.String" },
    { "name": "updatedAt",   "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true }
  ]
}
```

`allowedPrincipals` holds Entra object IDs of users AND groups with read access (plus a sentinel like `tenant:{id}:all` for org-wide docs). This single field is the heart of the whole project.

**Cosmos DB `chunks` container** — partition key `/tenantId`; full chunk text, neighbor chunk IDs (for context expansion), extraction metadata, content hash.

**Azure SQL** — `Tenants`, `Sources`, `SyncRuns`, `Documents` (registry + hash + ACL snapshot), `EvalRuns`, `EvalResults`, `UsageLedger` (per-request tokens/cost).

**Redis** — semantic cache (embedding of the question → cached answer for near-duplicate questions **scoped per tenant + permission-set hash**, or you build a cache that leaks data across users — great README callout), plus rate-limit counters.

---

## 5. The two patterns that make this project senior-level

### 5a. Security trimming at query time

Every search call gets a mandatory filter built from the caller's token — never from request parameters:

```csharp
// claims from the validated JWT — NOT from the request body
var principals = new List<string> { $"user:{oid}" };
principals.AddRange(groupIds.Select(g => $"group:{g}"));
principals.Add($"tenant:{tenantId}:all");

var options = new SearchOptions
{
    Filter = $"tenantId eq '{tenantId}' and allowedPrincipals/any(p: search.in(p, '{string.Join('|', principals)}', '|'))",
    VectorSearch = { Queries = { new VectorizedQuery(questionEmbedding) { KNearestNeighborsCount = 50, Fields = { "contentVector" } } } },
    QueryType = SearchQueryType.Semantic,   // hybrid + semantic reranker
    SemanticSearch = new() { SemanticConfigurationName = "default" },
    Size = 10
};
```

Rules to enforce (and test): the filter is constructed in one place (a `SecurityTrimmingService`), it's impossible to call search without it (wrap the client, don't expose it raw), and an integration test proves a user without access gets zero chunks from a planted "secret" document.

**Group transitivity:** Entra tokens don't include nested groups by default. v1: expand transitive groups via Graph at login and cache in Redis (15-min TTL). Document the trade-off.

### 5b. The token flow

APIM validates the JWT → forwards to Gateway/Query with the original bearer token → services re-validate and extract `tid`, `oid`, `groups`. Service-to-service calls (Query → Answer) propagate the user context via a signed internal header or token pass-through. Managed Identity is used for all Azure resource access (Search, Cosmos, OpenAI, Service Bus) — the demo has **zero connection strings**. If you later add per-user Graph calls (e.g., group expansion), that's where the OBO flow comes in — implement it there and you've got the full story.

---

## 6. RAG pipeline details

**Chunking (in `KnowVault.Domain`, fully unit-tested):**
- Structure-aware first: split on headings/sections from the parser layout, then recursive token-based splitting to ~512 tokens with 15% overlap.
- Preserve breadcrumbs: prepend `Title > Section` to each chunk's embedded text (cheap, big retrieval gains).
- Tables: extract as markdown, keep whole where possible.
- Make chunk size/overlap per-tenant config so you can A/B via evals.

**Query flow:**
1. **Query rewrite** (GPT-4o-mini): resolve pronouns from chat history, expand acronyms — skip if no history.
2. **Semantic cache check** (Redis): cosine similarity ≥ 0.97 against cached questions *within the same tenant + permission hash* → return cached answer with a `cached: true` flag.
3. **Hybrid retrieval** (top 50) → **semantic reranker** → top 8–10.
4. **Context assembly**: dedupe by document, optionally pull neighbor chunks from Cosmos for the top hit.
5. **Generation** with a strict grounding prompt: answer only from context, cite as `[n]`, say "I don't have information on that" when context is insufficient (test this behavior explicitly).
6. **Streaming** via SSE; citation markers resolved client-side against a `sources` array sent as the first event.
7. **Post-hoc groundedness scoring** on a 10% sample (async, doesn't block the response).

---

## 7. Evaluation harness (your biggest differentiator)

**Golden set:** 40–60 questions over a fixture corpus (use public docs — e.g., Azure architecture docs — so the repo is shareable). Each entry: question, expected source document IDs, reference answer, and a category (factual / multi-doc / unanswerable / permission-restricted).

**Metrics per run:**
- Retrieval hit-rate@10 and MRR (did the right chunks come back, how high)
- Groundedness (LLM judge: is every claim supported by the retrieved context?)
- Citation accuracy (do cited chunks actually contain the claim?)
- Refusal correctness (unanswerable questions → refusal, answerable → no refusal)
- **Security**: permission-restricted questions asked as an unauthorized user must return no restricted content — this metric must be 100%, always.
- Latency p50/p95 and cost per question.

**CI integration:** eval runs against an ephemeral or staging index on every PR touching prompts, chunking, or retrieval; results posted as a PR comment (delta vs. main); hard gate on security metric and >2-point drops in hit-rate/groundedness.

---

## 8. Phased roadmap (10–12 weeks part-time)

### Phase 0 — Foundations (week 1)
- Solution skeleton with Aspire AppHost + ServiceDefaults (OTel, health checks, Polly resilience handlers).
- Bicep: resource group, Log Analytics + App Insights, Key Vault, ACR, Container Apps environment, Azure SQL serverless, Service Bus namespace, Storage account. Deploy it once, tear it down, deploy again — prove reproducibility now, not in week 10.
- GitHub Actions: build + unit test + `dotnet format` on PR; OIDC federated credentials to Azure (no publish-profile secrets).
- **Done when:** `azd up` (or your Bicep script) stands up the empty environment and a hello-world Container App shows traces in App Insights.

### Phase 1 — Ingestion pipeline via direct upload (weeks 2–3)
- Skip connectors first: Admin API issues SAS URLs, files land in Blob, Event Grid → Service Bus → Ingestion worker.
- Text extraction (start with PDF via Document Intelligence + Markdown/HTML native), chunking in Domain with full unit tests, batched embeddings, upsert to AI Search + Cosmos.
- Idempotency via content hash (re-uploading the same file is a no-op); DLQ handling with a small requeue CLI.
- **Done when:** you can drop 50 PDFs in and watch one trace per document flow upload → indexed, with failures landing in the DLQ.

### Phase 2 — Query + Answer (weeks 4–5)
- Query service: hybrid retrieval + reranking (hardcode a single tenant for now).
- Answer service: grounded prompt, SSE streaming, citations, refusal behavior.
- Minimal chat UI (Blazor or a simple React page) — you need it for demos and for dogfooding quality.
- **Done when:** you can ask questions about your corpus and get streamed, cited, correct answers.

### Phase 3 — Multi-tenancy + security trimming (weeks 6–7)
- Entra ID app registrations, JWT validation, tenant onboarding in Admin.
- `allowedPrincipals` populated at ingestion; `SecurityTrimmingService`; group expansion with Redis cache.
- Integration tests: cross-tenant isolation test + planted-secret-document test (run in CI against emulators/test index).
- **Done when:** two test users with different permissions get provably different answers to the same question.

### Phase 4 — Eval harness + CI quality gates (week 8)
- Golden set, Eval service, metric computation, PR-comment reporting, hard gates.
- Baseline run recorded; try one chunking change and watch the eval diff — that experiment writes itself into the README.
- **Done when:** a deliberately-broken retrieval PR fails CI with a readable report.

### Phase 5 — Connector + polish (weeks 9–10)
- One real connector: SharePoint via Microsoft Graph (app-only, delta queries for change detection, ACL capture, deletion tombstones). Confluence stays "future work."
- Semantic cache; per-tenant usage ledger; cost dashboard (App Insights workbook: tokens, cost/query, cache hit rate, per-stage latency).
- **Done when:** editing a SharePoint page updates answers within one sync interval, and revoking a user's access removes those chunks from their results.

### Phase 6 — Production posture + demo (weeks 11–12)
- Private endpoints for SQL/Cosmos/Search/OpenAI, APIM in front, rate limiting per tenant.
- Load test (k6): 50 concurrent questioners, measure p95 and cost.
- README with architecture diagrams, ADR index, eval report, cost breakdown, "what I'd change at 10x scale" section. Record a 3-minute demo video.

---

## 9. CI/CD shape (GitHub Actions)

```
pr.yml:        build → unit tests → integration tests (Testcontainers/emulators)
               → [if prompts|chunking|retrieval changed] eval suite vs staging index
               → post eval diff as PR comment → gates
main.yml:      build → push images to ACR → deploy Bicep (staging)
               → smoke tests + full eval run → manual approval → prod
               → Container Apps revision shift 10% → 100% with health gate
security.yml:  CodeQL + dependency review + secret scanning + Trivy image scan
```

The security workflow (secret scanning, image scanning) mirrors what production teams run — the same tooling here as in professional pipelines.

---

## 10. Observability plan

- **Traces:** one distributed trace per question spanning gateway → query (with retrieval span: filter, top-k, rerank duration) → answer (with generation span: model, tokens, TTFT). Same for ingestion per document.
- **Custom metrics:** `knowvault.retrieval.hitrate` (sampled), `knowvault.tokens.{prompt,completion}`, `knowvault.cost.usd`, `knowvault.cache.hit`, `knowvault.trimming.filtered_count`, DLQ depth.
- **Dashboards:** an ops workbook (latency, errors, queue lag) and a quality workbook (groundedness trend, cost/query, cache hit rate).
- **Alerts:** DLQ depth > 10, p95 answer latency > 8s, daily OpenAI spend > threshold, any security-eval failure in production sampling.

## 11. Cost control (put the numbers in the README)

- Container Apps scale-to-zero on Ingestion/Eval; min-replica 1 only on Query/Answer during demo periods.
- AI Search Basic tier; Azure SQL serverless auto-pause; Cosmos serverless.
- GPT-4o-mini for generation (~₹0.1–0.3 per answer at typical context sizes); embeddings batched.
- Tear down non-essential resources between build sessions — Bicep makes rebuild one command. Realistic burn: roughly $30–60/month during active development if you're disciplined, mostly AI Search + OpenAI.

## 12. The five questions this design answers

1. "How do you prevent RAG from leaking documents users shouldn't see?" → denormalized ACLs, query-time trimming, sync-latency trade-off, tombstoning, 100%-gate security evals.
2. "How do you know your RAG system is any good?" → golden sets, hit-rate/MRR/groundedness, LLM-as-judge caveats, eval gates in CI, prompt changes as reviewed diffs.
3. "How do you control LLM costs?" → model tiering, semantic caching (and its permission-scoping pitfall), token metrics per tenant, batch embeddings.
4. "Walk me through a request" → full distributed trace, resilience policies, streaming, graceful degradation (reranker down → plain hybrid; OpenAI throttled → 429 with retry-after).
5. "How would this scale 10x?" → index-per-tenant sharding, AKS migration path, provisioned throughput, regional replicas — all pre-written in the README.

## 13. First week, concretely

1. `dotnet new` the Aspire solution, add the seven projects, wire ServiceDefaults.
2. Write ADR-001 (compute) and ADR-002 (ACL strategy) — 15 minutes each, huge payoff.
3. Write the Bicep for the Phase 0 resource set; deploy; confirm App Insights traces.
4. Create the golden-questions file with just 10 questions against 5 sample docs — the eval mindset starts on day one, even before there's a pipeline to evaluate.
