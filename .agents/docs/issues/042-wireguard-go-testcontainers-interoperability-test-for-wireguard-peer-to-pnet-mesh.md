---
issue: 042
date: 2026-07-01
source: wireguard/tests
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
completion-date: 2026-07-01
commits:
  - e0e9b62d08c951dc63176fb3450aeb32c0f3dfe7
split-status: child
parent-issue: 035
gate: "Wait for the core WireGuard-compatible transport and raw plaintext helpers."
gate-depends:
  - 036
  - 037
  - 038
  - 040
  - 041
gate-reason: "The interop test becomes implementation-ready once the core transport and plaintext helper slices are ready; #039 is recommended before any load/cookie scenario."
gate-last-checked: 2026-07-01
gate-status: cleared
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 042 - Add WireGuard-Go/Testcontainers Interoperability Test For WireGuard Peer To PNet.Mesh

## Description

Add a `wireguard-go` or equivalent Testcontainers interoperability test that exercises WireGuard peer communication with PNet.Mesh.

## Playbook

- `External peer`: run a WireGuard peer in `wireguard-go` or an equivalent container harness.
- `Handshake`: verify the peer and PNet.Mesh complete a working handshake.
- `Exchange`: send encrypted packets in both directions and confirm plaintext decrypt and outbound packet creation.
- `Cleanup`: keep the harness deterministic and isolate logs and teardown.

## Research

The existing Testcontainers harness is the right e2e home for native PNet transport, but this issue still depends on the WireGuard-compatible crypto, framing, raw plaintext, and PNet helper slices. Once those land, the test can prove both-direction `X000PPPP` protobuf frame exchange without involving stock `wireguard-go` packet plaintext.

## Scope

- In scope: Testcontainers or equivalent harness, `wireguard-go` peer setup, handshake verification, encrypted packet exchange, plaintext assertions, outbound packet creation checks.
- Out of scope: core transport implementation details except what the test must drive, raw decrypt boundary refactors, IP helper logic, cookie gate internals.

## Acceptance Criteria

- A real WireGuard peer can handshake with PNet.Mesh.
- Encrypted packets can move between the peer and PNet.Mesh and decrypt to the expected plaintext.
- PNet.Mesh can create outbound packets toward the peer through the same harness.

## Parent Tracking

- Parent: #035
- Extracted scope: external WireGuard peer interoperability and packet exchange verification.
- Standalone reason: this slice proves compatibility without expanding the transport internals beyond the test harness.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The interoperability harness should prove handshake and encrypted packet exchange. | verified | source | The user asked for peer-to-peer communication verification. |
| 2 | F | `wireguard-go` or an equivalent Testcontainers setup is the intended external peer harness. | verified | source | The user explicitly named `wireguard-go` or equivalent Testcontainers setup. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [036, 037, 038, 040, 041]` | source | ready | #036, #037, #038, #040, and #041 are completed, so #042 is implementation-ready. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037, #038, #040, and #041 keep #042 gated.
- 2026-07-01: dependency gate cleared by #037; remaining dependency gates #038, #040, and #041 keep #042 gated.
- 2026-07-01: dependency gate cleared by #038; remaining dependency gates #040 and #041 keep #042 gated.
- 2026-07-01: dependency gate cleared by #040; remaining dependency gate #041 keeps #042 gated.
- 2026-07-01: dependency gate cleared by #041; #042 is now ready.

## Completion Report

Implemented an unprivileged Testcontainers equivalent WireGuard peer harness.

Changes:
- Added `WireGuardPeer` mode to `PNet.Mesh.TestNode`, backed by raw UDP and `PNetMeshProtocol` in WireGuard transport mode.
- Extended `PNetMeshTestNodeSpec` and `PNetMeshTestNodeHarness` to publish a UDP port and wait for mode-specific readiness logs.
- Added `wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol`, which starts the peer container, completes a WireGuard-mode handshake from the host-side PNet.Mesh protocol, sends an encrypted request, decrypts the encrypted response, and asserts peer logs.

Verification:
- `timeout 120s dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `timeout 120s dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarness.cs src/PNet.Mesh.E2ETests/PNetMeshTestNodeSpec.cs src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarnessTests.cs src/PNet.Mesh.TestNode/Program.cs src/PNet.Mesh.TestNode/WireGuardPeerService.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 180s dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed, 138/138.
- `timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol -parallel none` passed, 1/1.
- Full Testcontainers e2e command timed out at 420s after multiple topology groups completed; tracked separately as #053.
- Review gate approved the staged implementation.

## Resolving Commits

- `e0e9b62d08c951dc63176fb3450aeb32c0f3dfe7` test: add WireGuard peer container interop
