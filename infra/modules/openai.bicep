param baseName string
param location string
param tags object

@description('Embedding model deployment name, model, and version (pinned to avoid default-version deprecation).')
param embeddingModel string = 'text-embedding-3-large'
param embeddingModelVersion string = '1'
param embeddingCapacity int = 120

@description('Generation model. gpt-4o-mini was retired 2026-03-31; gpt-5-mini is the current small model with GlobalStandard quota.')
param generationModel string = 'gpt-5-mini'
param generationModelVersion string = '2025-08-07'
param generationSku string = 'GlobalStandard'
param generationCapacity int = 100

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'oai-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'oai-${baseName}-${uniqueString(resourceGroup().id)}'
    publicNetworkAccess: 'Enabled' // Phase 6 moves this behind a private endpoint
    disableLocalAuth: true // Managed Identity only — no keys anywhere
  }
}

resource embeddings 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: embeddingModel
  sku: { name: 'Standard', capacity: embeddingCapacity }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModel
      version: embeddingModelVersion
    }
  }
}

resource generation 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: generationModel
  sku: { name: generationSku, capacity: generationCapacity }
  properties: {
    model: {
      format: 'OpenAI'
      name: generationModel
      version: generationModelVersion
    }
  }
  dependsOn: [embeddings] // deployments must be created serially
}

// The eval judge (a larger model, GPT-4o-class) is deployed with the Eval
// phase — it only runs in CI and sampled production, so it shouldn't exist
// before the eval harness does.

output endpoint string = openai.properties.endpoint
output accountName string = openai.name
output generationDeployment string = generation.name
output embeddingDeployment string = embeddings.name
