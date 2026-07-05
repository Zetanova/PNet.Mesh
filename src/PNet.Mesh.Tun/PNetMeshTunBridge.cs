using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PNet.Mesh;
using System;
using System.Buffers;
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
        static readonly Action<ILogger, int, string, string, IPAddress, IPAddress, Exception?> QueuedTunPacket =
            LoggerMessage.Define<int, string, string, IPAddress, IPAddress>(
                LogLevel.Debug,
                new EventId(1000, nameof(LogQueuedTunPacket)),
                "queued {packetLength} byte packet from TUN {interfaceName} to peer {peerName}: {sourceAddress} -> {destinationAddress}");
        static readonly Action<ILogger, int, string, string, IPAddress, IPAddress, Exception?> ForwardedPeerPacket =
            LoggerMessage.Define<int, string, string, IPAddress, IPAddress>(
                LogLevel.Debug,
                new EventId(1001, nameof(LogForwardedPeerPacket)),
                "forwarded {packetLength} byte packet from peer {peerName} to TUN {interfaceName}: {sourceAddress} -> {destinationAddress}");

        public PNetMeshTunBridge(
            PNetMeshServer server,
            ITunDevice tunDevice,
            IEnumerable<PNetMeshTunPeerRoute> peerRoutes,
            ILogger<PNetMeshTunBridge>? logger = null)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _tunDevice = tunDevice ?? throw new ArgumentNullException(nameof(tunDevice));
            _logger = logger ?? NullLogger<PNetMeshTunBridge>.Instance;
            _peers = ValidatePeerRoutes(peerRoutes).Select(route => new PeerState(this, route)).ToArray();
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
            while (true)
            {
                IMemoryOwner<byte>? packetOwner = MemoryPool<byte>.Shared.Rent(_tunDevice.Mtu);

                try
                {
                    var buffer = packetOwner.Memory;
                    var bytesRead = await _tunDevice.ReadPacketAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                        continue;

                    if (!PNetMeshIpPacket.TryRead(buffer.Span[..bytesRead], out var packet))
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

                    var packetBytes = buffer.Slice(0, packet.TotalLength);
                    if (!peer.TryQueuePacket(packetBytes, packetOwner))
                    {
                        _logger.LogWarning("dropping packet to {peerName} because the peer send queue is full", peer.Route.Name);
                        continue;
                    }

                    packetOwner = null;

                    if (_logger.IsEnabled(LogLevel.Debug))
                        LogQueuedTunPacket(peer, packet);
                }
                finally
                {
                    ClearAndDispose(packetOwner);
                }
            }
        }

        async Task RunPeerWriterAsync(PeerState peer, CancellationToken cancellationToken)
        {
            try
            {
                while (await peer.WaitToSendAsync(cancellationToken))
                {
                    while (peer.TryTakePacket(out var queuedPacket))
                    {
                        IMemoryOwner<byte>? memoryOwner = queuedPacket.MemoryOwner;
                        try
                        {
                            var channel = await peer.GetChannelAsync(_server, cancellationToken);
                            if (channel.TryWriteUnreliableIpPacket(queuedPacket.Payload, memoryOwner!))
                            {
                                memoryOwner = null;
                            }
                            else
                            {
                                var transferOwner = memoryOwner!;
                                memoryOwner = null;
                                await channel.EnqueueUnreliableIpPacketAsync(queuedPacket.Payload, transferOwner, cancellationToken);
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "dropping packet to {peerName} because the mesh channel did not accept it", peer.Route.Name);
                        }
                        finally
                        {
                            ClearAndDispose(memoryOwner);
                        }
                    }
                }
            }
            finally
            {
                peer.CompleteSendQueueAndDisposeQueuedPackets();
            }
        }

        async Task RunPeerReaderAsync(PeerState peer, CancellationToken cancellationToken)
        {
            var channel = await peer.GetChannelAsync(_server, cancellationToken);
            channel.AttachRawIpFrameSink(peer);

            try
            {
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
                        if (_logger.IsEnabled(LogLevel.Debug))
                            LogForwardedPeerPacket(peer, packet);
                    }
                }
            }
            finally
            {
                channel.DetachRawIpFrameSink(peer);
            }
        }

        PNetMeshRawIpFrameSinkResult TryReceivePeerPacket(
            PeerState peer,
            ReadOnlySpan<byte> packet,
            PNetMeshIpPacketVersion expectedVersion)
        {
            if (!PNetMeshIpPacket.TryRead(packet, out var parsed) || parsed.Version != expectedVersion)
            {
                _logger.LogDebug("dropping invalid raw IP payload from peer {peerName}", peer.Route.Name);
                return PNetMeshRawIpFrameSinkResult.Consumed;
            }

            if (!peer.AllowsSource(parsed.SourceAddress))
            {
                _logger.LogWarning(
                    "dropping spoofed packet from peer {peerName}: source {sourceAddress} is outside AllowedIPs",
                    peer.Route.Name,
                    parsed.SourceAddress);
                return PNetMeshRawIpFrameSinkResult.Consumed;
            }

            if (_tunDevice is ITunDeviceFastWriter fastWriter
                && fastWriter.TryWritePacket(packet[..parsed.TotalLength]))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    LogForwardedPeerPacket(peer, parsed);
                return PNetMeshRawIpFrameSinkResult.Delivered;
            }

            return PNetMeshRawIpFrameSinkResult.FallbackToChannel;
        }

        PeerState? FindPeerByDestination(IPAddress destinationAddress)
        {
            PeerState? match = null;
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
                if (route.Peer is null)
                    throw new ArgumentException($"Peer route '{route.Name}' is missing a peer.", nameof(peerRoutes));
                if (route.AllowedIPs == null || route.AllowedIPs.Count == 0)
                    throw new ArgumentException($"Peer route '{route.Name}' requires at least one AllowedIPs prefix.", nameof(peerRoutes));
            }

            return routes;
        }

        void LogQueuedTunPacket(
            PeerState peer,
            PNetMeshIpPacket packet)
        {
            QueuedTunPacket(
                _logger,
                packet.TotalLength,
                _tunDevice.Name,
                peer.Route.Name,
                packet.SourceAddress,
                packet.DestinationAddress,
                null);
        }

        void LogForwardedPeerPacket(
            PeerState peer,
            PNetMeshIpPacket packet)
        {
            ForwardedPeerPacket(
                _logger,
                packet.TotalLength,
                peer.Route.Name,
                _tunDevice.Name,
                packet.SourceAddress,
                packet.DestinationAddress,
                null);
        }

        sealed class PeerState : IPNetMeshRawIpFrameSink
        {
            // multi-threading: the TUN reader and peer reader startup loops can both open the same peer channel; memoize one connect task and cache one channel.
            readonly Channel<QueuedPacket> _sendQueue = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(256)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            readonly PNetMeshTunBridge? _bridge;
            Task<PNetMeshChannel>? _connectTask;
            PNetMeshChannel? _channel;

            public PeerState(PNetMeshTunBridge bridge, PNetMeshTunPeerRoute route)
                : this(route)
            {
                _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            }

            public PeerState(PNetMeshTunPeerRoute route)
            {
                Route = route;
            }

            public PNetMeshTunPeerRoute Route { get; }

            public bool AllowsSource(IPAddress sourceAddress)
            {
                foreach (var prefix in Route.AllowedIPs)
                {
                    if (prefix.Contains(sourceAddress))
                        return true;
                }

                return false;
            }

            public PNetMeshRawIpFrameSinkResult TryReceiveIPv4(ReadOnlySpan<byte> packet)
            {
                return Bridge.TryReceivePeerPacket(this, packet, PNetMeshIpPacketVersion.IPv4);
            }

            public PNetMeshRawIpFrameSinkResult TryReceiveIPv6(ReadOnlySpan<byte> packet)
            {
                return Bridge.TryReceivePeerPacket(this, packet, PNetMeshIpPacketVersion.IPv6);
            }

            PNetMeshTunBridge Bridge => _bridge ?? throw new InvalidOperationException("Peer state is not attached to a TUN bridge.");

            public bool TryQueuePacket(ReadOnlyMemory<byte> packet, IMemoryOwner<byte> memoryOwner)
            {
                return _sendQueue.Writer.TryWrite(new QueuedPacket(packet, memoryOwner));
            }

            public ValueTask<bool> WaitToSendAsync(CancellationToken cancellationToken)
            {
                return _sendQueue.Reader.WaitToReadAsync(cancellationToken);
            }

            public bool TryTakePacket(out QueuedPacket packet)
            {
                return _sendQueue.Reader.TryRead(out packet);
            }

            public void CompleteSendQueueAndDisposeQueuedPackets()
            {
                _sendQueue.Writer.TryComplete();
                while (_sendQueue.Reader.TryRead(out var packet))
                    ClearAndDispose(packet.MemoryOwner);
            }

            public async ValueTask<PNetMeshChannel> GetChannelAsync(PNetMeshServer server, CancellationToken cancellationToken)
            {
                var channel = Volatile.Read(ref _channel);
                if (channel != null)
                    return channel;

                var connectTask = Volatile.Read(ref _connectTask);
                if (connectTask == null)
                {
                    var completion = new TaskCompletionSource<PNetMeshChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var currentTask = Interlocked.CompareExchange(ref _connectTask, completion.Task, null);
                    if (currentTask == null)
                    {
                        _ = ConnectAndCacheChannelAsync(server, completion);
                        connectTask = completion.Task;
                    }
                    else
                    {
                        connectTask = currentTask;
                    }
                }

                return await connectTask.WaitAsync(cancellationToken);
            }

            async Task ConnectAndCacheChannelAsync(
                PNetMeshServer server,
                TaskCompletionSource<PNetMeshChannel> completion)
            {
                try
                {
                    var channel = await server.ConnectToAsync(Route.Peer);
                    Volatile.Write(ref _channel, channel);
                    completion.TrySetResult(channel);
                }
                catch (OperationCanceledException ex)
                {
                    _ = Interlocked.CompareExchange(ref _connectTask, null, completion.Task);
                    completion.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    _ = Interlocked.CompareExchange(ref _connectTask, null, completion.Task);
                    completion.TrySetException(ex);
                }
            }
        }

        readonly struct QueuedPacket
        {
            public QueuedPacket(ReadOnlyMemory<byte> payload, IMemoryOwner<byte> memoryOwner)
            {
                Payload = payload;
                MemoryOwner = memoryOwner;
            }

            public ReadOnlyMemory<byte> Payload { get; }

            public IMemoryOwner<byte> MemoryOwner { get; }
        }

        static void ClearAndDispose(IMemoryOwner<byte>? memoryOwner)
        {
            if (memoryOwner == null)
                return;

            memoryOwner.Memory.Span.Clear();
            memoryOwner.Dispose();
        }
    }
}
