---
issue: 095
date: 2026-07-03
source: architecture/performance
priority: high
status: completed
split-status: parent
terminal-state: completed
gate-depends: [096, 097, 098]
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-04
completed-date: 2026-07-03
completed: 2026-07-03
completed-commits:
  - a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1
brief: "description+playbook+proposed-split+scope+benchmark-plan+acceptance-criteria+assumptions"
views:
  enrich: "description+related-issues+playbook+proposed-split+scope+out-of-scope+benchmark-plan+acceptance-criteria+assumptions"
  fix: "description+related-issues+playbook+proposed-split+scope+out-of-scope+benchmark-plan+acceptance-criteria+assumptions"
  complete: "description+completion-report+performance-closeout-2026-07-04+resolving-commits"
---

# 095 - Split Session Secure Frame Transport

## Description

`PNetMeshSession` currently mixes raw encrypted frame transport with the PNet reliable/control protocol. The raw fast path should operate on plaintext frames directly: plaintext frame bytes are encrypted for outbound send, encrypted packets are decrypted to plaintext frames on receive, and only the first plaintext byte is classified as `IPv4`, `IPv6`, or `PNet`.

Refactor the design so protobuf serialization is not required for raw frame delivery. PNet protobuf/control behavior should move behind a later handler that only processes frames classified as `PNet`, while IPv4/IPv6 frames can be handled by allowlist, TUN, router, or policy components without entering protobuf session parsing.

This is a parent architecture issue. Enrich and fine-grain it before implementation.

## Related Issues

- `#040`: exposed decrypted WireGuard transport plaintext as raw payload bytes.
- `#044`: defined the PNet frame header/padding format.
- `#067`: reduced session protobuf/frame allocation pressure without replacing the session design.
- `#092`: added steady-state session benchmark coverage.
- `#094`: added decrypted cleartext and first-byte classification benchmarks.

## Playbook

- `Secure frame transport`: extract plaintext-frame encrypt/decrypt from `PNetMeshSession` into a component with no protobuf dependency.
- `Frame dispatcher`: classify decrypted first byte, then route `IPv4`, `IPv6`, and `PNet` to separate handlers.
- `PNet reliable control`: keep ACK, retransmit, SYN/open negotiation, relay, ICE candidate exchange, compression metadata, and protobuf packet parsing in a PNet-only component.
- `Raw frame fast path`: IPv4/IPv6 frames must not be wrapped in `Protos.Packet`.
- `Compatibility`: keep the current session API as an adapter during migration until callers move to raw frame APIs or PNet control APIs.
- `Benchmark gate`: validate raw plaintext -> crypto -> plaintext -> first-byte classification separately from PNet reliable/protobuf behavior.

## Proposed Split

| Component | Owns | Does Not Own |
|---|---|---|
| `PNetMeshSecureFrameSession` | `PNetMeshTransport2`, encrypt/decrypt, outbound encrypted packets, decrypted plaintext frames, buffer ownership, endpoint packet metadata. | Protobuf, ACK, retransmit, relay, IP parsing, routing policy. |
| `PNetMeshFrameDispatcher` | First-byte classification and routing to IPv4, IPv6, or PNet handlers. | Transport crypto, protobuf schema, retransmit windows. |
| `PNetMeshReliableControlSession` | PNet protobuf/control frames, ACK, retransmit, SYN, relay, candidate exchange, compression metadata. | IPv4/IPv6 raw frame handling and TUN/router policy. |
| IPv4/IPv6 handlers | Allowlist, TUN, router, and policy behavior for raw IP frames. | PNet protobuf packet parsing. |

Target outbound flow:

```text
caller plaintext frame
  -> PNetMeshSecureFrameSession encrypts frame
  -> encrypted outbound packet
```

Target inbound flow:

```text
encrypted packet
  -> PNetMeshSecureFrameSession decrypts to plaintext frame
  -> PNetMeshFrameDispatcher classifies first byte
  -> IPv4/IPv6 handler OR PNet reliable/control handler
```

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #096 | Raw secure-frame transport extracted from `PNetMeshSession` | open | Standalone transport boundary |
| #097 | First-byte dispatcher for decrypted frame routing | open | Standalone routing boundary |
| #098 | PNet-only reliable/control session handler | open | Standalone PNet control-plane boundary |

## Residual Scope
none

## Scope

- Introduce a raw secure-frame component and API, for example `TryWriteFrame` and `TryReadFrame`.
- Route decrypted frames through a dispatcher that uses `PNetMeshPayloadFraming.TryClassify` before deeper parsing.
- Move or wrap the current protobuf session behavior into a PNet-only reliable/control component.
- Preserve current ACK/retransmit/relay semantics for PNet control traffic unless a child issue explicitly designs a replacement compact binary control header.
- Add focused tests and benchmarks proving raw frame delivery does not allocate or parse protobuf.
- Keep compatibility adapters until existing callers are migrated.

## Out Of Scope

- Replacing the protobuf schema in the same step as the split.
- Changing WireGuard/Noise cryptographic behavior.
- Parsing IPv4/IPv6 headers in the secure-frame component.
- Rewriting TUN/router allowlist policy before the dispatcher boundary exists.
- Removing reliable delivery semantics for PNet control traffic without a separate design issue.

## Benchmark Plan

Primary raw-frame benchmark:

```bash
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.DecryptThenClassify*Cleartext*'
```

Reference checks:

```bash
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.WriteThenRead*CleartextOnly*'
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.ClassifyAlreadyDecrypted*FirstByte*'
```

After the split introduces a session-level raw-frame API, add a benchmark such as:

```text
SecureFrameSessionWriteReadRawFrameSteadyState
```

The new benchmark should measure plaintext frame -> encrypted packet -> decrypted plaintext frame -> first-byte classification, with no `Protos.Packet` construction or parse.

## Acceptance Criteria

- Raw frame delivery has an API that does not allocate or parse `Protos.Packet`.
- Decrypted plaintext frames are classified before IPv4/IPv6/PNet-specific parsing.
- PNet protobuf parsing is only reached for frames classified as `PNet`.
- Existing PNet reliable/control behavior remains covered by tests or compatibility adapters during migration.
- Benchmarks distinguish raw secure-frame delivery from PNet protobuf reliable/control behavior.
- Completion report records before/after BenchmarkDotNet evidence for raw delivery, crypto-only, classification-only, and any retained PNet control/session benchmark.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | `PNetMeshSession.WritePayload` currently wraps payloads in `Protos.Payload` and `ByteString`. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` creates `new Protos.Payload()` and sets `item.Raw = ByteString.CopyFrom(payload)`. |
| 2 | F | `PNetMeshSession.WritePacket` currently serializes `Protos.Packet` before encryption. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` calls `packet.CalculateSize()` and `packet.WriteTo(...)` before `Transport.WriteMessage(...)`. |
| 3 | F | `PNetMeshSession.TryReadMessage` currently parses decrypted PNet frames as protobuf packets. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` calls `Protos.Packet.Parser.ParseFrom(frame.Payload)`. |
| 4 | F | `PNetMeshPayloadFraming.TryClassify` can classify a plaintext frame by first byte without full frame or protobuf parsing. | verified | source | `src/PNet.Mesh/PNetMeshPayloadFraming.cs` maps the first header byte to `PNet`, `IPv4`, or `IPv6`. |
| 5 | F | Existing benchmarks already isolate decrypted cleartext classification from full protobuf session parsing. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` includes `DecryptThenClassify*Cleartext` and `ClassifyAlreadyDecrypted*FirstByte`. |
| 6 | R | Splitting secure frame transport from PNet reliable/control handling is required before `PNetMeshSession` can participate in no-protobuf raw frame benchmarks. | verified | logical | Assumptions 1-5 show the current session path always enters protobuf, while raw transport/classification benchmarks already prove the lower boundary can exist without it. |

## Completion Report

Resolved by completion of `#096`, `#097`, and `#098` in `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`.

The full session split is now in place: secure-frame transport owns raw plaintext encrypt/decrypt, the dispatcher routes by first byte, and the PNet-only control handler keeps protobuf-backed reliable behavior isolated from raw IP frame handling. Residual scope is none, so the parent gate closes.

## Performance Closeout 2026-07-04

Retained change: `WireGuardTransportBenchmarks` now includes `SecureFrameWriteReadDispatchPNetRawFrame`, `SecureFrameWriteReadDispatchIPv4RawFrame`, and `SecureFrameWriteReadDispatchIPv6RawFrame`. These measure plaintext frame bytes through `PNetMeshSecureFrameSession.TryWriteFrame`, `TryReadFrame`, and `PNetMeshFrameDispatcher.TryDispatch` with no-op handlers, before protobuf or IP handler logic.

Baseline command:

```bash
timeout 900s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.SecureFrameWriteReadDispatch*RawFrame*'
```

Artifacts:

| Kind | Artifact |
|---|---|
| Baseline | `artifacts/benchmarks/raw-frame/20260703T225900Z-baseline/WireGuardTransportBenchmarks-report.csv` |
| Final | `artifacts/benchmarks/raw-frame/20260704T020700Z-final/WireGuardTransportBenchmarks-report.csv` |
| Rejected padding candidate | `artifacts/benchmarks/raw-frame/20260704T020300Z-precomputed-padding/WireGuardTransportBenchmarks-report.csv` |
| Rejected tracker candidate | `artifacts/benchmarks/raw-frame/20260704T015600Z-tracker-integrated/WireGuardTransportBenchmarks-report.csv` |

Final run allocations, Gen1, and Gen2 remained zero across the raw-boundary matrix. No product optimization was retained: the final source diff is benchmark-only, and final-vs-baseline timing moved both directions under short-run noise (`all avg +3.5%`, with no corresponding product code change).

Rejected paths:

| Attempt | Result |
|---|---|
| Padding/ArrayPool threshold | Reverted after raw-boundary and crypto-only regressions. |
| Classifier out-init, marker switch, and version switch variants | Reverted after mixed or broad raw-boundary regressions. |
| Keypair-recording local variable change | Reverted after neutral aggregate and targeted regressions. |
| Direct dispatcher classification/handler selection | Reverted after neutral aggregate and IPv4/IPv6 regressions. |
| Secure-frame out-assignment cleanup | Reverted after broad raw-boundary regressions. |
| Packet-tracker sequential fast paths | Reverted after flat aggregate and material per-family regressions. |
| Precomputed secure-frame padding | Reverted after flat aggregate and IPv6 regressions. |

Diminishing returns: the remaining inspected hotspots are dominated by transport encryption/decryption and Noise.NET internals. Further gains likely require a larger transport/framing redesign, crypto-library change, or a higher-fidelity benchmark campaign with repeated baselines rather than another local micro-change.

Closeout assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 7 | F | The final retained source diff changes only `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs`. | verified | source | `git diff --stat` showed one changed source file after all product candidates were reverted. |
| 8 | F | The final raw-boundary checkpoint completed and artifacts were preserved under `artifacts/benchmarks/raw-frame/20260704T020700Z-final/`. | verified | test | The final `BenchmarkDotNet` command exited 0 and exported CSV/Markdown reports before archival. |
| 9 | R | Final timing deltas are run variance rather than a retained product performance change. | verified | logical | The retained diff is benchmark-only, while comparable short-run checkpoints moved individual cases both faster and slower with no product code difference. |

## Resolving Commits

- `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`
