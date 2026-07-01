---
issue: 020
date: 2026-06-30
source: coverage/readme
priority: medium
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
terminal-state: completed
commits: [708236e]
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 020 - Cover ECN And LEDBAT Behavior

## Description

Implement and test the ECN and LEDBAT behavior referenced by the README's Used Protocols section, or update the README if these are protocol aspirations rather than supported behavior.

## Playbook

- `Probe telemetry`: probe/probe-reply messages carry timestamps, ECN markers, and optional reports.
- `LEDBAT delay`: ack delay samples or timestamps feed congestion/backoff decisions.
- `Claim hygiene`: README states whether ECN/LEDBAT are implemented, partially modeled, or planned.

## Scope

- Determine the implemented behavior for `Probe`, `ProbeReply`, `Report`, ack delay samples, and packet timestamps.
- Add protocol/component tests for serialization and decision behavior.
- Add integration coverage only after runtime behavior exists.
- Update README if no runtime behavior is implemented.

## Acceptance Criteria

- ECN/probe fields have deterministic tests if supported.
- LEDBAT delay or timestamp behavior has deterministic tests if supported.
- README protocol references accurately describe support level.

## Research

### Current State

The proto defines probe, probe reply, ECN enum, reports, ack delay, and timestamps. `PNetMeshSession` currently has placeholder branches for probe/probe reply and reads packet timestamps without visible completed ECN/LEDBAT behavior in the inspected path. README now narrows ECN and LEDBAT references to field/timestamp models and states runtime marking, reporting, congestion behavior, and delay-based congestion control are not implemented.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README references ECN and LEDBAT. | verified | source | README Used Protocols includes both protocol references. |
| 2 | F | Proto fields exist for probe, ECN, reports, timestamps, and ack delay. | verified | source | `MeshProtocol.proto` defines those fields. |
| 3 | F | Runtime ECN/LEDBAT behavior needs source verification before implementation. | verified | source | Probe/probe-reply branches are empty and timestamp is read then discarded in the session path. |

## Enrichment History

- 2026-06-30: Marked ready after confirming probe, ECN, and LEDBAT schema exists while runtime behavior remains stubbed in the session path. Evidence: `MeshProtocol.proto`, `PNetMeshSession.cs`.

## Completion Report

Completed on 2026-06-30 in `708236e`.

- Narrowed the README ECN reference to an ECN field model and documented that runtime ECN marking, reporting, and congestion behavior are not implemented.
- Narrowed the README LEDBAT reference to a timestamp/delay field model and documented that runtime delay-based congestion control is not implemented.
- Left runtime code unchanged because the session path still has placeholder probe/probe-reply branches and only reads packet timestamps into a local value.

Verification:

- `timeout 10s git diff --check`
- `timeout 10s git diff --cached --check`
- Source evidence: `MeshProtocol.proto` defines probe, ECN/report, ack delay, and timestamp fields; `PNetMeshSession` contains placeholder probe/probe-reply branches and reads timestamp without applying behavior.
