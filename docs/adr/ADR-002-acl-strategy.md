# ADR-002: ACL strategy — denormalized ACLs enforced by query-time filters

**Status:** Accepted · **Date:** 2026-07-08

## Context

Answers must only draw from documents the asking user can read (README success criterion 2). Source systems (SharePoint, Confluence) own the permissions. Two broad options exist for enforcing them at retrieval time:

1. **Query-time authorization against the source** — retrieve candidate chunks, then check each against the source system (e.g., Graph calls under an On-Behalf-Of token) before use.
2. **Denormalized ACLs** — capture each document's ACL (users and groups with read access) at sync time, store it on every chunk in the search index, and apply it as a mandatory filter inside the search query itself.

## Decision

Denormalize ACLs into the index at sync time and enforce them via query-time filters. **OBO is not used for retrieval.**

Each chunk carries an `allowedPrincipals` field: Entra object IDs of users and groups with read access, plus a sentinel `tenant:{id}:all` for org-wide documents. Every search call gets a mandatory filter built from the **validated JWT's claims** (`tid`, `oid`, `groups`) — never from request parameters:

```
tenantId eq '{tid}' and allowedPrincipals/any(p: search.in(p, 'user:{oid}|group:{g1}|...|tenant:{tid}:all', '|'))
```

The filter is constructed in exactly one place (`SecurityTrimmingService`), and the raw search client is never exposed — services can only search through a wrapper that requires a trimming context.

## Rationale

- Per-chunk OBO calls to source systems at query time would be slow (top-50 candidates × Graph latency) and rate-limited into uselessness.
- Filtering inside the search query means unauthorized content is never even a candidate — it can't leak via reranking, context assembly, or logging.
- This is how M365 Copilot-style systems work in practice; it's the industry-standard shape for permission-aware RAG.

## Trade-offs and mitigations

- **Permission changes propagate at sync latency.** A revoked user keeps access until the next sync. Mitigated by short sync intervals and deletion/revocation **tombstones** processed with priority.
- **Group transitivity.** Entra tokens don't include nested groups by default. v1: expand transitive groups via Graph at login and cache in Redis (15-minute TTL). The TTL bounds the staleness window for group-membership changes.
- **Index write amplification.** An ACL change on a widely-shared document rewrites all its chunks. Acceptable at v1 scale; at 10x, move to an ACL-indirection design (permission-set IDs).

## Verification

- Integration test: a planted "secret" document must return zero chunks for a user without access.
- Integration test: cross-tenant isolation — Tenant A user gets nothing from Tenant B's corpus.
- Eval suite: permission-restricted golden questions asked as an unauthorized user must return no restricted content. This metric gates CI at **100%, always**.
