---
issue: 072
date: 2026-07-02
source: performance/analysis
priority: medium
status: completed
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
split-status: parent
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 0433a9e
  - 4619f47
  - 7482232
brief: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+completion-report+assumptions"
views:
  enrich: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+completion-report+assumptions"
  fix: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+completion-report+assumptions"
  complete: "description+completion-report"
---

# 072 - Apply .NET 10 Memory And Toolkit Optimizations

## Description

Apply a follow-up optimization pass after the .NET 10 migration using .NET 10 span, stack allocation, and runtime collection APIs plus `CommunityToolkit.HighPerformance` helpers to reduce heap allocations and avoidable buffer copies in production hot paths.

This is a parent tracking issue. Implement the child issues below, not this parent directly.

This extends the completed measured-hotspot work by covering additional source-identified opportunities that need benchmark proof before implementation.

## Related Issues

- `#058`: post-baseline benchmark optimization backlog.
- `#066`-`#069`: completed measured allocation hotspot children.

## Playbook

- `Evidence first`: benchmark before and after with the existing BenchmarkDotNet, macro, and TUN benchmark harnesses.
- `Ownership first`: preserve buffer ownership and lifetime across channel, retransmit, socket, and protobuf boundaries.
- `Small buffers`: use `stackalloc` and `IPAddress.TryWriteBytes` for fixed temporaries up to IPv6-sized buffers.
- `Synchronous pooled temps`: use `SpanOwner<byte>` for non-escaping synchronous buffers where `Memory<T>` is not needed.
- `Async or queued ownership`: use `MemoryOwner<byte>` or `IMemoryOwner<byte>` for buffers crossing async or channel boundaries.
- `Protobuf safety`: avoid unsafe `ByteString` wrapping unless immutable ownership and lifetime are proven by tests.

## Scope

- Split the optimization work into child issues #074, #075, and #076.
- Keep the byte-key lookup, TUN ownership, and ACK/tracker cleanup slices separate so each can be benchmarked independently.
- Leave packet-tracker sizing verification in child #076 rather than the parent.

## Out Of Scope

- Replacing the protobuf schema or wire format.
- Unsafe protobuf buffer wrapping without owning immutable buffers.
- Broad networking rewrites away from the existing `SocketAsyncEventArgs` loop unless benchmark evidence justifies them.

## Analysis Notes

- `src/PNet.Mesh/PNetMeshHelpers.cs:7`: `PNetMeshByteArrayComparer` uses `StructuralComparisons` and cannot perform span lookups.
- `src/PNet.Mesh/PNetMeshWireGuardPeerState.cs:150`: remote public key lookup currently clones a span with `ToArray()` before dictionary lookup.
- `src/PNet.Mesh.Tun/PNetMeshTunBridge.cs:74` and `src/PNet.Mesh/PNetMeshChannel.cs:376`: the TUN path copies once to `byte[]` and again to pooled channel memory.
- `src/PNet.Mesh/PNetMeshSession.cs:720`: synchronous frame writing rents a `MemoryPool` owner.
- `src/PNet.Mesh/PNetMeshProtocol.cs:852`: padding uses a manual `ArrayPool` or `stackalloc` split that can be standardized.
- `src/PNet.Mesh/PNetMeshSession.cs:1307` and `src/PNet.Mesh/PNetMeshPacketBuffer.cs:154`: ACK bitmap handling copies to array and uses `BitArray` with LINQ.
- `src/PNet.Mesh.Tun/IpPrefix.cs:34`, `src/PNet.Mesh/PNetMeshIpPacket.cs:74`, and `src/PNet.Mesh/PNetMeshUtils.cs:73`: small IP byte arrays are allocated in hot or utility paths.
- `src/PNet.Mesh/PNetMeshPacketTracker.cs:28`: tracker ring sizing needs verification before changing.

## Acceptance Criteria

- Child issues #074, #075, and #076 are completed or explicitly superseded.
- The parent remains gated until all three child issues are complete.
- Residual scope remains `none` after the child issues land.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #074 | Span-based byte-key and IP-byte helper optimizations | completed | Implemented in `0433a9e`; closed by `cdf3ea2`. |
| #075 | TUN packet ownership transfer and no-copy channel enqueue | completed | Implemented in `4619f47`; closed by `aec0bd9`. |
| #076 | ACK bitmap processing and packet tracker cleanup | completed | Implemented in `7482232`; closed by `fa7f111`. |

## Residual Scope
`none`

## Completion Report

Completed by implementing the three fine-grained child issues:

- `#074` added span-based byte-key lookup and small IP-byte helper optimizations in `0433a9e`.
- `#075` added TUN packet owner transfer and no-copy channel enqueue overloads in `4619f47`.
- `#076` added no-copy ACK bitmap processing and packet tracker sizing cleanup in `7482232`.

The parent had no direct source-code patch beyond those child slices. Its gate is satisfied because #074, #075, and #076 are all completed, each with focused tests and benchmark evidence recorded in its issue file.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The project targets `net10.0` and references `CommunityToolkit.HighPerformance` 8.4.2. | verified | test | `dotnet list src/PNet.Mesh/PNet.Mesh.csproj package` reported `CommunityToolkit.HighPerformance` 8.4.2 under `net10.0`; `dotnet --version` reported 10.0.108. |
| 2 | F | Existing completed issues #066-#069 reduced measured allocation hotspots but did not cover the full source-identified .NET 10 and Toolkit optimization map. | verified | source | Completed issue files #066-#069 and #058 scope measured hotspot children, not the broader byte-key lookup, TUN ownership, ACK bitmap, IP-byte, and packet-tracker follow-up. |
| 3 | F | Before child #074, `PNetMeshByteArrayComparer` used `StructuralComparisons` and span lookup callers allocated byte arrays before dictionary lookup. | verified | source | `PNetMeshHelpers.cs` and `PNetMeshWireGuardPeerState.cs` showed `StructuralComparisons` and `remotePublicKey.ToArray()`. |
| 4 | F | Before child #075, the TUN send path copied packets into a new array and later copied into pooled channel memory. | verified | source | `PNetMeshTunBridge.RunTunReaderAsync` called `ToArray()` and `PNetMeshChannel.EnqueueWriteAsync` rented and copied the payload. |
| 5 | F | Before child #076, ACK processing materialized a byte array and packet-buffer methods allocated `BitArray` and LINQ enumerators. | verified | source | `PNetMeshSession.ProcessAck` called `ToByteArray()` and `PNetMeshPacketBuffer` used `BitArray` with LINQ. |
| 6 | R | The completed child issues reduced scoped copies or allocations without changing the protobuf wire format. | verified | source | Child issue completion reports #074, #075, and #076 record the implementation, focused tests, and benchmark evidence. |
| 7 | F | Child #076 reduced packet-tracker storage while preserving tracker semantics. | verified | source | The #076 completion report records the tracker storage shrink and regression tests for window behavior. |
