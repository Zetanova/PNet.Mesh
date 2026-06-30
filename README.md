# PNet.Mesh

## Description

P2P protocol to use inside managed dotnet application

## Features

.) no extended OS permission required
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
timeout 120s scripts/e2e-mesh-topology.sh --no-build --timeout 90
```
