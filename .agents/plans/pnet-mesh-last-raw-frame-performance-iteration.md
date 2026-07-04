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

## Loop

1. Select one likely hotspot from secure-frame crypto, buffer ownership, frame dispatch, first-byte classification, or session handshake/setup evidence.
2. Make one small optimization batch, preferably one to three related changes.
3. Run cheap correctness validation after each micro-change:
   - scoped Release build for touched projects
   - targeted unit test only when correctness-sensitive behavior changed
   - scoped whitespace verification for touched files
4. Run the fast raw delivery smoke after changes touching secure-frame write/read, payload framing, buffers, dispatcher routing, or first-byte classification.
5. Run the fast handshake smoke after changes touching handshake/setup paths.
6. Run the full raw in-memory and handshake checkpoints before keeping a batch, before claiming improvement, and at final closeout.
7. Run the 5-second raw SecureFrame throughput checkpoint last before keeping a batch and at final closeout.
8. Keep a batch only when it improves at least one primary metric without material regression in the others.
9. Promote the latest successful checkpoint as the working baseline and continue until diminishing returns.
10. Replace `Latest Execution` for each run; do not append historical execution sections to this plan.

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

Diminishing returns is the only terminal stop condition.

Stop when all are true:

| Condition | Requirement |
|---|---|
| Checkpoint trend | Multiple comparable checkpoint runs show no material relative gain. |
| Hotspot search | Source inspection finds no remaining low-risk, plausible secure-frame/dispatch/classification or handshake/setup hotspot. |
| Next-step cost | Further improvement would require a larger transport/framing redesign, crypto-library change, workload change, or riskier rewrite. |

## Closeout

When diminishing returns is reached:

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
| Baseline artifacts | `artifacts/benchmarks/raw-frame/20260703T225900Z-baseline/` |
| Final artifacts | `artifacts/benchmarks/raw-frame/20260704T020700Z-final/` |
| Improvement log entry | `.agents/docs/benchmarks/improvement-log.md` row dated `2026-07-04`, scope `raw-frame-boundary`, commit `d038a90` |
| Completion report | `.agents/docs/issues/095-split-session-secure-frame-transport.md#performance-closeout-2026-07-04` |
| Retained source change | `d038a90 Add raw-frame boundary benchmark coverage` |
| Result | Raw-boundary benchmark coverage retained; product optimization candidates rejected. |
| Allocation note | Raw-boundary steady-state remained `0 B`; this does not apply to `FullHandshakeSetup`. |
| Throughput note | Latest completed optimization execution predates `--macro raw-secureframe`; next optimization run must replace this row with the 5-second packets/sec artifact. |
| Diminishing returns | Issue #095 performance closeout records rejected micro-candidates and remaining bottlenecks in transport encryption/decryption and Noise.NET internals. |

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
