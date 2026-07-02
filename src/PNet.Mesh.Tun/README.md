# PNet.Mesh.Tun

Optional Linux TUN bridge for PNet.Mesh. The core `PNet.Mesh` library remains unprivileged and does not create TUN/TAP devices or inject OS routes unless this component and CLI are used explicitly.

## Quick Start

Build and run the fake-device bridge tests:

```bash
dotnet restore PNet.Mesh.sln
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none
```

Build the Linux smoke image:

```bash
docker build -f src/PNet.Mesh.Tun.Cli/Dockerfile -t localhost/pnet-mesh-tun:dev .
```

## Privileges

| Need | Requirement |
|---|---|
| Create or attach `/dev/net/tun` | Linux host/container with `/dev/net/tun` mounted |
| Configure addresses, MTU, link state, and routes | `CAP_NET_ADMIN` |
| Run `ping` in the smoke container | `CAP_NET_RAW` or image-level ping capability |
| Run throughput smoke | `iperf3` in the TUN CLI image |

The CLI configures the interface by default with `ip addr replace`, `ip link set`, and `ip route replace`. Use `--no-configure-interface` when another namespace setup tool owns interface state.

## Container Smoke

Recommended isolated smoke path:

```bash
docker network create --subnet 172.28.59.0/24 pnet-tun

docker run -d --rm --name pnet-tun-a --network pnet-tun --ip 172.28.59.2 \
  --cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun \
  localhost/pnet-mesh-tun:dev run \
  --interface pnet0 --mtu 1280 \
  --address 10.80.0.1/24 --address fd80::1/64 \
  --bind 0.0.0.0:12401 \
  --public-key zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg= \
  --private-key H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI= \
  --psk lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4= \
  --peer node-b:ytmio4/zTDDXHt0A6jb8G7Gcr3ty7iMOEkduloie1Rk=@172.28.59.3:12402 \
  --allowed-ip node-b=10.80.0.2/32 --allowed-ip node-b=fd80::2/128

docker run -d --rm --name pnet-tun-b --network pnet-tun --ip 172.28.59.3 \
  --cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun \
  localhost/pnet-mesh-tun:dev run \
  --interface pnet0 --mtu 1280 \
  --address 10.80.0.2/24 --address fd80::2/64 \
  --bind 0.0.0.0:12402 \
  --public-key ytmio4/zTDDXHt0A6jb8G7Gcr3ty7iMOEkduloie1Rk= \
  --private-key 3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk= \
  --psk lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4= \
  --peer node-a:zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=@172.28.59.2:12401 \
  --allowed-ip node-a=10.80.0.1/32 --allowed-ip node-a=fd80::1/128
```

Run one traffic check per fresh two-container topology until the benchmark harness adds multi-flow warmup and readiness handling. Recreate the containers before switching checks.

IPv4 ping connectivity:

```bash
docker exec pnet-tun-a ping -c 3 -W 2 10.80.0.2
```

IPv4 one-datagram UDP payload:

```bash
docker exec -d pnet-tun-b sh -c 'rm -f /tmp/pnet-nc.out; timeout 8s nc -u -l -p 9000 -s 10.80.0.2 > /tmp/pnet-nc.out'
printf pnet | docker exec -i pnet-tun-a nc -u -w 2 10.80.0.2 9000
docker exec pnet-tun-b grep -q pnet /tmp/pnet-nc.out
```

IPv6 one-datagram UDP payload:

```bash
docker exec -d pnet-tun-a sh -c 'rm -f /tmp/pnet-nc6.out; timeout 8s nc -6 -u -l -p 9001 -s fd80::1 > /tmp/pnet-nc6.out'
printf pnet6 | docker exec -i pnet-tun-b nc -6 -u -w 2 fd80::1 9001
docker exec pnet-tun-a grep -q pnet6 /tmp/pnet-nc6.out
```

The smoke image includes `iperf3`, but this component-level smoke only proves connectivity. Full repeated-ping and `iperf3` benchmark runs need explicit warmup, packet-loss handling, and machine-readable result capture; that work is tracked separately in the benchmark integration issues.

Clean up:

```bash
docker rm -f pnet-tun-a pnet-tun-b
docker network rm pnet-tun
```

## Packet Flow

`PNetMeshTunBridge` reads exact IPv4/IPv6 packets from `ITunDevice`, validates them with `PNetMeshIpPacket.TryRead`, longest-prefix matches the destination against each peer route's `AllowedIPs`, and writes the packet bytes through `PNetMeshChannel.TryWrite`.

The reverse path reads peer channel payloads, validates IPv4/IPv6 framing, enforces that the source address belongs to that peer's `AllowedIPs`, and writes exact packet bytes back to the TUN device.
