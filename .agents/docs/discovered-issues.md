---
brief: "open+outgoing-mrs+incoming-mrs+remote-issues+completed"
last-entry: 2026-06-30
last-opened: 2026-06-30-020
open-count: 16
last-completed: 2026-06-30
---

# Discovered Issues

Issue tracker for PNet.Mesh. Append during work, process via `/team-task fix issues`.

## Open

| # | Date | Source | Summary | Priority | Status | File |
|---|------|--------|---------|----------|--------|------|
| 005 | 2026-06-30 | user/request | Move test projects from `tests/` into the `src/` project layout and update all references. | medium | enrich | [005-tests-projects-under-src](issues/005-tests-projects-under-src.md) |
| 006 | 2026-06-30 | e2e/testcontainers | Track Testcontainers migration and coverage expansion child issues through completion. | high | gated | [006-testcontainers-coverage-tracking](issues/006-testcontainers-coverage-tracking.md) |
| 007 | 2026-06-30 | e2e/testcontainers | Add a Testcontainers-based xUnit e2e harness for PNet.Mesh test nodes. | high | enrich | [007-testcontainers-e2e-harness](issues/007-testcontainers-e2e-harness.md) |
| 008 | 2026-06-30 | e2e/testcontainers | Port the existing six-node compose mesh smoke topology to Testcontainers. | high | enrich | [008-port-compose-topology-to-testcontainers](issues/008-port-compose-topology-to-testcontainers.md) |
| 009 | 2026-06-30 | e2e/coverage | Expand container e2e coverage for direct peers, multi-hop routing, discovery, restarts, and negative paths. | high | enrich | [009-expand-container-e2e-coverage](issues/009-expand-container-e2e-coverage.md) |
| 010 | 2026-06-30 | tests/unit-doubles | Add deterministic unit-test doubles for routing, session, relay, and control-channel behavior. | high | enrich | [010-unit-test-doubles-for-routing](issues/010-unit-test-doubles-for-routing.md) |
| 011 | 2026-06-30 | e2e/cleanup | Remove Docker Compose e2e artifacts after Testcontainers reaches equivalent coverage. | medium | gated | [011-cleanup-compose-after-equivalent-coverage](issues/011-cleanup-compose-after-equivalent-coverage.md) |
| 012 | 2026-06-30 | coverage/readme | Implement and cover direct P2P communication behavior advertised by the README. | high | enrich | [012-readme-p2p-communication-coverage](issues/012-readme-p2p-communication-coverage.md) |
| 013 | 2026-06-30 | coverage/readme | Verify the no-extended-OS-permission feature claim with documented and executable checks. | medium | enrich | [013-no-extended-os-permissions-coverage](issues/013-no-extended-os-permissions-coverage.md) |
| 014 | 2026-06-30 | coverage/readme | Implement or test UDP fragment transport and the 18-byte datagram-overhead claim. | high | enrich | [014-udp-fragments-and-overhead-coverage](issues/014-udp-fragments-and-overhead-coverage.md) |
| 015 | 2026-06-30 | coverage/readme | Expand packet ordering, retransmission, and flow-control implementation coverage. | high | enrich | [015-packet-ordering-flow-control-coverage](issues/015-packet-ordering-flow-control-coverage.md) |
| 016 | 2026-06-30 | coverage/readme | Implement and test WireGuard/Noise security invariants behind the README security claim. | high | enrich | [016-wireguard-noise-security-coverage](issues/016-wireguard-noise-security-coverage.md) |
| 017 | 2026-06-30 | coverage/readme | Implement and test crypto routing and crypto discovery behavior. | high | enrich | [017-crypto-routing-discovery-coverage](issues/017-crypto-routing-discovery-coverage.md) |
| 018 | 2026-06-30 | coverage/readme | Implement and test NAT traversal, neighbor detection, and ICE candidate behavior. | high | enrich | [018-nat-traversal-neighbor-ice-coverage](issues/018-nat-traversal-neighbor-ice-coverage.md) |
| 019 | 2026-06-30 | coverage/readme | Implement and test compression negotiation and compressed payload handling. | medium | enrich | [019-compression-feature-coverage](issues/019-compression-feature-coverage.md) |
| 020 | 2026-06-30 | coverage/readme | Implement and test ECN and LEDBAT probe/telemetry behavior referenced by the README. | medium | enrich | [020-ecn-ledbat-coverage](issues/020-ecn-ledbat-coverage.md) |

## Outgoing MRs

| MR# | Date | Upstream | Summary | Status | Review-Deadline | Issue-Ref |
|-----|------|----------|---------|--------|-----------------|-----------|

## Incoming MRs

| MR# | Date | Source | Summary | Status | Review-Deadline |
|-----|------|--------|---------|--------|-----------------|

## Remote Issues

| # | Date | Project | Summary | Priority | Ref |
|---|------|---------|---------|----------|-----|

## Completed

| # | Date | Completed | Summary | Commits | File |
|---|------|-----------|---------|---------|------|
| 001 | 2026-06-30 | 2026-06-30 | Migrate project, packages, tests, and test-node container from `net5.0` to .NET 10. | 30ea5f8 | [001-dotnet-5-eol-migration.md](issues/001-dotnet-5-eol-migration.md) |
| 002 | 2026-06-30 | 2026-06-30 | Replace unavailable `Noise` package with `Noise.NET` and restore/build/audit successfully. | 30ea5f8 | [002-noise-package-restore-blocker.md](issues/002-noise-package-restore-blocker.md) |
| 003 | 2026-06-30 | 2026-06-30 | Add compose e2e story and runnable mesh topology smoke flow. | 30ea5f8 | [003-mesh-topology-e2e-story.md](issues/003-mesh-topology-e2e-story.md) |
| 004 | 2026-06-30 | 2026-06-30 | Normalize existing C# whitespace and add formatting config. | 7e8340f, 30ea5f8 | [004-dotnet-format-drift.md](issues/004-dotnet-format-drift.md) |
