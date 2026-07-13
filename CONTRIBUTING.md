# Contributing

This is a personal portfolio project, but issues and pull requests are welcome.

## Ground rules

- **Everything ships through a PR.** The pipeline runs build + 60 unit tests, `dotnet format`,
  Bicep lint, secret scanning, and the **eval gate** — the golden-question suite executed against
  the live dev environment. A PR that drops the security metric below 100%, or hit-rate /
  refusal correctness / groundedness below 90%, does not merge.
- **Warnings are errors.** The solution builds with `TreatWarningsAsErrors` and the strictest
  recommended analyzers; `dotnet format` is enforced.
- **Decisions get an ADR.** Anything architectural belongs in `docs/adr/` with alternatives and
  trade-offs, following the existing four.
- **Identity never travels in request bodies.** Caller identity comes from validated tokens
  (or dev headers behind the explicit flag); the search filter is built only by
  `SecurityTrimming.BuildFilter`. Changes touching this path need tests that attack it.

## Getting started

Prereqs: .NET 10 SDK + Docker Desktop (the local storage/messaging emulators are containers).
Then `dotnet run --project src/KnowVault.AppHost` — the Aspire dashboard link prints in the console.
Azure-backed features (search, embeddings) additionally need `az login` and your own deployed
environment (`infra/` + `scripts/deploy-dev.ps1`).

## Commit style

Imperative subject, body explains *why* and records anything learned the hard way — the git
history doubles as the project's incident log.
