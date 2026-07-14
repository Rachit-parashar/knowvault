using 'main.bicep'

param environmentName = 'dev'
// southeastasia: closest region to the team with full model availability;
// eastus2 rejects new-subscription SQL/Search provisioning (capacity limits).
param location = 'southeastasia'
param sqlAdminLogin = 'knowvaultadmin'
// Cost-minimized dev tiers: AI Search free (no semantic reranker until
// Phase 2), Service Bus Basic (queues + DLQ only). Only ACR (~$5/month)
// bills while idle; tear down between sessions regardless.
param searchSku = 'free'
param serviceBusSku = 'Basic'
param imageTag = 'v4'

// The OpenAI account exists and works but Azure's anti-abuse check (715-123420)
// rejects even idempotent re-PUTs on this young subscription — deploys skip the
// module and reference the live endpoint instead.
param deployOpenAi = false
param openAiEndpointOverride = 'https://oai-knowvault-dev-woaaq7havkzpg.openai.azure.com/'

// Entra ID sign-in (knowvault-chat app registration) with demo identity
// mappings so existing index ACLs (user:alice / group:hr) keep working.
param entraTenantId = 'ef2b695d-55b4-4f36-af8e-f34b7d78f790'
param entraClientId = 'f63b2e89-ef8c-4a59-9692-5e0adbce6e8a'
param entraIdentityEnv = [
  { name: 'Entra__UserNames__43d6c082-4b88-488c-acfc-7de7d519baab', value: 'alice' }
  { name: 'Entra__UserNames__bb3e3579-54fa-4c63-9ffc-39746b2dc182', value: 'mallory' }
  { name: 'Entra__GroupNames__6172c593-9f16-4029-8b6d-460e455215d8', value: 'hr' }
]
// Pass the password at deploy time:
//   az deployment sub create ... --parameters main.dev.bicepparam sqlAdminPassword=<value>
param sqlAdminPassword = ''

