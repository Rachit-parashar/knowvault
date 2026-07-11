param baseName string
param location string
param tags object

@description('Principal of the shared app identity; grants Cosmos data-plane access when set.')
param appPrincipalId string = ''

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: 'cosmos-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    capabilities: [
      { name: 'EnableServerless' }
    ]
    minimalTlsVersion: 'Tls12'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: 'knowvault'
  properties: {
    resource: { id: 'knowvault' }
  }
}

// Partition by tenant: physical separation of tenant data, and every
// answer-time point-read carries the tenant id anyway.
resource chunks 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'chunks'
  properties: {
    resource: {
      id: 'chunks'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        // Point-reads by id + tenantId only; skip indexing chunk bodies.
        excludedPaths: [{ path: '/content/?' }, { path: '/embeddedText/?' }]
        includedPaths: [{ path: '/*' }]
      }
    }
  }
}

resource appDataRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(appPrincipalId)) {
  parent: account
  name: guid(account.id, appPrincipalId, 'data-contributor')
  properties: {
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: appPrincipalId
    scope: account.id
  }
}

output accountEndpoint string = account.properties.documentEndpoint
output accountName string = account.name
