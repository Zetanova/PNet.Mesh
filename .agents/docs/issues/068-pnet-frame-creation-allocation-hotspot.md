---
issue: 068
date: 2026-07-02
source: benchmark/hotspot
priority: medium
status: ready
baseline: 057
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
  fix: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
  complete: "description+completion-report"
---

# 068 - PNet Frame Creation Allocation Hotspot

## Description

Add a non-allocating or pooled PNet frame creation path for callers that already own an output buffer. The baseline shows `CreatePNet` allocation scales with payload size.

## Playbook

- `Evidence`: compare against `.agents/docs/benchmarks/2026-07-02-baseline.md`.
- `API`: prefer additive helpers such as `TryWritePNet` or `WritePNet` over breaking existing `CreatePNet` callers.
- `Safety`: preserve padding byte, padding length, and extended-header semantics.
- `Target`: keep the existing allocation-returning API, but let hot paths avoid it.

## Scope

- Add a buffer-writing PNet framing API if it keeps call sites simpler and safe.
- Use the new API in session/transport benchmark hot paths where ownership is clear.
- Keep `CreatePNet` for convenience and compatibility.
- Verify parser/property tests still cover padding and marker behavior.

## Out of Scope

- Changing PNet frame format.
- Rewriting IP packet framing.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| PNet frame create | 64 | 22.85 ns | 104 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*CreatePNetFrame*'` |
| PNet frame create | 1420 | 184.81 ns | 1448 B/op | same command with payload matrix |

Relevant source evidence:

- `PNetMeshPayloadFraming.CreatePNet` allocates a new `byte[]` for every frame.
- `PNetMeshSession.WritePacket` calls `CreatePNet` in a hot send path before encryption.

## Acceptance Criteria

- A non-allocating or pooled PNet frame creation path exists and is used by at least one measured hot path, or the issue closes with rationale that current allocation is acceptable.
- Existing frame parsing tests pass.
- Benchmark evidence reports before/after `CreatePNetFrame` and any updated session/transport allocations.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Baseline PNet frame creation allocation scales from 104 B/op to 1448 B/op. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records `PNet frame create` allocations. |
| 2 | F | `CreatePNet` currently returns a newly allocated array. | verified | source | `src/PNet.Mesh/PNetMeshPayloadFraming.cs` constructs `new byte[payload.Length + 1 + paddingLength]`. |
| 3 | R | A buffer-writing API can avoid allocation for callers with existing packet buffers. | verified | logical | The frame size is deterministic from payload length and padding, so callers can pre-size output buffers. |
