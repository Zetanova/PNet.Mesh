---
brief: "open+outgoing-mrs+incoming-mrs+remote-issues+completed"
last-entry: 2026-06-30
last-opened: 2026-06-30-033
open-count: 29
last-completed: 2026-06-30
---

# Discovered Issues

Issue tracker for PNet.Mesh. Append during work, process via `/team-task fix issues`.

## Open

| # | Date | Source | Summary | Priority | Status | File |
|---|------|--------|---------|----------|--------|------|
| 005 | 2026-06-30 | user/request | Move test projects from `tests/` into the `src/` project layout and update all references. | medium | ready | [005-tests-projects-under-src](issues/005-tests-projects-under-src.md) |
| 006 | 2026-06-30 | e2e/testcontainers | Track Testcontainers migration and coverage expansion child issues through completion. | high | gated | [006-testcontainers-coverage-tracking](issues/006-testcontainers-coverage-tracking.md) |
| 007 | 2026-06-30 | e2e/testcontainers | Add a Testcontainers-based xUnit e2e harness for PNet.Mesh test nodes. | high | ready | [007-testcontainers-e2e-harness](issues/007-testcontainers-e2e-harness.md) |
| 008 | 2026-06-30 | e2e/testcontainers | Port the existing six-node compose mesh smoke topology to Testcontainers. | high | ready | [008-port-compose-topology-to-testcontainers](issues/008-port-compose-topology-to-testcontainers.md) |
| 009 | 2026-06-30 | e2e/coverage | Expand container e2e coverage for direct peers, multi-hop routing, discovery, restarts, and negative paths. | high | gated | [009-expand-container-e2e-coverage](issues/009-expand-container-e2e-coverage.md) |
| 010 | 2026-06-30 | tests/unit-doubles | Add deterministic unit-test doubles for routing, session, relay, and control-channel behavior. | high | ready | [010-unit-test-doubles-for-routing](issues/010-unit-test-doubles-for-routing.md) |
| 011 | 2026-06-30 | e2e/cleanup | Remove Docker Compose e2e artifacts after Testcontainers reaches equivalent coverage. | medium | gated | [011-cleanup-compose-after-equivalent-coverage](issues/011-cleanup-compose-after-equivalent-coverage.md) |
| 012 | 2026-06-30 | coverage/readme | Implement and cover direct P2P communication behavior advertised by the README. | high | ready | [012-readme-p2p-communication-coverage](issues/012-readme-p2p-communication-coverage.md) |
| 013 | 2026-06-30 | coverage/readme | Verify the no-extended-OS-permission feature claim with documented and executable checks. | medium | ready | [013-no-extended-os-permissions-coverage](issues/013-no-extended-os-permissions-coverage.md) |
| 014 | 2026-06-30 | coverage/readme | Implement or test UDP fragment transport and the 18-byte datagram-overhead claim. | high | clarify | [014-udp-fragments-and-overhead-coverage](issues/014-udp-fragments-and-overhead-coverage.md) |
| 015 | 2026-06-30 | coverage/readme | Expand packet ordering, retransmission, and flow-control implementation coverage. | high | gated | [015-packet-ordering-flow-control-coverage](issues/015-packet-ordering-flow-control-coverage.md) |
| 016 | 2026-06-30 | coverage/readme | Implement and test WireGuard/Noise security invariants behind the README security claim. | high | gated | [016-wireguard-noise-security-coverage](issues/016-wireguard-noise-security-coverage.md) |
| 017 | 2026-06-30 | coverage/readme | Implement and test crypto routing and crypto discovery behavior. | high | gated | [017-crypto-routing-discovery-coverage](issues/017-crypto-routing-discovery-coverage.md) |
| 018 | 2026-06-30 | coverage/readme | Implement and test NAT traversal, neighbor detection, and ICE candidate behavior. | high | ready | [018-nat-traversal-neighbor-ice-coverage](issues/018-nat-traversal-neighbor-ice-coverage.md) |
| 019 | 2026-06-30 | coverage/readme | Implement and test compression negotiation and compressed payload handling. | medium | ready | [019-compression-feature-coverage](issues/019-compression-feature-coverage.md) |
| 020 | 2026-06-30 | coverage/readme | Implement and test ECN and LEDBAT probe/telemetry behavior referenced by the README. | medium | ready | [020-ecn-ledbat-coverage](issues/020-ecn-ledbat-coverage.md) |
| 021 | 2026-06-30 | e2e/coverage | Direct peer Testcontainers scenario with bidirectional payload exchange. | high | ready | [021-direct-peer-e2e-coverage](issues/021-direct-peer-e2e-coverage.md) |
| 022 | 2026-06-30 | e2e/coverage | Bootstrap discovery Testcontainers scenario through a connected peer. | high | ready | [022-bootstrap-discovery-e2e-coverage](issues/022-bootstrap-discovery-e2e-coverage.md) |
| 023 | 2026-06-30 | e2e/coverage | Multi-hop relay Testcontainers scenario across separated segments. | high | ready | [023-multi-hop-route-e2e-coverage](issues/023-multi-hop-route-e2e-coverage.md) |
| 024 | 2026-06-30 | e2e/coverage | Restart recovery Testcontainers scenario for a rejoining node. | high | ready | [024-restart-recovery-e2e-coverage](issues/024-restart-recovery-e2e-coverage.md) |
| 025 | 2026-06-30 | e2e/coverage | Negative-path Testcontainers scenario for invalid keys or routes. | high | ready | [025-negative-path-e2e-coverage](issues/025-negative-path-e2e-coverage.md) |
| 026 | 2026-06-30 | coverage/readme | Deterministic packet ordering and retransmission regression tests. | high | ready | [026-packet-ordering-retransmission-coverage](issues/026-packet-ordering-retransmission-coverage.md) |
| 027 | 2026-06-30 | coverage/readme | Flow-control limit and negotiated SYN assertion coverage. | high | ready | [027-flow-control-negotiation-coverage](issues/027-flow-control-negotiation-coverage.md) |
| 028 | 2026-06-30 | coverage/readme | Noise handshake and authentication happy-path coverage. | high | ready | [028-handshake-authentication-coverage](issues/028-handshake-authentication-coverage.md) |
| 029 | 2026-06-30 | coverage/readme | Tamper rejection coverage for wrong keys, PSKs, and corrupted packets. | high | ready | [029-tamper-rejection-coverage](issues/029-tamper-rejection-coverage.md) |
| 030 | 2026-06-30 | coverage/readme | Replay and cookie-guard regression coverage. | high | ready | [030-replay-cookie-guard-coverage](issues/030-replay-cookie-guard-coverage.md) |
| 031 | 2026-06-30 | coverage/readme | README security-claim wording cleanup and narrowing. | high | ready | [031-readme-security-claim-hygiene](issues/031-readme-security-claim-hygiene.md) |
| 032 | 2026-06-30 | coverage/readme | Crypto routing and discovery behavior regression coverage. | high | ready | [032-crypto-routing-discovery-behavior-coverage](issues/032-crypto-routing-discovery-behavior-coverage.md) |
| 033 | 2026-06-30 | coverage/readme | Route-path observability and diagnostics coverage. | high | ready | [033-route-path-observability-diagnostics](issues/033-route-path-observability-diagnostics.md) |

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
