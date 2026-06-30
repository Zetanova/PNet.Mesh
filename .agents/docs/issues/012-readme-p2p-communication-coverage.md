---
issue: 012
date: 2026-06-30
source: coverage/readme
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 012 - Cover README P2P Communication

## Description

Implement and cover the README description that PNet.Mesh is a P2P protocol for managed .NET applications. The coverage should prove direct peer communication at both component and e2e levels.

## Playbook

- `Direct two-peer flow`: two peers open channels and exchange payloads in both directions.
- `Managed .NET flow`: scenario runs through the public .NET API rather than only protocol internals.
- `Container smoke`: equivalent direct peer exchange runs in container e2e once Testcontainers exists.

## Scope

- Keep existing localhost integration tests but make their coverage explicit and reliable.
- Add or link a Testcontainers direct-peer scenario.
- Document the command that verifies the feature.

## Acceptance Criteria

- Direct two-peer payload exchange has a named regression test.
- The test asserts both peers receive the expected payloads.
- README or project testing docs identify the feature coverage command.

## Research

### Current State

`PNetMeshServerTests` already has two-server localhost exchange coverage, but it lives inside the unit test project and is not mapped as README feature coverage.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README describes PNet.Mesh as a P2P protocol. | verified | source | README Description says "P2P protocol to use inside managed dotnet application". |
| 2 | F | Existing tests include a two-server localhost exchange. | verified | source | `PNetMeshServerTests` contains `bind_two_server_to_localhost_and_exchange`. |

## Enrichment History

- 2026-06-30: Marked ready after confirming direct two-peer localhost exchange already exists and can anchor the feature coverage command. Evidence: `PNetMeshServerTests.cs` and `README.md`.

## Completion Report

Pending.
