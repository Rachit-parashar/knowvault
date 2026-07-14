# Configuration Reference

Everything the system needs to run — connection strings, endpoints, parameters, and secrets —
and how .NET Aspire wires them so the **same service code** runs locally against emulators and
in Azure against real resources, with **no secret values in code, config files, or this document**.

---

## 1. The model: names, not values

Services never hardcode where anything lives. Each dependency is referenced **by name**, and the
*environment* supplies the value for that name:

| Mechanism | Config key shape | Who sets it locally | Who sets it in Azure |
|---|---|---|---|
| Aspire connection string | `ConnectionStrings__{name}` | AppHost `WithReference(resource)` | Bicep env on the container app |
| Aspire service discovery | `services__{name}__http__0` | AppHost `WithReference(project)` | Bicep env (`http://query` — Container Apps internal DNS) |
| Plain options | `Section__Key` (e.g. `Azure__Search__Endpoint`) | `appsettings.Development.json` | Bicep env |
| True secrets | — | never in app config (see §4) | Key Vault |

So `builder.AddAzureBlobContainerClient("uploads")` is the whole story in code: locally the
AppHost points "uploads" at an Azurite container it started in Docker; in Azure the container
app's environment points it at the real storage account. Same line, both worlds.

## 2. Why there are (almost) no secrets: keyless connection strings

Under Entra ID authentication, a "connection string" is just an **address**:

| Dependency | Connection string value (shape) | Auth |
|---|---|---|
| Blob container `uploads` / `sync-state` | `Endpoint=https://{account}.blob.core.windows.net;ContainerName={name}` | `DefaultAzureCredential` |
| Service Bus `messaging` | `{namespace}.servicebus.windows.net` (just the FQDN) | `DefaultAzureCredential` |
| AI Search | `https://{service}.search.windows.net` | `DefaultAzureCredential` |
| Azure OpenAI | `https://{account}.openai.azure.com/` (keys disabled on the account) | `DefaultAzureCredential` |
| Cosmos DB | `https://{account}.documents.azure.com:443/` | `DefaultAzureCredential` |
| Document Intelligence | `https://{account}.cognitiveservices.azure.com/` | `DefaultAzureCredential` |

`DefaultAzureCredential` resolves to **your `az login`** on a dev machine and to the shared
**user-assigned managed identity** in Container Apps (selected via the `AZURE_CLIENT_ID` env var).
Access comes from RBAC role assignments (declared in `infra/modules/roles.bicep`), not from keys —
which is why leaking any of these "connection strings" leaks nothing.

## 3. Full per-service configuration matrix

`__` in env-var names maps to `:` in .NET configuration. Local values live in each service's
`appsettings.Development.json`; cloud values are set by `infra/modules/container-apps.bicep`.

**Every service (cloud):** `AZURE_CLIENT_ID` (managed identity selector),
`APPLICATIONINSIGHTS_CONNECTION_STRING` (telemetry; an ingestion address, not a secret).

| Service | Key | Purpose |
|---|---|---|
| **Admin** | `ConnectionStrings__uploads` | SAS issuance + upload verification |
| | `ConnectionStrings__messaging` | emits `DocumentChanged` |
| **Ingestion** | `ConnectionStrings__uploads`, `ConnectionStrings__messaging` | consume queue, download blobs |
| | `Azure__OpenAI__Endpoint`, `Azure__OpenAI__EmbeddingDeployment` | embeddings (default `text-embedding-3-large`) |
| | `Azure__Search__Endpoint`, `Azure__Cosmos__Endpoint` | index + chunk store |
| | `Azure__DocumentIntelligence__Endpoint` | PDF extraction |
| **Query** | `Azure__Search__Endpoint`, `Azure__OpenAI__Endpoint` | hybrid retrieval |
| | `Azure__Cosmos__Endpoint` | neighbor-chunk expansion |
| | `DevUsers__{user}__{n}` | dev group directory (e.g. `DevUsers__alice__0=hr`) |
| | `Entra__TenantId`, `Entra__ClientId` | JWT validation (empty ⇒ auth off) |
| | `Entra__AllowDevHeaders` | `false` ⇒ bearer tokens mandatory (production posture) |
| | `Entra__AppTenant` | logical tenant for signed-in users |
| | `Entra__UserNames__{oid}`, `Entra__GroupNames__{gid}` | map Entra object ids → principal names |
| **Answer** | `Azure__OpenAI__Endpoint`, `Azure__OpenAI__GenerationDeployment` | answers (default `gpt-5-mini`) |
| | `Azure__OpenAI__PromptPricePerMTokens`, `…CompletionPricePerMTokens` | cost-metric rates |
| | `services__query__http__0` | service discovery → Query |
| | `Entra__*` | same as Query |
| **Connector** | `ConnectionStrings__uploads`, `ConnectionStrings__sync-state`, `ConnectionStrings__messaging` | staging, inventory, events |
| | `Sync__IntervalSeconds` | sync cadence (default 30) |
| | `Connector__InboxPath` | local-folder source (unset ⇒ off) |
| | `Connector__GoogleDrive__FolderId` | Drive source root (unset ⇒ off) |
| | `Connector__GoogleDrive__ServiceAccountKeyPath` **or** `…ServiceAccountJson` | Drive credential (see §4) |
| | `Connector__GoogleDrive__Tenant`, `…UserNames__{email}` | logical tenant + email→principal map |
| **Eval** | `services__admin__http__0`, `services__query__http__0`, `services__answer__http__0` | targets under test |
| | `Azure__OpenAI__Endpoint`, `Azure__OpenAI__JudgeDeployment` | LLM judge |
| | `EVALS_DIR` | golden set / corpus location (upward-search fallback) |

## 4. The actual secrets — inventory and storage

The short list of values that *are* secret, and the single place each lives:

| Secret | Stored in | Consumed by | Never appears in |
|---|---|---|---|
| SQL admin password | Key Vault `kvknowvaultdevwoaaq7havk` / `sql-admin-password` | Bicep `@secure()` param at deploy time; GitHub Actions secret `SQL_ADMIN_PASSWORD` for CI deploys | app config, repo, logs |
| Test-user passwords | Key Vault `test-user-alice`, `test-user-mallory` | humans signing in to demo | repo, app config |
| Google Drive service-account key (JSON) | local `.secrets/gdrive-sa.json` (gitignored); as a container secret/Key Vault reference when the connector runs in Azure | Connector | repo (path is configured, not the key) |
| GitHub → Azure trust | **no secret at all** — OIDC federated credential | GitHub Actions `azure/login` | anywhere (that's the point) |

GitHub Actions also stores `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` as
"secrets" for convenience — these are identifiers, not credentials; possession grants nothing
without the federated trust.

## 5. Where Aspire parameters/user-secrets would fit (and why they're mostly unused here)

Aspire has a first-class mechanism for secret configuration:
`builder.AddParameter("name", secret: true)` in the AppHost, backed by **.NET user secrets**
locally (`dotnet user-secrets set Parameters:name …` — stored per-user outside the repo) and by
secure stores when deployed. It exists precisely so a value can flow to services without ever
being committed.

KnowVault deliberately needs almost none of it: the architecture is **keyless**, so the values
Aspire injects (`ConnectionStrings__*`, `services__*`) are all non-secret addresses. The pattern
becomes relevant the moment a dependency can't do Entra auth — e.g., the Google Drive
service-account key is exactly the kind of value that would be modeled as a secret Aspire
parameter for local runs (today it's a gitignored file path, which achieves the same
keep-it-out-of-git property).

Rule of thumb applied throughout: **prefer removing the secret (managed identity / OIDC) over
managing the secret; when one must exist, it lives in exactly one vault and is referenced by
name everywhere else.**
