# Security Policy

KnowVault's core promise is permission-aware retrieval: content a user cannot read must never
appear in their answers — or even become a search candidate. Reports that break that promise
are treated as the highest severity.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Instead, use GitHub's
[private vulnerability reporting](../../security/advisories/new) on this repository.
Include reproduction steps and, if it concerns the trimming model, the identities involved
(tenant / user / groups) and the document ACLs.

You can expect an acknowledgement within a few days. Confirmed cross-tenant or cross-user
leakage is fixed before any other work continues — the eval suite's security metric gates
every merge at 100% for exactly this reason.

## Scope notes

- The dev environment intentionally allows header-based identity behind the
  `Entra:AllowDevHeaders` flag for local development and CI; production posture disables it.
  Reports against the dev flag itself are out of scope, reports that bypass **token-validated**
  trimming are very much in scope.
- Secrets: the system runs keyless (managed identity). Any credential found in the repository
  history is a valid finding.

## Automated scanning

Every push runs TruffleHog secret scanning; CodeQL and dependency review run on public builds.
The golden-question suite includes planted-secret documents whose leakage fails CI.
