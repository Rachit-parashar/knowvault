param baseName string
param location string
param tags object

@description('free = $0 dev tier (50 MB, no semantic reranker, one per subscription); basic = ~$75/month, needed from Phase 2 for the semantic reranker.')
@allowed(['free', 'basic'])
param sku string = 'free'

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: 'srch-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  sku: { name: sku }
  properties: union(
    {
      replicaCount: 1
      partitionCount: 1
      hostingMode: 'default'
      authOptions: {
        aadOrApiKey: { aadAuthFailureMode: 'http401WithBearerChallenge' }
      }
    },
    // Semantic reranker is unavailable on the free tier.
    sku == 'free' ? {} : { semanticSearch: 'free' }
  )
}

// The 'chunks' index (vector profile + allowedPrincipals filter field) is
// created by the application on startup — index schemas are code, not infra.

output searchEndpoint string = 'https://${search.name}.search.windows.net'
output searchName string = search.name
