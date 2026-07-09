param baseName string
param location string
param tags object

@description('F0 free tier: 500 pages/month, one per subscription — plenty for dev. S0 for production volume.')
@allowed(['F0', 'S0'])
param sku string = 'F0'

resource docIntelligence 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'di-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: { name: sku }
  properties: {
    customSubDomainName: 'di-${baseName}-${uniqueString(resourceGroup().id)}'
    publicNetworkAccess: 'Enabled' // Phase 6 moves this behind a private endpoint
    disableLocalAuth: true // Managed Identity only
  }
}

output endpoint string = docIntelligence.properties.endpoint
output accountName string = docIntelligence.name
