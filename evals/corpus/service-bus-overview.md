# Azure Service Bus Messaging Overview

## What it is

Azure Service Bus is a fully managed enterprise message broker with queues and
publish-subscribe topics. Producers place messages on a queue; competing
consumers pull them off at their own pace, which decouples the two sides.

## Delivery guarantees

Messages are delivered at-least-once. A consumer locks a message while
processing; if processing fails or the lock expires, the message becomes
available again for redelivery.

## Dead-lettering

Every queue has an associated dead-letter queue (DLQ). When a message exceeds
its maximum delivery count, or expires, it is moved to the dead-letter queue
where it can be inspected and reprocessed separately. This prevents a poison
message from blocking the queue forever.

## Tiers

| Tier | Highlights |
|------|-----------|
| Basic | Queues only, pay per operation |
| Standard | Adds topics/subscriptions, sessions, duplicate detection |
| Premium | Dedicated capacity, predictable latency, larger messages |

## When to use it

Service Bus fits classic competing-consumers work distribution: order
processing, document pipelines, and any workload needing retries, dead-lettering,
and per-message time-to-live.
