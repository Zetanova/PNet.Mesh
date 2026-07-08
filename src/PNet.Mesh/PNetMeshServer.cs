using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    public sealed class PNetMeshServer : IDisposable
    {
        readonly PNetMeshServerSettings _settings;
        readonly PNetMeshProtocol _protocol;
        readonly PNetMeshRouter _router;
        readonly IServiceProvider? _services;
        readonly ILogger _logger;
        readonly PNetMeshPeer _localPeer;
        readonly Func<PNetMeshChannel> _channelFactory;
        readonly PNetMeshEndpointUpdater _endpointUpdater;

        PNetMeshOutboundDispatcher? _outboundDispatcher;
        Task _outboundTask = Task.CompletedTask;

        Channel<PNetMeshControlCommands.Command>? _controlChannel;
        Task _controlTask = Task.CompletedTask;

        ImmutableArray<Socket> _sockets = ImmutableArray<Socket>.Empty;
        ImmutableArray<Task> _receiveTasks = ImmutableArray<Task>.Empty;

        Channel<PNetMeshControlCommands.Command> ControlChannel => _controlChannel ?? throw new InvalidOperationException("Server is not started.");

        public PNetMeshServer(PNetMeshServerSettings settings, IServiceProvider? services = null, ILogger<PNetMeshServer>? logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _services = services;
            _logger = logger ?? NullLogger<PNetMeshServer>.Instance;

            _channelFactory = _services != null
                ? () => ActivatorUtilities.CreateInstance<PNetMeshChannel>(_services)
                : () => new PNetMeshChannel();

            //todo gen key pair

            _localPeer = new PNetMeshPeer
            {
                PublicKey = settings.PublicKey,
                EndPoints = settings.BindTo //todo lookup
            };

            _protocol = new PNetMeshProtocol(settings.PrivateKey, settings.PublicKey, settings.Psk);

            _router = new PNetMeshRouter();
            _endpointUpdater = new PNetMeshEndpointUpdater(_router, _logger);
            var address = new byte[10];

            if (settings.Peers != null)
                foreach (var entry in settings.Peers)
                {
                    PNetMeshUtils.GetAddressFromPublicKey(entry.PublicKey, address);

                    foreach (var endpoint in entry.EndPoints ?? Array.Empty<string>())
                    {
                        var ep = PNetMeshUtils.ParseEndPoint(endpoint);
                        _router.SetEntry(address, ep);
                    }
                }
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start()
        {
            _controlChannel = Channel.CreateBounded<PNetMeshControlCommands.Command>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            var controlChannel = _controlChannel;
            _outboundDispatcher = new PNetMeshOutboundDispatcher(() => _sockets, _logger);
            var outboundDispatcher = _outboundDispatcher;
            var sessionTable = new PNetMeshSessionTable();
            var inboundDispatcher = new PNetMeshInboundDispatcher(
                _protocol,
                sessionTable,
                _endpointUpdater,
                controlChannel.Writer,
                _logger);

            _outboundTask = Task.Run(() => ProcessOutbound(outboundDispatcher.Reader));
            _ = _outboundTask.ContinueWith(t => _logger.LogError(t.Exception, "outbound process error"), TaskContinuationOptions.OnlyOnFaulted);
            _controlTask = Task.Run(() => ProcessControl(
                controlChannel,
                outboundDispatcher.Writer,
                sessionTable,
                inboundDispatcher));
            _ = _controlTask.ContinueWith(t => _logger.LogError(t.Exception, "control process error"), TaskContinuationOptions.OnlyOnFaulted);

            var sockets = ImmutableArray.CreateBuilder<Socket>();
            var receiveTasks = ImmutableArray.CreateBuilder<Task>();
            var useBlockingUdpReceive = IsBlockingUdpReceiveEnabled();
            var udpSocketBufferBytes = GetUdpSocketBufferBytes(_settings);

            foreach (var bind in _settings.BindTo)
            {
                var ep = IPEndPoint.Parse(bind);
                //todo any endpoint parsing

                var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                ApplyUdpSocketBufferSize(socket, udpSocketBufferBytes);
                socket.Bind(ep);
                sockets.Add(socket);
            }

            _sockets = sockets.ToImmutable();

            //System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            //    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            //    .Select(n => n.Address.ToString())
            //    .ToArray();

            foreach (var socket in _sockets)
            {
                var item = new PNetMeshSocketReceiveWorkItem
                {
                    MemoryOwner = MemoryPool<byte>.Shared.Rent(ushort.MaxValue),
                    Protocol = _protocol,
                    InboundDispatcher = inboundDispatcher,
                    Logger = _logger
                };

                if (useBlockingUdpReceive)
                {
                    var task = Task.Factory.StartNew(
                        static state =>
                        {
                            var (receiveSocket, receiveItem) = ((Socket, PNetMeshSocketReceiveWorkItem))state!;
                            ProcessReceiveLoop(receiveSocket, receiveItem);
                        },
                        (socket, item),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    _ = task.ContinueWith(t => _logger.LogError(t.Exception, "udp receive process error"), TaskContinuationOptions.OnlyOnFaulted);
                    receiveTasks.Add(task);
                    continue;
                }

                var args = new SocketAsyncEventArgs();
                args.UserToken = item;
                args.SetBuffer(item.MemoryOwner.Memory);
                args.Completed += ProcessReceive;
                args.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                if (!socket.ReceiveMessageFromAsync(args))
                {
                    ProcessReceive(socket, args);
                }
            }

            _receiveTasks = receiveTasks.ToImmutable();
        }

        public void Stop()
        {
            _outboundDispatcher?.Complete();
            _outboundDispatcher = null;

            _controlChannel?.Writer.TryComplete();
            _controlChannel = null;

            foreach (var socket in _sockets)
            {
                socket.Dispose();
            }
            _sockets = ImmutableArray<Socket>.Empty;
            _receiveTasks = ImmutableArray<Task>.Empty;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _controlChannel?.Writer.TryComplete();

            await _controlTask;

            _controlChannel = null;

            _outboundDispatcher?.Complete();
            _outboundDispatcher = null;

            await _outboundTask;

            Stop();
        }

        public async Task<PNetMeshChannel> ConnectToAsync(PNetMeshPeer peer, CancellationToken cancellationToken = default)
        {
            if (peer == null)
                throw new ArgumentNullException(nameof(peer));
            if (_localPeer.Equals(peer)) //maybe it should be possible
                throw new ArgumentException("remote peer required", nameof(peer));

            var cmd = new PNetMeshControlCommands.OpenChannel
            {
                Peer = peer,
                Result = new TaskCompletionSource<PNetMeshChannel>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            //todo if (cancellationToken.IsCancellationRequested)
            await using CancellationTokenRegistration reg = cancellationToken.Register(() => cmd.Result.TrySetCanceled(cancellationToken));

            await ControlChannel.Writer.WriteAsync(cmd, cancellationToken);

            return await cmd.Result.Task;
        }

        async Task ProcessControl(
            Channel<PNetMeshControlCommands.Command> controlChannel,
            ChannelWriter<PNetMeshOutboundMessages.Message> outboundWriter,
            PNetMeshSessionTable sessions,
            PNetMeshInboundDispatcher inboundDispatcher)
        {
            var reader = controlChannel.Reader;

            Dictionary<byte[], PNetMeshChannel> channels = new Dictionary<byte[], PNetMeshChannel>(PNetMeshByteArrayComparer.Default);
            var senderIndex = 0u;
            PNetMeshSession? session;

            var localAddresses = new HashSet<byte[]>(PNetMeshByteArrayComparer.Default);

            do
            {
                while (reader.TryRead(out var command))
                {
                    switch (command)
                    {
                        case PNetMeshControlCommands.Receive cmd:
                            {
                                if (!PNetMeshPacketFraming.TryReadMessageType(cmd.MemoryBuffer.Span, out var messageType))
                                    continue;

                                switch (messageType)
                                {
                                    case PNetMeshMessageType.PacketData:
                                        {
                                            //push to session
                                            var receiverIndex = _protocol.GetReceiverIndex(cmd.MemoryBuffer.Span);

                                            if (sessions.TryGet(receiverIndex, out session))
                                            {
                                                if (!session.SupportsDirectEndpointDiscovery)
                                                    ApplyLegacyEndpointUpdate(session, cmd);

                                                var read = false;
                                                try
                                                {
#if PNET_MESH_PACKET_TRACE
                                                    PNetMeshPacketTrace.SetUdpReceiveTimestamp(cmd.PacketTraceReceiveTimestamp);
                                                    PNetMeshPacketTrace.MarkInboundDirectStart();
#endif
                                                    read = session.TryReadMessage(cmd.MemoryBuffer.Span);
                                                }
                                                finally
                                                {
                                                    PNetMeshPacketTrace.ClearUdpReceiveTimestamp();
                                                }

                                                if (read && session.SupportsDirectEndpointDiscovery)
                                                {
                                                    ApplyAuthenticatedEndpointUpdate(session, cmd);
                                                }
                                            }
                                            else
                                            {
                                                //todo log unknown session
                                            }
                                        }
                                        break;
                                    case PNetMeshMessageType.HandshakeInitiation:
                                        {
                                            //create new sesssion
                                            session = new PNetMeshSession(_protocol, outboundWriter, _logger)
                                            {
                                                LocalEndPoint = cmd.LocalEndPoint,
                                                RemoteEndPoint = cmd.RelayCandidateEndPoint is null ? cmd.RemoteEndPoint : null
                                            };

                                            if (!session.TryReadInitialize(++senderIndex, cmd.MemoryBuffer.Span))
                                            {
                                                session.Dispose();
                                                break;
                                            }

                                            if (cmd.RelayCandidateEndPoint is not null)
                                            {
                                                if (session.SupportsDirectEndpointDiscovery)
                                                {
                                                    if (session.TryApplyDirectEndpointHint(cmd.RelayCandidateEndPoint, DateTimeOffset.UtcNow))
                                                    {
                                                        _logger.LogInformation(
                                                            "event=wireguard_endpoint_hint_queued session={sessionIndex} endpoint_id={endpointId}",
                                                            session.SenderIndex,
                                                            PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                                                    }
                                                    else
                                                    {
                                                        _logger.LogInformation(
                                                            "event=wireguard_endpoint_hint_ignored session={sessionIndex} endpoint_id={endpointId}",
                                                            session.SenderIndex,
                                                            PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                                                    }
                                                }
                                                else
                                                {
                                                    session.ConfirmRemoteEndpoint(cmd.RelayCandidateEndPoint);
                                                    _logger.LogInformation(
                                                        "event=relay_endpoint_confirmed session={sessionIndex} mode=legacy_hint endpoint_id={endpointId}",
                                                        session.SenderIndex,
                                                        PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                                                }
                                            }
                                            if (!channels.TryGetValue(session.RemoteAddress, out var channel))
                                            {
                                                channel = _channelFactory();

                                                channels.Add(session.RemoteAddress, channel);

                                                localAddresses.Add(session.LocalAddress);
                                            }

                                            if (channel.Timestamp < session.Timestamp)
                                            {
                                                channel.AddSession(session);

                                                sessions.Add(senderIndex, session);

                                                session.WriteResponse();
                                                Debug.Assert(session.RemoteEndPoint is null
                                                    ? session.Status == PNetMeshSessionStatus.Opening
                                                    : session.Status == PNetMeshSessionStatus.Open);

                                                if (session.RemoteEndPoint is not null)
                                                    _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
                                            }
                                            else
                                            {
                                                //ignore replayed message
                                                session.Dispose();
                                            }
                                        }
                                        break;
                                    case PNetMeshMessageType.HandshakeResponse:
                                        {
                                            //push to session
                                            var receiverIndex = _protocol.GetReceiverIndex(cmd.MemoryBuffer.Span);

                                            if (sessions.TryGet(receiverIndex, out session))
                                            {
                                                if (!session.SupportsDirectEndpointDiscovery)
                                                    ApplyLegacyEndpointUpdate(session, cmd);

                                                if (!session.TryReadResponse(cmd.MemoryBuffer.Span))
                                                    break;

                                                if (channels.TryGetValue(session.RemoteAddress, out var channel))
                                                {
                                                    if (session.SupportsDirectEndpointDiscovery)
                                                        ApplyAuthenticatedEndpointUpdate(session, cmd);

                                                    localAddresses.Add(session.LocalAddress);
                                                }
                                                else
                                                {
                                                    _logger.LogDebug("channel not found");
                                                }
                                            }
                                            else
                                            {
                                                _logger.LogDebug("unknown session");
                                            }
                                        }
                                        break;
                                    case PNetMeshMessageType.PacketCookieReply:
                                        //todo
                                        break;
                                    default:
                                        //ignore unknown
                                        break;
                                }

                                //free memory
                                cmd.MemoryOwner?.Dispose();
                            }
                            break;
                        case PNetMeshControlCommands.OpenChannel cmd:
                            {
                                byte[] address = new byte[10];
                                PNetMeshUtils.GetAddressFromPublicKey(cmd.Peer.PublicKey, address);

                                if (!channels.TryGetValue(address, out var channel))
                                {
                                    channel = _channelFactory();

                                    channels.Add(address, channel);

                                    IEnumerable<EndPoint> endpoints = Array.Empty<EndPoint>();

                                    //maybe concat wellknown peers and routing endpoints

                                    if (cmd.Peer.EndPoints?.Length == 0)
                                    {
                                        if (_router.TryGetEntry(address, out var entry) && entry.EndPoint != null)
                                        {
                                            //todo multi endpoint routing
                                            endpoints = new[] { entry.EndPoint };
                                        }
                                        else
                                        {
                                            //maybe endpoints = new[] { null };

                                            //no endpoint
                                            session = new PNetMeshSession(_protocol, outboundWriter, _logger)
                                            {
                                                RemoteEndPoint = null //broadcast
                                            };

                                            session.WriteInitialize(++senderIndex, cmd.Peer.PublicKey);
                                            sessions.Add(senderIndex, session);

                                            channel.AddSession(session);

                                            localAddresses.Add(session.LocalAddress);
                                        }
                                    }
                                    else if (cmd.Peer.EndPoints?.Length > 0)
                                    {
                                        endpoints = cmd.Peer.EndPoints
                                            .Select(n => PNetMeshUtils.ParseEndPoint(n));
                                    }

                                    endpoints = PNetMeshRouter.ResolveRemoteEndPoints(endpoints);

                                    //todo check multi session support
                                    foreach (var ep in endpoints)
                                    {
                                        //create new sesssion
                                        session = new PNetMeshSession(_protocol, outboundWriter, _logger)
                                        {
                                            //LocalEndPoint = cmd.LocalEndPoint,
                                            RemoteEndPoint = ep
                                        };

                                        session.WriteInitialize(++senderIndex, cmd.Peer.PublicKey);
                                        sessions.Add(senderIndex, session);

                                        channel.AddSession(session);

                                        localAddresses.Add(session.LocalAddress);
                                    }
                                }

                                if (!cmd.Result.TrySetResult(channel))
                                {
                                    _logger.LogError("open channel timeout");
                                }
                            }
                            break;
                        case PNetMeshControlCommands.RelayPacket { Packet: var packet } cmd:
                            {
                                if (!_router.TryAcceptRelayPacket(packet))
                                {
                                    //ignore duplicate
                                    break;
                                }

                                //maybe push ice candidates into router entry

                                var routeDecision = _router.SelectRelayRoute(packet, localAddresses, channels);

                                switch (routeDecision.Kind)
                                {
                                    case PNetMeshRelayRouteKind.Local:
                                        {
                                            //var localEndPoint = default(EndPoint);
                                            var remoteAddress = routeDecision.LocalRemoteAddress
                                                ?? throw new InvalidOperationException("Local relay route did not include a remote address.");
                                            var remoteEndPoint = routeDecision.RelayCandidateEndPoint;

                                            if (!channels.TryGetValue(remoteAddress, out var channel))
                                            {
                                                channel = _channelFactory();

                                                channels.Add(remoteAddress, channel);
                                            }

                                            //deliver local
                                            var receive = new PNetMeshControlCommands.Receive
                                            {
                                                MemoryOwner = cmd.MemoryOwner,
                                                LocalEndPoint = null,
                                                RemoteEndPoint = null,
                                                RelayCandidateEndPoint = remoteEndPoint,
                                                MemoryBuffer = packet.Payload
                                            };

                                            if (!inboundDispatcher.TryHandleDirect(receive)
                                                && !controlChannel.Writer.TryWrite(receive))
                                            {
                                                cmd.MemoryOwner?.Dispose();
                                                //todo better error message
                                                cmd.Result?.SetException(new Exception("control channel write error"));
                                            }
                                            else
                                            {
                                                try
                                                {
                                                    channel.TryWriteRoutePath(PNetMeshRoutePath.FromRelayPacket(packet, remoteEndPoint));
                                                    _logger.LogDebug(
                                                        "event=relay_packet_delivered destination_id={destinationId} source_id={sourceId} route_length={routeLength} candidate_endpoint_id={candidateEndpointId}",
                                                        PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                        PNetMeshDiagnosticRedactor.AddressId(remoteAddress),
                                                        packet.Route.Length,
                                                        PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint));
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogDebug(ex, "route path diagnostic write failed");
                                                }

                                                cmd.Result?.SetResult();
                                            }

                                            break;
                                        }
                                    case PNetMeshRelayRouteKind.Direct:
                                        {
                                            var channel = routeDecision.DirectChannel
                                                ?? throw new InvalidOperationException("Direct relay route did not include a channel.");

                                            //relay to known peer
                                            if (cmd.Result != null)
                                            {
                                                _logger.LogDebug(
                                                    "event=relay_packet_forward_queued destination_id={destinationId} route_length={routeLength} mode=direct",
                                                    PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                    packet.Route.Length);
                                                _ = channel.RelayAsync(packet, cmd.MemoryOwner)
                                                    .ContinueWith(_ => cmd.Result.SetResult());
                                            }
                                            else
                                            {
                                                if (!channel.TryRelay(packet, cmd.MemoryOwner))
                                                {
                                                    _logger.LogDebug(
                                                        "event=relay_packet_forward_rejected destination_id={destinationId} route_length={routeLength} mode=direct",
                                                        PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                        packet.Route.Length);
                                                }
                                            }

                                            break;
                                        }
                                    case PNetMeshRelayRouteKind.Fanout:
                                        {
                                            //relay to unknown address
                                            var tasks = new List<Task>(routeDecision.FanoutChannels.Count);
                                            foreach (var channel in routeDecision.FanoutChannels)
                                                tasks.Add(channel.RelayAsync(packet));

                                            _logger.LogDebug(
                                                "event=relay_packet_fanout_queued destination_id={destinationId} source_id={sourceId} route_length={routeLength} fanout_count={fanoutCount}",
                                                PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                PNetMeshDiagnosticRedactor.AddressId(packet.Route[0]),
                                                packet.Route.Length,
                                                tasks.Count);
                                            _ = Task.WhenAll(tasks).ContinueWith(t =>
                                            {
                                                cmd.MemoryOwner?.Dispose();

                                                if (t.IsFaulted)
                                                {
                                                    //maybe only log error
                                                    if (cmd.Result != null)
                                                        cmd.Result?.SetException(t.Exception);
                                                    else
                                                        _logger.LogError(t.Exception, "relay error");
                                                }
                                                else
                                                    cmd.Result?.SetResult();
                                            });

                                            break;
                                        }
                                    case PNetMeshRelayRouteKind.RouteMissed:
                                        {
                                            _logger.LogDebug(
                                                "event=relay_packet_route_missed destination_id={destinationId} source_id={sourceId} route_length={routeLength}",
                                                PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                PNetMeshDiagnosticRedactor.AddressId(packet.Route[0]),
                                                packet.Route.Length);
                                            cmd.MemoryOwner?.Dispose();
                                            //todo better error message
                                            cmd.Result?.SetException(new InvalidOperationException("route not found"));
                                            break;
                                        }
                                    case PNetMeshRelayRouteKind.HopLimitExceeded:
                                        {
                                            //ignore unknown peer

                                            _logger.LogDebug(
                                                "event=relay_packet_dropped destination_id={destinationId} route_length={routeLength} reason=hop_limit",
                                                PNetMeshDiagnosticRedactor.AddressId(packet.Address),
                                                packet.Route.Length);
                                            cmd.MemoryOwner?.Dispose();
                                            //todo better error message
                                            cmd.Result?.SetException(new InvalidOperationException("route not found"));
                                            break;
                                        }
                                    default:
                                        throw new InvalidOperationException("Unknown relay route decision.");
                                }
                            }
                            break;
                        default:
                            //todo log unknown
                            break;
                    }
                }

                sessions.DisposeClosedSessions();
            }
            while (await reader.WaitToReadAsync());

            _logger.LogDebug("control process completed");
        }

        async Task ProcessOutbound(ChannelReader<PNetMeshOutboundMessages.Message> outboundReader)
        {
            var reader = outboundReader;

            var sendChannel = Channel.CreateUnbounded<SocketAsyncEventArgs>(new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true
            });

            var sendItem = new PNetMeshSocketSendWorkItem
            {
                Writer = sendChannel.Writer
            };

            var args = new SocketAsyncEventArgs();
            args.Completed += ProcessSend;
            args.UserToken = sendItem;

            var relay_number = (ulong)(DateTime.UtcNow - new DateTime(2020, 01, 01)).TotalSeconds;
            relay_number *= 100;

            do
            {
                while (reader.TryRead(out var message))
                {
                    switch (message)
                    {
                        case PNetMeshOutboundMessages.Packet msg when msg.RemoteEndPoint != null:
                            {
                                Socket? socket = null;

                                if (msg.LocalEndPoint != null)
                                    foreach (var s in _sockets)
                                    {
                                        if (s.LocalEndPoint == msg.LocalEndPoint)
                                        {
                                            socket = s;
                                            break;
                                        }
                                    }

                                if (socket is null && _sockets.Length > 0)
                                    socket = _sockets[0]; //todo GetBestInterface Or use from all

                                if (socket != null)
                                {
                                    sendItem.MemoryOwner = msg.MemoryOwner;
                                    args.RemoteEndPoint = msg.RemoteEndPoint;
                                    args.SetBuffer(msg.MemoryBuffer);

                                    if (socket.SendToAsync(args))
                                        args = await sendChannel.Reader.ReadAsync();
                                    else
                                        ProcessSend(socket, args);
                                }
                                else
                                {
                                    //log socket not found
                                    _logger.LogDebug("socket not found");
                                }
                            }
                            break;
                        case PNetMeshOutboundMessages.Packet msg:
                            {
                                if (msg.RemoteAddress is null || msg.LocalAddress is null)
                                {
                                    _logger.LogDebug("event=relay_packet_enqueue_failed reason=missing_address");
                                    msg.MemoryOwner?.Dispose();
                                    break;
                                }

                                //todo prealloc candidates
                                var candidates = ImmutableArray.CreateBuilder<PNetMeshCandidate>();

                                for (int i = 0; i < _sockets.Length; i++)
                                {
                                    var socket = _sockets[i];

                                    //maybe case of DnsEndPoint not required 
                                    var name = socket.LocalEndPoint switch
                                    {
                                        IPEndPoint ipEP => ipEP.Address.ToString(),
                                        DnsEndPoint dnsEP => dnsEP.Host,
                                        _ => "unknown"
                                    };

                                    candidates.Add(new PNetMeshCandidate
                                    {
                                        Address = socket.LocalEndPoint,
                                        Base = socket.LocalEndPoint,
                                        ComponentId = 1,
                                        Foundation = $"host-{name}-udp",
                                        Protocol = PNetMeshProtocolType.UDP,
                                        Type = PNetMeshCandidateType.Host,
                                        Priority = (uint)(1000 + i) //tmp
                                    });
                                }

                                //relay packet to address
                                var cmd = new PNetMeshControlCommands.RelayPacket
                                {
                                    Packet = new PNetMeshRelayPacket
                                    {
                                        Address = msg.RemoteAddress,
                                        SeqNumber = ++relay_number,
                                        HopCount = 3, //todo setting
                                        Route = ImmutableArray.Create<byte[]>(msg.LocalAddress),
                                        Payload = msg.MemoryBuffer,
                                        CandidateExchange = new PNetMeshCandidateExchange
                                        {
                                            Candidates = candidates.ToImmutable()
                                        }
                                    },
                                    MemoryOwner = msg.MemoryOwner,
                                    Result = null
                                };

                                var controlWriter = _controlChannel?.Writer;
                                if (controlWriter is null || !controlWriter.TryWrite(cmd))
                                {
                                    _logger.LogDebug(
                                        "event=relay_packet_enqueue_failed destination_id={destinationId}",
                                        PNetMeshDiagnosticRedactor.AddressId(msg.RemoteAddress));

                                    msg.MemoryOwner?.Dispose();
                                }
                            }
                            break;
                        case PNetMeshOutboundMessages.Relay msg:
                            {
                                var cmd = new PNetMeshControlCommands.RelayPacket
                                {
                                    Packet = msg.Packet,
                                    MemoryOwner = null,
                                    Result = null
                                };

                                var controlWriter = _controlChannel?.Writer;
                                if (controlWriter is null || !controlWriter.TryWrite(cmd))
                                {
                                    _logger.LogDebug(
                                        "event=relay_packet_enqueue_failed destination_id={destinationId}",
                                        PNetMeshDiagnosticRedactor.AddressId(msg.Packet.Address));

                                    //msg.MemoryOwner?.Dispose();
                                }
                            }
                            break;
                    }
                }
            }
            while (await reader.WaitToReadAsync());

            _logger.LogDebug("outbound process completed");
        }

        void ApplyAuthenticatedEndpointUpdate(PNetMeshSession session, PNetMeshControlCommands.Receive cmd)
        {
            _endpointUpdater.ApplyAuthenticatedEndpointUpdate(session, cmd);
        }

        void ApplyLegacyEndpointUpdate(PNetMeshSession session, PNetMeshControlCommands.Receive cmd)
        {
            _endpointUpdater.ApplyLegacyEndpointUpdate(session, cmd);
        }

        internal static PNetMeshControlCommands.Receive CreateReceiveCommand(
            IMemoryOwner<byte>? memoryOwner,
            EndPoint? localEndPoint,
            EndPoint? remoteEndPoint,
            ReadOnlyMemory<byte> memoryBuffer)
        {
            return new PNetMeshControlCommands.Receive
            {
                MemoryOwner = memoryOwner,
                LocalEndPoint = localEndPoint,
                RemoteEndPoint = PNetMeshEndpointUpdater.NormalizeRemoteEndPoint(remoteEndPoint),
                MemoryBuffer = memoryBuffer
            };
        }

        static void ProcessSend(object? sender, SocketAsyncEventArgs args)
        {
            if (args.UserToken is not PNetMeshSocketSendWorkItem item)
                return;

            item.MemoryOwner?.Dispose();
            item.Writer.TryWrite(args);
        }

        static bool IsBlockingUdpReceiveEnabled()
        {
            var mode = Environment.GetEnvironmentVariable("PNET_MESH_UDP_RECEIVE_MODE");
            if (string.IsNullOrWhiteSpace(mode))
                return false;
            if (string.Equals(mode, "async", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(mode, "blocking", StringComparison.OrdinalIgnoreCase))
                return true;

            throw new InvalidOperationException("PNET_MESH_UDP_RECEIVE_MODE must be either 'async' or 'blocking'.");
        }

        internal static int? GetUdpSocketBufferBytes(PNetMeshServerSettings settings)
        {
            return GetUdpSocketBufferBytes(settings, Environment.GetEnvironmentVariable("PNET_MESH_UDP_SOCKET_BUFFER_BYTES"));
        }

        internal static int? GetUdpSocketBufferBytes(PNetMeshServerSettings settings, string? environmentValue)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.UdpSocketBufferBytes is { } bufferBytes)
            {
                if (bufferBytes <= 0)
                    throw new InvalidOperationException($"{nameof(PNetMeshServerSettings.UdpSocketBufferBytes)} must be a positive integer.");

                return bufferBytes;
            }

            return GetUdpSocketBufferBytesFromEnvironment(environmentValue);
        }

        internal static int? GetUdpSocketBufferBytesFromEnvironment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                || bytes <= 0)
            {
                throw new InvalidOperationException("PNET_MESH_UDP_SOCKET_BUFFER_BYTES must be a positive integer.");
            }

            return bytes;
        }

        static void ApplyUdpSocketBufferSize(Socket socket, int? bufferBytes)
        {
            if (bufferBytes is not { } bytes)
                return;

            socket.ReceiveBufferSize = bytes;
            socket.SendBufferSize = bytes;
        }

        static EndPoint CreateReceiveRemoteEndPoint(Socket socket)
        {
            return socket.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);
        }

        static bool IsSocketClosed(SocketError error)
        {
            return error is SocketError.Interrupted
                   or SocketError.NotSocket
                   or SocketError.OperationAborted
                   or SocketError.Shutdown;
        }

        static void ProcessReceiveLoop(Socket socket, PNetMeshSocketReceiveWorkItem item)
        {
            var remoteEndPoint = CreateReceiveRemoteEndPoint(socket);
            while (true)
            {
                try
                {
                    var socketFlags = SocketFlags.None;
                    var bytesRead = socket.ReceiveMessageFrom(
                        item.MemoryOwner.Memory.Span,
                        ref socketFlags,
                        ref remoteEndPoint,
                        out _);
#if PNET_MESH_PACKET_TRACE
                    var packetTraceReceiveTimestamp = Stopwatch.GetTimestamp();
#endif
                    if (socketFlags.HasFlag(SocketFlags.Truncated))
                        continue;

                    var data = item.MemoryOwner.Memory.Span.Slice(0, bytesRead);
                    if (!ShouldAcceptReceivedPacket(socket, item, remoteEndPoint, data))
                        continue;

                    if (!TryGetReceiveLocalEndPoint(socket, item.MemoryOwner, out var localEndPoint))
                        return;

                    var cmd = CreateReceiveCommand(
                        item.MemoryOwner,
                        localEndPoint,
                        remoteEndPoint,
                        item.MemoryOwner.Memory.Slice(0, bytesRead));
#if PNET_MESH_PACKET_TRACE
                    cmd.PacketTraceReceiveTimestamp = packetTraceReceiveTimestamp;
#endif

                    item.InboundDispatcher.Dispatch(cmd);
                    item.MemoryOwner = MemoryPool<byte>.Shared.Rent(ushort.MaxValue);
                }
                catch (ObjectDisposedException)
                {
                    item.MemoryOwner.Dispose();
                    return;
                }
                catch (SocketException ex) when (IsSocketClosed(ex.SocketErrorCode))
                {
                    item.MemoryOwner.Dispose();
                    return;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                {
                }
                catch
                {
                    item.MemoryOwner.Dispose();
                    throw;
                }
            }
        }

        static void ProcessReceive(object? sender, SocketAsyncEventArgs args)
        {
            //receive data from any socket

            if (sender is not Socket socket || args.UserToken is not PNetMeshSocketReceiveWorkItem item)
                return;

            while (true)
            {
                //args.SocketFlags == SocketFlags.Truncated
                //args.SocketError == SocketError.MessageSize
                //args.SocketError == SocketError.NoBufferSpaceAvailable
                //args.SocketError == SocketError.Success

                if (args.SocketError == SocketError.Success)
                {
#if PNET_MESH_PACKET_TRACE
                    var packetTraceReceiveTimestamp = Stopwatch.GetTimestamp();
#endif
                    //read packet
                    var data = args.MemoryBuffer.Span.Slice(args.Offset, args.BytesTransferred);
                    if (ShouldAcceptReceivedPacket(socket, item, args.RemoteEndPoint, data))
                    {
                        if (!TryGetReceiveLocalEndPoint(socket, item.MemoryOwner, out var localEndPoint))
                            return;

                        //try write packet to channel
                        var cmd = CreateReceiveCommand(
                            item.MemoryOwner,
                            localEndPoint,
                            args.RemoteEndPoint,
                            args.MemoryBuffer.Slice(args.Offset, args.BytesTransferred));
#if PNET_MESH_PACKET_TRACE
                        cmd.PacketTraceReceiveTimestamp = packetTraceReceiveTimestamp;
#endif

                        item.InboundDispatcher.Dispatch(cmd);

                        //set new buffer
                        item.MemoryOwner = MemoryPool<byte>.Shared.Rent(ushort.MaxValue);
                        args.SetBuffer(item.MemoryOwner.Memory);
                    }
                    else
                    {
                        //ignore invalid datagram
                        ;
                    }
                }
                else if (args.SocketError == SocketError.MessageSize)
                {
                    //todo jumbo ipv6 datagrams
                }
                else
                {
                    //log socket error
                }
                try
                {
                    if (socket.ReceiveMessageFromAsync(args))
                        break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        internal static bool TryGetReceiveLocalEndPoint(
            Socket socket,
            IMemoryOwner<byte> memoryOwner,
            out EndPoint? localEndPoint)
        {
            try
            {
                localEndPoint = socket.LocalEndPoint;
                return true;
            }
            catch (ObjectDisposedException)
            {
                memoryOwner.Dispose();
                localEndPoint = null;
                return false;
            }
        }

        static bool ShouldAcceptReceivedPacket(
            Socket socket,
            PNetMeshSocketReceiveWorkItem item,
            EndPoint? remoteEndPoint,
            ReadOnlySpan<byte> data)
        {
            if (PNetMeshPacketFraming.TryReadMessageType(data, out var messageType)
                && (messageType == PNetMeshMessageType.HandshakeInitiation
                    || messageType == PNetMeshMessageType.HandshakeResponse))
            {
                if (remoteEndPoint == null)
                    return false;

                var result = item.Protocol.CookieGate.EvaluateHandshake(
                    data,
                    remoteEndPoint,
                    DateTimeOffset.UtcNow,
                    item.Protocol.RequireCookieMac2);
                if (result == PNetMeshCookieGateResult.CookieRequired)
                    TrySendCookieReply(socket, item.Protocol, remoteEndPoint, data);
                item.Logger?.LogDebug(
                    "event=wireguard_cookie_gate result={result} message_type={messageType} endpoint_id={endpointId}",
                    result,
                    messageType,
                    PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint));
                return result == PNetMeshCookieGateResult.Accepted;
            }

            return item.Protocol.ValidatePacket(data);
        }

        static void TrySendCookieReply(
            Socket socket,
            PNetMeshProtocol protocol,
            EndPoint remoteEndPoint,
            ReadOnlySpan<byte> triggeringPacket)
        {
            if (remoteEndPoint == null)
                return;

            Span<byte> reply = stackalloc byte[PNetMeshPacketFraming.CookieReplyMessageSize];
            if (!protocol.CookieGate.TryWriteCookieReply(triggeringPacket, remoteEndPoint, DateTimeOffset.UtcNow, reply, out var bytesWritten))
                return;

            try
            {
                socket.SendTo(reply[..bytesWritten], SocketFlags.None, remoteEndPoint);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
