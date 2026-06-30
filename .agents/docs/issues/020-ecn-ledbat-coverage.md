---
issue: 020
date: 2026-06-30
source: coverage/readme
priority: medium
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

The proto defines probe, probe reply, ECN enum, reports, ack delay, and timestamps. `PNetMeshSession` currently has placeholder branches for probe/probe reply and no visible completed ECN/LEDBAT behavior in the inspected path.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README references ECN and LEDBAT. | verified | source | README Used Protocols includes both protocol references. |
| 2 | F | Proto fields exist for probe, ECN, reports, timestamps, and ack delay. | verified | source | `MeshProtocol.proto` defines those fields. |
| 3 | F | Runtime ECN/LEDBAT behavior needs source verification before implementation. | unverified | source | Current issue filing only verified placeholders in inspected session code. |

## Completion Report

Pending.
