---
issue: 032
date: 2026-06-30
source: coverage/readme
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
split-status: child
parent-issue: 017
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 032 - Crypto Routing And Discovery Behavior Coverage

## Description

Add deterministic router and session tests for crypto address derivation, relay routing, endpoint learning, and discovery through already-connected peers.

## Parent Tracking

- Parent: #017
- Extracted scope: routing and discovery behavior.
- Standalone reason: route selection and discovery behavior can be tested separately from route-path diagnostics.

## Scope

- In scope: address derivation from public keys, known/unknown route selection, hop budget behavior, duplicate suppression.
- Out of scope: route-path observability surface, README wording, parent tracking.

## Acceptance Criteria

- Direct and discovered peer routes have passing tests.
- Duplicate relay packet suppression has unit coverage.
- Address derivation and relay routing behavior remain deterministic.

## Research

### Current State

Parent research says the server relay logic already covers local delivery, known-route relay, unknown-route flooding, and hop count.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists crypto routing and crypto discovery as a feature. | verified | source | #017 assumptions and README features include the claim. |
| 2 | F | Server relay logic includes known-route and unknown-route branches. | verified | source | #017 research references `PNetMeshServer.ProcessControl` relay branches. |

## Completion Report

Pending.
