param principalId string

// Data-plane roles for the shared app identity, scoped to the resource group.
// (Cosmos uses its own data-plane role system — assigned in cosmos.bicep.)
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  serviceBusDataOwner: '090c5cfd-751d-490a-894a-3ce6f1109419'
  searchServiceContributor: '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
  searchIndexDataContributor: '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  cognitiveServicesUser: 'a97b65f3-24c7-4388-baec-2e87135dc908'
}

resource assignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in items(roles): {
    name: guid(resourceGroup().id, principalId, role.value)
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role.value)
    }
  }
]
