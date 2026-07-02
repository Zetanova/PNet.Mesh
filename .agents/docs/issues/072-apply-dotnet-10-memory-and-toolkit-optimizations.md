---
issue: 072
date: 2026-07-02
source: performance/analysis
priority: medium
status: open
research-status: partial
research-date: 2026-07-02
assumptions-date: 2026-07-02
split-status: parent
terminal-state: gated
gate-depends:
  - 074
  - 075
  - 076
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
brief: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+assumptions"
views:
  enrich: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+assumptions"
  fix: "description+related-issues+playbook+scope+out-of-scope+analysis-notes+acceptance-criteria+tracking+residual-scope+assumptions"
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
| #074 | Span-based byte-key and IP-byte helper optimizations | ready | Lookup helper and IP-byte allocation slice. |
| #075 | TUN packet ownership transfer and no-copy channel enqueue | ready | Ownership handoff and enqueue copy removal slice. |
| #076 | ACK bitmap processing and packet tracker cleanup | ready | Span-based ACK handling and tracker sizing review. |

## Residual Scope
`none`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The project targets `net10.0` and references `CommunityToolkit.HighPerformance` 8.4.2. | verified | test | `dotnet list src/PNet.Mesh/PNet.Mesh.csproj package` reported `CommunityToolkit.HighPerformance` 8.4.2 under `net10.0`; `dotnet --version` reported 10.0.108. |
| 2 | F | Existing completed issues #066-#069 reduced measured allocation hotspots but did not cover the full source-identified .NET 10 and Toolkit optimization map. | verified | source | Completed issue files #066-#069 and #058 scope measured hotspot children, not the broader byte-key lookup, TUN ownership, ACK bitmap, IP-byte, and packet-tracker follow-up. |
| 3 | F | `PNetMeshByteArrayComparer` uses `StructuralComparisons` and current span lookup callers allocate byte arrays before dictionary lookup. | verified | source | `PNetMeshHelpers.cs` and `PNetMeshWireGuardPeerState.cs` show `StructuralComparisons` and `remotePublicKey.ToArray()`. |
| 4 | F | The TUN send path currently copies packets into a new array and later copies into pooled channel memory. | verified | source | `PNetMeshTunBridge.RunTunReaderAsync` calls `ToArray()` and `PNetMeshChannel.EnqueueWriteAsync` rents and copies the payload. |
| 5 | F | ACK processing currently materializes a byte array and packet-buffer methods allocate `BitArray` and LINQ enumerators. | verified | source | `PNetMeshSession.ProcessAck` calls `ToByteArray()` and `PNetMeshPacketBuffer` uses `BitArray` with LINQ. |
| 6 | R | Replacing copies with owner transfer and span-based lookup can reduce allocations without changing wire format. | unverified | logical | Needs implementation plus benchmark and test proof because ownership and protobuf lifetimes are correctness-sensitive. |
| 7 | R | `PNetMeshPacketTracker` can reduce rented memory by revising word sizing. | unverified | source | The constructor has a `todo smaller implementation`; verify current window semantics before changing. |
