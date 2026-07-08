param baseName string
param location string
param tags object

// Key Vault names are capped at 24 chars and must be globally unique.
var vaultName = take(replace('kv-${baseName}-${uniqueString(resourceGroup().id)}', '-', ''), 24)

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

output vaultUri string = vault.properties.vaultUri
output vaultName string = vault.name
