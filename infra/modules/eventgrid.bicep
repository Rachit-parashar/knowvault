param baseName string
param location string
param tags object
param storageAccountId string
param documentChangedQueueId string

resource systemTopic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' = {
  name: 'egst-${baseName}-uploads'
  location: location
  tags: tags
  properties: {
    source: storageAccountId
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

// BlobCreated in the uploads container → document-changed queue. Note the
// payload is Event Grid schema, not the DocumentChanged contract — Ingestion
// maps it (the direct-upload completion API is the local-dev equivalent).
resource blobCreated 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2024-06-01-preview' = {
  parent: systemTopic
  name: 'uploads-blob-created'
  properties: {
    destination: {
      endpointType: 'ServiceBusQueue'
      properties: {
        resourceId: documentChangedQueueId
      }
    }
    filter: {
      includedEventTypes: ['Microsoft.Storage.BlobCreated']
      subjectBeginsWith: '/blobServices/default/containers/uploads/'
    }
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
  }
}

output systemTopicName string = systemTopic.name
