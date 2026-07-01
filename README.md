# PNet.Mesh

## Description

P2P protocol to use inside managed dotnet application

## Features

.) no extended OS permission required for covered container flows: ordinary UDP sockets, no TUN/TAP device, no raw-socket capability, no `CAP_NET_ADMIN`, and no privileged container mode
.) communiction over data fragments (UDP)
.) packet ordering and flow control
.) low overhead of 18bytes per datagram
.) same security as wireguard
.) crypto routing and crypto discovery
.) NAT trafersal and neighbor detection
.) compression

## Used Protocols

Wireguard
https://www.wireguard.com/protocol/

Noise Protocl Framework
http://noiseprotocol.org/noise.pdf

Interactive Connectivity Establishment (ICE)
https://tools.ietf.org/html/rfc8445

Explicit Congestion Notification (ECN) for RTP over UDP
https://tools.ietf.org/html/rfc6679

Low Extra Delay Background Transport (LEDBAT)
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
