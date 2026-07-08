param baseName string
param location string
param tags object

@description('Basic (~$0.05/M ops, queues + DLQ only) suffices until topics are needed; Standard is ~$10/month.')
@allowed(['Basic', 'Standard'])
param sku string = 'Basic'

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${baseName}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource ingestQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: 'document-changed'
  properties: {
    maxDeliveryCount: 5 // then dead-letter — a requeue CLI handles the DLQ
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P1D'
    deadLetteringOnMessageExpiration: true
  }
}

resource deleteQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: 'document-deleted'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
    deadLetteringOnMessageExpiration: true
  }
}

output namespaceName string = namespace.name
output documentChangedQueueId string = ingestQueue.id
