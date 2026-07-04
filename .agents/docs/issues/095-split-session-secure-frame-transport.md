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
  - d038a90d43d59ff6f8b619bc89ff131aa79d0ea4
brief: "description+playbook+proposed-split+scope+benchmark-plan+acceptance-criteria+assumptions"
views:
  enrich: "description+related-issues+playbook+proposed-split+scope+out-of-scope+benchmark-plan+acceptance-criteria+assumptions"
  fix: "description+related-issues+playbook+proposed-split+scope+out-of-scope+benchmark-plan+acceptance-criteria+assumptions"
  complete: "description+completion-report+performance-closeout-2026-07-04+performance-iteration-2026-07-04-reflection-cache+performance-iteration-2026-07-04-lazy-keypair-tracker+performance-iteration-2026-07-04-lazy-transport-tracker-rejection+performance-iteration-2026-07-04-response-key-copy-rejection+performance-diminishing-returns-closeout-2026-07-04+resolving-commits"
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

Final run allocations, Gen1, and Gen2 remained zero across the raw-boundary matrix. No product optimization was retained: the final source diff is benchmark-only, and final-vs-baseline timing moved both directions under short-run noise (`all avg +1.7%`; PNet -1.3%, IPv4 +5.4%, IPv6 +1.1%, with no corresponding product code change).

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

## Performance Iteration 2026-07-04 Reflection Cache

Retained change: `PNetMeshTransport2` now caches Noise transport private field metadata and `SetNonce(ulong)` method metadata used while creating nonce setter delegates. This targets session handshake/setup, where each established transport pair creates two `PNetMeshTransport2` instances.

Artifacts:

| Kind | Artifact |
|---|---|
| Baseline | `artifacts/benchmarks/raw-frame/20260704T035013Z-runbook/` |
| Candidate | `artifacts/benchmarks/raw-frame/20260704T051607Z-reflection-cache/` |

Result: `FullHandshakeSetup` allocation improved from `12.44`-`12.65 KB` to `11.88`-`11.94 KB` per operation. Raw-boundary allocations stayed `0 B`; raw-boundary timing moved both directions and is treated as guardrail noise for this constructor-only change. The raw SecureFrame macro completed with unchanged allocation/GC shape.

Correctness and format evidence:

| Command | Result |
|---|---|
| `timeout 300s dotnet build PNet.Mesh.sln -c Release --no-restore` | Passed, `0` warnings, `0` errors. |
| `timeout 300s dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | Passed `205` tests. |
| `timeout 180s dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshProtocol.cs --no-restore --verify-no-changes --verbosity minimal` | Passed. |

Attempted alternate: a temporary .NET 10 `UnsafeAccessorTypeAttribute` probe could read Noise `initiator`, but `c1` field access failed with `MissingFieldException` and `SetNonce` failed with `InvalidProgramException`. That path was not retained.

Iteration assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 10 | F | Caching Noise reflection metadata preserves secure-frame and handshake behavior. | verified | test | Build, unit tests, whitespace check, handshake smoke, raw-boundary checkpoint, handshake checkpoint, and raw SecureFrame macro completed after the change. |
| 11 | F | `UnsafeAccessorTypeAttribute` cannot fully replace the Noise private-member reflection path for this transport. | verified | test | Temporary probe read `initiator`, but failed for `c1` and `SetNonce`; the probe was removed. |

## Performance Iteration 2026-07-04 Lazy Keypair Tracker

Retained change: `PNetMeshWireGuardKeypair` now allocates its `PNetMeshPacketTracker` replay window lazily when `TryAddReceivedCounter` is used. The product secure-frame transport already uses its transport-level replay tracker on the normal encrypted packet receive path, so this avoids constructing an unused keypair tracker during handshake keypair setup while preserving the public keypair replay API.

Artifacts:

| Kind | Artifact |
|---|---|
| Baseline | `artifacts/benchmarks/raw-frame/20260704T051607Z-reflection-cache/` |
| Candidate | `artifacts/benchmarks/raw-frame/20260704T060105Z-lazy-keypair-tracker/` |

Result: `FullHandshakeSetup` allocation improved from `11.88`-`11.94 KB` to `10.75`-`10.81 KB` per operation. Raw-boundary allocations stayed `0 B`; raw-boundary timing moved both directions and is treated as guardrail noise for this handshake/setup-only change. The raw SecureFrame macro completed with the same allocation/GC shape.

Correctness and format evidence:

| Command | Result |
|---|---|
| `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` | Passed, `0` warnings, `0` errors. |
| `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | Passed `205` tests. |
| `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshWireGuardPeerState.cs src/PNet.Mesh/PNetMeshProtocol.cs --no-restore --verify-no-changes --verbosity minimal` | Passed. |

Iteration assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 12 | F | Lazy allocation avoids constructing `PNetMeshPacketTracker` during normal handshake keypair setup. | verified | test | `FullHandshakeSetup` allocation moved from `11.88`-`11.94 KB` to `10.75`-`10.81 KB`; a temporary allocation probe showed `TryReadResponseMessage` fell from `2528.0 B/op` to `1416.0 B/op`. |
| 13 | F | Lazy allocation preserves keypair replay-window behavior when `TryAddReceivedCounter` is used. | verified | test | `keypair_tracks_timers_counters_and_replay_window` asserts first counter acceptance, duplicate rejection, and `LastReceivedCounter`; the unit suite passed after the change. |

## Performance Iteration 2026-07-04 Lazy Transport Tracker Rejection

Rejected change: `PNetMeshTransport2.Tracker` was temporarily changed from an eager constructor allocation to a lazy `PNetMeshPacketTracker` property. The property surface and receive path remained behaviorally valid after using an explicit `LazyInitializer` factory, but the measured allocation delta was too small for a hotter receive/ACK path change.

Artifacts:

| Kind | Artifact |
|---|---|
| Baseline | `artifacts/benchmarks/raw-frame/20260704T060105Z-lazy-keypair-tracker/` |
| Rejected candidate | `artifacts/benchmarks/raw-frame/20260704T063738Z-lazy-transport-tracker-rejected/` |

Result: `FullHandshakeSetup` allocation moved from `10.75`-`10.81 KB` to `10.74 KB` per operation, only about `0.01`-`0.07 KB/op`. The same run widened the `1420` payload mean from `719.5 us` to `1,405.0 us`. The batch was reverted before raw-boundary and raw SecureFrame macro checkpoints.

Validation and rejection evidence:

| Command | Result |
|---|---|
| `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` | Passed, `0` warnings, `0` errors after the candidate and after revert. |
| `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | Passed `205` tests after the candidate and after revert. |
| `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.FullHandshakeSetup*'` | Candidate measured `10.74 KB`; no material allocation win. |
| `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshWireGuardPeerState.cs src/PNet.Mesh/PNetMeshProtocol.cs --no-restore --verify-no-changes --verbosity minimal` | Passed after revert. |

Iteration assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 14 | F | Lazy allocation of the transport replay tracker does not produce a material handshake/setup allocation win. | verified | test | The rejected candidate measured `10.74 KB` per `FullHandshakeSetup` operation versus the retained `10.75`-`10.81 KB` baseline. |
| 15 | F | The retained source no longer contains the rejected transport replay tracker lazy-allocation change. | verified | source | `PNetMeshTransport2` again has a readonly `_tracker`, initializes it in the constructor, returns it directly from `Tracker`, and disposes it directly. |

## Performance Iteration 2026-07-04 Response Key Copy Rejection

Rejected change: `PNetMeshHandshake.TryWriteResponseMessage` was temporarily changed to avoid copying `_handshake.RemoteStaticPublicKey` a second time on the responder path. `TryReadInitiationMessage` already populates `RemotePublicKey` before a valid response can be written, but the measured result did not justify retaining the micro-change.

Artifacts:

| Kind | Artifact |
|---|---|
| Baseline | `artifacts/benchmarks/raw-frame/20260704T060105Z-lazy-keypair-tracker/` |
| Rejected candidate | `artifacts/benchmarks/raw-frame/20260704T065200Z-response-remote-key-copy-rejected/` |

Result: `FullHandshakeSetup` allocation stayed inside the retained baseline band: candidate `10.76 KB` versus baseline `10.75`-`10.81 KB` per operation. Candidate timing measured `642.4`-`686.6 us`, but the error bars were wide enough that it was not a defensible timing win. The batch was reverted before raw-boundary and raw SecureFrame macro checkpoints.

Validation and rejection evidence:

| Command | Result |
|---|---|
| `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` | Passed, `0` warnings, `0` errors after the candidate and after revert. |
| `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | Passed `205` tests after the candidate and after revert. |
| `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.FullHandshakeSetup*'` | Candidate measured `10.76 KB`; no material allocation win. |
| `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshWireGuardPeerState.cs src/PNet.Mesh/PNetMeshProtocol.cs --no-restore --verify-no-changes --verbosity minimal` | Passed after revert. |

Iteration assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 16 | F | Removing the duplicate responder remote-public-key copy does not produce a material handshake/setup allocation win. | verified | test | The rejected candidate measured `10.76 KB` per `FullHandshakeSetup` operation versus the retained `10.75`-`10.81 KB` baseline. |
| 17 | F | The retained source no longer contains the rejected response remote-public-key copy removal. | verified | source | `TryWriteResponseMessage` again assigns `RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray()` before responder keypair registration. |

## Performance Diminishing Returns Closeout 2026-07-04

Retained changes:

| Area | Source | Result |
|---|---|---|
| Noise transport setup | `src/PNet.Mesh/PNetMeshProtocol.cs` | Caches Noise private-field metadata and `SetNonce(ulong)` method metadata by runtime type. |
| WireGuard keypair setup | `src/PNet.Mesh/PNetMeshWireGuardPeerState.cs` | Lazily allocates the keypair replay tracker only when receive counters are checked. |

Final retained current-baseline artifacts:

| Run | Artifact | Key result |
|---|---|---|
| 1 | `artifacts/benchmarks/raw-frame/20260704T060105Z-lazy-keypair-tracker/` | Raw-boundary `0 B`; handshake `10.75`-`10.81 KB`; macro `523,640.022` packets/sec. |
| 2 | `artifacts/benchmarks/raw-frame/20260704T070000Z-current-baseline-repeat-2/` | Raw-boundary `0 B`; handshake `10.78`-`10.81 KB`; macro `538,579.071` packets/sec. |
| 3 | `artifacts/benchmarks/raw-frame/20260704T070500Z-current-baseline-repeat-3/` | Raw-boundary `0 B`; handshake `10.81`-`10.82 KB`; macro `541,032.368` packets/sec. |

Stop-gate result:

| Gate | Status | Evidence |
|---|---|---|
| Three comparable current-baseline runs | Satisfied | Each retained run includes raw-boundary CSV/Markdown, `FullHandshakeSetup` CSV/Markdown, and `raw-secureframe-macro.json`. |
| Candidate coverage | Satisfied | Raw-boundary micro-candidates, reflection metadata cache, lazy keypair tracker, lazy transport tracker, response remote-key copy removal, and `UnsafeAccessorTypeAttribute` probe were attempted or source-dispositioned. |
| Recent no-retain batch | Satisfied | Lazy transport tracker and response remote-key copy removal were reverted after no material allocation win. |
| No low-risk hotspot remains | Satisfied | Remaining opportunities cross dependency/private internals, crypto allocation policy, replay tracker/key ownership, or protocol/framing design. |
| Larger-scope follow-up captured | Satisfied | This section preserves the remaining hotspot classes and defers them outside the local micro-optimization runbook. |

Remaining hotspot classes:

| Class | Disposition |
|---|---|
| Noise.NET transport private state and nonce setup | Further improvement needs dependency/source integration or a public API change; the .NET 10 `UnsafeAccessorTypeAttribute` probe did not cover the needed field and method access. |
| BouncyCastle BLAKE2s digest allocation in MAC/hash paths | Further improvement needs pooling, replacement, or a crypto-library change with a wider safety and benchmark pass. |
| Replay tracker and key ownership structures | A keypair-local lazy allocation was retained, but transport tracker laziness was not worth the receive-path complexity. Broader ownership changes should be designed separately. |
| Raw frame/protocol framing design | Raw-boundary steady state is already zero-allocation in the measured path; larger protocol or framing changes need their own issue and benchmark campaign. |

Closeout assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 18 | F | The final retained current-baseline set has three comparable runs with raw-boundary, handshake/setup, and raw SecureFrame macro evidence. | verified | test | Artifacts exist under `20260704T060105Z-lazy-keypair-tracker`, `20260704T070000Z-current-baseline-repeat-2`, and `20260704T070500Z-current-baseline-repeat-3`. |
| 19 | F | The retained current source keeps raw-boundary allocation at `0 B` and handshake/setup allocation at `10.75`-`10.82 KB` across the final baseline repeat set. | verified | test | Read the three `raw-boundary-report.csv` and `handshake-report.csv` files. |
| 20 | F | The last two product candidates did not produce a material retained improvement. | verified | test | Lazy transport tracker and response remote-key copy removal benchmark artifacts are recorded under their rejected artifact directories. |
| 21 | F | Remaining inspected hotspot classes are larger-scope than the local low-risk micro-change gate. | verified | source | The remaining classes require dependency/private internals, crypto-library/pooling, replay tracker ownership, or protocol/framing changes. |

## Resolving Commits

- `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`
- `d038a90d43d59ff6f8b619bc89ff131aa79d0ea4`
