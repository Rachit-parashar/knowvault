# ADR-004: LLM model selection and version pinning

**Status:** Accepted · **Date:** 2026-07-08

## Context

The build plan specified Azure OpenAI models: `text-embedding-3-large` for embeddings, `gpt-4o-mini` for answers and query rewrite, and a GPT-4o-class model as the eval judge (CI/sampled only). When first deploying the infrastructure, two things surfaced that the plan predated:

1. **`gpt-4o-mini` was retired on 2026-03-31.** Its only remaining version (`2024-07-18`) reports `Deprecating`, and a deployment of it fails preflight with `ServiceModelDeprecated`.
2. **New subscriptions get zero `GlobalStandard` quota for older models.** `gpt-4o-mini` showed `GlobalStandard` limit 0 across every region checked, while newer models carry real quota.

## Decision

- **Embeddings:** `text-embedding-3-large`, version pinned to `1`, `Standard` SKU. Unchanged from the plan; version pinned so a future default-version change can't silently break deploys.
- **Generation (answers, query rewrite):** `gpt-5-mini`, version `2025-08-07`, `GlobalStandard` SKU — the current small, cheap chat model, and the one with available quota (500K TPM GlobalStandard in `eastus2`). This replaces the retired `gpt-4o-mini` in the same role.
- **Eval judge:** a larger current-generation model, deployed only when the Eval phase lands (it runs in CI and sampled production, not per query).
- **All model names, versions, SKUs, and capacities are Bicep parameters** (`infra/modules/openai.bicep`), not literals.

## Rationale

- The cost-tiering principle from the plan is unchanged: a cheap small model serves every query; the expensive judge runs only in CI/sampled paths. Only the specific model names moved forward a generation.
- Parameterizing model identity means the next deprecation is a parameter change, not a code change — the same resilience the plan wanted from Bicep reproducibility, applied to the fastest-moving dependency in the system.
- Pinning versions makes deploys deterministic; relying on the service default is what caused the first failed deploy.

## Consequences

- The build plan's prose still says `gpt-4o-mini`; it is a historical planning document. This ADR is the source of truth for deployed models.
- Model quota is region- and subscription-dependent. A `README` "operations" note documents how to check quota (`az cognitiveservices usage list`) before changing regions.
- When quota for a preferred model/SKU (e.g. a `GlobalStandard` successor) is granted, flip the corresponding parameter — no template edit required.
