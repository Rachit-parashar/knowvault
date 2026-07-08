targetScope = 'subscription'

@description('Environment name, used in resource names (e.g. dev, staging, prod).')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = 'eastus2'

@description('SQL admin login for the serverless database.')
param sqlAdminLogin string = 'knowvaultadmin'

@secure()
@description('SQL admin password. Pass at deploy time; stored in Key Vault by the module.')
param sqlAdminPassword string

@description('AI Search tier: free for early development, basic from Phase 2 (semantic reranker).')
@allowed(['free', 'basic'])
param searchSku string = 'free'

@description('Service Bus tier: Basic until topics are needed.')
@allowed(['Basic', 'Standard'])
param serviceBusSku string = 'Basic'

@description('Azure OpenAI can be blocked on brand-new subscriptions (error 715-123420, clears within ~a day). Disable to deploy everything else.')
param deployOpenAi bool = true

var baseName = 'knowvault-${environmentName}'
var tags = {
  project: 'knowvault'
  environment: environmentName
  managedBy: 'bicep'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${baseName}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  scope: rg
  name: 'keyvault'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module registry 'modules/registry.bicep' = {
  scope: rg
  name: 'registry'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  scope: rg
  name: 'container-apps-env'
  params: {
    baseName: baseName
    location: location
    tags: tags
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

module sql 'modules/sql.bicep' = {
  scope: rg
  name: 'sql'
  params: {
    baseName: baseName
    location: location
    tags: tags
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  scope: rg
  name: 'servicebus'
  params: {
    baseName: baseName
    location: location
    tags: tags
    sku: serviceBusSku
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module search 'modules/search.bicep' = {
  scope: rg
  name: 'search'
  params: {
    baseName: baseName
    location: location
    tags: tags
    sku: searchSku
  }
}

module cosmos 'modules/cosmos.bicep' = {
  scope: rg
  name: 'cosmos'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module openai 'modules/openai.bicep' = if (deployOpenAi) {
  scope: rg
  name: 'openai'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module eventGrid 'modules/eventgrid.bicep' = {
  scope: rg
  name: 'eventgrid'
  params: {
    baseName: baseName
    location: location
    tags: tags
    storageAccountId: storage.outputs.accountId
    documentChangedQueueId: serviceBus.outputs.documentChangedQueueId
  }
}

output resourceGroupName string = rg.name
output containerAppsEnvironmentId string = containerAppsEnv.outputs.environmentId
output containerRegistryLoginServer string = registry.outputs.loginServer
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output keyVaultUri string = keyVault.outputs.vaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output storageAccountName string = storage.outputs.accountName
output searchEndpoint string = search.outputs.searchEndpoint
output cosmosEndpoint string = cosmos.outputs.accountEndpoint
output openAiEndpoint string = deployOpenAi ? openai!.outputs.endpoint : ''
