---
assumptions-date: 2026-07-04
created: 2026-07-03
last-refined: 2026-07-04
status: repeatable
last-execution: 2026-07-04
last-execution-status: completed
title: PNet.Mesh Last Raw-Frame In-Memory Performance Runbook
---

# PNet.Mesh Last Raw-Frame In-Memory Performance Runbook

Repeatable runbook for improving raw PNet.Mesh in-memory frame delivery through the last raw-frame boundary and session handshake/setup. Track only the latest execution in this plan; preserve historical benchmark evidence in `.agents/docs/benchmarks/improvement-log.md` and issue reports.

## Packet Flow

| Step | Operation | Included |
|---|---|---|
| 1 | Start with already-built cleartext frame bytes. First byte identifies `PNet`, `IPv4`, or `IPv6`. | Yes |
| 2 | Encrypt cleartext frame with `PNetMeshSecureFrameSession.TryWriteFrame`. | Yes |
| 3 | Decrypt encrypted packet with `PNetMeshSecureFrameSession.TryReadFrame`. | Yes |
| 4 | Dispatch decrypted plaintext with `PNetMeshFrameDispatcher` using first-byte classification and no-op/raw-boundary handlers. | Yes |
| 5 | Enter `PNetMeshReliableControlSession` and parse `Protos.Packet`. | No |
| 6 | Enter IPv4/IPv6 handler logic, parse IP headers, route, TUN, or policy-handle packets. | No |

## Goal

Improve raw in-memory crypto/frame dispatch performance without changing frame semantics.
Improve session handshake/setup performance without changing handshake semantics.

| Area | Primary Benchmark | Direction |
|---|---|---|
| Last raw-frame boundary | `SecureFrameWriteReadDispatchPNetRawFrame`, `SecureFrameWriteReadDispatchIPv4RawFrame`, `SecureFrameWriteReadDispatchIPv6RawFrame` | Lower time, lower allocations, fewer GC collections before downstream handling. |
| Session handshake/setup | `FullHandshakeSetup` | Lower setup time, allocations, and GC collections. Zero allocations are not required. |
| Raw SecureFrame throughput | `--macro raw-secureframe --duration 00:00:05` | Higher packets/sec and bytes/sec over a real fixed-duration run; lower latency, allocations, and GC collections. |
| Crypto + PNet frame delivery | `DecryptThenClassifyPNetCleartext` | Lower time, lower allocations, fewer GC collections. |
| Crypto + IPv4 frame delivery | `DecryptThenClassifyIPv4Cleartext` | Lower time, lower allocations, fewer GC collections. |
| Crypto + IPv6 frame delivery | `DecryptThenClassifyIPv6Cleartext` | Lower time, lower allocations, fewer GC collections. |
| Crypto-only reference | `WriteThenReadPNetCleartextOnly`, `WriteThenReadIPv4CleartextOnly`, `WriteThenReadIPv6CleartextOnly` | Lower time and allocations. |
| Classification-only reference | `ClassifyAlreadyDecryptedPNetFirstByte`, `ClassifyAlreadyDecryptedIPv4FirstByte`, `ClassifyAlreadyDecryptedIPv6FirstByte` | Lower time and allocations. |

## Scope Boundaries

| Include | Exclude |
|---|---|
| Raw plaintext frame bytes. | `PNetMeshSession.WritePayload` / `TryReadMessage`, because the compatibility path still includes PNet control/protobuf behavior. |
| `PNetMeshSecureFrameSession` write/read in memory. | `--macro in-memory`, because it uses session/protobuf. |
| `--macro raw-secureframe` fixed-duration throughput for raw secure-frame encrypt/decrypt/dispatch. | `--macro all` as final evidence, because it includes session and UDP scenarios outside this raw checkpoint. |
| `PNetMeshFrameDispatcher` first-byte handler selection with no-op/raw-boundary handlers. | `PNetMeshReliableControlSession.TryHandleFrame`, `Protos.Packet`, `packet.WriteTo`, `Parser.ParseFrom`, `ByteString.CopyFrom`. |
| PNet/IPv4/IPv6 frame kind coverage at the dispatch boundary. | `TryReadIpFrames`, `TryReadIPv4Header`, `TryReadIPv6Header`, materialized IP packet reads, routing policy, allowlist, and TUN. |
| Session handshake/setup benchmark evidence. | Claims that handshake setup should allocate `0 B`; optimize relative cost instead. |
| BenchmarkDotNet focused runs. | `--tun-*`, Docker/TUN topology, and `--macro udp-loopback`. |

## Execution Contract

This runbook optimizes implementation, not just benchmark coverage. Benchmark refreshes may update `Latest Execution`, but they do not satisfy the optimization objective.

| Execution type | Requirement |
|---|---|
| Implementation optimization | Select a source hotspot, make a small implementation batch, benchmark before/current, and end in one optimization state. |
| Benchmark refresh | Refresh baseline evidence only; the next implementation run must select a hotspot or prove diminishing returns. |

Implementation optimization runs end in exactly one state:

| State | Meaning |
|---|---|
| `kept-improvement` | A source change improved at least one primary metric without material regression. Promote this run as the new baseline. |
| `rejected-batch` | A tested source change failed the success gate. Revert or discard it, record the rejection, and continue with the next hotspot. |
| `diminishing-returns` | No low-risk implementation hotspot remains after comparable benchmark evidence and candidate attempts. This is the only terminal state. |

## Loop

1. Establish the current working baseline with raw-boundary, handshake/setup, and raw SecureFrame throughput checkpoints.
2. Inspect benchmark evidence and source for one likely implementation hotspot.
3. Make one small implementation batch, preferably one to three related source changes.
4. Run cheap correctness validation after each micro-change:
   - scoped Release build for touched projects
   - targeted unit test only when correctness-sensitive behavior changed
   - scoped whitespace verification for touched files
5. Run the fast raw delivery smoke after changes touching secure-frame write/read, payload framing, buffers, dispatcher routing, or first-byte classification.
6. Run the fast handshake smoke after changes touching handshake/setup paths.
7. Run full comparable raw-boundary, handshake/setup, and raw SecureFrame throughput checkpoints.
8. Keep a batch only when it improves at least one primary metric outside measured variance without material regression in the others.
9. If kept, promote the result as the new working baseline and continue.
10. If rejected, discard the batch, record the rejected hotspot and reason, then select the next hotspot.
11. Stop only when the `diminishing-returns` gate is proven.
12. Replace `Latest Execution` for each run; do not append historical execution sections to this plan.

## Fast Benchmark Smoke

Use this after small changes to confirm the raw crypto + first-byte classification path still runs. Do not use smoke numbers for claims.

```bash
timeout 240s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.DecryptThenClassify*Cleartext*' --runOncePerIteration --iterationCount 1 --warmupCount 0 --invocationCount 1 --unrollFactor 1 --join
```

After adding last-boundary benchmarks, prefer this smoke for batches that touch `PNetMeshSecureFrameSession` or `PNetMeshFrameDispatcher`:

```bash
timeout 240s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.SecureFrameWriteReadDispatch*RawFrame*' --runOncePerIteration --iterationCount 1 --warmupCount 0 --invocationCount 1 --unrollFactor 1 --join
```

Use this after changes touching handshake/setup paths. Do not use smoke numbers for claims.

```bash
timeout 240s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.FullHandshakeSetup*' --runOncePerIteration --iterationCount 1 --warmupCount 0 --invocationCount 1 --unrollFactor 1 --join
```

## Checkpoint Commands

Run from a clean built Release state:

```bash
timeout 300s dotnet build PNet.Mesh.sln -c Release --no-restore
```

Run the primary raw delivery and handshake checkpoints:

```bash
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.SecureFrameWriteReadDispatch*RawFrame*'
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.FullHandshakeSetup*'
```

If the last-boundary benchmarks are not added yet, add them before optimizing the boundary. Use the existing lower-boundary checkpoint only as a temporary reference:

```bash
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.DecryptThenClassify*Cleartext*'
```

Run reference checkpoints when relevant to the attempted optimization:

```bash
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.WriteThenRead*CleartextOnly*'
timeout 600s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.ClassifyAlreadyDecrypted*FirstByte*'
```

Run the raw SecureFrame throughput checkpoint last for optimization/validation. This is a real fixed-duration packets/sec run, not a BenchmarkDotNet single-operation `Op/s` estimate:

```bash
timeout 120s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro raw-secureframe --payload 128 --warmup 00:00:01 --duration 00:00:05
```

For performance claims, run the same checkpoint commands before and after the batch and keep at least three comparable runs per side.

## Success Criteria

A batch succeeds when it improves at least one target metric relative to the current working baseline and does not materially regress the others:

| Metric | Source | Success Interpretation |
|---|---|---|
| Last raw-frame boundary mean time | `SecureFrameWriteReadDispatch*RawFrame*` | Relative decrease before downstream PNet/IP handling. |
| Last raw-frame boundary allocations | `SecureFrameWriteReadDispatch*RawFrame*` | Relative decrease or unchanged at zero. |
| Session handshake setup mean time | `FullHandshakeSetup` | Relative decrease. |
| Session handshake setup allocations | `FullHandshakeSetup` | Relative decrease. Zero allocations are not required. |
| Raw SecureFrame packets/sec | `--macro raw-secureframe --duration 00:00:05` | Relative increase in a fixed-duration run. |
| Raw SecureFrame latency | `--macro raw-secureframe --duration 00:00:05` | Relative decrease in p50/p95/p99 operation latency. |
| Raw SecureFrame allocations/GC | `--macro raw-secureframe --duration 00:00:05` | Relative decrease or no material increase. |
| Raw delivery mean time | `DecryptThenClassify*Cleartext` | Relative decrease. |
| Raw delivery allocations | `DecryptThenClassify*Cleartext` | Relative decrease or unchanged at zero. |
| Gen0/Gen1/Gen2 | `SecureFrameWriteReadDispatch*RawFrame*`, `DecryptThenClassify*Cleartext`, `FullHandshakeSetup` | No material increase. |
| Crypto-only reference | `WriteThenRead*CleartextOnly` | Must not regress when transport code is touched. |
| Classification-only reference | `ClassifyAlreadyDecrypted*FirstByte` | Must not regress when classification/framing code is touched. |

Do not use absolute target values as success or stop criteria.

## Regression Triage

If a checkpoint shows material regression in last-boundary time, last-boundary allocations, raw SecureFrame throughput, raw SecureFrame latency, handshake time, handshake allocations, GC, raw delivery time, crypto-only reference, or classification-only reference:

1. Do not close out the batch.
2. Identify whether the regression is from secure-frame crypto, buffer ownership, frame construction, dispatcher routing, first-byte classification, handshake/setup, or benchmark noise.
3. Fix or improve the current attempt, then re-run the relevant smoke and checkpoint commands.
4. If the attempt cannot be repaired cleanly, roll it back and try a different optimization path.
5. Continue the improvement loop.

Regression triage is not a stop condition.

## Stop Condition

Diminishing returns is the only terminal stop condition for implementation optimization.

Stop when all are true:

| Condition | Requirement |
|---|---|
| Comparable baseline | At least three comparable runs exist for the current baseline, including raw-boundary, handshake/setup, and raw SecureFrame throughput. |
| Candidate coverage | At least three distinct low-risk hotspot classes were attempted or source-dispositioned, such as secure-frame crypto, buffer ownership, dispatch, first-byte classification, and handshake/setup. |
| No retained gain | Recent candidate batches failed to improve a primary metric outside measured variance, or caused material regression elsewhere. |
| No low-risk hotspot remains | Source inspection finds no remaining small implementation change likely to improve the measured path. |
| Remaining work is larger-scope | Further gains require redesign, workload change, crypto-library replacement, protocol/framing change, or riskier rewrite. |
| Follow-up captured | Any larger-scope remaining opportunity is recorded outside this runbook, for example in a discovered issue or benchmark note. |

## Closeout

When `diminishing-returns` is proven:

1. Run final full raw in-memory and `FullHandshakeSetup` checkpoints.
2. Run the 5-second raw SecureFrame throughput checkpoint last.
3. Replace `Latest Execution` in this plan with the current run evidence.
4. Update `.agents/docs/benchmarks/improvement-log.md` with before/current/delta evidence for kept batches or final evidence worth preserving.
5. Update the relevant issue or completion report.
6. Report to the user:
   - starting baseline command set and artifacts
   - final command set and artifacts
   - raw-boundary, raw SecureFrame throughput, and handshake per-metric before/current/delta
   - successful optimization batches
   - attempted but reverted paths, if any
   - remaining bottlenecks
   - why diminishing returns was reached

## Latest Execution

Update this section in place for each run. Do not append historical executions here.

| Field | Value |
|---|---|
| Execution date | `2026-07-04` |
| Status | `completed` |
| Execution type | `implementation optimization closeout` |
| Optimization state | `diminishing-returns` |
| Candidate hotspot | Final current-baseline repeat and remaining-hotspot source disposition. |
| Source commit | `98af0d8+dirty` |
| Source changes | Retained the Noise reflection metadata cache in `src/PNet.Mesh/PNetMeshProtocol.cs` and the lazy keypair replay tracker in `src/PNet.Mesh/PNetMeshWireGuardPeerState.cs`. Rejected candidate source changes were reverted. |
| Baseline artifacts | Initial baseline `artifacts/benchmarks/raw-frame/20260704T035013Z-runbook/`; retained current-baseline set `artifacts/benchmarks/raw-frame/20260704T060105Z-lazy-keypair-tracker/`, `artifacts/benchmarks/raw-frame/20260704T070000Z-current-baseline-repeat-2/`, and `artifacts/benchmarks/raw-frame/20260704T070500Z-current-baseline-repeat-3/`. |
| Candidate artifacts | Rejected candidates: `artifacts/benchmarks/raw-frame/20260704T063738Z-lazy-transport-tracker-rejected/` and `artifacts/benchmarks/raw-frame/20260704T065200Z-response-remote-key-copy-rejected/`. |
| Build evidence | `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with `0 Warning(s)` and `0 Error(s)`. |
| Unit evidence | `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed `205` tests, `0` failed, `0` skipped. |
| Format evidence | `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshWireGuardPeerState.cs src/PNet.Mesh/PNetMeshProtocol.cs --no-restore --verify-no-changes --verbosity minimal` passed. |
| Smoke evidence | Not run separately for closeout; final evidence uses full build, unit, format, raw-boundary, handshake, and raw SecureFrame macro checkpoints. |
| Raw-boundary checkpoint | Three retained current-baseline runs completed with `0 B` allocated. Mean ranges: `1.307`-`6.748 us`, `1.361`-`5.831 us`, and `1.262`-`6.072 us`. |
| Handshake checkpoint | Three retained current-baseline runs completed. `FullHandshakeSetup` allocation range was `10.75`-`10.82 KB`, improved from the initial `12.44`-`12.65 KB` baseline; mean ranges were `664.0`-`856.3 us`, `669.2`-`734.9 us`, and `654.0`-`758.2 us`. |
| Throughput checkpoint | Three raw SecureFrame macro runs completed: `523,640.022`, `538,579.071`, and `541,032.368` packets/sec. p50 was `1.5 us`; p95 was `2.301`-`2.4 us`; p99 was `3.3`-`4.2 us`; allocated bytes were `16,777,696`-`16,783,888`; Gen0/1/2 remained `2/2/2`. |
| Result | Diminishing returns reached under the low-risk/local-micro-change gate. The retained changes reduce handshake/setup allocation, raw-boundary allocation remains `0 B`, and the last two candidate batches failed to improve outside variance or were too risky for the measured gain. |
| Decision | Keep the retained source changes, reject the lazy transport replay tracker and response remote-key copy candidates, and close this runbook. |
| Rejected alternate | A temporary .NET 10 `UnsafeAccessorTypeAttribute` probe could read Noise `initiator`, but `c1` failed with `MissingFieldException` and `SetNonce` failed with `InvalidProgramException`; not retained. The lazy transport replay tracker and response remote-key copy removal were also not retained. |
| Next hotspot | None under the low-risk gate; remaining opportunities require larger Noise.NET/private-state integration, crypto allocation, replay-tracker ownership, or protocol/framing work. |
| Prior closeout | Product and benchmark closeout details are captured in `.agents/docs/issues/095-split-session-secure-frame-transport.md#performance-diminishing-returns-closeout-2026-07-04`. |

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | `DecryptThenClassifyPNetCleartext`, `DecryptThenClassifyIPv4Cleartext`, and `DecryptThenClassifyIPv6Cleartext` exist in `WireGuardTransportBenchmarks`. | verified | source | Read `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs`. |
| 2 | F | `DecryptThenClassify*Cleartext` encrypts cleartext, decrypts it, then classifies the decrypted first byte. | verified | source | `DecryptThenClassifyCleartext` calls `WriteThenReadCleartext` and `PNetMeshPayloadFraming.TryClassify`. |
| 3 | F | `PNetMeshPayloadFraming.TryClassify` reads only the first byte and maps it to `PNet`, `IPv4`, or `IPv6`. | verified | source | Read `src/PNet.Mesh/PNetMeshPayloadFraming.cs`. |
| 4 | F | Session benchmarks and macro `in-memory` include protobuf packet encode/decode. | verified | source | `PNetMeshSession` uses `Protos.Packet.CalculateSize`, `packet.WriteTo`, and `Protos.Packet.Parser.ParseFrom`. |
| 5 | C | The requested plan must exclude protobuf serialization and focus on raw plaintext -> crypto -> plaintext -> first-byte frame-kind classification. | verified | source | User explicitly narrowed the benchmark target in the original request. |
| 6 | F | `PNetMeshSecureFrameSession` and `PNetMeshFrameDispatcher` are the current raw-frame boundary before protobuf or IPv4/IPv6 handling. | verified | source | `PNetMeshSession` wires secure-frame read/write through `PNetMeshSecureFrameSession` and dispatches decrypted frames through `PNetMeshFrameDispatcher`. |
| 7 | F | `PNetMeshReliableControlSession` is where PNet protobuf parsing begins after dispatch. | verified | source | `src/PNet.Mesh/PNetMeshReliableControlSession.cs` calls `Protos.Packet.Parser.ParseFrom` only after `PNet` classification. |
| 8 | C | The updated plan should optimize until the last raw frame before PNet protobuf serialization or IPv4/IPv6 handling. | verified | source | User requested this update on 2026-07-04. |
| 9 | C | This plan should be repeatable and track only the latest execution in the plan file. | verified | source | User requested this update on 2026-07-04. |
| 10 | C | Session handshake/setup should be optimized beside the raw encrypt/decrypt/dispatch flow. | verified | source | User requested this update on 2026-07-04. |
| 11 | F | `FullHandshakeSetup` exists in `WireGuardTransportBenchmarks`. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` includes the benchmark method. |
| 12 | C | Raw SecureFrame throughput should use a real multi-second packets/sec checkpoint with a short 5-second duration as the last benchmark. | verified | source | User requested this update on 2026-07-04. |
| 13 | F | `--macro raw-secureframe` is the fixed-duration raw SecureFrame throughput scenario. | verified | source | `src/PNet.Mesh.Benchmarks/MacroBenchmarkRunner.cs` includes `MacroBenchmarkOptions.RawSecureFrameScenario`. |
| 14 | C | The runbook should drive implementation optimization until the diminishing-returns stop condition is proven. | verified | source | User requested this update on 2026-07-04. |
| 15 | F | Caching Noise transport field metadata and `SetNonce(ulong)` method metadata does not change secure-frame or handshake semantics. | verified | test | Release build, `PNet.Mesh.UnitTests`, whitespace verification, `FullHandshakeSetup` smoke, raw-boundary checkpoint, handshake checkpoint, and raw SecureFrame macro all completed after the source change. |
| 16 | F | .NET 10 `UnsafeAccessorTypeAttribute` cannot fully replace the Noise private-member reflection path for this code. | verified | test | A temporary probe read Noise `initiator`, but `c1` field access failed with `MissingFieldException` and `SetNonce` failed with `InvalidProgramException`; the probe was removed. |
| 17 | F | Lazy allocation avoids constructing `PNetMeshPacketTracker` during normal handshake keypair setup. | verified | test | `FullHandshakeSetup` allocation moved from `11.88`-`11.94 KB` to `10.75`-`10.81 KB`; a temporary allocation probe showed `TryReadResponseMessage` fell from `2528.0 B/op` to `1416.0 B/op`. |
| 18 | F | Lazy allocation preserves the keypair replay-window behavior when `TryAddReceivedCounter` is used. | verified | test | `keypair_tracks_timers_counters_and_replay_window` asserts first counter acceptance, duplicate rejection, and `LastReceivedCounter`; the unit suite passed after the change. |
| 19 | F | Lazy allocation of the transport replay tracker does not produce a material handshake/setup allocation win. | verified | test | The rejected candidate measured `10.74 KB` per `FullHandshakeSetup` operation versus the retained `10.75`-`10.81 KB` baseline, a delta too small to justify the hot-path lazy property. |
| 20 | F | The working tree no longer contains the rejected transport replay tracker lazy-allocation change. | verified | source | `PNetMeshTransport2` again has a readonly `_tracker`, initializes it in the constructor, returns it directly from `Tracker`, and disposes it directly. |
| 21 | F | Removing the duplicate responder remote-public-key copy does not produce a material handshake/setup allocation win. | verified | test | The rejected candidate measured `10.76 KB` per `FullHandshakeSetup` operation versus the retained `10.75`-`10.81 KB` baseline. |
| 22 | F | The working tree no longer contains the rejected response remote-public-key copy removal. | verified | source | `TryWriteResponseMessage` again assigns `RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray()` before responder keypair registration. |
| 23 | F | The retained current-baseline set has three comparable runs that include raw-boundary, handshake/setup, and raw SecureFrame macro evidence. | verified | test | Artifacts exist under `20260704T060105Z-lazy-keypair-tracker`, `20260704T070000Z-current-baseline-repeat-2`, and `20260704T070500Z-current-baseline-repeat-3`. |
| 24 | F | The final retained current-baseline runs keep raw-boundary allocation at `0 B` and handshake/setup allocation at `10.75`-`10.82 KB`. | verified | test | Read `raw-boundary-report.csv` and `handshake-report.csv` from the three retained current-baseline artifact directories. |
| 25 | F | The remaining inspected hotspot classes require larger changes than this low-risk local micro-optimization gate allows. | verified | source | Remaining classes are Noise.NET private-state integration, BouncyCastle digest allocation/pooling, replay tracker/key ownership, and protocol/framing design. |
| 26 | F | The larger-scope remaining work is captured outside the runbook. | verified | source | See `.agents/docs/issues/095-split-session-secure-frame-transport.md#performance-diminishing-returns-closeout-2026-07-04`. |
