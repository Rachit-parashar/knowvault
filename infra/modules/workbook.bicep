param location string
param tags object
param appInsightsId string

// The cost/quality dashboard (success criterion 5), pinned to App Insights.
resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, 'knowvault-cost-quality')
  location: location
  tags: tags
  kind: 'shared'
  properties: {
    displayName: 'KnowVault — Cost & Quality'
    category: 'workbook'
    sourceId: appInsightsId
    serializedData: loadTextContent('knowvault-workbook.json')
  }
}
