---
issue: 050
date: 2026-07-01
source: wireguard/relay
priority: medium
status: gated
research-status: complete
research-date: 2026-07-01
terminal-state: gated
gate: "Wait for the relay lease, demux, and promotion runtime surfaces."
gate-depends:
  - 045
  - 046
  - 047
gate-reason: "Concrete diagnostic field names depend on the relay runtime surfaces created by the relay issues."
gate-last-checked: 2026-07-01
gate-status: blocked
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 050 - Relay Audit And Diagnostics Hardening

## Description
Add relay diagnostics and audit events for WireGuard relay leases, demux decisions, receiver-index fast-path routing, endpoint hints, direct-path promotion, fallback, and expiry without logging secrets, keys, decrypted plaintext, or packet contents.

This gives operators enough visibility to debug relay behavior while preserving the opaque relay security boundary.

## Playbook
- `Lease events`: record registration, renewal, release, expiry, and rejection reasons.
- `Demux events`: record MAC1 scan hit/miss/ambiguous results and malformed packet rejection using safe identifiers.
- `Fast path`: record receiver-index mapping creation, hit, miss, expiry, and endpoint-filter rejection.
- `Promotion`: record endpoint hint emission, direct probe start, authenticated promotion, timeout, and relay fallback.
- `Counters`: expose aggregate counters for accepted, forwarded, dropped, expired, and rate-limited packets.
- `Redaction`: never log private keys, shared secrets, decrypted plaintext, protobuf payloads, full packet bytes, or cookie material.

## Research

The relay diagnostics can stay redacted and still be useful: keep only safe lease, demux, fast-path, hint, probe, promotion, fallback, and expiry metadata. The field names should be bound once the relay runtime types from #045-#047 are concrete.

## Related Tracking
- Related: #045 PNet relay to WireGuard peer interop.
- Related: #046 shared-port relay registration and demux.
- Related: #047 relay-assisted WireGuard endpoint discovery.

## Scope
- In scope: structured relay events, safe counters, redaction tests, diagnostic snapshots for active leases and mappings, focused operator-facing docs if needed.
- Out of scope: packet capture, decrypted payload logging, external metrics backend integration, UI dashboards.

## Acceptance Criteria
- Relay lease lifecycle emits structured diagnostics with safe identifiers.
- MAC1 demux and receiver-index fast-path decisions emit safe success/failure counters.
- Endpoint hint, direct promotion, timeout, and fallback decisions are diagnosable.
- Tests assert that diagnostics omit private keys, shared secrets, decrypted plaintext, protobuf payloads, packet bytes, and cookie material.
- Diagnostics can be disabled or reduced without changing relay behavior.

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Current relay issues require some observability but do not explicitly define safe audit diagnostics. | verified | source | #045 mentions focused observability, while #046/#047 focus on relay mechanics and promotion behavior. |
| 2 | F | Relay diagnostics must preserve the opaque relay boundary and avoid secret/plaintext logging. | verified | logical | #045/#046 require node2 not to decrypt or own the session; diagnostics must not expose data that node2 should not inspect. |
| 3 | F | Safe counters and redacted events are sufficient for initial operator diagnostics. | verified | logical | Existing route-path diagnostics and the relay opaque-boundary requirements are enough to support safe redacted events and counters. |
| 4 | F | Relay diagnostics can include redacted lease, demux, and promotion metadata without exposing secrets or payloads. | verified | logical | Derived from the planned relay surfaces and the existing safe diagnostics pattern; field names remain to be bound to implemented types. |
| 5 | F | Relay diagnostics must exclude private keys, shared secrets, cookies, decrypted plaintext, packet bytes, full public keys, protobuf payloads, and raw endpoint lists. | verified | logical | The relay is intentionally opaque, so diagnostics cannot expose the data it must not inspect. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [045, 046, 047]` | source | blocked | #046 is completed, but #045 and #047 remain open, so the issue stays gated. |

## Validation History

- 2026-07-01: dependency gate cleared by #046; remaining dependency gates #045 and #047 keep #050 gated.
