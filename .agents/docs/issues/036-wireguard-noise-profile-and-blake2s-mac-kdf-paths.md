---
issue: 036
date: 2026-07-01
source: wireguard/transport
priority: high
status: ready
research-status: complete
research-date: 2026-07-01
terminal-state: ready
split-status: child
parent-issue: 035
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 036 - Implement WireGuard Noise Profile And BLAKE2s MAC/KDF Paths

## Description

Implement the WireGuard Noise profile and BLAKE2s MAC/KDF paths required for the transport mode.

## Playbook

- `Noise profile`: select or add the WireGuard `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s` path.
- `Prologue`: use the exact WireGuard prologue for handshake hashing.
- `MAC/KDF`: feed mac1, mac2, and cookie inputs through WireGuard BLAKE2s keyed hash and derive paths.

## Research

Primary WireGuard protocol docs define the required `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s` profile, prologue, and BLAKE2s MAC/KDF behavior. Local `PNetMeshProtocol` still uses the BLAKE2b transport path, so this is a real implementation slice rather than a duplicate of existing coverage.

## Scope

- In scope: transport-mode selection, WireGuard prologue constants, BLAKE2s keyed-hash helpers, handshake hash/KDF wiring, tests for expected outputs.
- Out of scope: packet framing, replay tracking, peer lifecycle, cookie reply behavior, raw plaintext boundary changes, IP packet helpers, external interop harness.

## Acceptance Criteria

- The transport mode can select the WireGuard Noise profile without ambiguity.
- mac1/mac2/cookie inputs use the WireGuard BLAKE2s paths.
- Tests cover the expected hash/KDF behavior and reject wrong inputs.

## Parent Tracking

- Parent: #035
- Extracted scope: WireGuard crypto profile, prologue, and MAC/KDF helpers.
- Standalone reason: this slice can be verified independently of packet framing and state lifecycle work.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The transport mode should use `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s`. | verified | source | The user named the exact WireGuard Noise profile. |
| 2 | F | The WireGuard prologue and BLAKE2s keyed hash paths belong in this slice. | verified | source | The user requested the prologue plus mac1/mac2/cookie BLAKE2s paths. |
