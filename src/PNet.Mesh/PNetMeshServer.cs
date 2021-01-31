using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Actor.Mesh
{
    public sealed class PNetMeshServerSettings
    {
        public byte[] PublicKey { get; init; }

        public byte[] PrivateKey { get; init; }

        public byte[] Psk { get; init; }

        public string[] BindTo { get; init; }

        public PNetMeshPeer[] Peers { get; init; }
    }

    public sealed class PNetMeshServer : IDisposable
    {
        readonly PNetMeshServerSettings _settings;

        readonly PNetMeshProtocol _protocol;

        readonly PNetMeshRouter _router;

        readonly ILogger _logger;

        readonly PNetMeshPeer _localPeer;

        Channel<PNetMeshOutboundMessages.Message> _outboundChannel;
        Task _outboundTask = Task.CompletedTask;

        Channel<PNetMeshControlCommands.Command> _controlChannel;
        Task _controlTask = Task.CompletedTask;

        ImmutableArray<Socket> _sockets = ImmutableArray<Socket>.Empty;

        public PNetMeshServer(PNetMeshServerSettings settings, ILogger<PNetMeshServer> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            //todo gen key pair

            _localPeer = new PNetMeshPeer
            {
                PublicKey = settings.PublicKey,
                EndPoints = settings.BindTo //todo lookup
            };

            _protocol = new PNetMeshProtocol(settings.PrivateKey, settings.PublicKey, settings.Psk);
            _logger = logger;

            _router = new PNetMeshRouter();
            var address = new byte[10];

            if (settings.Peers != null)
                foreach (var entry in settings.Peers)
                {
                    PNetMeshUtils.GetAddressFromPublicKey(entry.PublicKey, address);

                    foreach (var endpoint in entry.EndPoints)
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
            _outboundChannel = Channel.CreateBounded<PNetMeshOutboundMessages.Message>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _controlChannel = Channel.CreateBounded<PNetMeshControlCommands.Command>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _outboundTask = Task.Run(ProcessOutbound);
            _controlTask = Task.Run(ProcessControl);

            var sockets = ImmutableArray.CreateBuilder<Socket>();

            foreach (var bind in _settings.BindTo)
            {
                var ep = IPEndPoint.Parse(bind);
                //todo any endpoint parsing

                var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
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
                    Writer = _controlChannel.Writer
                };
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
        }

        public void Stop()
        {
            _outboundChannel?.Writer.Complete();
            _outboundChannel = null;

            _controlChannel?.Writer.Complete();
            _controlChannel = null;

            foreach (var socket in _sockets)
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            _sockets = ImmutableArray<Socket>.Empty;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _controlChannel?.Writer.Complete();

            await _controlTask;

            _controlChannel = null;

            _outboundChannel?.Writer.Complete();
            _outboundChannel = null;

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
            await using CancellationTokenRegistration reg = cancellationToken.Register(state => ((TaskCompletionSource<PNetMeshChannel>)state).TrySetCanceled(), cmd.Result);

            await _controlChannel.Writer.WriteAsync(cmd, cancellationToken);

            return await cmd.Result.Task;
        }

        async Task ProcessControl()
        {
            var reader = _controlChannel.Reader;

            Dictionary<byte[], PNetMeshChannel> channels = new Dictionary<byte[], PNetMeshChannel>(PNetMeshByteArrayComparer.Default);
            var sessions = new Dictionary<uint, PNetMeshSession>();
            var senderIndex = 0u;
            PNetMeshSession session;

            var localAddresses = new HashSet<byte[]>(PNetMeshByteArrayComparer.Default);

            do
            {
                while (reader.TryRead(out var command))
                {
                    switch (command)
                    {
                        case PNetMeshControlCommands.Receive cmd:
                            {
                                switch ((PNetMeshMessageType)cmd.MemoryBuffer.Span[0])
                                {
                                    case PNetMeshMessageType.PacketData:
                                        {
                                            //push to session
                                            var receiverIndex = _protocol.GetReceiverIndex(cmd.MemoryBuffer.Span);

                                            if (sessions.TryGetValue(receiverIndex, out session))
                                            {
                                                if (session.LocalEndPoint != cmd.LocalEndPoint)
                                                    session.LocalEndPoint = cmd.LocalEndPoint;
                                                if (session.RemoteEndPoint != cmd.RemoteEndPoint)
                                                {
                                                    session.RemoteEndPoint = cmd.RemoteEndPoint;

                                                    _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
                                                }

                                                session.ReadMessage(cmd.MemoryBuffer.Span);
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
                                            session = new PNetMeshSession(_protocol, _outboundChannel.Writer)
                                            {
                                                LocalEndPoint = cmd.LocalEndPoint,
                                                RemoteEndPoint = cmd.RemoteEndPoint
                                            };

                                            session.ReadInitialize(++senderIndex, cmd.MemoryBuffer.Span);

                                            if (!channels.TryGetValue(session.RemoteAddress, out var channel))
                                            {
                                                channel = new PNetMeshChannel
                                                {

                                                };
                                                channels.Add(session.RemoteAddress, channel);

                                                localAddresses.Add(session.LocalAddress);
                                            }

                                            if (channel.Timestamp < session.Timestamp)
                                            {
                                                channel.AddSession(session);

                                                sessions.Add(senderIndex, session);

                                                session.WriteResponse();
                                                Debug.Assert(session.Status == PNetMeshSessionStatus.Opening);

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

                                            if (sessions.TryGetValue(receiverIndex, out session))
                                            {
                                                session.LocalEndPoint = cmd.LocalEndPoint;
                                                session.RemoteEndPoint = cmd.RemoteEndPoint;

                                                session.ReadResponse(cmd.MemoryBuffer.Span);
                                                Debug.Assert(session.Status == PNetMeshSessionStatus.Open);

                                                if (channels.TryGetValue(session.RemoteAddress, out var channel))
                                                {
                                                    _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);

                                                    localAddresses.Add(session.LocalAddress);
                                                }
                                                else
                                                {
                                                    //log channel not found
                                                }
                                            }
                                            else
                                            {
                                                //todo log unknown session
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
                                    channel = new PNetMeshChannel
                                    {
                                        //RemotePublicKey = cmd.Peer.PublicKey
                                    };
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
                                            session = new PNetMeshSession(_protocol, _outboundChannel.Writer)
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
                                        endpoints = cmd.Peer.EndPoints.Select(n => PNetMeshUtils.ParseEndPoint(n));
                                    }

                                    //todo check multi session support
                                    foreach (var ep in endpoints)
                                    {
                                        //create new sesssion
                                        session = new PNetMeshSession(_protocol, _outboundChannel.Writer)
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
                                    //log open channel timeout
                                    ;
                                }
                            }
                            break;
                        case PNetMeshControlCommands.RelayPacket { Packet: var packet } cmd:
                            {
                                if (packet.Route.Length > 1)
                                {
                                    //deduplicate packet with remoteAddress+seqNumber
                                    var remoteAddress = packet.Route[0];

                                    var entry = _router.GetOrCreateEntry(remoteAddress);
                                    entry.Tracker ??= new PNetMeshPacketTracker(200);

                                    if (!entry.Tracker.TryAdd(packet.SeqNumber))
                                    {
                                        //ignore duplicat
                                        break;
                                    }
                                }

                                //maybe push ice candidates into router entry

                                if (localAddresses.Contains(packet.Address))
                                {
                                    //var localEndPoint = default(EndPoint);
                                    var remoteAddress = packet.Route[0];
                                    var remoteEndPoint = default(EndPoint);

                                    if (!channels.TryGetValue(remoteAddress, out var channel))
                                    {
                                        channel = new PNetMeshChannel
                                        {
                                        };

                                        channels.Add(remoteAddress, channel);
                                    }

                                    //temp till ICE agent
                                    {
                                        var candidates = packet.CandidateExchange.Candidates;

                                        if (_router.TryGetEntry(remoteAddress, out var entry)
                                            && entry.EndPoint != null
                                            && candidates.Any(n => n.Address.Equals(entry.EndPoint)))
                                        {
                                            remoteEndPoint = entry.EndPoint;
                                        }
                                        else
                                        {
                                            //todo select neighbor 
                                            remoteEndPoint = candidates.FirstOrDefault()?.Address;
                                        }
                                    }

                                    //deliver local
                                    var receive = new PNetMeshControlCommands.Receive
                                    {
                                        MemoryOwner = cmd.MemoryOwner,
                                        LocalEndPoint = null,
                                        RemoteEndPoint = remoteEndPoint,
                                        MemoryBuffer = packet.Payload
                                    };

                                    if (!_controlChannel.Writer.TryWrite(receive))
                                    {
                                        cmd.MemoryOwner?.Dispose();
                                        //todo better error message
                                        cmd.Result?.SetException(new Exception("control channel write error"));
                                    }
                                    else
                                    {
                                        cmd.Result?.SetResult();
                                    }
                                }
                                else if (channels.TryGetValue(packet.Address, out var channel) && channel.IsOpen)
                                {
                                    //relay to known peer
                                    if (cmd.Result != null)
                                    {
                                        _ = channel.RelayAsync(packet, cmd.MemoryOwner)
                                            .ContinueWith(_ => cmd.Result.SetResult());
                                    }
                                    else
                                    {
                                        if (!channel.TryRelay(packet, cmd.MemoryOwner))
                                        {
                                            //todo log warning
                                        }
                                    }
                                }
                                else if (packet.HopCount > 0)
                                {
                                    //relay to unknown address

                                    var rset = new HashSet<byte[]>(packet.Route, PNetMeshByteArrayComparer.Default);
                                    var tasks = new List<Task>(channels.Count);
                                    foreach (var (k, c) in channels)
                                    {
                                        //do not relay to peers in route
                                        //todo filter opening routes 
                                        if (!rset.Contains(k))
                                            tasks.Add(c.RelayAsync(packet));
                                    }

                                    if (tasks.Count > 0)
                                    {
                                        _ = Task.WhenAll(tasks).ContinueWith(_ =>
                                        {
                                            cmd.MemoryOwner?.Dispose();
                                            cmd.Result?.SetResult();
                                        });
                                    }
                                    else
                                    {
                                        cmd.MemoryOwner?.Dispose();
                                        //todo better error message
                                        cmd.Result?.SetException(new InvalidOperationException("route not found"));
                                    }
                                }
                                else
                                {
                                    //ignore unknown peer

                                    cmd.MemoryOwner?.Dispose();
                                    //todo better error message
                                    cmd.Result?.SetException(new InvalidOperationException("route not found"));
                                }
                            }
                            break;
                        default:
                            //todo log unknown
                            break;
                    }
                }

                //session cleanup
                List<uint> closedSessions = null;
                foreach (var entry in sessions)
                {
                    if (entry.Value.Status == PNetMeshSessionStatus.Closed)
                    {
                        //maybe add age filter
                        entry.Value.Dispose();

                        closedSessions ??= new List<uint>();
                        closedSessions.Add(entry.Key);
                    }
                }
                if (closedSessions != null)
                    foreach (var id in closedSessions)
                        sessions.Remove(id);
            }
            while (await reader.WaitToReadAsync());

            _logger.LogDebug("control process completed");
        }

        async Task ProcessOutbound()
        {
            var reader = _outboundChannel.Reader;

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
                                Socket socket = null;

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
                                //todo prealloc candidates
                                var candidates = ImmutableArray.CreateBuilder<PNetMeshCandidate>();

                                for (int i = 0; i < _sockets.Length; i++)
                                {
                                    var socket = _sockets[i];

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

                                if (!_controlChannel.Writer.TryWrite(cmd))
                                {
                                    //log warning

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

                                if (!_controlChannel.Writer.TryWrite(cmd))
                                {
                                    //log warning

                                    //msg.MemoryOwner?.Dispose();
                                }
                            }
                            break;
                    }
                }
            }
            while (await reader.WaitToReadAsync());

            sendChannel.Writer.Complete();

            _logger.LogDebug("outbound process completed");
        }

        static void ProcessSend(object sender, SocketAsyncEventArgs args)
        {
            //var socket = sender as Socket;
            var item = args.UserToken as PNetMeshSocketSendWorkItem;

            if (args.SocketError != SocketError.Success)
            {
                //todo log error
                //_logger.LogError("socket error");
            }

            item.MemoryOwner?.Dispose();
            item.Writer.TryWrite(args);
        }

        static void ProcessReceive(object sender, SocketAsyncEventArgs args)
        {
            //receive data from any socket

            var socket = sender as Socket;
            var item = args.UserToken as PNetMeshSocketReceiveWorkItem;

            do
            {
                //args.SocketFlags == SocketFlags.Truncated
                //args.SocketError == SocketError.MessageSize
                //args.SocketError == SocketError.NoBufferSpaceAvailable
                //args.SocketError == SocketError.Success

                if (args.SocketError == SocketError.Success)
                {
                    //read packet
                    var data = args.MemoryBuffer.Span.Slice(args.Offset, args.BytesTransferred);
                    if (item.Protocol.ValidatePacket(data))
                    {
                        //try write packet to channel
                        var cmd = new PNetMeshControlCommands.Receive
                        {
                            MemoryOwner = item.MemoryOwner,
                            LocalEndPoint = socket.LocalEndPoint,
                            RemoteEndPoint = args.RemoteEndPoint,
                            MemoryBuffer = args.MemoryBuffer.Slice(args.Offset, args.BytesTransferred)
                        };

                        if (!item.Writer.TryWrite(cmd))
                        {
                            //full channel

                            //todo no cookie => send cookie reply
                            //todo valid cookie => push
                        }

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
            }
            while (!socket.ReceiveMessageFromAsync(args));
        }
    }

    internal sealed class PNetMeshSocketReceiveWorkItem
    {
        public PNetMeshProtocol Protocol { get; set; }

        public IMemoryOwner<byte> MemoryOwner { get; set; }

        public ChannelWriter<PNetMeshControlCommands.Command> Writer { get; set; }
    }

    internal sealed class PNetMeshSocketSendWorkItem
    {
        public EndPoint RemoteEndPoint { get; set; }

        public IMemoryOwner<byte> MemoryOwner { get; set; }

        public Memory<byte> MemoryBuffer { get; set; }

        public ChannelWriter<SocketAsyncEventArgs> Writer { get; set; }
    }



    public sealed class PNetMeshRelayPacket
    {
        /// <summary>
        /// destination address (hashed)
        /// not-set indicate a broadcast
        /// </summary>
        public byte[] Address { get; init; }

        /// <summary>
        /// senders relay sequence number
        /// duplication detection over sender + seq_number
        /// </summary>
        public ulong SeqNumber { get; init; }

        /// <summary>
        /// decrementing hop count
        /// at zero the receiving node should only relay to a known peer
        /// local to remote to known-remote: 0
	    /// local to remote to remote to known-remote: 1 
	    /// local to cluster: 0
	    /// local to cluster to known-remote: 1
	    /// local to cluster1 to cluster2 to known-remote: 2
	    /// broadcast to everyone: 3-4
        /// </summary>
        public ushort HopCount { get; init; }

        /// <summary>
        /// route of relay starting with sender 
        /// </summary>
        public ImmutableArray<byte[]> Route { get; init; }

        public ReadOnlyMemory<byte> Payload { get; init; }


        public PNetMeshCandidateExchange CandidateExchange { get; init; }
    }

    internal static class PNetMeshControlCommands
    {
        public abstract class Command
        {
        }

        public sealed class Receive : Command
        {
            public IMemoryOwner<byte> MemoryOwner { get; set; }

            public EndPoint RemoteEndPoint { get; set; }

            public EndPoint LocalEndPoint { get; set; }

            public ReadOnlyMemory<byte> MemoryBuffer { get; set; }
        }

        public sealed class OpenChannel : Command
        {
            public PNetMeshPeer Peer { get; init; }

            public TaskCompletionSource<PNetMeshChannel> Result { get; init; }
        }

        //public sealed class PulseChannel : Command
        //{
        //    public PNetMeshPeer Peer { get; init; }
        //}

        public sealed class RelayPacket : Command
        {
            public PNetMeshRelayPacket Packet { get; init; }

            public IMemoryOwner<byte> MemoryOwner { get; set; }

            public TaskCompletionSource Result { get; init; }
        }
    }

    public static class PNetMeshOutboundMessages
    {
        public abstract class Message
        {

        }
        public sealed class Packet : Message
        {
            public IMemoryOwner<byte> MemoryOwner { get; set; }

            /// <summary>
            /// hash of remote public key
            /// </summary>
            public byte[] RemoteAddress { get; set; }

            public EndPoint RemoteEndPoint { get; set; }

            /// <summary>
            /// hash of local public key
            /// </summary>
            public byte[] LocalAddress { get; set; }

            public EndPoint LocalEndPoint { get; set; }

            public Memory<byte> MemoryBuffer { get; set; }
        }

        public sealed class Relay : Message
        {
            public PNetMeshRelayPacket Packet { get; init; }
        }
    }
}
