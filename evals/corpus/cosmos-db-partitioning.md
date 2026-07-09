# Azure Cosmos DB Partitioning and Horizontal Scaling

## Partition keys

Every container in Azure Cosmos DB has a partition key. The partition key
determines how items are distributed across physical partitions. Items sharing
a partition key value form a logical partition; related data stays co-located,
which makes queries within one logical partition efficient, and transactions
are scoped to a single logical partition.

## Choosing a partition key

A good partition key has high cardinality and spreads both storage and request
volume evenly. A property like tenant id or user id is common. A poor choice
concentrates traffic on a few "hot" partitions.

## Limits

A single item in Azure Cosmos DB can be up to 2 MB in size. A logical
partition can hold up to 20 GB of data. Physical partitions are managed by the
service and split automatically as data grows.

## Throughput models

| Model | Behavior |
|-------|----------|
| Provisioned | Reserve request units per second; predictable, always billed |
| Serverless | Pay per request unit consumed; nothing when idle |
| Autoscale | Provisioned that scales within a range automatically |

Serverless suits spiky or development workloads; provisioned suits steady
production traffic.
