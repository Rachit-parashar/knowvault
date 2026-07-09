# Azure Container Apps Overview

## What it is

Azure Container Apps is a fully managed serverless container platform. You bring
container images; the platform handles infrastructure, scaling, and networking.
It is built on Kubernetes but hides cluster operations entirely.

## Scaling

Container Apps uses KEDA-based autoscaling. Scale rules can be driven by HTTP
traffic, CPU, memory, or external event sources such as Azure Service Bus queue
length. Applications can scale to zero replicas when there is no load, and no
compute charges accrue while an app is at zero replicas.

### Queue-based scaling

For queue-driven background workers, a KEDA scale rule on Service Bus queue
depth is the canonical pattern: the worker scales out as the queue deepens and
scales back to zero when the queue drains.

## Revisions and traffic splitting

Each deployment creates an immutable revision. Traffic can be split between
revisions by percentage, which enables blue-green deployments: route a small
share of traffic to the new revision, watch its health, then shift to 100%.

## Availability

Container Apps environments can be deployed zone-redundant. With zone
redundancy enabled, replicas are spread across availability zones, so the loss
of a single zone does not take the application down.
