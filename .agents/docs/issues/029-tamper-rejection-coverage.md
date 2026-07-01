---
issue: 029
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [7c66b9589b6e3e8c660b2fd55ed063823f3312a0]
split-status: child
parent-issue: 016
terminal-state: completed
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 029 - Tamper Rejection Coverage

## Description

Add negative tests that reject corrupted MACs, wrong PSKs, wrong keys, replayed packets, and unknown-session traffic.

## Parent Tracking

- Parent: #016
- Extracted scope: tamper rejection.
- Standalone reason: negative rejection behavior can be verified without the happy-path handshake or README wording work.

## Scope

- In scope: corrupted MACs, wrong PSKs, wrong keys, unknown session traffic, non-delivery assertions.
- Out of scope: positive handshake path, replay/cookie state bookkeeping, README wording, parent tracking.

## Acceptance Criteria

- Wrong key and wrong PSK peers cannot exchange accepted payloads.
- Tampered handshake or payload packets are rejected.
- Unknown-session traffic does not deliver payloads.

## Research

### Current State

Parent research says the current coverage is happy-path handshake plus replay checks, not the broader negative matrix implied by the README.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The security playbook names tamper rejection as a separate concern. | verified | source | #016 playbook lists corrupted MAC, wrong PSK, wrong key, replayed packet, and unknown session traffic. |
| 2 | F | Tamper rejection can be isolated from replay-cookie bookkeeping. | verified | source | #016 splits replay/cookie guard into its own coverage item. |

## Completion Report

Completed in `7c66b9589b6e3e8c660b2fd55ed063823f3312a0`.

- Added protocol regressions for corrupted handshake initiation/response MACs, wrong responder keys, wrong PSKs, tampered transport payloads, tampered-packet replay-state poisoning, and unknown receiver indexes.
- Converted Noise authentication failures in `TryReadInitiationMessage`, `TryReadResponseMessage`, and `TryReadMessage` into `false` results without hiding non-cryptographic misuse exceptions.
- Moved transport replay-tracker mutation after successful AEAD authentication so tampered packets cannot consume counters; duplicate rejection clears decrypted bytes before returning `false`.
- Verification rerun passed formatting, Release build, targeted regressions `7/7`, `PNetMeshProtocolTest` `11/11`, `PNetMeshServerTests` `2/2`, and the full unit suite `71/71`.

## Resolving Commits

- `7c66b9589b6e3e8c660b2fd55ed063823f3312a0` - add tamper rejection regressions and authentication-failure handling
