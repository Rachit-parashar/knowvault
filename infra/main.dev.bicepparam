using 'main.bicep'

param environmentName = 'dev'
param location = 'eastus2'
param sqlAdminLogin = 'knowvaultadmin'
// Cost-minimized dev tiers: AI Search free (no semantic reranker until
// Phase 2), Service Bus Basic (queues + DLQ only). Only ACR (~$5/month)
// bills while idle; tear down between sessions regardless.
param searchSku = 'free'
param serviceBusSku = 'Basic'
// Pass the password at deploy time:
//   az deployment sub create ... --parameters main.dev.bicepparam sqlAdminPassword=<value>
param sqlAdminPassword = ''
