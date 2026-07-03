---
title: PNet.Mesh TUN Peer Performance Iteration
created: 2026-07-03
last-refined: 2026-07-03
status: complete
assumptions-date: 2026-07-03
baseline-artifact: artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json
---

# PNet.Mesh TUN Peer Performance Iteration

Iteratively improve the primary PNet.Mesh/PNet.Mesh.Tun hot path against `wireguard-go` on the `tun-mtu-64k` peer comparison without running full suites after every micro-change.

## Goal

Improve relative performance without simplifying or removing any features for:

| Area | Direction |
|---|---|
| Latency | Lower is better. |
| Throughput | Preserve capped throughput or improve it. |
| RSS | Lower is better. |
| Thread count | Lower is better. |
| CPU ticks | Lower is better. |
| Packet loss | Must stay stable. |

Use `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` as the starting comparison baseline.

## Loop

1. Select one likely hotspot from source inspection or benchmark evidence.
2. Make one small optimization batch, preferably one to three related changes.
3. Run cheap validation after each micro-change:
   - scoped build for touched projects
   - targeted unit or smoke test only when correctness-sensitive behavior changed
   - no full unit suite
   - no full microbenchmark suite
   - no full TUN comparison after every tiny edit
4. Run a lightweight TUN smoke comparison only when it gives useful feedback for the current attempt.
5. Run the full TUN comparison at batch checkpoints, before claiming improvement, and at final closeout.
6. If the checkpoint improves at least one primary metric without material regression, keep the change, promote that artifact as the working baseline, record the result, and start the loop again.
7. Continue until the diminishing-returns stop condition is met.

## Checkpoint Command

```bash
timeout 900s scripts/bench-tun-comparison.sh \
  --output-dir artifacts/benchmarks/tun-comparison/$(date -u +%Y%m%dT%H%M%SZ) \
  --baseline artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json \
  --ping-count 5 \
  --warmup 2s \
  --iperf-duration 3s \
  --mtu 1420 \
  --payload-mode mtu
```

After each successful checkpoint, use the latest successful `comparison.json` as the next working baseline.

## Success Criteria

A batch succeeds when it improves at least one target metric relative to the current working baseline and does not materially regress the others:

| Metric | Success Interpretation |
|---|---|
| Latency | Relative decrease. |
| RSS | Relative decrease. |
| Thread count | Relative decrease. |
| CPU ticks | Relative decrease. |
| Throughput | Neutral under the cap or relative increase. |
| Packet loss | No degradation. |

Do not use absolute target values as success or stop criteria.

## Regression Triage

If a checkpoint shows material regression in packet loss, capped throughput, latency, RSS, thread count, CPU ticks, or benchmark reliability:

1. Do not close out the batch.
2. Analyze the current attempt and identify the cause.
3. Fix or improve the current attempt, then re-run relevant validation.
4. If the attempt cannot be repaired cleanly, roll it back and try a different optimization path.
5. Continue the improvement loop.

Regression triage is not a stop condition.

## Stop Condition

Diminishing returns is the only terminal stop condition.

Stop when all are true:

| Condition | Requirement |
|---|---|
| Checkpoint trend | Multiple consecutive full checkpoint comparisons show no material relative gain. |
| Hotspot search | Source inspection finds no remaining low-risk, plausible hotspot. |
| Next-step cost | Further improvement would require a larger design change, runtime change, workload change, or riskier rewrite. |

## Closeout

When diminishing returns is reached:

1. Run one final full TUN comparison.
2. Update `.agents/docs/benchmarks/peer-comparison-log.md`.
3. Update `.agents/docs/benchmarks/comparisons/wireguard-go-tun.md`.
4. Update the relevant issue or completion report.
5. Report to the user:
   - starting baseline artifact
   - final comparison artifact
   - per-metric before/current/delta
   - successful optimization batches
   - attempted but reverted paths, if any
   - remaining bottlenecks
   - why diminishing returns was reached

## Completion Report

Completed on 2026-07-03 after one kept batch, four reverted batches, and a current-HEAD final closeout comparison.

| Field | Value |
|---|---|
| Starting baseline | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| Best kept checkpoint | `artifacts/benchmarks/tun-comparison/20260703T114429Z/comparison.json` |
| Final closeout artifact | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| Final result vs start | IPv4 ping -3.3%, IPv6 ping -13.7%, RSS -1.2%, CPU +9.0%; thread count unchanged; capped throughput and packet loss stable. |
| Validation | Current-HEAD scoped Release build, whitespace verification, focused `PNetMeshTunBridgeTests`, Docker image rebuild, final full TUN comparison. |

| Batch | Outcome | Evidence |
|---|---|---|
| Cached `IpPrefix` mask metadata, compiled debug log delegates, foreach source-prefix scan, nullable cleanup | Kept; best checkpoint improved IPv4 latency, IPv6 latency, and RSS, with CPU watch. | `artifacts/benchmarks/tun-comparison/20260703T114429Z/comparison.json` |
| Exact-host `IpPrefix` fast path | Reverted; latency and CPU regressed materially. | `artifacts/benchmarks/tun-comparison/20260703T115706Z/comparison.json` |
| Direct `LinuxTunDevice` `ValueTask` returns | Reverted; latency and CPU regressed materially. | `artifacts/benchmarks/tun-comparison/20260703T120212Z/comparison.json` |
| Raw packet-header route matching | Reverted; IPv6 latency, RSS, and CPU regressed materially. | `artifacts/benchmarks/tun-comparison/20260703T121132Z/comparison.json` |
| `SingleWriter = true` peer send queue | Reverted; CPU regressed materially. | `artifacts/benchmarks/tun-comparison/20260703T121624Z/comparison.json` |

Remaining bottlenecks are broader than a low-risk micro-change: per-packet `IPAddress` materialization, channel handoff overhead, managed runtime RSS, and noisy short ping samples. Diminishing returns was reached because multiple plausible small hot-path changes failed full checkpoints, and further gains need a larger parser/channel/runtime design pass or a benchmark variance refinement.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The starting peer comparison artifact exists at `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json`. | verified | source | The fresh comparison was created and recorded during this session. |
| 2 | F | The project has a wrapper command for PNet.Mesh.Tun versus `wireguard-go` comparisons. | verified | source | `scripts/bench-tun-comparison.sh` is documented by `.agents/docs/benchmarks/tun-workflow.md` and was used successfully. |
| 3 | F | Peer comparison tracking uses `.agents/docs/benchmarks/peer-comparison-log.md` and `.agents/docs/benchmarks/comparisons/wireguard-go-tun.md`. | verified | source | Both files were created for the project comparison workflow. |
| 4 | C | The requested process must avoid absolute success targets and stop only on diminishing returns. | verified | source | The user requested relative criteria only, loop-after-success behavior, regression triage with fix or rollback, and final closeout only after diminishing returns. |
