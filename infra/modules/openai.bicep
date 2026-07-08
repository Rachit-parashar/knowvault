param baseName string
param location string
param tags object

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
  name: 'text-embedding-3-large'
  sku: { name: 'Standard', capacity: 120 }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
    }
  }
}

resource generation 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4o-mini'
  sku: { name: 'GlobalStandard', capacity: 100 }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
    }
  }
  dependsOn: [embeddings] // deployments must be created serially
}

// GPT-4o (eval judge) is deployed with the Eval phase — it only runs in CI
// and sampled production, so it shouldn't exist before the eval harness does.

output endpoint string = openai.properties.endpoint
output accountName string = openai.name
