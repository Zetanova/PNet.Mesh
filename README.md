# PNet.Mesh

## Description

P2P protocol to use inside managed dotnet application

## Features

.) no extended OS permission required for covered container flows: ordinary UDP sockets, no TUN/TAP device, no raw-socket capability, no `CAP_NET_ADMIN`, and no privileged container mode
.) communiction over data fragments (UDP)
.) packet ordering and flow control
.) low overhead of 18bytes per datagram
.) Noise IKpsk2 handshake and encrypted transport coverage; see Security coverage below. This is not a full WireGuard-equivalence claim
.) crypto routing and crypto discovery
.) neighbor endpoint detection and relay candidate exchange for covered container flows; full ICE/STUN/TURN NAT traversal is not implemented
.) compression protocol fields are reserved; runtime compression negotiation and compressed payload handling are not implemented

## Used Protocols

WireGuard protocol reference; PNet.Mesh does not claim full WireGuard behavior or equivalence
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
timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none
timeout 120s scripts/e2e-mesh-topology.sh --no-build --timeout 90
```

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
