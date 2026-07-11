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

@description('Deploy the KnowVault services as container apps. Requires images in ACR (dotnet publish /t:PublishContainer) and deployOpenAi.')
param deployApps bool = false

@description('Image tag the container apps run.')
param imageTag string = 'v1'

@description('Entra ID sign-in configuration for the deployed apps; empty disables JWT auth.')
param entraTenantId string = ''
param entraClientId string = ''
param entraIdentityEnv array = []

@description('Existing OpenAI endpoint for app deploys when the openai module is skipped (Azure error 715-123420 flags even idempotent re-PUTs of young accounts).')
param openAiEndpointOverride string = ''

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

module identity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity'
  params: {
    baseName: baseName
    location: location
    tags: tags
  }
}

module roles 'modules/roles.bicep' = {
  scope: rg
  name: 'roles'
  params: {
    principalId: identity.outputs.principalId
  }
}

module cosmos 'modules/cosmos.bicep' = {
  scope: rg
  name: 'cosmos'
  params: {
    baseName: baseName
    location: location
    tags: tags
    appPrincipalId: identity.outputs.principalId
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

module docIntelligence 'modules/docintelligence.bicep' = {
  scope: rg
  name: 'docintelligence'
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

module containerApps 'modules/container-apps.bicep' = if (deployApps && (deployOpenAi || !empty(openAiEndpointOverride))) {
  scope: rg
  name: 'container-apps'
  params: {
    location: location
    tags: tags
    environmentId: containerAppsEnv.outputs.environmentId
    registryServer: registry.outputs.loginServer
    identityId: identity.outputs.identityId
    identityClientId: identity.outputs.clientId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    openAiEndpoint: !empty(openAiEndpointOverride) ? openAiEndpointOverride : openai!.outputs.endpoint
    searchEndpoint: search.outputs.searchEndpoint
    cosmosEndpoint: cosmos.outputs.accountEndpoint
    docIntelligenceEndpoint: docIntelligence.outputs.endpoint
    storageAccountName: storage.outputs.accountName
    serviceBusNamespace: serviceBus.outputs.namespaceName
    imageTag: imageTag
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    entraIdentityEnv: entraIdentityEnv
  }
}

output resourceGroupName string = rg.name
output answerUrl string = (deployApps && (deployOpenAi || !empty(openAiEndpointOverride))) ? containerApps!.outputs.answerUrl : ''
output containerAppsEnvironmentId string = containerAppsEnv.outputs.environmentId
output containerRegistryLoginServer string = registry.outputs.loginServer
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output keyVaultUri string = keyVault.outputs.vaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output storageAccountName string = storage.outputs.accountName
output searchEndpoint string = search.outputs.searchEndpoint
output cosmosEndpoint string = cosmos.outputs.accountEndpoint
output openAiEndpoint string = deployOpenAi ? openai!.outputs.endpoint : ''
output docIntelligenceEndpoint string = docIntelligence.outputs.endpoint
