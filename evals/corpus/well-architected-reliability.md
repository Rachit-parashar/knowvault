# Azure Well-Architected Framework — Reliability Pillar

## Principles

Reliability means a workload keeps meeting its commitments when things fail.
Design for failure: assume components will fault, and make faults survivable
rather than trying to prevent them entirely.

## Redundancy

Deploy redundant instances across fault domains so no single failure takes the
workload down. Within a region, availability zones provide physically separate
data centers with independent power, cooling, and networking; a zone-redundant
deployment keeps a workload available when a single zone fails.

## Failure mode analysis

Enumerate the ways each component can fail, the blast radius of each failure,
and the mitigation. Prefer graceful degradation — reduced functionality — over
full outage.

## Recovery targets

Define a Recovery Time Objective (RTO, how long recovery may take) and a
Recovery Point Objective (RPO, how much data loss is tolerable), and test that
backups and failover actually meet them.

## Resilience patterns

Retry with exponential backoff for transient faults, circuit breakers to stop
hammering a failing dependency, and health probes so orchestrators can replace
unhealthy instances automatically.
