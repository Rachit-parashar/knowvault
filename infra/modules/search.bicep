param baseName string
param location string
param tags object

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: 'srch-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  sku: { name: 'basic' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'free' // semantic reranker; standard tier at scale
    authOptions: {
      aadOrApiKey: { aadAuthFailureMode: 'http401WithBearerChallenge' }
    }
  }
}

// The 'chunks' index (vector profile + allowedPrincipals filter field) is
// created by the application on startup — index schemas are code, not infra.

output searchEndpoint string = 'https://${search.name}.search.windows.net'
output searchName string = search.name
