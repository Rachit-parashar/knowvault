param location string
param tags object
param environmentId string
param registryServer string
param identityId string
param identityClientId string
param appInsightsConnectionString string
param openAiEndpoint string
param searchEndpoint string
param cosmosEndpoint string
param docIntelligenceEndpoint string
param storageAccountName string
param serviceBusNamespace string
param imageTag string = 'v1'

var serviceBusFqdn = '${serviceBusNamespace}.servicebus.windows.net'
var uploadsConnection = 'Endpoint=https://${storageAccountName}.blob.${environment().suffixes.storage};ContainerName=uploads'
var syncStateConnection = 'Endpoint=https://${storageAccountName}.blob.${environment().suffixes.storage};ContainerName=sync-state'

var commonEnv = [
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
]

var identityBlock = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identityId}': {} }
}

var registryBlock = [{ server: registryServer, identity: identityId }]

// ---- Query: internal ingress, called by Answer ----
resource query 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'query'
  location: location
  tags: tags
  identity: identityBlock
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      registries: registryBlock
      ingress: { external: false, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'query'
          image: '${registryServer}/knowvault/query:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [
            { name: 'Azure__Search__Endpoint', value: searchEndpoint }
            { name: 'Azure__OpenAI__Endpoint', value: openAiEndpoint }
            { name: 'DevUsers__alice__0', value: 'hr' }
          ])
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

// ---- Answer: the public front door (chat UI + SSE API) ----
resource answer 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'answer'
  location: location
  tags: tags
  identity: identityBlock
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      registries: registryBlock
      ingress: { external: true, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'answer'
          image: '${registryServer}/knowvault/answer:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [
            { name: 'Azure__OpenAI__Endpoint', value: openAiEndpoint }
            { name: 'services__query__http__0', value: 'http://query' }
          ])
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

// ---- Admin: upload APIs ----
resource admin 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'admin'
  location: location
  tags: tags
  identity: identityBlock
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      registries: registryBlock
      ingress: { external: true, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'admin'
          image: '${registryServer}/knowvault/admin:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__uploads', value: uploadsConnection }
            { name: 'ConnectionStrings__messaging', value: serviceBusFqdn }
          ])
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

// ---- Ingestion: queue worker, always one warm replica ----
resource ingestion 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ingestion'
  location: location
  tags: tags
  identity: identityBlock
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      registries: registryBlock
    }
    template: {
      containers: [
        {
          name: 'ingestion'
          image: '${registryServer}/knowvault/ingestion:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__uploads', value: uploadsConnection }
            { name: 'ConnectionStrings__messaging', value: serviceBusFqdn }
            { name: 'Azure__OpenAI__Endpoint', value: openAiEndpoint }
            { name: 'Azure__Search__Endpoint', value: searchEndpoint }
            { name: 'Azure__Cosmos__Endpoint', value: cosmosEndpoint }
            { name: 'Azure__DocumentIntelligence__Endpoint', value: docIntelligenceEndpoint }
          ])
        }
      ]
      // KEDA scale-on-queue-depth with managed identity replaces this fixed
      // replica when demo load justifies it; a warm worker keeps demos snappy.
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ---- Connector: parked at zero until a cloud source (SharePoint) exists ----
resource connector 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'connector'
  location: location
  tags: tags
  identity: identityBlock
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      registries: registryBlock
    }
    template: {
      containers: [
        {
          name: 'connector'
          image: '${registryServer}/knowvault/connector:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__uploads', value: uploadsConnection }
            { name: 'ConnectionStrings__sync-state', value: syncStateConnection }
            { name: 'ConnectionStrings__messaging', value: serviceBusFqdn }
          ])
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}

output answerUrl string = 'https://${answer.properties.configuration.ingress.fqdn}'
output adminUrl string = 'https://${admin.properties.configuration.ingress.fqdn}'
