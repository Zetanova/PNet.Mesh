---
issue: 031
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [172e333b80860435c09b265dbdbab7805783d4fe]
split-status: child
parent-issue: 016
terminal-state: completed
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

Completed in `172e333b80860435c09b265dbdbab7805783d4fe`.

- Replaced the broad "same security as wireguard" feature claim with scoped Noise IKpsk2 handshake and encrypted transport coverage language.
- Clarified WireGuard is a protocol reference only and that PNet.Mesh does not claim full WireGuard behavior or equivalence.
- Added a README security coverage section naming the regression tests backing handshake, wrong key/PSK, corrupted MAC, tamper, replay, unknown receiver, cookie MAC, and packet tracker guard claims.
- Verified the README security coverage command passed with `PNetMeshProtocolTest` and `PNetMeshPacketTrackerTest`: total `33`, failed `0`, skipped `0`.

## Resolving Commits

- `172e333b80860435c09b265dbdbab7805783d4fe` - narrow README security claim and add test-backed coverage references
