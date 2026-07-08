using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using MeshProtos = global::PNet.Actor.Mesh.Protos;
using Candidate = global::PNet.Actor.Mesh.Protos.CandidateExchange.Types.Candidate;
using CandidateType = global::PNet.Actor.Mesh.Protos.CandidateExchange.Types.Candidate.Types.CandidateType;
using EnvelopeBody = global::PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Body;
using EnvelopeAck = global::PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Ack;
using Forward = global::PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Body.Types.Forward;
using ProtoMetadata = global::PNet.Protos.ProtoMetadata;
using ReliableEnvelope = global::PNet.Actor.Mesh.Protos.ReliableEnvelope;
using TransportProtocol = global::PNet.Actor.Mesh.Protos.CandidateExchange.Types.Candidate.Types.TransportProtocol;

namespace PNet.Mesh
{
    public enum PNetMeshSessionStatus
    {
        Unknown = 0,
        Opening,
        Open,
        Closing,
        Closed,
        Disposed
    }

    public sealed class PNetMeshSession : IDisposable, IPNetMeshReliableControlPacketHandler
    {
        readonly struct RawIpFrameReceive
        {
            public RawIpFrameReceive(
                PNetMeshPayloadFrameKind kind,
                int frameLength,
                IPNetMeshRawIpFrameSink? sink,
                ChannelWriter<ReadOnlyMemory<byte>>? inboundWriter)
            {
                Kind = kind;
                FrameLength = frameLength;
                Sink = sink;
                InboundWriter = inboundWriter;
            }

            public PNetMeshPayloadFrameKind Kind { get; }

            public int FrameLength { get; }

            public IPNetMeshRawIpFrameSink? Sink { get; }

            public ChannelWriter<ReadOnlyMemory<byte>>? InboundWriter { get; }
        }

        private readonly struct Ack
        {
            public ulong SeqNumber { get; init; }

            public ReadOnlyMemory<byte> OutOfOrder { get; init; }
        }

        sealed class PendingPayload : IDisposable
        {
            readonly IMemoryOwner<byte> _owner;

            public PendingPayload(ReadOnlySpan<byte> payload)
            {
                _owner = MemoryPool<byte>.Shared.Rent(payload.Length);
                payload.CopyTo(_owner.Memory.Span);
                Length = payload.Length;
            }

            public int Length { get; }

            public ReadOnlyMemory<byte> Memory => _owner.Memory.Slice(0, Length);

            public void Dispose()
            {
                _owner.Memory.Span.Slice(0, Length).Clear();
                _owner.Dispose();
            }
        }

        readonly struct ReliableFrameLayout
        {
            public ReliableFrameLayout(
                PNetMeshPNetFrameType frameType,
                int packetLength,
                int payloadTailLength,
                int pnetPayloadLength,
                int frameSize)
            {
                FrameType = frameType;
                PacketLength = packetLength;
                PayloadTailLength = payloadTailLength;
                PNetPayloadLength = pnetPayloadLength;
                FrameSize = frameSize;
            }

            public PNetMeshPNetFrameType FrameType { get; }

            public int PacketLength { get; }

            public int PayloadTailLength { get; }

            public int PNetPayloadLength { get; }

            public int FrameSize { get; }
        }

        readonly PNetMeshProtocol _protocol;

        readonly ChannelWriter<PNetMeshOutboundMessages.Message> _outboundWriter;

        readonly ILogger _logger;

        readonly PNetMeshPacketBuffer _retransBuffer;

        // multi-threading: server receive handling, channel control/relay sends, timers, and disposal can all enter a session concurrently.
        // The session owner gate serializes mutable session state so protocol counters, open packets, ACK state, retransmit buffers,
        // endpoint discovery, and pending control commands are updated through one ownership path instead of independent locks.
        readonly System.Threading.Lock _sessionOwnerLock = new System.Threading.Lock();

        PNetMeshHandshake? _handshake;

        PNetMeshTransport2? _transport;

        PNetMeshPacketTracker? _transportReplayTracker;

        readonly PNetMeshReliableControlSession _reliableControlSession;

        readonly PNetMeshWireGuardEndpointDiscovery _endpointDiscovery =
            new PNetMeshWireGuardEndpointDiscovery(null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        EndPoint? _localEndPoint;
        EndPoint? _remoteEndPoint;

        public EndPoint? LocalEndPoint
        {
            get
            {
                lock (_sessionOwnerLock)
                    return _localEndPoint;
            }
            internal set
            {
                lock (_sessionOwnerLock)
                    _localEndPoint = value;
            }
        }

        public EndPoint? RemoteEndPoint
        {
            get
            {
                lock (_sessionOwnerLock)
                    return _remoteEndPoint;
            }
            internal set
            {
                lock (_sessionOwnerLock)
                    _remoteEndPoint = value;
            }
        }

        PNetMeshSessionStatus _status;
        bool _statusChangedQueued;
        public PNetMeshSessionStatus Status
        {
            get
            {
                if (_sessionOwnerLock.IsHeldByCurrentThread)
                    return _status;

                lock (_sessionOwnerLock)
                    return _status;
            }
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    var handler = StatusChanged;
                    if (handler is null)
                        return;

                    if (_sessionOwnerLock.IsHeldByCurrentThread)
                        QueueStatusChanged();
                    else
                        handler.Invoke(this, EventArgs.Empty);
                }
            }
        }

        void QueueStatusChanged()
        {
            if (_statusChangedQueued)
                return;

            _statusChangedQueued = true;
            ThreadPool.QueueUserWorkItem(_ => RaiseQueuedStatusChanged());
        }

        void RaiseQueuedStatusChanged()
        {
            lock (_sessionOwnerLock)
                _statusChangedQueued = false;

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        PNetMeshHandshake Handshake => _handshake ?? throw new InvalidOperationException("Session handshake is not initialized.");

        PNetMeshTransport2 Transport => _transport ?? throw new InvalidOperationException("Session transport is not initialized.");

        PNetMeshPacketTracker TransportReplayTracker => _transportReplayTracker ?? throw new InvalidOperationException("Session transport replay tracker is not initialized.");

        public byte[] LocalPublicKey => Handshake.LocalPublicKey;

        public byte[] LocalAddress { get; private set; } = Array.Empty<byte>();

        public byte[] RemotePublicKey => Handshake.RemotePublicKey;

        public byte[] RemoteAddress { get; private set; } = Array.Empty<byte>();

        public long Timestamp => Handshake.Timestamp;

        public uint SenderIndex => Handshake.SenderIndex;

        internal EndPoint? PendingDirectEndpoint
        {
            get
            {
                lock (_sessionOwnerLock)
                    return _endpointDiscovery.CandidateEndpoint;
            }
        }

        internal bool AllowOutOfOrderPayloadDelivery { get; set; }

        internal bool UnreliablePayloadDelivery { get; set; }

        internal bool SupportsDirectEndpointDiscovery => true;

        internal bool TryApplyDirectEndpointHint(EndPoint endpoint, DateTimeOffset now)
        {
            if (!SupportsDirectEndpointDiscovery)
                return false;

            lock (_sessionOwnerLock)
                return _endpointDiscovery.DirectEndpoint is null
                       && _endpointDiscovery.TryApplyEndpointHint(endpoint, now);
        }

        internal bool TryPromoteDirectEndpoint(EndPoint endpoint, DateTimeOffset now)
        {
            if (!SupportsDirectEndpointDiscovery)
                return false;

            lock (_sessionOwnerLock)
            {
                if (!_endpointDiscovery.TryPromoteAuthenticatedDirectEndpoint(endpoint, now))
                    return false;

                _remoteEndPoint = endpoint;
                return true;
            }
        }

        internal void ConfirmRemoteEndpoint(EndPoint endpoint)
        {
            if (endpoint is null)
                return;

            lock (_sessionOwnerLock)
            {
                _remoteEndPoint = endpoint;
                _endpointDiscovery.FallbackToRelay();
            }
        }

        internal void UpdateLocalEndpoint(EndPoint endpoint)
        {
            if (endpoint is null)
                return;

            lock (_sessionOwnerLock)
            {
                if (!endpoint.Equals(_localEndPoint))
                    _localEndPoint = endpoint;
            }
        }

        internal bool TryGetDirectProbeEndpoint(DateTimeOffset now, [NotNullWhen(true)] out EndPoint? endpoint)
        {
            if (!SupportsDirectEndpointDiscovery)
            {
                endpoint = default!;
                return false;
            }

            lock (_sessionOwnerLock)
                return TryGetDirectProbeEndpointCore(now, out endpoint);
        }

        bool TryGetDirectProbeEndpointCore(DateTimeOffset now, [NotNullWhen(true)] out EndPoint? endpoint)
        {
            if (!SupportsDirectEndpointDiscovery)
            {
                endpoint = default!;
                return false;
            }

            endpoint = default!;
            var started = _endpointDiscovery.DirectEndpoint is null
                          && _endpointDiscovery.TryBeginDirectProbe(now, out endpoint);
            if (started)
            {
                _logger.LogInformation(
                    "event=wireguard_direct_probe_started session={sessionIndex} endpoint_id={endpointId}",
                    SenderIndex,
                    PNetMeshDiagnosticRedactor.EndpointId(endpoint));
            }

            return started;
        }

        public event EventHandler? StatusChanged;

        internal event EventHandler? MessageReceived;

        //public PNetMeshChannel Channel { get; set; }

        ChannelWriter<ReadOnlyMemory<byte>>? _inboundWriter;
        ChannelWriter<PNetMeshChannelCommands.Command>? _controlWriter;
        IPNetMeshRawIpFrameSink? _rawIpFrameSink;
        readonly Queue<PNetMeshChannelCommands.Invoke> _pendingControlCommands = new Queue<PNetMeshChannelCommands.Invoke>();
        bool _controlQueueDraining;
        const int MaxPendingControlCommands = 32;

        ReliableEnvelope _openEnvelope = new ReliableEnvelope();
        readonly List<PendingPayload> _openBodyPayloads = new List<PendingPayload>();
        readonly List<TaskCompletionSource?> _openEnvelopeResults = new List<TaskCompletionSource?>();
        bool _disposing;

        Ack _remoteAck = new Ack { SeqNumber = 0, OutOfOrder = ReadOnlyMemory<byte>.Empty };

        ulong _receiveCounter = 0;
        ulong _receiveAck = 0;
        ulong _receiveAckRequired = 0;
        readonly SortedDictionary<ulong, PNetMeshReliableEnvelope> _pendingPackets = new SortedDictionary<ulong, PNetMeshReliableEnvelope>();
        readonly HashSet<ulong> _deliveredOutOfOrderPackets = new HashSet<ulong>();
        int _cumAck_max = 2;
        //int _cumAck_count = 0;
        int _cumAck_timeout = 100;

        int _outOfOrder_max = 2;
        int _outOfOrder_count = 0;
        // multi-threading: SYN negotiation publishes send-side limits on the receive thread while the channel control/timer loop reads them for outbound gating.
        int _outstanding_max = 32;
        int _packetSize_max = ushort.MaxValue - 8;

        int _retrans_timeout = 250;

        Timer? _retransTimer;
        Timer? _cumAckTimer;

        // multi-threading: raw-frame fast-path writers reserve the transport under _sessionOwnerLock,
        // encrypt outside the lock, then release in a finally block. Dispose waits on this drain event
        // before ClearTransport() so a reserved transport cannot be cleared while encryption is active.
        int _activeTransportWriters;
        ManualResetEventSlim? _transportWritersDrained;

        readonly struct RawFrameWriteReservation
        {
            public RawFrameWriteReservation(
                PNetMeshTransport2.WriteReservation transportReservation,
                EndPoint? localEndPoint,
                byte[] localAddress,
                EndPoint? remoteEndPoint,
                byte[] remoteAddress,
                EndPoint? directProbeEndPoint)
            {
                TransportReservation = transportReservation;
                LocalEndPoint = localEndPoint;
                LocalAddress = localAddress;
                RemoteEndPoint = remoteEndPoint;
                RemoteAddress = remoteAddress;
                DirectProbeEndPoint = directProbeEndPoint;
            }

            public PNetMeshTransport2.WriteReservation TransportReservation { get; }

            public EndPoint? LocalEndPoint { get; }

            public byte[] LocalAddress { get; }

            public EndPoint? RemoteEndPoint { get; }

            public byte[] RemoteAddress { get; }

            public EndPoint? DirectProbeEndPoint { get; }
        }

        internal PNetMeshSession(PNetMeshProtocol protocol, ChannelWriter<PNetMeshOutboundMessages.Message> writer, ILogger? logger = null)
        {
            _protocol = protocol;
            _outboundWriter = writer;
            _logger = logger ?? NullLogger<PNetMeshSession>.Instance;
            _retransBuffer = new PNetMeshPacketBuffer();
            _reliableControlSession = new PNetMeshReliableControlSession(this);
        }

        public void Dispose()
        {
            var exception = new ObjectDisposedException(nameof(PNetMeshSession));
            TaskCompletionSource?[]? results;
            ManualResetEventSlim? transportWritersDrained;
            lock (_sessionOwnerLock)
            {
                _disposing = true;
                Status = PNetMeshSessionStatus.Disposed;
                results = FailOpenPacket();
                transportWritersDrained = _activeTransportWriters == 0
                    ? null
                    : (_transportWritersDrained ??= new ManualResetEventSlim(false));
            }
            CompleteOpenPacketResults(results, exception);
            transportWritersDrained?.Wait();

            _cumAckTimer?.Dispose();
            _retransTimer?.Dispose();

            _handshake?.Dispose();
            ClearTransport();
            _transportWritersDrained?.Dispose();
        }

        internal void AttachTo(ChannelWriter<ReadOnlyMemory<byte>> inboundWriter, ChannelWriter<PNetMeshChannelCommands.Command>? controlWriter)
        {
            lock (_sessionOwnerLock)
            {
                _inboundWriter = inboundWriter;
                _controlWriter = controlWriter;

                _retransTimer = new Timer(OnRetransTimeout, null, Timeout.Infinite, Timeout.Infinite);
                _cumAckTimer = new Timer(OnCumAckTimeout, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        internal void AttachRawIpFrameSink(IPNetMeshRawIpFrameSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            lock (_sessionOwnerLock)
                _rawIpFrameSink = sink;
        }

        internal void DetachRawIpFrameSink(IPNetMeshRawIpFrameSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            lock (_sessionOwnerLock)
            {
                if (ReferenceEquals(_rawIpFrameSink, sink))
                    _rawIpFrameSink = null;
            }
        }

        void OnRetransTimeout(object? state)
        {
            //var writer = state as ChannelWriter<PNetMeshChannelCommands.Command>;
            lock (_sessionOwnerLock)
            {
                if (_disposing || _status != PNetMeshSessionStatus.Open)
                    return;
            }

            QueueControl(new PNetMeshChannelCommands.Invoke
            {
                Handler = RetransmitPackets
            });
        }

        void OnCumAckTimeout(object? state)
        {
            //var writer = state as ChannelWriter<PNetMeshChannelCommands.Command>;
            lock (_sessionOwnerLock)
            {
                if (_disposing || _status != PNetMeshSessionStatus.Open)
                    return;

                if (!HasOpenPacket() && !HasPendingAck())
                    return;

                if (HasOpenPacket() && !HasSendWindow() && !HasPendingAck())
                    return;
            }

            QueueControl(new PNetMeshChannelCommands.Invoke
            {
                Handler = FlushAckTimeoutPacket
            });
        }

        public void WriteInitialize(uint senderIndex, byte[] remotePublicKey)
        {
            var remoteAddress = new byte[10];
            PNetMeshUtils.GetAddressFromPublicKey(remotePublicKey, remoteAddress);

            var localAddress = new byte[10]; //todo set local address external
            PNetMeshUtils.GetAddressFromPublicKey(_protocol.LocalStaticPublicKey, localAddress);

            lock (_sessionOwnerLock)
            {
                RemoteAddress = remoteAddress;

                _handshake = _protocol.CreateInitiator(senderIndex, remotePublicKey);

                LocalAddress = localAddress;

                var buffer = MemoryPool<byte>.Shared.Rent(_protocol.HandshakeInitiationMessageSize);

                _handshake.WriteInitiationMessage(buffer.Memory.Span, out var bytesWritten);
                Debug.Assert(bytesWritten == _protocol.HandshakeInitiationMessageSize);

                Status = PNetMeshSessionStatus.Opening;

                //send initiation message
                var item = new PNetMeshOutboundMessages.Packet
                {
                    MemoryOwner = buffer,
                    MemoryBuffer = buffer.Memory.Slice(0, bytesWritten),
                    LocalEndPoint = _localEndPoint,
                    LocalAddress = LocalAddress,
                    RemoteEndPoint = _remoteEndPoint, //maybe use router
                    RemoteAddress = remoteAddress
                };

                if (!_outboundWriter.TryWrite(item))
                {
                    //todo log error

                    buffer.Dispose();
                    Status = PNetMeshSessionStatus.Closed;
                    return;
                }
            }
        }

        public void ReadInitialize(uint senderIndex, ReadOnlySpan<byte> payload)
        {
            TryReadInitialize(senderIndex, payload);
        }

        public bool TryReadInitialize(uint senderIndex, ReadOnlySpan<byte> payload)
        {
            var localAddress = new byte[10]; //todo set local address external
            PNetMeshUtils.GetAddressFromPublicKey(_protocol.LocalStaticPublicKey, localAddress);

            lock (_sessionOwnerLock)
            {
                _handshake = _protocol.CreateResponder(senderIndex);

                LocalAddress = localAddress;

                if (!_handshake.TryReadInitiationMessage(payload))
                {
                    //invalid handshake
                    Status = PNetMeshSessionStatus.Closed;
                    return false;
                }

                var remoteAddress = new byte[10];
                PNetMeshUtils.GetAddressFromPublicKey(RemotePublicKey, remoteAddress);
                RemoteAddress = remoteAddress;
                return true;
            }
        }

        public void WriteResponse()
        {
            lock (_sessionOwnerLock)
            {
                var buffer = MemoryPool<byte>.Shared.Rent(PNetMeshHandshake.ResponseMessageSize);

                if (!Handshake.TryWriteResponseMessage(buffer.Memory.Span,
                    out var bytesWritten, out var transport))
                {
                    //invalid noise sequence
                    buffer.Dispose();
                    Status = PNetMeshSessionStatus.Closed;
                    return;
                }
                SetTransport(transport);
                Debug.Assert(bytesWritten == PNetMeshHandshake.ResponseMessageSize);

                Status = PNetMeshSessionStatus.Opening;
                var openAfterResponse = _remoteEndPoint is not null;
                if (openAfterResponse)
                {
                    if (_disposing || _status == PNetMeshSessionStatus.Disposed || _status == PNetMeshSessionStatus.Closed)
                    {
                        buffer.Dispose();
                        ClearTransport();
                        return;
                    }

                    _openEnvelope = new ReliableEnvelope();
                }

                //send response message
                var item = new PNetMeshOutboundMessages.Packet
                {
                    MemoryOwner = buffer,
                    MemoryBuffer = buffer.Memory.Slice(0, bytesWritten),
                    LocalEndPoint = _localEndPoint,
                    LocalAddress = LocalAddress,
                    RemoteEndPoint = _remoteEndPoint,
                    RemoteAddress = RemoteAddress
                };

                if (!_outboundWriter.TryWrite(item))
                {
                    //todo log error

                    buffer.Dispose();
                    Status = PNetMeshSessionStatus.Closed;
                    return;
                }

                if (openAfterResponse)
                    Status = PNetMeshSessionStatus.Open;
            }
        }

        public void ReadResponse(ReadOnlySpan<byte> payload)
        {
            TryReadResponse(payload);
        }

        public bool TryReadResponse(ReadOnlySpan<byte> payload)
        {
            lock (_sessionOwnerLock)
            {
                if (!Handshake.TryReadResponseMessage(payload, out var transport))
                {
                    //invalid session
                    Status = PNetMeshSessionStatus.Closed;
                    return false;
                }
                SetTransport(transport);

                if (_disposing || _status == PNetMeshSessionStatus.Disposed || _status == PNetMeshSessionStatus.Closed)
                {
                    ClearTransport();
                    return false;
                }

                _openEnvelope = new ReliableEnvelope();
                Status = PNetMeshSessionStatus.Open;
            }

            return true;
        }

        public void WritePayload(ReadOnlySpan<byte> payload)
        {
            WritePayload(payload, null);
        }

        public void WritePayload(ReadOnlySpan<byte> payload, TaskCompletionSource? result)
        {
            WritePayload(payload, result, UnreliablePayloadDelivery);
        }

        internal void WritePayload(ReadOnlySpan<byte> payload, TaskCompletionSource? result, bool unreliablePayloadDelivery)
        {
            if (!TryWritePayload(payload, result, unreliablePayloadDelivery))
                throw new InvalidOperationException("session not open");
        }

        internal bool TryWritePayload(ReadOnlySpan<byte> payload, TaskCompletionSource? result, bool unreliablePayloadDelivery)
        {
            PendingPayload? pendingPayload = new PendingPayload(payload);
            var item = CreateBody(payload.Length);
            TaskCompletionSource?[]? results = null;
            Exception? resultException = null;
            try
            {
                lock (_sessionOwnerLock)
                {
                    if (_disposing || _status != PNetMeshSessionStatus.Open)
                        return false;

                    var itemIndex = _openEnvelope.Bodies.Count;
                    var payloadIndex = _openBodyPayloads.Count;
                    var resultIndex = _openEnvelopeResults.Count;
                    _openEnvelope.Bodies.Add(item);
                    _openBodyPayloads.Add(pendingPayload!);
                    pendingPayload = null;
                    _openEnvelopeResults.Add(result);

                    try
                    {
                        EnsurePacketSize(CalculatePNetFrameSize(_openEnvelope, _openBodyPayloads), nameof(payload));

                        if (unreliablePayloadDelivery || HasSendWindow())
                        {
                            WriteOpenPacketOrFail(
                                nameof(payload),
                                failBatchOnSizeError: false,
                                trackForRetransmit: !unreliablePayloadDelivery,
                                out results,
                                out resultException);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_openEnvelope.Bodies.Count > itemIndex)
                            _openEnvelope.Bodies.RemoveAt(itemIndex);
                        if (_openBodyPayloads.Count > payloadIndex)
                        {
                            var removedPayload = _openBodyPayloads[payloadIndex];
                            _openBodyPayloads.RemoveAt(payloadIndex);
                            removedPayload.Dispose();
                        }
                        if (_openEnvelopeResults.Count > resultIndex)
                            _openEnvelopeResults.RemoveAt(resultIndex);
                        if (results is null && result is not null)
                        {
                            results = new[] { result };
                            resultException = ex;
                        }
                        throw;
                    }
                }
            }
            finally
            {
                pendingPayload?.Dispose();
                CompleteOpenPacketResults(results, resultException);
            }

            return true;
        }

        internal void WriteRawFrame(ReadOnlySpan<byte> frame, TaskCompletionSource? result)
        {
            try
            {
                if (!TryWriteRawFrame(frame))
                    throw new InvalidOperationException("session not open");

                result?.TrySetResult();
            }
            catch (Exception ex)
            {
                result?.TrySetException(ex);
                throw;
            }
        }

        internal bool TryWriteRawFrame(ReadOnlySpan<byte> frame)
        {
            var frameLength = GetRawIpFrameLength(frame, nameof(frame));
            return TryWriteRawFrame(frame, frameLength);
        }

        internal bool TryWriteRawFrame(ReadOnlySpan<byte> frame, int frameLength)
        {
            if ((uint)frameLength > (uint)frame.Length)
                throw new ArgumentOutOfRangeException(nameof(frameLength));

            Debug.Assert(frameLength == GetRawIpFrameLength(frame, nameof(frame)));
            return TryWriteRawFrameCore(frame.Slice(0, frameLength));
        }

        bool TryWriteRawFrameCore(ReadOnlySpan<byte> frame)
        {
            if (!TryReserveRawFrameWrite(out var reservation))
                return false;

            IMemoryOwner<byte>? bufferOwner = null;
            try
            {
#if PNET_MESH_PACKET_TRACE
                var hasTraceKey = PNetMeshPacketTrace.TryCreateKey(frame, out var traceKey);
#endif
                PNetMeshPacketTrace.RecordPacket(
                    PNetMeshPacketTraceStage.EncryptStart,
                    frame,
                    frame.Length);
                bufferOwner = MemoryPool<byte>.Shared.Rent(PNetMeshTransport2.CalculatePacketSize(frame.Length));
                var buffer = bufferOwner.Memory;
                if (!reservation.TransportReservation.TryWriteFrame(frame, buffer.Span, out var byteWritten))
                    throw new InvalidOperationException("Unable to write secure frame.");

                PNetMeshPacketTrace.RecordPacket(
                    PNetMeshPacketTraceStage.EncryptDone,
                    frame,
                    frame.Length,
                    byteWritten);
                CompleteRawFrameWrite(reservation);
                var item = new PNetMeshOutboundMessages.Packet
                {
                    MemoryOwner = bufferOwner,
                    MemoryBuffer = buffer.Slice(0, byteWritten),
                    LocalEndPoint = reservation.LocalEndPoint,
                    LocalAddress = reservation.LocalAddress,
                    RemoteEndPoint = reservation.RemoteEndPoint,
                    RemoteAddress = reservation.RemoteAddress
                };
#if PNET_MESH_PACKET_TRACE
                if (hasTraceKey)
                    item.PacketTraceKey = traceKey;
#endif

                TryWriteDirectProbe(
                    item.MemoryBuffer,
                    reservation.DirectProbeEndPoint,
                    reservation.LocalEndPoint,
                    reservation.LocalAddress,
                    reservation.RemoteAddress);

                if (!_outboundWriter.TryWrite(item))
                {
                    _ = WriteOutboundPacketAsync(_outboundWriter, item, _logger);
                }

                bufferOwner = null;
                return true;
            }
            finally
            {
                ReleaseTransportWriter();
                bufferOwner?.Dispose();
            }
        }

        bool TryReserveRawFrameWrite(out RawFrameWriteReservation reservation)
        {
            lock (_sessionOwnerLock)
            {
                if (_status != PNetMeshSessionStatus.Open)
                {
                    reservation = default;
                    return false;
                }

                ThrowIfDisposing();
                var transportReservation = Transport.ReserveWrite();
                _retransBuffer.AddUntracked(transportReservation.Counter);
                TryGetDirectProbeEndpointCore(DateTimeOffset.UtcNow, out var directProbeEndPoint);
                _activeTransportWriters++;
                _transportWritersDrained?.Reset();
                reservation = new RawFrameWriteReservation(
                    transportReservation,
                    _localEndPoint,
                    LocalAddress,
                    _remoteEndPoint,
                    RemoteAddress,
                    directProbeEndPoint);
                return true;
            }
        }

        void CompleteRawFrameWrite(RawFrameWriteReservation reservation)
        {
            lock (_sessionOwnerLock)
            {
                reservation.TransportReservation.RecordWritten();
            }
        }

        void ReleaseTransportWriter()
        {
            lock (_sessionOwnerLock)
            {
                if (_activeTransportWriters == 0)
                    throw new InvalidOperationException("No active transport writer is available.");

                _activeTransportWriters--;
                if (_activeTransportWriters == 0)
                    _transportWritersDrained?.Set();
            }
        }

        public void WriteRelay(PNetMeshRelayPacket packet)
        {
            WriteRelay(packet, null);
        }

        public void WriteRelay(PNetMeshRelayPacket packet, TaskCompletionSource? result)
        {
            PendingPayload? pendingPayload = new PendingPayload(packet.Payload.Span);
            var item = CreateForwardBody(packet);
            TaskCompletionSource?[]? results = null;
            Exception? resultException = null;
            try
            {
                lock (_sessionOwnerLock)
                {
                    ThrowIfDisposing();

                    var itemIndex = _openEnvelope.Bodies.Count;
                    var payloadIndex = _openBodyPayloads.Count;
                    var resultIndex = _openEnvelopeResults.Count;
                    _openEnvelope.Bodies.Add(item);
                    _openBodyPayloads.Add(pendingPayload!);
                    pendingPayload = null;
                    _openEnvelopeResults.Add(result);

                    try
                    {
                        EnsurePacketSize(CalculatePNetFrameSize(_openEnvelope, _openBodyPayloads), nameof(packet));

                        if (HasSendWindow())
                        {
                            WriteOpenPacketOrFail(
                                nameof(packet),
                                failBatchOnSizeError: false,
                                trackForRetransmit: true,
                                out results,
                                out resultException);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_openEnvelope.Bodies.Count > itemIndex)
                            _openEnvelope.Bodies.RemoveAt(itemIndex);
                        if (_openBodyPayloads.Count > payloadIndex)
                        {
                            var removedPayload = _openBodyPayloads[payloadIndex];
                            _openBodyPayloads.RemoveAt(payloadIndex);
                            removedPayload.Dispose();
                        }
                        if (_openEnvelopeResults.Count > resultIndex)
                            _openEnvelopeResults.RemoveAt(resultIndex);
                        if (results is null && result is not null)
                        {
                            results = new[] { result };
                            resultException = ex;
                        }
                        throw;
                    }
                }
            }
            finally
            {
                pendingPayload?.Dispose();
                CompleteOpenPacketResults(results, resultException);
            }
        }

        void FlushOpenPacket()
        {
            TaskCompletionSource?[]? results = null;
            Exception? resultException = null;
            try
            {
                lock (_sessionOwnerLock)
                {
                    if (HasOpenPacket() && HasSendWindow())
                        WriteOpenPacketOrFail(
                            "packet",
                            failBatchOnSizeError: true,
                            trackForRetransmit: true,
                            out results,
                            out resultException);
                }
            }
            finally
            {
                CompleteOpenPacketResults(results, resultException);
            }
        }

        void FlushAckTimeoutPacket()
        {
            TaskCompletionSource?[]? results = null;
            Exception? resultException = null;
            try
            {
                lock (_sessionOwnerLock)
                {
                    if (_disposing)
                        return;

                    if (!HasOpenPacket())
                    {
                        try
                        {
                            if (HasPendingAck())
                                WriteControlPacket();
                        }
                        catch
                        {
                            Status = PNetMeshSessionStatus.Closed;
                            throw;
                        }
                        return;
                    }

                    if (HasSendWindow())
                    {
                        WriteOpenPacketOrFail(
                            "packet",
                            failBatchOnSizeError: true,
                            trackForRetransmit: true,
                            out results,
                            out resultException);
                        return;
                    }

                    try
                    {
                        if (HasPendingAck())
                            WriteControlPacket();
                    }
                    catch (Exception ex)
                    {
                        Status = PNetMeshSessionStatus.Closed;
                        results = FailOpenPacket();
                        resultException = ex;
                        throw;
                    }
                }
            }
            finally
            {
                CompleteOpenPacketResults(results, resultException);
            }
        }

        public void WritePacket()
        {
            TaskCompletionSource?[]? results = null;
            Exception? resultException = null;
            try
            {
                lock (_sessionOwnerLock)
                    WriteOpenPacketOrFail(
                        "packet",
                        failBatchOnSizeError: true,
                        trackForRetransmit: true,
                        out results,
                        out resultException);
            }
            finally
            {
                CompleteOpenPacketResults(results, resultException);
            }
        }

        TaskCompletionSource?[]? WriteEnvelope(string sizeParamName)
        {
            return WriteEnvelope(sizeParamName, trackForRetransmit: true);
        }

        TaskCompletionSource?[]? WriteEnvelope(string sizeParamName, bool trackForRetransmit)
        {
            return WriteEnvelope(
                _openEnvelope.Clone(),
                _openBodyPayloads.ToArray(),
                clearOpenPacket: true,
                sizeParamName,
                trackForRetransmit);
        }

        void WriteControlPacket()
        {
            WriteEnvelope(
                new ReliableEnvelope(),
                Array.Empty<PendingPayload>(),
                clearOpenPacket: false,
                "packet",
                trackForRetransmit: false);
        }

        TaskCompletionSource?[]? WriteEnvelope(
            ReliableEnvelope envelope,
            IReadOnlyList<PendingPayload> bodyPayloads,
            bool clearOpenPacket,
            string sizeParamName,
            bool trackForRetransmit = true)
        {
            ThrowIfDisposing();
            if (_status != PNetMeshSessionStatus.Open)
                throw new InvalidOperationException("session not open");

            var layout = PrepareEnvelopeForWrite(envelope, bodyPayloads, sizeParamName, trackForRetransmit);
            TryGetDirectProbeEndpointCore(DateTimeOffset.UtcNow, out var directProbeEndPoint);

            using var frameBuffer = MemoryPool<byte>.Shared.Rent(layout.FrameSize);

            var frame = frameBuffer.Memory.Span.Slice(0, layout.FrameSize);
            WriteReliablePNetPayload(envelope, bodyPayloads, layout, frame.Slice(1, layout.PNetPayloadLength));
            if (!PNetMeshPayloadFraming.TryWritePNet(
                    frame,
                    layout.PNetPayloadLength,
                    out var frameBytesWritten,
                    hasExtendedHeaderSignal: true))
            {
                throw new InvalidOperationException("Unable to write PNet frame.");
            }

            Debug.Assert(frameBytesWritten == layout.FrameSize);
            frame = frame.Slice(0, frameBytesWritten);

            if (trackForRetransmit)
            {
                var rented = false;
                try
                {
                    var buffer = _retransBuffer.Rent(frame.Length);
                    rented = true;
                    if (!Transport.TryWriteFrame(frame, buffer.Span, out var byteWritten, out var counter))
                        throw new InvalidOperationException("Unable to write secure frame.");
                    Debug.Assert(counter == _retransBuffer.Current);
                    var item = new PNetMeshOutboundMessages.Packet
                    {
                        MemoryOwner = null,
                        MemoryBuffer = _retransBuffer.SliceCurrent(byteWritten),
                        LocalEndPoint = _localEndPoint,
                        LocalAddress = LocalAddress,
                        RemoteEndPoint = _remoteEndPoint,
                        RemoteAddress = RemoteAddress
                    };

                    TryWriteDirectProbe(item.MemoryBuffer, directProbeEndPoint);

                    if (!_outboundWriter.TryWrite(item))
                    {
                        //todo log error
                        Status = PNetMeshSessionStatus.Closed;
                        throw new InvalidOperationException("Unable to queue outbound packet.");
                    }

                    UpdateRetransmissionTimer();
                    rented = false;
                }
                catch
                {
                    if (rented)
                        _retransBuffer.DiscardCurrent();
                    throw;
                }
            }
            else
            {
                var bufferOwner = MemoryPool<byte>.Shared.Rent(PNetMeshTransport2.CalculatePacketSize(frame.Length));
                try
                {
                    var buffer = bufferOwner.Memory;
                    if (!Transport.TryWriteFrame(frame, buffer.Span, out var byteWritten, out var counter))
                        throw new InvalidOperationException("Unable to write secure frame.");

                    var item = new PNetMeshOutboundMessages.Packet
                    {
                        MemoryOwner = bufferOwner,
                        MemoryBuffer = buffer.Slice(0, byteWritten),
                        LocalEndPoint = _localEndPoint,
                        LocalAddress = LocalAddress,
                        RemoteEndPoint = _remoteEndPoint,
                        RemoteAddress = RemoteAddress
                    };

                    TryWriteDirectProbe(item.MemoryBuffer, directProbeEndPoint);

                    if (!_outboundWriter.TryWrite(item))
                    {
                        //todo log error
                        bufferOwner.Dispose();
                        bufferOwner = null;
                        Status = PNetMeshSessionStatus.Closed;
                        throw new InvalidOperationException("Unable to queue outbound packet.");
                    }

                    _retransBuffer.AddUntracked(counter);

                    bufferOwner = null;
                }
                finally
                {
                    bufferOwner?.Dispose();
                }
            }

            if (clearOpenPacket)
                return CompleteOpenPacket();

            return null;
        }

        void TryWriteDirectProbe(Memory<byte> packet, EndPoint? directProbeEndPoint)
        {
            TryWriteDirectProbe(packet, directProbeEndPoint, _localEndPoint, LocalAddress, RemoteAddress);
        }

        void TryWriteDirectProbe(
            Memory<byte> packet,
            EndPoint? directProbeEndPoint,
            EndPoint? localEndPoint,
            byte[] localAddress,
            byte[] remoteAddress)
        {
            if (directProbeEndPoint is null)
                return;

            var bufferOwner = MemoryPool<byte>.Shared.Rent(packet.Length);
            packet.CopyTo(bufferOwner.Memory);
            var item = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = bufferOwner,
                MemoryBuffer = bufferOwner.Memory.Slice(0, packet.Length),
                LocalEndPoint = localEndPoint,
                LocalAddress = localAddress,
                RemoteEndPoint = directProbeEndPoint,
                RemoteAddress = remoteAddress
            };

            if (!_outboundWriter.TryWrite(item))
                bufferOwner.Dispose();
        }

        void WriteOpenPacketOrFail(
            string sizeParamName,
            bool failBatchOnSizeError,
            bool trackForRetransmit,
            out TaskCompletionSource?[]? results,
            out Exception? resultException)
        {
            results = null;
            resultException = null;
            try
            {
                results = WriteEnvelope(sizeParamName, trackForRetransmit);
            }
            catch (ArgumentOutOfRangeException) when (!failBatchOnSizeError)
            {
                throw;
            }
            catch (Exception ex)
            {
                Status = PNetMeshSessionStatus.Closed;
                results = FailOpenPacket();
                resultException = ex;
                throw;
            }
        }

        TaskCompletionSource?[]? CompleteOpenPacket()
        {
            _openEnvelope = new ReliableEnvelope();
            ClearOpenPayloads();
            return DetachOpenPacketResults();
        }

        TaskCompletionSource?[]? FailOpenPacket()
        {
            _openEnvelope = new ReliableEnvelope();
            ClearOpenPayloads();
            return DetachOpenPacketResults();
        }

        void ClearOpenPayloads()
        {
            foreach (var payload in _openBodyPayloads)
                payload.Dispose();
            _openBodyPayloads.Clear();
        }

        TaskCompletionSource?[]? DetachOpenPacketResults()
        {
            if (_openEnvelopeResults.Count == 0)
                return null;

            var results = _openEnvelopeResults.ToArray();
            _openEnvelopeResults.Clear();
            return results;
        }

        static void CompleteOpenPacketResults(TaskCompletionSource?[]? results, Exception? exception)
        {
            if (results is null)
                return;

            foreach (var result in results)
            {
                if (exception is null)
                    result?.TrySetResult();
                else
                    result?.TrySetException(exception);
            }
        }

        void ThrowIfDisposing()
        {
            if (_disposing || _status == PNetMeshSessionStatus.Disposed || _status == PNetMeshSessionStatus.Closed)
                throw new ObjectDisposedException(nameof(PNetMeshSession));
        }

        void SetTransport(PNetMeshTransport2 transport)
        {
            _transport?.Dispose();
            _transportReplayTracker?.Dispose();
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transportReplayTracker = new PNetMeshPacketTracker();
        }

        void ClearTransport()
        {
            _transport?.Dispose();
            _transport = null;
            _transportReplayTracker?.Dispose();
            _transportReplayTracker = null;
        }

        int PrepareEnvelopeForWrite(ReliableEnvelope packet, string sizeParamName)
        {
            return PrepareEnvelopeForWrite(
                packet,
                Array.Empty<PendingPayload>(),
                sizeParamName,
                trackForRetransmit: true).PacketLength;
        }

        ReliableFrameLayout PrepareEnvelopeForWrite(
            ReliableEnvelope packet,
            IReadOnlyList<PendingPayload> bodyPayloads,
            string sizeParamName,
            bool trackForRetransmit)
        {
            ulong? receiveAck = null;
            if (_receiveAck < _receiveAckRequired || _pendingPackets.Count > 0)
            {
                var ackSeqNumber = _receiveCounter;

                Span<byte> bitmap = stackalloc byte[16];
                TransportReplayTracker.GetBitmap(ackSeqNumber, bitmap, out var bytesWritten);
                bitmap = bitmap.Slice(0, bytesWritten);

                ackSeqNumber += PNetMeshPacketTracker.RightShift(bitmap, out bytesWritten);
                bitmap = bitmap.Slice(0, bytesWritten);

                packet.Ack = new EnvelopeAck
                {
                    AckSeqNumber = ackSeqNumber,
                    OutOfSequencePackets = ByteString.CopyFrom(bitmap)
                };
                receiveAck = ackSeqNumber;
            }

            packet.DoNotAck = !trackForRetransmit || packet.Bodies.Count == 0;

            var layout = CalculateReliableFrameLayout(packet, bodyPayloads);
            EnsurePacketSize(layout.FrameSize, sizeParamName);

            if (receiveAck.HasValue)
            {
                _receiveAck = receiveAck.Value;
                _cumAckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }

            return layout;
        }

        static ReliableFrameLayout CalculateReliableFrameLayout(
            ReliableEnvelope packet,
            IReadOnlyList<PendingPayload> bodyPayloads)
        {
            if (packet.Bodies.Count != bodyPayloads.Count)
                throw new InvalidOperationException("Envelope body count does not match body tail count.");

            var packetLength = packet.CalculateSize();
            var payloadTailLength = GetPayloadTailLength(bodyPayloads);
            var frameType = packet.Bodies.Count == 0
                ? PNetMeshPNetFrameType.ReliableEnvelope
                : PNetMeshPNetFrameType.ReliableBodies;
            var framePayloadLength = frameType == PNetMeshPNetFrameType.ReliableEnvelope
                ? packetLength
                : checked(PNetMeshUtils.GetVarint32Size((uint)packetLength) + packetLength + payloadTailLength);
            var pnetPayloadLength = PNetMeshPayloadFraming.CalculatePNetTypedPayloadSize(frameType, framePayloadLength);
            var frameSize = PNetMeshPayloadFraming.CalculatePNetFrameSize(pnetPayloadLength);

            return new ReliableFrameLayout(
                frameType,
                packetLength,
                payloadTailLength,
                pnetPayloadLength,
                frameSize);
        }

        static int GetPayloadTailLength(IReadOnlyList<PendingPayload> payloads)
        {
            var payloadTailLength = 0;
            foreach (var payload in payloads)
                payloadTailLength = checked(payloadTailLength + payload.Length);

            return payloadTailLength;
        }

        static void WriteReliablePNetPayload(
            ReliableEnvelope packet,
            IReadOnlyList<PendingPayload> bodyPayloads,
            ReliableFrameLayout layout,
            Span<byte> destination)
        {
            var offset = 0;
            offset += PNetMeshUtils.WriteVarint32((int)layout.FrameType, destination[offset..]);

            if (layout.FrameType == PNetMeshPNetFrameType.ReliableBodies)
                offset += PNetMeshUtils.WriteVarint32(layout.PacketLength, destination[offset..]);

            packet.WriteTo(destination.Slice(offset, layout.PacketLength));
            offset += layout.PacketLength;

            foreach (var payload in bodyPayloads)
            {
                payload.Memory.Span.CopyTo(destination.Slice(offset, payload.Length));
                offset += payload.Length;
            }

            Debug.Assert(offset == layout.PNetPayloadLength);
        }

        void RetransmitPackets()
        {
            RetransmitPackets(GetRemoteAckSnapshot(), expectedLatest: null);
        }

        void RetransmitPackets(Ack ack, ulong? expectedLatest)
        {
            lock (_sessionOwnerLock)
            {
                if (_disposing || _status != PNetMeshSessionStatus.Open)
                    return;

                if (expectedLatest.HasValue && _retransBuffer.Latest != expectedLatest.Value)
                    return;

                var packets = ack.OutOfOrder.Length > 0
                    ? _retransBuffer.GetMissingSequence(ack.OutOfOrder)
                    : _retransBuffer.GetSequence();

                foreach (var packet in packets)
                {
                    var item = new PNetMeshOutboundMessages.Packet
                    {
                        MemoryOwner = null,
                        MemoryBuffer = packet,
                        LocalEndPoint = _localEndPoint,
                        RemoteEndPoint = _remoteEndPoint,
                        RemoteAddress = RemoteAddress
                    };

                    if (!_outboundWriter.TryWrite(item))
                    {
                        //todo log error
                        Status = PNetMeshSessionStatus.Closed;
                        return;
                    }
                }

                UpdateRetransmissionTimer();
            }
        }

        public void ReadMessage(ReadOnlySpan<byte> payload)
        {
            TryReadMessage(payload);
        }

        public bool TryReadMessage(ReadOnlySpan<byte> payload)
        {
            using var buffer = MemoryPool<byte>.Shared.Rent(payload.Length);
            bool result;
            bool payloadReceived;
            RawIpFrameReceive? rawIpFrame;

            lock (_sessionOwnerLock)
            {
                var openResponderSession = _status == PNetMeshSessionStatus.Opening;
                if (openResponderSession
                    && (_disposing || _status == PNetMeshSessionStatus.Disposed || _status == PNetMeshSessionStatus.Closed))
                {
                    return false;
                }

                result = ReadMessage(payload, buffer.Memory, openResponderSession, out payloadReceived, out rawIpFrame);
            }

            if (result && rawIpFrame is { } frame)
                result = TryHandleRawIpFrame(
                    buffer.Memory.Span.Slice(0, frame.FrameLength),
                    frame.Kind,
                    frame.Sink,
                    frame.InboundWriter,
                    out payloadReceived);

            if (result && payloadReceived)
                MessageReceived?.Invoke(this, EventArgs.Empty);

            return result;
        }

        bool ReadMessage(
            ReadOnlySpan<byte> payload,
            Memory<byte> buffer,
            bool openResponderSession,
            out bool payloadReceived,
            out RawIpFrameReceive? rawIpFrame)
        {
            payloadReceived = false;
            rawIpFrame = null;
            PNetMeshPacketTrace.MarkDecryptStart();
            if (!Transport.TryReadFrame(payload, buffer.Span, TransportReplayTracker, out var plaintext))
            {
                //buffer.Dispose();

                if (plaintext.Counter > 0)
                {
                    //duplicate message
                }
                else
                {
                    //invalid message
                    //todo log
                }

                return false;
            }

            if (openResponderSession && _status == PNetMeshSessionStatus.Opening)
            {
                if (_disposing || _status == PNetMeshSessionStatus.Disposed || _status == PNetMeshSessionStatus.Closed)
                    return false;

                _openEnvelope = new ReliableEnvelope();
                Status = PNetMeshSessionStatus.Open;
            }

            var frame = buffer.Span.Slice(0, plaintext.BytesWritten);
            PNetMeshPacketTrace.RecordPacket(
                PNetMeshPacketTraceStage.DecryptDone,
                frame,
                plaintext.BytesWritten);
            if (!PNetMeshPayloadFraming.TryClassify(frame, out var kind, out _))
                return false;

            switch (kind)
            {
                case PNetMeshPayloadFrameKind.PNet:
                    return _reliableControlSession.TryHandleFrame(frame, plaintext.Counter, out payloadReceived);
                case PNetMeshPayloadFrameKind.IPv4:
                case PNetMeshPayloadFrameKind.IPv6:
                    if (!PNetMeshPayloadFraming.TryRead(frame, out var payloadFrame, out _))
                        return false;

                    PNetMeshPacketTrace.RecordPacket(
                        kind == PNetMeshPayloadFrameKind.IPv4
                            ? PNetMeshPacketTraceStage.PlaintextIPv4Detected
                            : PNetMeshPacketTraceStage.PlaintextIPv6Detected,
                        frame,
                        payloadFrame.TotalLength);
                    rawIpFrame = new RawIpFrameReceive(
                        kind,
                        payloadFrame.TotalLength,
                        _rawIpFrameSink,
                        _inboundWriter);
                    return true;
                default:
                    return false;
            }
        }

        bool TryHandleRawIpFrame(
            ReadOnlySpan<byte> frame,
            PNetMeshPayloadFrameKind kind,
            IPNetMeshRawIpFrameSink? sink,
            ChannelWriter<ReadOnlyMemory<byte>>? inboundWriter,
            out bool payloadReceived)
        {
            payloadReceived = false;
            if (sink is not null)
            {
                var result = kind == PNetMeshPayloadFrameKind.IPv4
                    ? sink.TryReceiveIPv4(frame)
                    : sink.TryReceiveIPv6(frame);

                switch (result)
                {
                    case PNetMeshRawIpFrameSinkResult.Delivered:
                        payloadReceived = true;
                        return true;
                    case PNetMeshRawIpFrameSinkResult.Consumed:
                        return true;
                    case PNetMeshRawIpFrameSinkResult.FallbackToChannel:
                        break;
                    default:
                        return false;
                }
            }

            var packet = frame.ToArray();

            if (inboundWriter is null || !inboundWriter.TryWrite(packet))
                return false;

            PNetMeshPacketTrace.RecordPacket(
                PNetMeshPacketTraceStage.InboundFallbackQueued,
                frame,
                frame.Length,
                1);
            payloadReceived = true;
            return true;
        }

        bool IPNetMeshReliableControlPacketHandler.TryHandleEnvelope(PNetMeshReliableEnvelope reliableEnvelope, ulong counter, out bool payloadReceived)
        {
            payloadReceived = false;
            var envelope = reliableEnvelope.Envelope;
            ProcessAck(envelope);

            if (_status != PNetMeshSessionStatus.Open)
                return false;

            if (counter > _receiveCounter)
            {
                if (envelope.DoNotAck && HasPayload(envelope))
                {
                    payloadReceived |= ProcessEnvelope(reliableEnvelope);
                    UpdateAckTimer(envelope);
                    return true;
                }

                MarkAckRequired(envelope, counter);
                _pendingPackets[counter] = reliableEnvelope;
                _outOfOrder_count = _pendingPackets.Count;
                if (AllowOutOfOrderPayloadDelivery && HasPayload(envelope))
                {
                    payloadReceived |= ProcessEnvelope(reliableEnvelope);
                    _deliveredOutOfOrderPackets.Add(counter);
                }
                UpdateAckTimer(envelope);
                return true;
            }

            if (counter < _receiveCounter)
                return false;

            MarkAckRequired(envelope, counter);
            payloadReceived |= ProcessEnvelope(reliableEnvelope);
            _receiveCounter++;

            while (_pendingPackets.TryGetValue(_receiveCounter, out var pendingPacket))
            {
                _pendingPackets.Remove(_receiveCounter);
                if (!_deliveredOutOfOrderPackets.Remove(_receiveCounter))
                    payloadReceived |= ProcessEnvelope(pendingPacket);
                _receiveCounter++;
            }

            _outOfOrder_count = _pendingPackets.Count;
            UpdateAckTimer(envelope);
            return true;
        }

        bool IPNetMeshReliableControlPacketHandler.TryHandleBody(
            EnvelopeBody body,
            ReadOnlyMemory<byte> bodyPayload,
            ulong counter,
            out bool payloadReceived)
        {
            payloadReceived = false;
            if (_status != PNetMeshSessionStatus.Open)
                return false;

            return ProcessBody(body, bodyPayload, out payloadReceived);
        }

        void UpdateAckTimer(ReliableEnvelope packet)
        {
            var ackTimeout = _cumAck_timeout;
            var pendingAckCount = _receiveAckRequired > _receiveAck
                ? _receiveAckRequired - _receiveAck
                : 0;

            if (packet.DoNotAck && pendingAckCount == 0 && _pendingPackets.Count == 0)
                ackTimeout = Timeout.Infinite;
            if ((int)pendingAckCount >= _cumAck_max)
                ackTimeout = 0;
            if (_outOfOrder_count >= _outOfOrder_max || _pendingPackets.Count > 0)
                ackTimeout = 0;

            _cumAckTimer?.Change(ackTimeout, Timeout.Infinite);
        }

        void MarkAckRequired(ReliableEnvelope packet, ulong counter)
        {
            if (!packet.DoNotAck)
                _receiveAckRequired = Math.Max(_receiveAckRequired, counter + 1);
        }

        bool ProcessEnvelope(PNetMeshReliableEnvelope reliableEnvelope)
        {
            var envelope = reliableEnvelope.Envelope;
            var bodyTail = reliableEnvelope.BodyTail;
            var payloadTailOffset = 0;
            var payloadReceived = false;
            if (envelope.Syn is not null)
            {
                var syn = envelope.Syn;

                _cumAck_max = syn.MaxCumulativeAcks;
                _outOfOrder_max = syn.MaxOutOfSequence;
                if (syn.MaxOutstandingSeq > 0)
                    _outstanding_max = syn.MaxOutstandingSeq;
                if (syn.MaxPacketSize > 0)
                    _packetSize_max = syn.MaxPacketSize;

                if (syn.RetransmissionTimeoutMs > 0)
                    _retrans_timeout = Math.Max(syn.RetransmissionTimeoutMs, 100);
                if (syn.CumulativeAckTimeoutMs > 0)
                    _cumAck_timeout = Math.Max(syn.CumulativeAckTimeoutMs, 100);
            }

            if (envelope.Probe is not null)
            {
                var probe = envelope.Probe;
            }

            if (envelope.ProbeReply is not null)
            {
                var reply = envelope.ProbeReply;
            }

            foreach (var body in envelope.Bodies)
            {
                if (!TryReadPayloadBody(body.Metadata, bodyTail, ref payloadTailOffset, out var bodyPayload))
                    return false;
                if (!ProcessBody(body, bodyPayload, out var bodyReceived))
                    return false;
                payloadReceived |= bodyReceived;
            }

            if (envelope.Reset)
            {

            }

            if (envelope.CandidateExchange is not null)
            {
                var ice = envelope.CandidateExchange;
            }

            if (envelope.SendTime is not null)
            {
                var ts = envelope.SendTime.ToDateTime();
            }

            return payloadTailOffset == bodyTail.Length && payloadReceived;
        }

        bool ProcessBody(EnvelopeBody body, ReadOnlyMemory<byte> bodyPayload, out bool bodyReceived)
        {
            bodyReceived = false;
            if (!CanReadPayloadData(body.Metadata))
            {
                //compressed payloads are not supported yet
                return true;
            }

            if (body.Forward is null)
            {
                if (_inboundWriter is not null && _inboundWriter.TryWrite(bodyPayload))
                {
                    bodyReceived = true;
                    return true;
                }

                //log write inbound error
                return true;
            }

            var forward = body.Forward;
            var (lite, checkPacing, userPass) = (false, 0u, string.Empty);
            var candidates = ImmutableArray.CreateBuilder<PNetMeshCandidate>(forward.CandidateExchange?.Candidates.Count ?? 1);

            if (forward.CandidateExchange is not null)
            {
                var ice = forward.CandidateExchange;
                (lite, checkPacing, userPass) = (ice.Lite, ice.CheckPacingMs, CombineIceUserPass(ice));

                foreach (var candidate in ice.Candidates)
                    candidates.Add(new PNetMeshCandidate
                    {
                        Address = PNetMeshUtils.MapToItem(candidate.Endpoint),
                        Protocol = MapCandidateProtocol(candidate.Protocol),
                        Type = MapCandidateType(candidate.Type),
                        Base = PNetMeshUtils.MapToItem(candidate.RelatedEndpoint),
                        ComponentId = candidate.ComponentId > 0
                            ? (byte)candidate.ComponentId : (byte)1,
                        Foundation = candidate.Foundation,
                        Priority = candidate.Priority
                    });
            }

            if (forward.Route.Count == 1)
            {
                //tmp add RemoteEndPoint as candidate

                //todo sending peer need to add peer reflexive form learned from probe
                if (!candidates.Any(n => n.Address is not null && n.Address.Equals(_remoteEndPoint)))
                {
                    candidates.Add(new PNetMeshCandidate
                    {
                        Address = _remoteEndPoint,
                        Protocol = PNetMeshProtocolType.UDP,
                        Type = PNetMeshCandidateType.ServerReflexive,
                        Base = null, //todo incorrect
                        ComponentId = 1,
                        Foundation = $"srflx-no_base-{Convert.ToBase64String(LocalAddress)}-udp",
                        Priority = 100
                    });
                }
            }

            var route = ImmutableArray.CreateBuilder<byte[]>(forward.Route.Count + 1);

            foreach (var r in forward.Route)
                route.Add(r.Address.ToByteArray());

            //add local address
            if (route.Count == 0 || !PNetMeshByteArrayComparer.Default.Equals(route[^1], LocalAddress))
                route.Add(LocalAddress);

            var msg = new PNetMeshOutboundMessages.Relay
            {
                Packet = new PNetMeshRelayPacket
                {
                    Address = forward.Destination?.Address.ToByteArray() ?? Array.Empty<byte>(),
                    SeqNumber = forward.SequenceNumber,
                    HopCount = forward.HopLimit > 0 ? (ushort)(forward.HopLimit - 1) : (ushort)0,
                    Route = route.ToImmutable(),
                    Payload = bodyPayload,
                    CandidateExchange = new PNetMeshCandidateExchange
                    {
                        Lite = lite,
                        CheckPacing = checkPacing,
                        UserPass = userPass,
                        Candidates = candidates.ToImmutable()
                    }
                }
            };

            if (!_outboundWriter.TryWrite(msg))
            {
                //log write outbound error
            }

            return true;
        }

        static bool TryReadPayloadBody(
            ProtoMetadata? metadata,
            ReadOnlyMemory<byte> payloadTail,
            ref int payloadTailOffset,
            out ReadOnlyMemory<byte> payloadBody)
        {
            payloadBody = ReadOnlyMemory<byte>.Empty;
            if (metadata is null)
                return true;

            if (!TryGetPayloadBodySize(metadata, out var payloadBodySize))
                return false;
            if (payloadTailOffset > payloadTail.Length - payloadBodySize)
                return false;

            payloadBody = payloadTail.Slice(payloadTailOffset, payloadBodySize);
            payloadTailOffset += payloadBodySize;
            return true;
        }

        static bool TryGetPayloadBodySize(ProtoMetadata metadata, out int payloadBodySize)
        {
            var size = metadata.CompressedSize != 0
                ? metadata.CompressedSize
                : metadata.PayloadSize;

            if (size > int.MaxValue)
            {
                payloadBodySize = 0;
                return false;
            }

            payloadBodySize = (int)size;
            return true;
        }

        static bool CanReadPayloadData(ProtoMetadata? metadata)
        {
            return metadata is null
                || metadata.DictionaryRefCase == global::PNet.Protos.ProtoMetadata.DictionaryRefOneofCase.None
                || (metadata.DictionaryRefCase == global::PNet.Protos.ProtoMetadata.DictionaryRefOneofCase.DictionaryId
                    && metadata.DictionaryId == 0);
        }

        static bool HasPayload(ReliableEnvelope packet)
        {
            return packet.Bodies.Count > 0;
        }

        static int GetRawIpFrameLength(ReadOnlySpan<byte> frame, string paramName)
        {
            if (!PNetMeshPayloadFraming.TryRead(frame, out var payloadFrame, out _)
                || (payloadFrame.Kind != PNetMeshPayloadFrameKind.IPv4 && payloadFrame.Kind != PNetMeshPayloadFrameKind.IPv6))
            {
                throw new ArgumentException("Frame must be a valid IPv4 or IPv6 packet.", paramName);
            }

            return payloadFrame.TotalLength;
        }

        static async Task WriteOutboundPacketAsync(
            ChannelWriter<PNetMeshOutboundMessages.Message> writer,
            PNetMeshOutboundMessages.Packet packet,
            ILogger logger)
        {
            var queued = false;
            try
            {
                await writer.WriteAsync(packet);
                queued = true;
            }
            catch (Exception ex) when (ex is ChannelClosedException or InvalidOperationException)
            {
                logger.LogDebug(ex, "dropping raw frame because the outbound channel is closed");
            }
            finally
            {
                if (!queued)
                    packet.MemoryOwner?.Dispose();
            }
        }

        void ProcessAck(ReliableEnvelope packet)
        {
            if (packet.Ack is null)
                return;

            var ack = packet.Ack;
            var outOfOrder = ack.OutOfSequencePackets.Memory;
            var remoteAck = new Ack
            {
                SeqNumber = ack.AckSeqNumber,
                OutOfOrder = outOfOrder
            };

            ulong retransmitBase;
            var ackAdvanced = _remoteAck.SeqNumber < ack.AckSeqNumber;
            var outOfOrderChanged = _remoteAck.SeqNumber == ack.AckSeqNumber
                && !_remoteAck.OutOfOrder.Span.SequenceEqual(outOfOrder.Span);

            if (!ackAdvanced && !outOfOrderChanged)
                return;

            if (remoteAck.SeqNumber > _retransBuffer.Current + 1)
                return;

            if (remoteAck.SeqNumber > 0)
                _retransBuffer.RemoveUntil(remoteAck.SeqNumber - 1);
            if (remoteAck.OutOfOrder.Length > 0)
                _retransBuffer.RemoveSequence(remoteAck.OutOfOrder);
            retransmitBase = _retransBuffer.Latest;

            _remoteAck = remoteAck;

            if (remoteAck.OutOfOrder.Length > 0)
            {
                var retransmitAck = remoteAck;
                QueueControl(new PNetMeshChannelCommands.Invoke
                {
                    Handler = () => RetransmitPackets(retransmitAck, retransmitBase)
                });
            }

            UpdateRetransmissionTimer();

            QueueControl(new PNetMeshChannelCommands.Invoke
            {
                Handler = FlushOpenPacket
            });
        }

        void QueueControl(PNetMeshChannelCommands.Invoke command)
        {
            var startDrain = false;
            var overflow = false;
            Exception? failException = null;
            ChannelWriter<PNetMeshChannelCommands.Command>? writer;
            lock (_sessionOwnerLock)
            {
                if (_disposing || _status != PNetMeshSessionStatus.Open)
                    return;

                writer = _controlWriter;
                if (writer is null)
                {
                    failException = new InvalidOperationException("Control channel is not available.");
                }
                else if (!_controlQueueDraining && _pendingControlCommands.Count == 0 && writer.TryWrite(command))
                {
                    return;
                }
                else if (_pendingControlCommands.Count >= MaxPendingControlCommands)
                {
                    _pendingControlCommands.Clear();
                    overflow = true;
                }
                else
                {
                    _pendingControlCommands.Enqueue(command);
                    if (!_controlQueueDraining)
                    {
                        _controlQueueDraining = true;
                        startDrain = true;
                    }
                }
            }

            if (failException is not null)
            {
                FailControlQueue(failException);
                return;
            }

            if (overflow)
            {
                FailControlQueue(new InvalidOperationException("Control channel backlog exceeded."));
                return;
            }

            if (startDrain && writer is not null)
                _ = DrainControlQueueAsync(writer);
        }

        async Task DrainControlQueueAsync(ChannelWriter<PNetMeshChannelCommands.Command> writer)
        {
            try
            {
                while (true)
                {
                    PNetMeshChannelCommands.Invoke command;
                    lock (_sessionOwnerLock)
                    {
                        if (_pendingControlCommands.Count == 0)
                        {
                            _controlQueueDraining = false;
                            return;
                        }

                        command = _pendingControlCommands.Peek();
                    }

                    await writer.WriteAsync(command);

                    lock (_sessionOwnerLock)
                    {
                        if (_pendingControlCommands.Count > 0 && ReferenceEquals(_pendingControlCommands.Peek(), command))
                            _pendingControlCommands.Dequeue();
                    }
                }
            }
            catch (Exception ex) when (ex is ChannelClosedException or InvalidOperationException)
            {
                lock (_sessionOwnerLock)
                {
                    _pendingControlCommands.Clear();
                    _controlQueueDraining = false;
                }

                FailControlQueue(ex);
            }
        }

        void FailControlQueue(Exception exception)
        {
            TaskCompletionSource?[]? results;
            lock (_sessionOwnerLock)
            {
                if (_disposing || _status == PNetMeshSessionStatus.Disposed)
                    return;

                Status = PNetMeshSessionStatus.Closed;
                results = FailOpenPacket();
            }
            CompleteOpenPacketResults(results, exception);
        }

        Ack GetRemoteAckSnapshot()
        {
            lock (_sessionOwnerLock)
                return _remoteAck;
        }

        ulong GetRemoteAckSeqNumber()
        {
            lock (_sessionOwnerLock)
                return _remoteAck.SeqNumber;
        }

        bool HasSendWindow()
        {
            return _retransBuffer.Count < _outstanding_max;
        }

        bool HasOpenPacket()
        {
            return _openEnvelope.Bodies.Count > 0;
        }

        bool HasPendingAck()
        {
            return _receiveAck < _receiveAckRequired || _pendingPackets.Count > 0;
        }

        void UpdateRetransmissionTimer()
        {
            var dueTime = Timeout.Infinite;
            if (!_disposing && _status == PNetMeshSessionStatus.Open && _retransBuffer.Count > 0)
                dueTime = _retrans_timeout;

            try
            {
                _retransTimer?.Change(dueTime, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        void EnsurePacketSize(int packetSize, string paramName)
        {
            if (packetSize <= _packetSize_max)
                return;

            throw new ArgumentOutOfRangeException(paramName, "Packet exceeds negotiated max packet size.");
        }

        static EnvelopeBody CreateBody(int bodyLength)
        {
            return new EnvelopeBody
            {
                Metadata = CreatePayloadMetadata(bodyLength)
            };
        }

        static EnvelopeBody CreateForwardBody(PNetMeshRelayPacket packet)
        {
            //maybe use shared proto3 message instead of PNetMeshRelayPacket
            var item = new EnvelopeBody
            {
                Metadata = CreatePayloadMetadata(packet.Payload.Length),
                Forward = new Forward
                {
                    Destination = new MeshProtos.MeshEndpoint
                    {
                        Address = ByteString.CopyFrom(packet.Address)
                    },
                    SequenceNumber = packet.SeqNumber,
                    HopLimit = packet.HopCount
                }
            };

            if (packet.CandidateExchange is not null)
            {
                var exg = packet.CandidateExchange;

                var m = new MeshProtos.CandidateExchange
                {
                    Lite = exg.Lite,
                    CheckPacingMs = exg.CheckPacing
                };

                if (!string.IsNullOrEmpty(exg.UserPass))
                {
                    SplitIceUserPass(exg.UserPass, out var usernameFragment, out var password);
                    m.UsernameFragment = usernameFragment;
                    m.Password = password;
                }

                foreach (var c in exg.Candidates)
                {
                    var address = PNetMeshUtils.MapToProtos(c.Address);
                    if (address is null)
                        continue;

                    var candidate = new Candidate
                    {
                        Endpoint = address,
                        ComponentId = c.ComponentId,
                        Foundation = c.Foundation ?? string.Empty,
                        Priority = c.Priority,
                        Protocol = MapCandidateProtocol(c.Protocol),
                        Type = MapCandidateType(c.Type)
                    };

                    var relatedAddress = PNetMeshUtils.MapToProtos(c.Base);
                    if (relatedAddress is not null)
                        candidate.RelatedEndpoint = relatedAddress;

                    m.Candidates.Add(candidate);
                }

                item.Forward.CandidateExchange = m;
            }

            foreach (var r in packet.Route)
            {
                item.Forward.Route.Add(new MeshProtos.MeshEndpoint
                {
                    Address = ByteString.CopyFrom(r)
                });
            }

            return item;
        }

        static string CombineIceUserPass(MeshProtos.CandidateExchange exchange)
        {
            if (exchange.UsernameFragment.Length == 0)
                return exchange.Password;
            if (exchange.Password.Length == 0)
                return exchange.UsernameFragment;

            return $"{exchange.UsernameFragment}:{exchange.Password}";
        }

        static void SplitIceUserPass(string userPass, out string usernameFragment, out string password)
        {
            var separator = userPass.IndexOf(':');
            if (separator < 0)
            {
                usernameFragment = userPass;
                password = string.Empty;
                return;
            }

            usernameFragment = userPass[..separator];
            password = userPass[(separator + 1)..];
        }

        static PNetMeshProtocolType MapCandidateProtocol(TransportProtocol protocol)
        {
            return protocol switch
            {
                TransportProtocol.Udp => PNetMeshProtocolType.UDP,
                TransportProtocol.Tcp => PNetMeshProtocolType.TCP,
                _ => PNetMeshProtocolType.UDP
            };
        }

        static TransportProtocol MapCandidateProtocol(PNetMeshProtocolType protocol)
        {
            return protocol switch
            {
                PNetMeshProtocolType.UDP => TransportProtocol.Udp,
                PNetMeshProtocolType.TCP => TransportProtocol.Tcp,
                _ => TransportProtocol.Unspecified
            };
        }

        static PNetMeshCandidateType MapCandidateType(CandidateType type)
        {
            return type switch
            {
                CandidateType.Host => PNetMeshCandidateType.Host,
                CandidateType.ServerReflexive => PNetMeshCandidateType.ServerReflexive,
                CandidateType.PeerReflexive => PNetMeshCandidateType.PeerReflexive,
                CandidateType.Relayed => PNetMeshCandidateType.Relayed,
                _ => PNetMeshCandidateType.Host
            };
        }

        static CandidateType MapCandidateType(PNetMeshCandidateType type)
        {
            return type switch
            {
                PNetMeshCandidateType.Host => CandidateType.Host,
                PNetMeshCandidateType.ServerReflexive => CandidateType.ServerReflexive,
                PNetMeshCandidateType.PeerReflexive => CandidateType.PeerReflexive,
                PNetMeshCandidateType.Relayed => CandidateType.Relayed,
                _ => CandidateType.Unspecified
            };
        }

        static ProtoMetadata CreatePayloadMetadata(int payloadLength)
        {
            return new ProtoMetadata
            {
                PayloadSize = (uint)payloadLength,
                CompressedSize = (uint)payloadLength
            };
        }

        static int CalculatePNetFrameSize(
            ReliableEnvelope packet,
            IReadOnlyList<PendingPayload> bodyPayloads)
        {
            return CalculateReliableFrameLayout(packet, bodyPayloads).FrameSize;
        }
    }
}
