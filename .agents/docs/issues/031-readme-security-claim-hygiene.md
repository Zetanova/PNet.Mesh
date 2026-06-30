---
issue: 031
date: 2026-06-30
source: coverage/readme
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
split-status: child
parent-issue: 016
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 031 - README Security Claim Hygiene

## Description

Update the README so the security wording matches the guarantees the implementation and tests actually provide.

## Parent Tracking

- Parent: #016
- Extracted scope: README security claim hygiene.
- Standalone reason: claim wording can be narrowed independently of the protocol and integration regression tests.

## Scope

- In scope: README wording, claim narrowing, support-level language, links to the relevant regression tests.
- Out of scope: handshake, tamper rejection, replay/cookie coverage, parent tracking.

## Acceptance Criteria

- README security wording matches the implemented guarantees.
- Any WireGuard-equivalence phrasing is narrowed if the evidence does not justify it.
- The README points to the named tests that back the claim.

## Research

### Current State

Parent research already flags that the current coverage proves happy-path handshakes and replay checks, but not the broader negative matrix implied by the README wording.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README security wording is part of #016 acceptance criteria. | verified | source | #016 acceptance criteria explicitly require accurate security wording. |
| 2 | F | Claim wording can be updated without waiting for unrelated scenario families. | verified | source | #016 makes claim hygiene a separate playbook bullet. |

## Completion Report

Pending.
