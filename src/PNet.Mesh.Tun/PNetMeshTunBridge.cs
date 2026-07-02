using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PNet.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh.Tun
{
    public sealed class PNetMeshTunBridge
    {
        readonly PNetMeshServer _server;
        readonly ITunDevice _tunDevice;
        readonly PeerState[] _peers;
        readonly ILogger _logger;

        public PNetMeshTunBridge(
            PNetMeshServer server,
            ITunDevice tunDevice,
            IEnumerable<PNetMeshTunPeerRoute> peerRoutes,
            ILogger<PNetMeshTunBridge> logger = null)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _tunDevice = tunDevice ?? throw new ArgumentNullException(nameof(tunDevice));
            _logger = logger ?? NullLogger<PNetMeshTunBridge>.Instance;
            _peers = ValidatePeerRoutes(peerRoutes).Select(route => new PeerState(route)).ToArray();
        }

        public IReadOnlyList<PNetMeshTunPeerRoute> PeerRoutes => _peers.Select(peer => peer.Route).ToArray();

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var workers = new List<Task>(_peers.Length * 2 + 1)
            {
                RunTunReaderAsync(cancellationToken)
            };

            foreach (var peer in _peers)
            {
                workers.Add(RunPeerWriterAsync(peer, cancellationToken));
                workers.Add(RunPeerReaderAsync(peer, cancellationToken));
            }

            await Task.WhenAll(workers);
        }

        async Task RunTunReaderAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[_tunDevice.Mtu];

            while (true)
            {
                var bytesRead = await _tunDevice.ReadPacketAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    continue;

                if (!PNetMeshIpPacket.TryRead(buffer.AsSpan(0, bytesRead), out var packet))
                {
                    _logger.LogWarning("dropping invalid packet read from TUN device {interfaceName}", _tunDevice.Name);
                    continue;
                }

                var peer = FindPeerByDestination(packet.DestinationAddress);
                if (peer == null)
                {
                    _logger.LogWarning("dropping unroutable packet from {sourceAddress} to {destinationAddress}", packet.SourceAddress, packet.DestinationAddress);
                    continue;
                }

                var packetBytes = buffer.AsSpan(0, packet.TotalLength).ToArray();
                if (!peer.TryQueuePacket(packetBytes))
                {
                    _logger.LogWarning("dropping packet to {peerName} because the peer send queue is full", peer.Route.Name);
                    continue;
                }

                _logger.LogDebug(
                    "queued {packetLength} byte packet from TUN {interfaceName} to peer {peerName}: {sourceAddress} -> {destinationAddress}",
                    packet.TotalLength,
                    _tunDevice.Name,
                    peer.Route.Name,
                    packet.SourceAddress,
                    packet.DestinationAddress);
            }
        }

        async Task RunPeerWriterAsync(PeerState peer, CancellationToken cancellationToken)
        {
            while (await peer.WaitToSendAsync(cancellationToken))
            {
                while (peer.TryTakePacket(out var payload))
                {
                    try
                    {
                        var channel = await peer.GetChannelAsync(_server, cancellationToken);
                        await channel.EnqueueUnreliableWriteAsync(payload, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "dropping packet to {peerName} because the mesh channel did not accept it", peer.Route.Name);
                    }
                }
            }
        }

        async Task RunPeerReaderAsync(PeerState peer, CancellationToken cancellationToken)
        {
            var channel = await peer.GetChannelAsync(_server, cancellationToken);

            while (await channel.WaitToReadAsync(cancellationToken))
            {
                while (channel.TryRead(out var payload))
                {
                    if (!PNetMeshIpPacket.TryRead(payload.Span, out var packet))
                    {
                        _logger.LogDebug("dropping non-IP payload from peer {peerName}", peer.Route.Name);
                        continue;
                    }

                    if (!peer.AllowsSource(packet.SourceAddress))
                    {
                        _logger.LogWarning(
                            "dropping spoofed packet from peer {peerName}: source {sourceAddress} is outside AllowedIPs",
                            peer.Route.Name,
                            packet.SourceAddress);
                        continue;
                    }

                    await _tunDevice.WritePacketAsync(payload[..packet.TotalLength], cancellationToken);
                    _logger.LogDebug(
                        "forwarded {packetLength} byte packet from peer {peerName} to TUN {interfaceName}: {sourceAddress} -> {destinationAddress}",
                        packet.TotalLength,
                        peer.Route.Name,
                        _tunDevice.Name,
                        packet.SourceAddress,
                        packet.DestinationAddress);
                }
            }
        }

        PeerState FindPeerByDestination(IPAddress destinationAddress)
        {
            PeerState match = null;
            var matchPrefixLength = -1;

            foreach (var peer in _peers)
            {
                foreach (var prefix in peer.Route.AllowedIPs)
                {
                    if (prefix.PrefixLength <= matchPrefixLength)
                        continue;
                    if (!prefix.Contains(destinationAddress))
                        continue;

                    match = peer;
                    matchPrefixLength = prefix.PrefixLength;
                }
            }

            return match;
        }

        static IReadOnlyList<PNetMeshTunPeerRoute> ValidatePeerRoutes(IEnumerable<PNetMeshTunPeerRoute> peerRoutes)
        {
            if (peerRoutes == null)
                throw new ArgumentNullException(nameof(peerRoutes));

            var routes = peerRoutes.ToArray();
            if (routes.Length == 0)
                throw new ArgumentException("At least one peer route is required.", nameof(peerRoutes));

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var route in routes)
            {
                if (route == null)
                    throw new ArgumentException("Peer routes cannot contain null entries.", nameof(peerRoutes));
                if (string.IsNullOrWhiteSpace(route.Name))
                    throw new ArgumentException("Peer route names are required.", nameof(peerRoutes));
                if (!names.Add(route.Name))
                    throw new ArgumentException($"Duplicate peer route name '{route.Name}'.", nameof(peerRoutes));
                if (route.Peer == null)
                    throw new ArgumentException($"Peer route '{route.Name}' is missing a peer.", nameof(peerRoutes));
                if (route.AllowedIPs == null || route.AllowedIPs.Count == 0)
                    throw new ArgumentException($"Peer route '{route.Name}' requires at least one AllowedIPs prefix.", nameof(peerRoutes));
            }

            return routes;
        }

        sealed class PeerState
        {
            // multi-threading: the TUN reader and peer reader startup loops can both open the same peer channel; serialize first connect and cache one channel.
            readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
            readonly Channel<ReadOnlyMemory<byte>> _sendQueue = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(256)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            PNetMeshChannel _channel;

            public PeerState(PNetMeshTunPeerRoute route)
            {
                Route = route;
            }

            public PNetMeshTunPeerRoute Route { get; }

            public bool AllowsSource(IPAddress sourceAddress)
            {
                return Route.AllowedIPs.Any(prefix => prefix.Contains(sourceAddress));
            }

            public bool TryQueuePacket(ReadOnlyMemory<byte> packet)
            {
                return _sendQueue.Writer.TryWrite(packet);
            }

            public ValueTask<bool> WaitToSendAsync(CancellationToken cancellationToken)
            {
                return _sendQueue.Reader.WaitToReadAsync(cancellationToken);
            }

            public bool TryTakePacket(out ReadOnlyMemory<byte> packet)
            {
                return _sendQueue.Reader.TryRead(out packet);
            }

            public async ValueTask<PNetMeshChannel> GetChannelAsync(PNetMeshServer server, CancellationToken cancellationToken)
            {
                if (_channel != null)
                    return _channel;

                await _connectLock.WaitAsync(cancellationToken);
                try
                {
                    _channel ??= await server.ConnectToAsync(Route.Peer, cancellationToken);
                    return _channel;
                }
                finally
                {
                    _connectLock.Release();
                }
            }
        }
    }
}
