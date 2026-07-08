param baseName string
param location string
param tags object

var registryName = take(replace('cr${baseName}${uniqueString(resourceGroup().id)}', '-', ''), 50)

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    // Managed Identity + AcrPull is used for image pulls — no admin user.
    adminUserEnabled: false
  }
}

output loginServer string = registry.properties.loginServer
output registryName string = registry.name
