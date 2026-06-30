---
issue: 008
date: 2026-06-30
source: e2e/testcontainers
priority: high
status: open
research-date: 2026-06-30
research-status: partial
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 008 - Port Compose Topology To Testcontainers

## Description

Recreate the current six-node compose e2e smoke topology as a Testcontainers scenario, preserving the same intended peer layout and route assertions while replacing shell log scraping with xUnit assertions.

## Playbook

- `Six-node topology`: test starts `node00`, `node01`, `node10`, `node11`, `node20`, and `node21`; expected result is the same logical mesh as the compose smoke.
- `Route parity`: test asserts the existing node21-to-node20 ping/pong route and records final pong counts per node.
- `Network parity`: test models the current network partitions and host-bridge path or replaces them with a clearer equivalent topology.

## Scope

- Translate compose service environment into code data structures.
- Preserve fixed keys/PSK until a generated-key fixture exists.
- Replace log grep success criteria with explicit per-node expected event assertions.
- Verify UDP port exposure and host/network alias behavior.

## Acceptance Criteria

- Testcontainers scenario covers the current compose smoke route.
- The new test fails if a node never starts, exits early, or misses the expected route.
- Logs are captured on failure.
- README e2e guidance includes the new command.

## Research

### Current State

`docker-compose.yml` defines the six test nodes and three Docker networks. `docker-compose.e2e.yml` defines the staged connect/ping behavior and runtime duration.

### Risk

The compose file publishes `12443:12401` without an explicit UDP protocol. Verify whether the current compose flow relies on TCP-default publishing, host-gateway behavior, Docker DNS aliases, or direct container networking before translating it.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The compose topology defines six named node services. | verified | source | `docker-compose.yml` contains node00, node01, node10, node11, node20, and node21. |
| 2 | F | The e2e overlay configures staged `ConnectNodes` and `PingNodes`. | verified | source | `docker-compose.e2e.yml` sets connect delays, ping targets, and run duration. |
| 3 | F | UDP port parity requires verification before direct migration. | unverified | source | The current compose publish syntax does not visibly include `/udp`. |

## Completion Report

Pending.
