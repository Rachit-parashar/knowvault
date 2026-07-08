param baseName string
param location string
param tags object

var accountName = take(replace('st${baseName}${uniqueString(resourceGroup().id)}', '-', ''), 24)

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: accountName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
}

// Direct-upload landing zone: Admin issues SAS URLs into this container,
// Event Grid picks up BlobCreated and feeds Service Bus (Phase 1).
resource uploads 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: 'uploads'
  properties: {
    publicAccess: 'None'
  }
}

output accountName string = account.name
