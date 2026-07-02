---
issue: 064
date: 2026-07-02
source: benchmark/integration-phase-5
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 5a5ce0c
gate-depends: [057, 063, 065]
gate-reason: "Requires baseline policy, stable comparison result schema, and the single-command benchmark runner before workflow automation or reporting thresholds."
gate-last-checked: 2026-07-02
gate-status: cleared
probeable: false
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria+gate"
views:
  enrich: "description+playbook+scope+gate+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+gate+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 064 - Manual Or Scheduled TUN Benchmark Workflow

## Description

Add the operator workflow for running TUN integration benchmarks manually or on a privileged scheduled runner. The workflow should archive results, compare against baselines, and start with report-only regression output until variance is understood.

## Playbook

- `Manual first`: keep the initial workflow manually runnable because TUN benchmarks require privileged network setup.
- `Scheduled when safe`: add scheduled execution only for runners that explicitly support the required isolation and privileges.
- `Report-only`: begin with non-blocking comparison output before enforcing thresholds.
- `Artifacts`: archive raw outputs, normalized result files, comparison summaries, logs, and environment metadata.

## Scope

- Use the runner script from #065 as the command entrypoint for the full PNet.Mesh.Tun and `wireguard-go` benchmark comparison.
- Add artifact layout for raw tool output, normalized result schema, comparison summaries, and logs.
- Document privileged runner requirements and skip behavior for unsupported environments.
- Wire report-only baseline comparison into the workflow.
- Define when data is stable enough to promote selected metrics to blocking regression gates.

## Out Of Scope

- Implementing privileged CI infrastructure if none exists.
- Enforcing blocking thresholds before repeated baseline data exists.
- Creating optimization issues without measured regression or hotspot evidence.

## Acceptance Criteria

- A single documented workflow can run the #065 benchmark runner and archive artifacts.
- Unsupported environments produce an explicit skip/inconclusive result rather than a false pass.
- Comparison output includes latency, bandwidth, CPU, RSS, GC, and allocation fields where applicable.
- Thresholds are report-only until repeated runs establish stable variance bounds.

## Gate

Cleared on 2026-07-02: #057, #063, and #065 are complete, so this issue is ready.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [057, 063, 065]` | source | ready | #057, #063, and #065 are complete, so #064 is ready. |

## Validation History

- 2026-07-02: dependency gates cleared by #057, #063, and #065; #064 was ready before completion.

## Completion Report

Implemented in `5a5ce0c`.

- Added the manual and scheduled privileged TUN benchmark workflow, including artifact layout, runner expectations, and unsupported-environment skip behavior.
- Wired report-only baseline regression output into the workflow so the comparison stays informational until repeated runs establish stable variance.
- Recorded managed-runtime availability context in the regression output and synchronized the README / best-practices guidance with the workflow.

Verification on 2026-07-02:

- `bash -n scripts/bench-tun-comparison.sh` passed.
- `scripts/bench-tun-comparison.sh --help` passed and showed `--baseline`.
- `scripts/bench-tun-comparison.sh --dry-run --baseline` passed.
- `scripts/bench-tun-comparison.sh --baseline` with no baseline returned rc=2 with a clear logfmt error.
- `timeout 900s rtk scripts/bench-tun-comparison.sh --output-dir /tmp/codex-team-task.krDAde/test/tun-script-064c --baseline /tmp/codex-team-task.krDAde/test/tun-script-065-recheck/comparison.json --warmup 0ms --iperf-duration 3s --ping-count 1 --timeout 30s --run-timeout 420s --build-timeout 180s` passed.
- `regression-report.json` reported `status: report-only` with 17 metrics and managedRuntime availability/reason metadata.
- Docker cleanup checks found no resources for label `pnet.mesh.benchmark.topology=pnet-tun-bench`.
- container/performance/doc-sync specialist reviews were CLEAR.
- `git diff --check` passed.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | R | TUN integration benchmarks should start as manual or report-only because they require privileged network setup. | verified | logical | Privileged networking makes benchmark availability environment-dependent and unsuitable for mandatory checks until runners are proven. |
| 2 | F | Baseline and regression policy is tracked by #057. | verified | source | #057 defines benchmark baselines, environment metadata, allocation budgets, and report-only thresholds. |
| 3 | F | Comparison result schema is tracked by #063. | verified | source | #063 defines normalized and raw result output for PNet.Mesh.Tun versus `wireguard-go`. |
| 4 | F | The single-command benchmark runner script is tracked separately by #065. | verified | source | #065 defines the runner script scope and acceptance criteria. |

## Resolving Commits

- `5a5ce0c` - `benchmarks: add TUN workflow regression reporting`
