---
issue: 033
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [0ae5bc38aaa4eaa99f2169d75967d5692c291734]
split-status: child
parent-issue: 017
terminal-state: completed
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 033 - Route Path Observability And Diagnostics

## Description

Expose enough diagnostics to assert the actual route path, not just the final payload, for crypto routing and discovery scenarios.

## Parent Tracking

- Parent: #017
- Extracted scope: route-path observability and diagnostics.
- Standalone reason: diagnostics surface work can proceed independently of the underlying routing behavior tests.

## Scope

- In scope: route-path diagnostics, observable hop or peer information, failure diagnostics, test assertions on the observed route.
- Out of scope: router logic changes, README wording, parent tracking.

## Acceptance Criteria

- Tests can assert the actual route path, not only a final pong.
- Route-path diagnostics are available for the routing/discovery scenarios.
- Failure output makes the chosen path visible when assertions fail.

## Research

### Current State

Parent research says the current localhost coverage still relies on comments and final message checks rather than a dedicated route-path diagnostics surface.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Route-path observability is the second concern named in #017. | verified | source | #017 split guidance names route-path observability and diagnostics separately. |
| 2 | F | The current assertions are still narrow enough that a diagnostics surface would reduce brittleness. | verified | source | #017 research notes route assertions are still narrow and comments are used for discovery cases. |

## Completion Report

Completed in `0ae5bc38aaa4eaa99f2169d75967d5692c291734`.

- Added `PNetMeshRoutePath` diagnostics with destination address, route addresses, remaining hop count, selected remote endpoint, and a route string form for assertion failure output.
- Added route-path reading on `PNetMeshChannel` using the same wait/try-read pattern as payload reads.
- Wrote diagnostics from the server local relay path after the payload receive command is accepted, so diagnostic failures do not block delivery.
- Added deterministic coverage for diagnostic snapshot copying, channel diagnostic reads, and the actual server `RelayPacket` branch that emits route diagnostics.
- Verification passed scoped whitespace formatting, Release build, targeted diagnostic tests `3/3`, and `PNetMeshRoutingUnitTests` `43/43`.

## Resolving Commits

- `0ae5bc38aaa4eaa99f2169d75967d5692c291734` - route-path diagnostic API and server relay coverage
