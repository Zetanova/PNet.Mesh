---
issue: 059
date: 2026-07-02
source: tun/optional-component
priority: medium
status: completed
research-status: complete
research-date: 2026-07-02
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 781084d
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria"
views:
  enrich: "description+playbook+scope+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 059 - Add Optional PNet.Mesh.Tun Component

## Description

Add an optional `PNet.Mesh.Tun` component that exposes PNet.Mesh as an OS TUN interface for IPv4/IPv6 packet exchange and system-level benchmark flows. Keep the core `PNet.Mesh` library unprivileged and preserve the documented no-TUN boundary for non-TUN flows.

## Playbook

- Component boundary: add a separate optional project or package for TUN integration instead of folding privileged interface handling into the core library.
- Packet flow: read raw IPv4/IPv6 packets from TUN, send them through PNet.Mesh transport payloads, and write received IPv4/IPv6 packets back to TUN.
- Protocol boundary: keep PNet protobuf control messages separate from raw IPv4/IPv6 payload handling.
- Operator boundary: document setup, interface naming, MTU, addressing, routes, and required host/container permissions for the supported platform.
- Benchmark bridge: enable normal tools such as `ping` and `iperf3` to exercise PNet.Mesh over an OS interface and compare with `wireguard-go`.

## Scope

- Add an optional `PNet.Mesh.Tun` project, package, or CLI that references core `PNet.Mesh`.
- Start Linux-first unless implementation research identifies an existing cross-platform TUN library that fits the project with low risk.
- Support creating or attaching to a TUN interface in a documented local namespace/container setup.
- Bridge raw IPv4/IPv6 packets between the TUN file descriptor/device abstraction and PNet.Mesh payload transport.
- Document exact privilege requirements and setup commands discovered during implementation.
- Keep the README's current WireGuard-equivalence claim scoped so core PNet.Mesh remains no-TUN/no-route-injection unless the optional component is explicitly enabled.

## Out Of Scope

- Making TUN support required for normal PNet.Mesh use.
- Replacing core unprivileged PNet.Mesh APIs with OS interface APIs.
- Claiming kernel WireGuard feature parity beyond the packet I/O path implemented by this component.
- Full Windows/macOS TUN support in the first implementation slice.

## Acceptance Criteria

- A new optional `PNet.Mesh.Tun` component builds independently and references core `PNet.Mesh`.
- A local Linux namespace/container smoke can exchange raw IPv4 and IPv6 packets between two PNet.Mesh TUN endpoints.
- `ping` and either `iperf3` or an equivalent traffic tool can run over the PNet.Mesh TUN setup with documented commands.
- Documentation states privilege requirements and preserves the existing no-TUN boundary for core flows.
- Existing unit and e2e tests remain green.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The user requested an optional `PNet.Mesh.Tun` component. | verified | source | Current user request: "file an issue to add PNet.Mesh.Tun as optional component". |
| 2 | F | Core PNet.Mesh currently excludes TUN/TAP, OS routing, and route injection from its WireGuard-equivalence claim. | verified | source | `README.md` states that WireGuard-equivalent IPv4/IPv6 packet payload behavior excludes TUN/TAP interfaces, OS routing, and route injection. |
| 3 | F | PNet.Mesh already has raw IPv4/IPv6 packet payload helpers in the core library. | verified | source | `src/PNet.Mesh/PNetMeshPayloadFraming.cs` and `src/PNet.Mesh/PNetMeshIpPacket.cs` provide IPv4/IPv6 packet classification and helper paths. |
| 4 | R | The TUN feature should live outside the core library to preserve the unprivileged no-TUN boundary. | verified | logical | This follows from the user's optional-component request plus the README's current core boundary. |

## Completion Report

Implemented in `781084d`.

- Added optional `src/PNet.Mesh.Tun` with a fakeable `ITunDevice`, CIDR prefix routing, `PNetMeshTunBridge`, Linux `/dev/net/tun` adapter, and `ip` interface configurator.
- Added `src/PNet.Mesh.Tun.Cli` with command-line configuration for interface, MTU, addresses, routes, bind endpoints, keys, peers, `AllowedIPs`, attach/configure switches, verbose logging, and a Linux smoke Dockerfile.
- Added `src/PNet.Mesh.Tun.UnitTests` with fake TUN devices and real local `PNetMeshServer` instances to verify exact IPv4 and IPv6 packet exchange.
- Updated README and TUN docs to preserve the core no-TUN/no-route-injection boundary and document `/dev/net/tun`, `CAP_NET_ADMIN`, `CAP_NET_RAW`, `ping`, UDP `nc`, and `iperf3` tooling.
- Updated #061 with #059 smoke evidence and the remaining benchmark warmup/packet-loss work.

Verification:

- `timeout 120s dotnet restore PNet.Mesh.sln` passed.
- `timeout 120s dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.Tun src/PNet.Mesh.Tun.Cli src/PNet.Mesh.Tun.UnitTests --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 180s dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `timeout 180s dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed: 154/154.
- `timeout 120s dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none` passed: 2/2.
- `timeout 420s docker build -f src/PNet.Mesh.Tun.Cli/Dockerfile -t localhost/pnet-mesh-tun:dev .` passed.
- Privileged Docker smoke proved IPv4 ping packet exchange plus IPv4 and IPv6 UDP `nc` payload exchange; repeated ping and `iperf3` benchmark stability remain tracked by #061.
- Package audits passed for vulnerable, deprecated, and outdated packages.
- Full e2e command timed out and reported a transient six-node topology failure; targeted rerun of the named method passed 1/1 in 112.602s, and recurrence tracking was filed as #070.

## Completion Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The optional TUN implementation is contained in commit `781084d`. | verified | source | `git commit` created `781084d tun: add optional Linux TUN bridge`. |
| 2 | F | Fake-device TUN tests pass after implementation. | verified | test | `PNet.Mesh.Tun.UnitTests` passed 2/2 after the final build. |
| 3 | F | The named six-node DNS-alias e2e method passes when run alone after the full-suite timeout. | verified | test | Targeted e2e rerun passed 1/1 in 112.602s. |
