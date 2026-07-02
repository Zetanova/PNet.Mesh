# PNet.Mesh

## Description

P2P protocol to use inside managed dotnet application

## Features

.) WireGuard-compatible UDP protocol for managed .NET applications
.) IPv4/IPv6 packet plaintext support for WireGuard transport payloads; core library TUN/TAP device integration and OS route injection are excluded
.) optional `PNet.Mesh.Tun` Linux component exposes mesh payloads through an OS TUN interface when explicitly enabled
.) no extended OS permission required for covered container flows: ordinary UDP sockets, no TUN/TAP device, no raw-socket capability, no `CAP_NET_ADMIN`, and no privileged container mode
.) communiction over data fragments (UDP)
.) packet ordering and flow control
.) low overhead: 32 bytes encrypted payload encapsulation per data packet (16-byte transport header + 16-byte AEAD tag; excludes outer UDP/IP and padding)
.) WireGuard Noise IKpsk2 handshake, BLAKE2s MAC/KDF, cookie reply, replay tracking, and encrypted transport coverage; see Security coverage below
.) crypto routing and crypto discovery
.) neighbor endpoint detection and relay candidate exchange for covered container flows; full ICE/STUN/TURN NAT traversal is not implemented
.) compression protocol fields are reserved; runtime compression negotiation and compressed payload handling are not implemented

## Used Protocols

WireGuard protocol reference; PNet.Mesh targets WireGuard-equivalent UDP handshake and transport packet behavior for IPv4/IPv6 packet payloads, excluding kernel/device integration such as TUN/TAP interfaces, OS routing, and route injection.
https://www.wireguard.com/protocol/

Noise Protocol Framework
http://noiseprotocol.org/noise.pdf

Interactive Connectivity Establishment (ICE) candidate model; full ICE checks, STUN, and TURN are not implemented
https://tools.ietf.org/html/rfc8445

Explicit Congestion Notification (ECN) field model; runtime ECN marking, reporting, and congestion behavior are not implemented
https://tools.ietf.org/html/rfc6679

Low Extra Delay Background Transport (LEDBAT) timestamp/delay field model; runtime delay-based congestion control is not implemented
https://tools.ietf.org/html/rfc6817

## Maintenance

NuGet package maintenance:

```bash
scripts/packages.sh --dry-run
scripts/packages.sh --name 'PNet*' --pre --dry-run
```

Build and test:

```bash
dotnet restore PNet.Mesh.sln
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none
dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none
```

Run the Testcontainers e2e suite as bounded batches rather than one monolithic command:

```bash
timeout 300s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.test_node_image_name_is_shared_across_harness_instances -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.starts_single_test_node_container_and_waits_for_readiness -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.failed_startup_exception_includes_node_logs -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.test_node_runs_without_extended_network_permissions
timeout 300s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_relay_container_forwards_opaque_exchange_to_peer_container -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.relay_assisted_endpoint_discovery_promotes_direct_after_authenticated_probe
timeout 300s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.direct_peers_exchange_payloads_in_both_directions -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.direct_peers_exchange_non_trivial_payload_size -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.invalid_psk_peers_do_not_deliver_payloads -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.bootstrap_peer_discovery_exchanges_payloads_between_learned_peers
timeout 300s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.multi_hop_route_crosses_separated_container_segments -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.restarted_node_rejoins_without_breaking_unrelated_peers
timeout 300s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.six_node_topology_matches_compose_smoke_route_with_docker_dns_aliases
```

Optional Linux TUN bridge:

```bash
dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none
docker build -f src/PNet.Mesh.Tun.Cli/Dockerfile -t localhost/pnet-mesh-tun:dev .
```

See `src/PNet.Mesh.Tun/README.md` for required `/dev/net/tun`, `CAP_NET_ADMIN`, `CAP_NET_RAW`, `ping`, UDP `nc`, and `iperf3` tooling commands.

Benchmarks:

```bash
dotnet restore PNet.Mesh.sln
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --macro in-memory --payload 128 --warmup 00:00:05 --duration 00:00:30
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --macro udp-loopback --payload 128 --warmup 00:00:05 --duration 00:00:30
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --tun-topology plan
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --tun-topology preflight
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --tun-benchmark pnet-mesh-tun --ping-count 1 --iperf-duration 3s
```

BenchmarkDotNet writes reports and exports to `artifacts/benchmarks/results/`. Debug runs fail with a Release command reminder. The initial matrix covers payload sizes 64, 128, 512, 1280, and 1420 with time, throughput, allocation, and GC metrics (`ns/op`, `ops/sec`, `B/op`, `Gen0`, `Gen1`, `Gen2`).

Macro benchmarks run through the same Release-only benchmark executable with `--macro`. The `in-memory` scenario measures two-session payload exchange without sockets or Docker. The `udp-loopback` scenario measures encrypted packet exchange over localhost UDP. Both emit JSON with packets/sec, payload bytes/sec, wire bytes/sec, p50/p95/p99 latency, allocated bytes, GC counts, runtime, OS, CPU architecture, and payload size. `--macro testnode-smoke` documents the optional manual TestNode/container perf smoke as non-deterministic; it is not run by default.

Privileged Linux TUN benchmark topology is manual and report-only. `--tun-topology plan` emits the fixed dual-stack Docker topology, `--tun-topology preflight` reports `pass`, `skip`, or `fail`, and `--tun-topology create`/`teardown` manages only labeled Docker resources for later PNet.Mesh.Tun and `wireguard-go` benchmark scenarios. `--tun-benchmark pnet-mesh-tun` is the current PNet.Mesh.Tun diagnostic runner: it creates that topology, attempts IPv4/IPv6 ping and `iperf3`, emits parsed JSON success/failure details plus process metrics, and tears the topology down. Sustained traffic stabilization is tracked in `.agents/docs/issues/071-stabilize-pnet-mesh-tun-os-traffic.md`.

Supported mesh E2E coverage lives in the Testcontainers project below; use the named methods for mesh topology coverage.

Direct P2P coverage:

```bash
dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshServerTests.direct_peers_exchange_payloads_in_both_directions_over_public_api
timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.direct_peers_exchange_payloads_in_both_directions
```

Permission boundary coverage:

```bash
timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.test_node_runs_without_extended_network_permissions
```

Security coverage:

- `PNetMeshProtocolTest.valid_peers_complete_noise_ikpsk2_handshake_and_exchange_payloads_over_derived_transports_regression`
- `PNetMeshProtocolTest.try_read_response_rejects_wrong_psk_regression`
- `PNetMeshProtocolTest.validate_packet_rejects_wrong_responder_key_regression`
- `PNetMeshProtocolTest.validate_packet_rejects_corrupted_handshake_initiation_mac_regression`
- `PNetMeshProtocolTest.validate_packet_rejects_corrupted_handshake_response_mac_regression`
- `PNetMeshProtocolTest.try_read_message_rejects_tampered_payload_without_plaintext_regression`
- `PNetMeshProtocolTest.try_read_message_does_not_consume_counter_for_tampered_payload_regression`
- `PNetMeshProtocolTest.try_read_message_rejects_replayed_packet_without_plaintext_regression`
- `PNetMeshProtocolTest.try_read_message_rejects_unknown_receiver_index_without_plaintext_regression`
- `PNetMeshProtocolTest.validate_packet_accepts_matching_cookie_macs_regression`
- `PNetMeshProtocolTest.validate_packet_rejects_missing_or_wrong_cookie_initiation_mac_regression`
- `PNetMeshProtocolTest.validate_packet_rejects_missing_or_wrong_cookie_response_mac_regression`
- `PNetMeshPacketTrackerTest.get_bitmap_rejects_counter_before_latest_regression`
- `PNetMeshPacketTrackerTest.get_bitmap_rejects_undersized_buffer_regression`

```bash
dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -class PNet.Actor.UnitTests.Mesh.PNetMeshProtocolTest -class PNet.Actor.UnitTests.Mesh.PNetMeshPacketTrackerTest -parallel none
```

Candidate exchange and segmented route coverage:

```bash
timeout 120s dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshRoutingUnitTests.server_relay_candidate_selection_prefers_known_route_then_reflexive_endpoint
timeout 120s dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshRoutingUnitTests.session_relay_roundtrips_route_hop_payload_and_candidate_exchange
timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.multi_hop_route_crosses_separated_container_segments
```
