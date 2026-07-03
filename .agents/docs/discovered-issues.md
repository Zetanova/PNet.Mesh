---
brief: "open+outgoing-mrs+incoming-mrs+remote-issues+completed"
last-entry: 2026-07-03
last-opened: 2026-07-03-094
open-count: 2
last-completed: 2026-07-03
---

# Discovered Issues

Issue tracker for PNet.Mesh. Append during work, process via `/team-task fix issues`.

## Open

| # | Date | Source | Summary | Priority | Status | File |
|---|------|--------|---------|----------|--------|------|
| 082 | 2026-07-03 | refine-version-check | Review direct Testcontainers package update from 4.12.0 to 4.13.0 and rerun affected e2e batches. | medium | migration | [082-testcontainers-4-13-package-review](issues/082-testcontainers-4-13-package-review.md) |
| 080 | 2026-07-03 | refine-version-check | Update local .NET 10 SDK/runtime from 10.0.108/10.0.8 to Fedora 10.0.109/runtime 10.0.9 and rerun build/test gates. | medium | migration | [080-dotnet-10-sdk-runtime-update](issues/080-dotnet-10-sdk-runtime-update.md) |

## Outgoing MRs

| MR# | Date | Upstream | Summary | Status | Review-Deadline | Issue-Ref |
|-----|------|----------|---------|--------|-----------------|-----------|

## Incoming MRs

| MR# | Date | Source | Summary | Status | Review-Deadline |
|-----|------|--------|---------|--------|-----------------|

## Remote Issues

| # | Date | Project | Summary | Priority | Ref |
|---|------|---------|---------|----------|-----|

## Completed

| # | Date | Completed | Summary | Commits | File |
|---|------|-----------|---------|---------|------|
| 094 | 2026-07-03 | 2026-07-03 | Refactor microbenchmarks around decrypted cleartext and first-byte PNet/IPv4/IPv6 classification. | ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8 | [094-decrypted-cleartext-classification-benchmarks](issues/094-decrypted-cleartext-classification-benchmarks.md) |
| 093 | 2026-07-03 | 2026-07-03 | Add allocation-free IP packet classification/header parsing with benchmark regression gate. | ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8 | [093-allocation-free-ip-packet-classification](issues/093-allocation-free-ip-packet-classification.md) |
| 092 | 2026-07-03 | 2026-07-03 | Add a steady-state session benchmark before optimizing session internals. | ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8 | [092-steady-state-session-benchmark](issues/092-steady-state-session-benchmark.md) |
| 091 | 2026-07-03 | 2026-07-03 | Split TUN benchmark report models and assembly helpers into a focused file. | 4cb0090 | [091-tunpnetbenchmarkrunner-report-models](issues/091-tunpnetbenchmarkrunner-report-models.md) |
| 090 | 2026-07-03 | 2026-07-03 | Split TUN benchmark execution parsers and metrics helpers into focused files. | 4cb0090 | [090-tunpnetbenchmarkrunner-execution-parsers-and-metrics](issues/090-tunpnetbenchmarkrunner-execution-parsers-and-metrics.md) |
| 089 | 2026-07-03 | 2026-07-03 | Split TUN benchmark options and CLI helpers into a focused file. | 4cb0090 | [089-tunpnetbenchmarkrunner-options-and-cli-surface](issues/089-tunpnetbenchmarkrunner-options-and-cli-surface.md) |
| 088 | 2026-07-03 | 2026-07-03 | Split server control commands and socket work-item types into focused files. | 4cb0090 | [088-pnetmeshserver-control-commands-and-work-items](issues/088-pnetmeshserver-control-commands-and-work-items.md) |
| 087 | 2026-07-03 | 2026-07-03 | Split shared server contract types into focused files. | 4cb0090 | [087-pnetmeshserver-shared-contract-types](issues/087-pnetmeshserver-shared-contract-types.md) |
| 086 | 2026-07-03 | 2026-07-03 | Split TUN benchmark runner options, parsers, metrics, and report models into cohesive files. | 4cb0090 | [086-refactor-tun-pnet-benchmark-runner](issues/086-refactor-tun-pnet-benchmark-runner.md) |
| 085 | 2026-07-03 | 2026-07-03 | Split server DTO, command, and socket work-item types out of the PNetMeshServer implementation file. | 4cb0090 | [085-refactor-pnetmeshserver-message-types](issues/085-refactor-pnetmeshserver-message-types.md) |
| 084 | 2026-07-03 | 2026-07-03 | Clarify PNetMeshChannel concurrent ownership and tighten session/dispose state publication. | 4cb0090 | [084-refactor-pnetmeshchannel-state-ownership](issues/084-refactor-pnetmeshchannel-state-ownership.md) |
| 083 | 2026-07-03 | 2026-07-03 | Replace the touched PNetMeshSession object owner lock with System.Threading.Lock. | 4cb0090 | [083-refactor-session-owner-lock](issues/083-refactor-session-owner-lock.md) |
| 081 | 2026-07-03 | 2026-07-03 | Reduce nullable-warning debt across core, tests, TUN CLI, and TestNode. | b89ea91 | [081-nullability-warning-debt](issues/081-nullability-warning-debt.md) |
| 051 | 2026-07-01 | 2026-07-02 | Review transitive NuGet package updates reported by the dependency audit. | — | [051-review-transitive-nuget-package-updates](issues/051-review-transitive-nuget-package-updates.md) |
| 073 | 2026-07-02 | 2026-07-02 | Refactor PNetMeshSession control flow to remove premature locks with channel-owned state and benchmark gates. | e480ba9, a8c2a6b, c89ada0 | [073-refactor-session-control-flow-lock-free](issues/073-refactor-session-control-flow-lock-free.md) |
| 077 | 2026-07-02 | 2026-07-02 | PNetMeshSession single-owner mailbox refactor. | e480ba9 | [077-pnetmeshsession-single-owner-mailbox-refactor](issues/077-pnetmeshsession-single-owner-mailbox-refactor.md) |
| 072 | 2026-07-02 | 2026-07-02 | Apply .NET 10 memory and CommunityToolkit.HighPerformance optimizations across mesh hot paths. | 0433a9e, 4619f47, 7482232 | [072-apply-dotnet-10-memory-and-toolkit-optimizations](issues/072-apply-dotnet-10-memory-and-toolkit-optimizations.md) |
| 075 | 2026-07-02 | 2026-07-02 | TUN packet ownership transfer and no-copy channel enqueue. | 4619f47 | [075-tun-packet-ownership-transfer-and-no-copy-channel-enqueue](issues/075-tun-packet-ownership-transfer-and-no-copy-channel-enqueue.md) |
| 076 | 2026-07-02 | 2026-07-02 | ACK bitmap processing and packet tracker cleanup. | 7482232 | [076-ack-bitmap-processing-and-packet-tracker-cleanup](issues/076-ack-bitmap-processing-and-packet-tracker-cleanup.md) |
| 079 | 2026-07-02 | 2026-07-02 | PNetMeshTunBridge peer connect memoization. | c89ada0 | [079-pnetmeshtunbridge-peer-connect-memoization](issues/079-pnetmeshtunbridge-peer-connect-memoization.md) |
| 078 | 2026-07-02 | 2026-07-02 | PNetMeshChannel relay-state atomic signaling. | a8c2a6b | [078-pnetmeshchannel-relay-state-atomic-signaling](issues/078-pnetmeshchannel-relay-state-atomic-signaling.md) |
| 074 | 2026-07-02 | 2026-07-02 | Span-based byte-key and IP-byte helper optimizations. | 0433a9e | [074-span-based-byte-key-and-ip-byte-helper-optimizations](issues/074-span-based-byte-key-and-ip-byte-helper-optimizations.md) |
| 071 | 2026-07-02 | 2026-07-02 | Stabilize sustained PNet.Mesh.Tun OS traffic before ping and iperf3 benchmarks can pass. | d667db3 | [071-stabilize-pnet-mesh-tun-os-traffic](issues/071-stabilize-pnet-mesh-tun-os-traffic.md) |
| 061 | 2026-07-02 | 2026-07-02 | Add PNet.Mesh.Tun ping and iperf3 IPv4/IPv6 benchmark scenario. | 53ca5bb, d667db3 | [061-pnet-mesh-tun-iperf3-benchmark-scenario](issues/061-pnet-mesh-tun-iperf3-benchmark-scenario.md) |
| 062 | 2026-07-02 | 2026-07-02 | Add wireguard-go TUN benchmark baseline using the same topology and traffic profile. | efecd4a | [062-wireguard-go-tun-comparison-benchmark](issues/062-wireguard-go-tun-comparison-benchmark.md) |
| 064 | 2026-07-02 | 2026-07-02 | Add manual or scheduled workflow for TUN benchmark runs and regression reporting. | 5a5ce0c | [064-manual-scheduled-tun-benchmark-workflow](issues/064-manual-scheduled-tun-benchmark-workflow.md) |
| 063 | 2026-07-02 | 2026-07-02 | Add comparison result schema for latency, bandwidth, CPU, RSS, GC, and allocations. | 9bd8a8ad393c4f4f34a9816b29c3b698f0a66c66 | [063-tun-comparison-result-schema-and-allocation-counters](issues/063-tun-comparison-result-schema-and-allocation-counters.md) |
| 065 | 2026-07-02 | 2026-07-02 | Create single-command PNet.Mesh.Tun vs wireguard-go latency, bandwidth, and RSS benchmark script. | e5e0c8d7b0bc24a20d6acae2da2fa41cafb048d6 | [065-tun-comparison-benchmark-runner-script](issues/065-tun-comparison-benchmark-runner-script.md) |
| 070 | 2026-07-02 | 2026-07-02 | Stabilize the full Testcontainers e2e suite after renewed timeout and six-node flake. | d667db3, e4c0158 | [070-testcontainers-e2e-full-suite-timeout-recurrence](issues/070-testcontainers-e2e-full-suite-timeout-recurrence.md) |
| 069 | 2026-07-02 | 2026-07-02 | Reduce UDP loopback macro harness receive allocation to isolate protocol cost. | d667db3 | [069-udp-loopback-macro-allocation-hotspot](issues/069-udp-loopback-macro-allocation-hotspot.md) |
| 068 | 2026-07-02 | 2026-07-02 | Add non-allocating or pooled PNet frame creation path for payload framing hotspots. | d667db3 | [068-pnet-frame-creation-allocation-hotspot](issues/068-pnet-frame-creation-allocation-hotspot.md) |
| 067 | 2026-07-02 | 2026-07-02 | Reduce session protobuf/frame allocation pressure measured in session and in-memory macro benchmarks. | d667db3 | [067-session-protobuf-frame-allocation-hotspot](issues/067-session-protobuf-frame-allocation-hotspot.md) |
| 066 | 2026-07-02 | 2026-07-02 | Reduce WireGuard hash/MAC allocation pressure measured in handshake and rejection benchmarks. | d667db3 | [066-wireguard-hash-mac-allocation-hotspot](issues/066-wireguard-hash-mac-allocation-hotspot.md) |
| 060 | 2026-07-02 | 2026-07-02 | Add manual privileged Docker topology plan, preflight, create, and teardown for TUN benchmarks. | 1e079d2 | [060-privileged-tun-benchmark-topology](issues/060-privileged-tun-benchmark-topology.md) |
| 059 | 2026-07-02 | 2026-07-02 | Add optional Linux PNet.Mesh.Tun bridge, CLI, container image, docs, and fake-device tests. | 781084d | [059-add-optional-pnet-mesh-tun-component](issues/059-add-optional-pnet-mesh-tun-component.md) |
| 058 | 2026-07-02 | 2026-07-02 | Fan out optimization issues only from measured benchmark hotspots. | 720e48a | [058-post-baseline-benchmark-optimization-backlog](issues/058-post-baseline-benchmark-optimization-backlog.md) |
| 057 | 2026-07-02 | 2026-07-02 | Capture benchmark baselines and define report-only performance/allocation regression policy. | 8af78e0 | [057-benchmark-baseline-and-regression-policy](issues/057-benchmark-baseline-and-regression-policy.md) |
| 056 | 2026-07-02 | 2026-07-02 | Add Release macro harnesses for in-memory session and UDP loopback throughput/latency JSON. | 64aa683 | [056-macro-throughput-and-latency-benchmarks](issues/056-macro-throughput-and-latency-benchmarks.md) |
| 055 | 2026-07-02 | 2026-07-02 | Benchmark handshake, transport, framing, rejection, and session hot paths with allocations. | 15537b4 | [055-core-protocol-microbenchmarks](issues/055-core-protocol-microbenchmarks.md) |
| 054 | 2026-07-02 | 2026-07-02 | Add BenchmarkDotNet project, Release-only commands, benchmark matrix, and allocation metrics. | a402a8a | [054-benchmarkdotnet-foundation-and-allocation-metrics](issues/054-benchmarkdotnet-foundation-and-allocation-metrics.md) |
| 053 | 2026-07-01 | 2026-07-01 | Reuse the TestNode image across Testcontainers e2e harness instances. | 9ce2d5e | [053-testcontainers-e2e-suite-timeout](issues/053-testcontainers-e2e-suite-timeout.md) |
| 050 | 2026-07-01 | 2026-07-01 | Add redacted relay diagnostics for leases, demux, promotion, fallback, and relay decisions. | bb740ee | [050-relay-audit-and-diagnostics-hardening](issues/050-relay-audit-and-diagnostics-hardening.md) |
| 035 | 2026-07-01 | 2026-07-01 | Complete WireGuard-compatible transport mode parent after all child slices finished. | fa6cd47 | [035-wireguard-compatible-transport-mode](issues/035-wireguard-compatible-transport-mode.md) |
| 047 | 2026-07-01 | 2026-07-01 | Implement relay-assisted WireGuard endpoint discovery and direct-path promotion. | fa6cd47 | [047-relay-assisted-wireguard-endpoint-discovery](issues/047-relay-assisted-wireguard-endpoint-discovery.md) |
| 052 | 2026-07-01 | 2026-07-01 | Fix the three-server localhost relay exchange timing failure through relay queueing. | fa6cd47 | [052-baseline-three-server-relay-test-failure](issues/052-baseline-three-server-relay-test-failure.md) |
| 045 | 2026-07-01 | 2026-07-01 | Add Testcontainers WireGuard relay interop coverage for opaque encrypted UDP forwarding. | 9bf1fba | [045-pnet-relay-to-wireguard-peer-interop](issues/045-pnet-relay-to-wireguard-peer-interop.md) |
| 042 | 2026-07-01 | 2026-07-01 | Add unprivileged Testcontainers WireGuard peer interop coverage for encrypted UDP exchange. | e0e9b62 | [042-wireguard-go-testcontainers-interoperability-test-for-wireguard-peer-to-pnet-mesh](issues/042-wireguard-go-testcontainers-interoperability-test-for-wireguard-peer-to-pnet-mesh.md) |
| 049 | 2026-07-01 | 2026-07-01 | Add deterministic parser property coverage for WireGuard demux, PNet markers, and padding. | 6e3ac9a | [049-packet-parser-fuzz-and-property-tests](issues/049-packet-parser-fuzz-and-property-tests.md) |
| 046 | 2026-07-01 | 2026-07-01 | Implement shared-port WireGuard relay registration, MAC1 demux, and receiver-index fast path. | c9efeba | [046-shared-port-wireguard-relay-registration-and-demux](issues/046-shared-port-wireguard-relay-registration-and-demux.md) |
| 048 | 2026-07-01 | 2026-07-01 | Add PNet-to-PNet WireGuard-compatible transport e2e coverage for internal protobuf frames. | a52fce1 | [048-pnet-to-pnet-wireguard-compatible-transport-e2e](issues/048-pnet-to-pnet-wireguard-compatible-transport-e2e.md) |
| 044 | 2026-07-01 | 2026-07-01 | Define PNet frame padding-count header and document WireGuard zero-padding behavior. | 3a36b4d | [044-pnet-frame-padding-and-default-protobuf-header](issues/044-pnet-frame-padding-and-default-protobuf-header.md) |
| 043 | 2026-07-01 | 2026-07-01 | Keep mesh channel raw bytes and add helpers to parse/craft PNet, IPv4, and IPv6 payloads. | 7165d94 | [043-mesh-channel-typed-messages](issues/043-mesh-channel-typed-messages.md) |
| 041 | 2026-07-01 | 2026-07-01 | Read and create IPv4/IPv6 packets from WireGuard plaintext payloads. | 9faf3bd | [041-read-and-create-ipv4-ipv6-packets-from-wireguard-plaintext-payloads](issues/041-read-and-create-ipv4-ipv6-packets-from-wireguard-plaintext-payloads.md) |
| 040 | 2026-07-01 | 2026-07-01 | Expose decrypted WireGuard transport plaintext as raw payload bytes. | 5cf316b | [040-expose-decrypted-wireguard-transport-plaintext-as-raw-payload-bytes](issues/040-expose-decrypted-wireguard-transport-plaintext-as-raw-payload-bytes.md) |
| 039 | 2026-07-01 | 2026-07-01 | Implement WireGuard cookie reply and DoS gate behavior. | 92a0133 | [039-wireguard-cookie-reply-and-dos-gate-behavior](issues/039-wireguard-cookie-reply-and-dos-gate-behavior.md) |
| 038 | 2026-07-01 | 2026-07-01 | Implement WireGuard peer, receiver-index, keypair, and rekey lifecycle state. | 6a37478 | [038-wireguard-peer-receiver-index-keypair-and-rekey-lifecycle-state](issues/038-wireguard-peer-receiver-index-keypair-and-rekey-lifecycle-state.md) |
| 037 | 2026-07-01 | 2026-07-01 | Implement WireGuard packet framing and TAI64N handshake replay tracking. | 216f246 | [037-wireguard-packet-framing-and-tai64n-handshake-replay-tracking](issues/037-wireguard-packet-framing-and-tai64n-handshake-replay-tracking.md) |
| 036 | 2026-07-01 | 2026-07-01 | Implement WireGuard Noise profile and BLAKE2s MAC/KDF paths. | ba0de3d | [036-wireguard-noise-profile-and-blake2s-mac-kdf-paths](issues/036-wireguard-noise-profile-and-blake2s-mac-kdf-paths.md) |
| 034 | 2026-07-01 | 2026-07-01 | Clear README/docs/workflow compose smoke dependencies so #011 can remove compose artifacts. | 4cb2ae3019cc4d15a2d4baadf8a53fac08db9844 | [034-clear-readme-docs-workflow-compose-smoke-deps](issues/034-clear-readme-docs-workflow-compose-smoke-deps.md) |
| 014 | 2026-06-30 | 2026-07-01 | Implement or test UDP fragment transport and the 32-byte encapsulation claim. | 672f48e489f8c3253df29ac02f9cf407a190b4ad | [014-udp-fragments-and-overhead-coverage](issues/014-udp-fragments-and-overhead-coverage.md) |
| 017 | 2026-06-30 | 2026-07-01 | Implement and test crypto routing and crypto discovery behavior. | b9c4cba, 0ae5bc3 | [017-crypto-routing-discovery-coverage](issues/017-crypto-routing-discovery-coverage.md) |
| 033 | 2026-06-30 | 2026-07-01 | Route-path observability and diagnostics coverage. | 0ae5bc3 | [033-route-path-observability-diagnostics](issues/033-route-path-observability-diagnostics.md) |
| 032 | 2026-06-30 | 2026-07-01 | Crypto routing and discovery behavior regression coverage. | b9c4cba | [032-crypto-routing-discovery-behavior-coverage](issues/032-crypto-routing-discovery-behavior-coverage.md) |
| 031 | 2026-06-30 | 2026-07-01 | README security-claim wording cleanup and narrowing. | 172e333 | [031-readme-security-claim-hygiene](issues/031-readme-security-claim-hygiene.md) |
| 016 | 2026-06-30 | 2026-07-01 | Implement and test WireGuard/Noise security invariants behind the README security claim. | b36bd7f, 7c66b95, 0320083, 172e333 | [016-wireguard-noise-security-coverage](issues/016-wireguard-noise-security-coverage.md) |
| 030 | 2026-06-30 | 2026-07-01 | Replay and cookie-guard regression coverage. | 0320083 | [030-replay-cookie-guard-coverage](issues/030-replay-cookie-guard-coverage.md) |
| 029 | 2026-06-30 | 2026-07-01 | Tamper rejection coverage for wrong keys, PSKs, and corrupted packets. | 7c66b95 | [029-tamper-rejection-coverage](issues/029-tamper-rejection-coverage.md) |
| 028 | 2026-06-30 | 2026-07-01 | Noise handshake and authentication happy-path coverage. | b36bd7f | [028-handshake-authentication-coverage](issues/028-handshake-authentication-coverage.md) |
| 027 | 2026-06-30 | 2026-07-01 | Flow-control limit and negotiated SYN assertion coverage. | c8648e6 | [027-flow-control-negotiation-coverage](issues/027-flow-control-negotiation-coverage.md) |
| 015 | 2026-06-30 | 2026-07-01 | Expand packet ordering, retransmission, and flow-control implementation coverage. | c77659b, c8648e6 | [015-packet-ordering-flow-control-coverage](issues/015-packet-ordering-flow-control-coverage.md) |
| 011 | 2026-06-30 | 2026-07-01 | Remove Docker Compose e2e artifacts after Testcontainers reaches equivalent coverage. | b517a273f58f8180b7feb9d839ef16e066c9fc49 | [011-cleanup-compose-after-equivalent-coverage](issues/011-cleanup-compose-after-equivalent-coverage.md) |
| 001 | 2026-06-30 | 2026-06-30 | Migrate project, packages, tests, and test-node container from `net5.0` to .NET 10. | 30ea5f8 | [001-dotnet-5-eol-migration.md](issues/001-dotnet-5-eol-migration.md) |
| 002 | 2026-06-30 | 2026-06-30 | Replace unavailable `Noise` package with `Noise.NET` and restore/build/audit successfully. | 30ea5f8 | [002-noise-package-restore-blocker.md](issues/002-noise-package-restore-blocker.md) |
| 003 | 2026-06-30 | 2026-06-30 | Add compose e2e story and runnable mesh topology smoke flow. | 30ea5f8 | [003-mesh-topology-e2e-story.md](issues/003-mesh-topology-e2e-story.md) |
| 004 | 2026-06-30 | 2026-06-30 | Normalize existing C# whitespace and add formatting config. | 7e8340f, 30ea5f8 | [004-dotnet-format-drift.md](issues/004-dotnet-format-drift.md) |
| 005 | 2026-06-30 | 2026-06-30 | Move test projects from `tests/` into the `src/` project layout and update all references. | 3b657cb | [005-tests-projects-under-src](issues/005-tests-projects-under-src.md) |
| 006 | 2026-06-30 | 2026-06-30 | Track Testcontainers migration and coverage expansion child issues through completion. | 52cf1f7, 948f553, 61af492, 56a70ff, 84bb53b, 6a02e9d, 7c09bd6, 49deb32 | [006-testcontainers-coverage-tracking.md](issues/006-testcontainers-coverage-tracking.md) |
| 007 | 2026-06-30 | 2026-06-30 | Add a Testcontainers-based xUnit e2e harness for PNet.Mesh test nodes. | 52cf1f7 | [007-testcontainers-e2e-harness](issues/007-testcontainers-e2e-harness.md) |
| 008 | 2026-06-30 | 2026-06-30 | Port the existing six-node compose mesh smoke topology to Testcontainers. | 948f553 | [008-port-compose-topology-to-testcontainers](issues/008-port-compose-topology-to-testcontainers.md) |
| 009 | 2026-06-30 | 2026-06-30 | Expand container e2e coverage parent gate after child scenario completion. | 61af492, 56a70ff, 84bb53b, 6a02e9d, 7c09bd6 | [009-expand-container-e2e-coverage](issues/009-expand-container-e2e-coverage.md) |
| 010 | 2026-06-30 | 2026-06-30 | Add deterministic routing, relay duplicate, route-loop, and session relay unit coverage. | 49deb32 | [010-unit-test-doubles-for-routing](issues/010-unit-test-doubles-for-routing.md) |
| 012 | 2026-06-30 | 2026-06-30 | Implement and cover direct P2P communication behavior advertised by the README. | 96c3efc | [012-readme-p2p-communication-coverage.md](issues/012-readme-p2p-communication-coverage.md) |
| 013 | 2026-06-30 | 2026-06-30 | Verify the no-extended-OS-permission feature claim with documented and executable checks. | 5c4d336 | [013-no-extended-os-permissions-coverage.md](issues/013-no-extended-os-permissions-coverage.md) |
| 018 | 2026-06-30 | 2026-06-30 | Narrow NAT traversal and ICE claims to covered candidate-exchange behavior. | 2f083f4 | [018-nat-traversal-neighbor-ice-coverage](issues/018-nat-traversal-neighbor-ice-coverage.md) |
| 019 | 2026-06-30 | 2026-06-30 | Narrow compression claim to reserved protocol fields, not runtime compression. | 073dee5 | [019-compression-feature-coverage](issues/019-compression-feature-coverage.md) |
| 020 | 2026-06-30 | 2026-06-30 | Narrow ECN and LEDBAT references to field models, not runtime behavior. | 708236e | [020-ecn-ledbat-coverage](issues/020-ecn-ledbat-coverage.md) |
| 021 | 2026-06-30 | 2026-06-30 | Add direct peer Testcontainers coverage for bidirectional payload exchange. | 61af492 | [021-direct-peer-e2e-coverage](issues/021-direct-peer-e2e-coverage.md) |
| 022 | 2026-06-30 | 2026-06-30 | Add bootstrap discovery Testcontainers coverage through a connected peer. | 56a70ff | [022-bootstrap-discovery-e2e-coverage](issues/022-bootstrap-discovery-e2e-coverage.md) |
| 023 | 2026-06-30 | 2026-06-30 | Multi-hop relay Testcontainers scenario across separated segments. | 84bb53b | [023-multi-hop-route-e2e-coverage](issues/023-multi-hop-route-e2e-coverage.md) |
| 024 | 2026-06-30 | 2026-06-30 | Restart recovery Testcontainers scenario for a rejoining node. | 6a02e9d | [024-restart-recovery-e2e-coverage](issues/024-restart-recovery-e2e-coverage.md) |
| 025 | 2026-06-30 | 2026-06-30 | Negative-path Testcontainers scenario for invalid PSK delivery rejection. | 7c09bd6 | [025-negative-path-e2e-coverage](issues/025-negative-path-e2e-coverage.md) |
| 026 | 2026-06-30 | 2026-06-30 | Deterministic packet ordering and retransmission regression tests. | c77659b | [026-packet-ordering-retransmission-coverage](issues/026-packet-ordering-retransmission-coverage.md) |
