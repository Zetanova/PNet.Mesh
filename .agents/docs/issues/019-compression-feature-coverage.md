---
issue: 019
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

# 019 - Cover Compression Feature

## Description

Implement and test the README compression feature claim, or narrow the README if compression is only a reserved protocol field.

## Playbook

- `Compression negotiation`: peers agree on supported compression type and dictionary metadata when enabled.
- `Compressed payload`: sender compresses payload and receiver restores the original payload.
- `Fallback`: unsupported compression type fails clearly or falls back according to documented policy.

## Scope

- Determine whether compression is intended now or future protocol surface.
- Implement the smallest supported compression path or update README to mark it as planned.
- Add unit tests for proto mapping and integration tests for payload round-trip if implemented.

## Acceptance Criteria

- README compression claim is accurate.
- Implemented compression has positive and negative tests.
- Unsupported algorithms are documented or rejected predictably.

## Research

### Current State

The proto defines compressed payload forms and compression metadata. `PNetMeshSession` iterates `packet.Compression` without implemented behavior.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists compression as a feature. | verified | source | README Features includes compression. |
| 2 | F | Compression protocol fields exist. | verified | source | `MeshProtocol.proto` defines `Payload` compressed forms and `Compression`. |
| 3 | F | Runtime compression behavior needs implementation or README narrowing. | unverified | source | Source review found no completed compression handling during issue filing. |

## Completion Report

Pending.
