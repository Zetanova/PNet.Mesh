---
issue: 060
date: 2026-07-02
source: benchmark/integration-phase-1
priority: medium
status: gated
terminal-state: gated
gate-depends: [059]
gate-reason: "Requires optional PNet.Mesh.Tun component before executable benchmark topology can be validated."
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

# 060 - Privileged TUN Benchmark Topology

## Description

Define the privileged Linux namespace or container topology used by PNet.Mesh.Tun integration benchmarks. This is the first integration-benchmark slice after the optional TUN component exists and should establish repeatable setup, teardown, routing, and preflight behavior before benchmark traffic is added.

## Playbook

- `Isolation`: use disposable Linux network namespaces or containers so interface, address, and route mutations do not touch the host network.
- `Preflight`: fail fast when TUN, namespace, route, or required privilege support is unavailable.
- `Parity`: design the topology so PNet.Mesh.Tun and `wireguard-go` can use the same addresses, MTU, traffic endpoints, and duration profile.
- `Cleanup`: remove interfaces, namespaces, routes, and background processes even after failed benchmark runs.

## Scope

- Document and implement the benchmark topology setup and teardown path.
- Define interface names, IPv4/IPv6 addresses, routes, MTU, UDP ports, and process placement.
- Add a preflight command that reports whether the current host/container can run privileged TUN benchmarks.
- Keep the topology Linux-first and explicitly mark it privileged/manual unless a CI-safe privileged runner exists.
- Emit machine-readable environment metadata needed by later benchmark result comparison.

## Out Of Scope

- Running `iperf3` traffic or collecting benchmark numbers.
- Implementing the optional TUN component itself.
- Adding Windows or macOS TUN benchmark support.

## Acceptance Criteria

- A documented command creates and tears down the benchmark topology without leaving namespaces, routes, or processes behind.
- Preflight reports clear `pass`, `skip`, or `fail` state for TUN and privilege requirements.
- The topology reserves equivalent slots for PNet.Mesh.Tun and `wireguard-go` runs.
- IPv4 and IPv6 addressing, route setup, MTU, ports, and cleanup are documented.

## Gate

This issue stays gated until #059 adds the optional `PNet.Mesh.Tun` component.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | #059 tracks adding optional `PNet.Mesh.Tun` support. | verified | source | `.agents/docs/issues/059-add-optional-pnet-mesh-tun-component.md` defines the optional TUN component and benchmark bridge. |
| 2 | F | The benchmark rollout already has foundation, micro, macro, baseline, and optimization tracking issues. | verified | source | Issues #054-#058 cover those benchmark phases. |
| 3 | R | Integration benchmarks that mutate network interfaces need isolated setup and teardown. | verified | logical | TUN, address, and route setup affects OS networking, so disposable namespaces or containers constrain side effects. |
