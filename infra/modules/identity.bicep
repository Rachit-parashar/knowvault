param baseName string
param location string
param tags object

// One user-assigned identity shared by all KnowVault container apps:
// a single set of role assignments, zero connection strings anywhere.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}-apps'
  location: location
  tags: tags
}

output identityId string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
