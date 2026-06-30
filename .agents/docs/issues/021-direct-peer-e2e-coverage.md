---
issue: 021
date: 2026-06-30
source: e2e/coverage
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
commits: [61af492]
split-status: child
parent-issue: 009
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 021 - Direct Peer E2E Coverage

## Description

Add a Testcontainers scenario that proves two peers on one network exchange payloads in both directions without relay hops.

## Parent Tracking

- Parent: #009
- Extracted scope: direct peer exchange.
- Standalone reason: direct-peer delivery can be implemented and verified independently of bootstrap, multi-hop, restart, and negative scenarios.

## Scope

- In scope: two-node topology, bidirectional payload exchange, direct route assertions, failure logs.
- Out of scope: bootstrap discovery, multi-hop relay, restart recovery, negative-path cases, parent tracking.

## Acceptance Criteria

- Two peers exchange the expected payloads in both directions.
- The scenario fails if a peer never becomes ready or traffic takes a non-direct route.
- Failure output includes the relevant container logs.

## Research

### Current State

Parent research identified direct peer exchange as one independent scenario family within the broader container e2e expansion.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Direct peer exchange is one of the scenario families named in #009. | verified | source | #009 playbook lists direct peer as a separate concern. |
| 2 | F | The shared Testcontainers harness from #007 is the intended implementation surface. | verified | source | #007 scope defines the reusable e2e harness and node topology builder. |

## Completion Report

Completed in `61af492`.

- Added a two-node direct peer Testcontainers topology using only `node00` and `node01` as each other's static peers on the shared test network.
- Added bidirectional assertions for ping and pong logs, including exact `1 pongs` counters on both peers.
- Reused the shared log wait and diagnostic capture path so missing readiness or payload logs fail with relevant container output.
- Verified Release build, unit tests (`28` total), e2e tests (`4` total), scoped whitespace formatting, `git diff --check`, and Docker cleanup; final testing and review passes approved the change.

## Resolving Commits

- `61af4927ae94d686a2e1597bb0b6df17be8ad0d5` - add direct peer Testcontainers coverage
