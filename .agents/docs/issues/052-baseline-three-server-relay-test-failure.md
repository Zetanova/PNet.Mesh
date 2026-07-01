---
issue: 052
date: 2026-07-01
source: testing/unit
priority: medium
status: gated
research-status: complete
research-date: 2026-07-01
terminal-state: gated
gate: "Debug in a dedicated relay-test pass outside the WireGuard packet-framing issue."
gate-reason: "The failure reproduces on a clean worktree before #037, so fixing it would expand the completed packet-framing slice."
assumptions-date: 2026-07-01
brief: "description+evidence+assumptions"
views:
  enrich: "description+evidence+scope+acceptance-criteria+assumptions"
  fix: "description+evidence+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 052 - Baseline Three-Server Relay Test Failure

## Description

Investigate the pre-existing unit-test failure in `PNetMeshServerTests.bind_three_server_to_localhost_and_relay_exchange`.

## Evidence

- Current #037 branch failure: `node3 waiting for node2 relay: timed out waiting for a payload`.
- Clean detached worktree at pre-#037 HEAD `aa03166` reproduced the same failure.
- The direct two-server exchange method passed after #037, so this follow-up is scoped to the relay path.

## Scope

- In scope: relay exchange test reliability, route forwarding, timeout behavior, and deterministic localhost setup.
- Out of scope: WireGuard packet framing, TAI64N replay tracking, and unrelated transport-mode changes.

## Acceptance Criteria

- The relay exchange test either passes reliably or is corrected to assert the intended relay behavior.
- The fix includes a regression test or preserves the corrected test as the regression.
- Full unit verification can run without this baseline relay failure.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | `PNetMeshServerTests.bind_three_server_to_localhost_and_relay_exchange` fails on the #037 branch. | verified | test | Focused unit command failed with `node3 waiting for node2 relay: timed out waiting for a payload`. |
| 2 | F | The same relay test fails on a clean detached worktree at `aa03166`. | verified | test | A `/tmp/pnet-head` worktree built at pre-#037 HEAD reproduced the same timeout. |
| 3 | F | The direct two-server exchange path still passes after #037. | verified | test | Focused `bind_two_server_to_localhost_and_exchange_payload` command passed after the packet-framing changes. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | dedicated relay-test debug pass | test | blocked | The failure is pre-existing and outside #037, so it is recorded as a follow-up rather than fixed in the packet-framing slice. |
