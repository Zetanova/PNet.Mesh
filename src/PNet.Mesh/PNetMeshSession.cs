using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Protos = PNet.Actor.Mesh.Protos;

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

    public sealed class PNetMeshSession : IDisposable
    {
        private readonly struct Ack
        {
            public ulong SeqNumber { get; init; }

            public ReadOnlyMemory<byte> OutOfOrder { get; init; }
        }

        readonly PNetMeshProtocol _protocol;

        readonly ChannelWriter<PNetMeshOutboundMessages.Message> _outboundWriter;

        readonly ILogger _logger;

        readonly PNetMeshPacketBuffer _retransBuffer;
        // multi-threading: receive-side ACK processing removes retransmit entries while the control loop counts, adds, and enumerates them for send-window gating.
        readonly object _retransBufferLock = new object();

        PNetMeshHandshake _handshake;

        PNetMeshTransport2 _transport;

        // multi-threading: relay hints/promotions arrive on the server receive/control loop while
        // channel writes and retransmits consume direct-probe state on session send paths.
        readonly object _endpointDiscoveryLock = new object();

        readonly PNetMeshWireGuardEndpointDiscovery _endpointDiscovery =
            new PNetMeshWireGuardEndpointDiscovery(null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        public EndPoint LocalEndPoint { get; set; }

        public EndPoint RemoteEndPoint { get; set; }

        PNetMeshSessionStatus _status;
        public PNetMeshSessionStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public byte[] LocalPublicKey => _handshake.LocalPublicKey;

        public byte[] LocalAddress { get; private set; } = Array.Empty<byte>();

        public byte[] RemotePublicKey => _handshake.RemotePublicKey;

        public byte[] RemoteAddress { get; private set; } = Array.Empty<byte>();

        public long Timestamp => _handshake.Timestamp;

        public uint SenderIndex => _handshake.SenderIndex;

        internal EndPoint PendingDirectEndpoint => _endpointDiscovery.CandidateEndpoint;

        internal bool AllowOutOfOrderPayloadDelivery { get; set; }

        internal bool UnreliablePayloadDelivery { get; set; }

        internal bool SupportsDirectEndpointDiscovery => true;

        internal bool TryApplyDirectEndpointHint(EndPoint endpoint, DateTimeOffset now)
        {
            if (!SupportsDirectEndpointDiscovery)
                return false;

            lock (_endpointDiscoveryLock)
                return _endpointDiscovery.DirectEndpoint is null
                       && _endpointDiscovery.TryApplyEndpointHint(endpoint, now);
        }

        internal bool TryPromoteDirectEndpoint(EndPoint endpoint, DateTimeOffset now)
        {
            if (!SupportsDirectEndpointDiscovery)
                return false;

            lock (_endpointDiscoveryLock)
            {
                if (!_endpointDiscovery.TryPromoteAuthenticatedDirectEndpoint(endpoint, now))
                    return false;

                RemoteEndPoint = endpoint;
                return true;
            }
        }

        internal void ConfirmRemoteEndpoint(EndPoint endpoint)
        {
            if (endpoint is null)
                return;

            lock (_endpointDiscoveryLock)
            {
                RemoteEndPoint = endpoint;
                _endpointDiscovery.FallbackToRelay();
            }
        }

        internal bool TryGetDirectProbeEndpoint(DateTimeOffset now, out EndPoint endpoint)
        {
            if (!SupportsDirectEndpointDiscovery)
            {
                endpoint = null;
                return false;
            }

            lock (_endpointDiscoveryLock)
            {
                endpoint = null;
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
        }


        public event EventHandler StatusChanged;

        internal event EventHandler MessageReceived;

        //public PNetMeshChannel Channel { get; set; }

        ChannelWriter<ReadOnlyMemory<byte>> _inboundWriter;
        ChannelWriter<PNetMeshChannelCommands.Command> _controlWriter;
        readonly object _controlQueueLock = new object();
        readonly Queue<PNetMeshChannelCommands.Invoke> _pendingControlCommands = new Queue<PNetMeshChannelCommands.Invoke>();
        bool _controlQueueDraining;
        const int MaxPendingControlCommands = 32;

        // multi-threading: Dispose can fail pending sends while the channel control loop stages or flushes open-packet contents.
        readonly object _openPacketLock = new object();
        Protos.Packet _openPacket;
        readonly List<TaskCompletionSource> _openPacketResults = new List<TaskCompletionSource>();
        bool _disposing;

        // multi-threading: server receive handling updates remote ACKs while the channel control/timer loop reads them for payload gating and retransmission.
        readonly object _remoteAckLock = new object();
        Ack _remoteAck = new Ack { SeqNumber = 0, OutOfOrder = ReadOnlyMemory<byte>.Empty };

        // multi-threading: server receive handling mutates receive counters/pending packets while the channel control/timer loop calls WritePacket to emit ACKs.
        readonly object _receiveStateLock = new object();
        ulong _receiveCounter = 0;
        ulong _receiveAck = 0;
        ulong _receiveAckRequired = 0;
        readonly SortedDictionary<ulong, Protos.Packet> _pendingPackets = new SortedDictionary<ulong, Protos.Packet>();
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

        Timer _retransTimer;
        Timer _cumAckTimer;

        internal PNetMeshSession(PNetMeshProtocol protocol, ChannelWriter<PNetMeshOutboundMessages.Message> writer, ILogger logger = null)
        {
            _protocol = protocol;
            _outboundWriter = writer;
            _logger = logger ?? NullLogger<PNetMeshSession>.Instance;
            _retransBuffer = new PNetMeshPacketBuffer();
        }

        public void Dispose()
        {
            var exception = new ObjectDisposedException(nameof(PNetMeshSession));
            TaskCompletionSource[] results;
            lock (_openPacketLock)
            {
                _disposing = true;
                Status = PNetMeshSessionStatus.Disposed;
                results = FailOpenPacket();
            }
            CompleteOpenPacketResults(results, exception);

            _cumAckTimer?.Dispose();
            _retransTimer?.Dispose();

            _handshake?.Dispose();
            _transport?.Dispose();
        }

        internal void AttachTo(ChannelWriter<ReadOnlyMemory<byte>> inboundWriter, ChannelWriter<PNetMeshChannelCommands.Command> controlWriter)
        {
            _inboundWriter = inboundWriter;
            _controlWriter = controlWriter;

            _retransTimer = new Timer(OnRetransTimeout, null, Timeout.Infinite, Timeout.Infinite);
            _cumAckTimer = new Timer(OnCumAckTimeout, null, Timeout.Infinite, Timeout.Infinite);
        }

        void OnRetransTimeout(object state)
        {
            //var writer = state as ChannelWriter<PNetMeshChannelCommands.Command>;
            lock (_openPacketLock)
            {
                if (_disposing || Status != PNetMeshSessionStatus.Open)
                    return;
            }

            QueueControl(new PNetMeshChannelCommands.Invoke
            {
                Handler = RetransmitPackets
            });
        }

        void OnCumAckTimeout(object state)
        {
            //var writer = state as ChannelWriter<PNetMeshChannelCommands.Command>;
            lock (_openPacketLock)
            {
                if (_disposing || Status != PNetMeshSessionStatus.Open)
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
            RemoteAddress = remoteAddress;

            _handshake = _protocol.CreateInitiator(senderIndex, remotePublicKey);

            var localAddress = new byte[10]; //todo set local address external
            PNetMeshUtils.GetAddressFromPublicKey(LocalPublicKey, localAddress);
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
                LocalEndPoint = LocalEndPoint,
                LocalAddress = LocalAddress,
                RemoteEndPoint = RemoteEndPoint, //maybe use router
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

        public void ReadInitialize(uint senderIndex, ReadOnlySpan<byte> payload)
        {
            TryReadInitialize(senderIndex, payload);
        }

        public bool TryReadInitialize(uint senderIndex, ReadOnlySpan<byte> payload)
        {
            _handshake = _protocol.CreateResponder(senderIndex);

            var localAddress = new byte[10]; //todo set local address external
            PNetMeshUtils.GetAddressFromPublicKey(LocalPublicKey, localAddress);
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

        public void WriteResponse()
        {
            var buffer = MemoryPool<byte>.Shared.Rent(PNetMeshHandshake.ResponseMessageSize);

            if (!_handshake.TryWriteResponseMessage(buffer.Memory.Span,
                out var bytesWritten, out _transport))
            {
                //invalid noise sequence
                buffer.Dispose();
                Status = PNetMeshSessionStatus.Closed;
                return;
            }
            Debug.Assert(bytesWritten == PNetMeshHandshake.ResponseMessageSize);

            Status = PNetMeshSessionStatus.Opening;
            var openAfterResponse = RemoteEndPoint is not null;
            if (openAfterResponse)
            {
                lock (_openPacketLock)
                {
                    if (_disposing || Status == PNetMeshSessionStatus.Disposed || Status == PNetMeshSessionStatus.Closed)
                    {
                        buffer.Dispose();
                        _transport?.Dispose();
                        return;
                    }

                    _openPacket = new Protos.Packet();
                }
            }

            //send response message
            var item = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = buffer,
                MemoryBuffer = buffer.Memory.Slice(0, bytesWritten),
                LocalEndPoint = LocalEndPoint,
                LocalAddress = LocalAddress,
                RemoteEndPoint = RemoteEndPoint,
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

        public void ReadResponse(ReadOnlySpan<byte> payload)
        {
            TryReadResponse(payload);
        }

        public bool TryReadResponse(ReadOnlySpan<byte> payload)
        {
            if (!_handshake.TryReadResponseMessage(payload, out _transport))
            {
                //invalid session
                Status = PNetMeshSessionStatus.Closed;
                return false;
            }

            lock (_openPacketLock)
            {
                if (_disposing || Status == PNetMeshSessionStatus.Disposed || Status == PNetMeshSessionStatus.Closed)
                {
                    _transport?.Dispose();
                    return false;
                }

                _openPacket = new Protos.Packet();
                Status = PNetMeshSessionStatus.Open;
            }

            return true;
        }

        public void WritePayload(ReadOnlySpan<byte> payload)
        {
            WritePayload(payload, null);
        }

        public void WritePayload(ReadOnlySpan<byte> payload, TaskCompletionSource result)
        {
            WritePayload(payload, result, UnreliablePayloadDelivery);
        }

        internal void WritePayload(ReadOnlySpan<byte> payload, TaskCompletionSource result, bool unreliablePayloadDelivery)
        {
            TaskCompletionSource[] results = null;
            Exception resultException = null;
            try
            {
                lock (_openPacketLock)
                {
                    ThrowIfDisposing();

                    var item = new Protos.Payload();
                    item.Raw = ByteString.CopyFrom(payload);

                    var itemIndex = _openPacket.Payload.Count;
                    var resultIndex = _openPacketResults.Count;
                    _openPacket.Payload.Add(item);
                    _openPacketResults.Add(result);

                    try
                    {
                        EnsurePacketSize(CalculatePNetFrameSize(_openPacket), nameof(payload));

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
                        if (_openPacket.Payload.Count > itemIndex)
                            _openPacket.Payload.RemoveAt(itemIndex);
                        if (_openPacketResults.Count > resultIndex)
                            _openPacketResults.RemoveAt(resultIndex);
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
                CompleteOpenPacketResults(results, resultException);
            }
        }

        public void WriteRelay(PNetMeshRelayPacket packet)
        {
            WriteRelay(packet, null);
        }

        public void WriteRelay(PNetMeshRelayPacket packet, TaskCompletionSource result)
        {
            TaskCompletionSource[] results = null;
            Exception resultException = null;
            try
            {
                lock (_openPacketLock)
                {
                    ThrowIfDisposing();

                    //maybe use shared proto3 message instead of PNetMeshRelayPacket
                    var item = new Protos.Relay
                    {
                        Address = new Protos.MeshEndPoint
                        {
                            Hash = ByteString.CopyFrom(packet.Address)
                        },
                        SeqNumber = packet.SeqNumber,
                        HopCount = packet.HopCount
                    };

                    if (packet.CandidateExchange is not null)
                    {
                        var exg = packet.CandidateExchange;

                        var m = new Protos.CandidateExchange
                        {
                            Lite = exg.Lite,
                            CheckPacing = exg.CheckPacing
                        };

                        if (!string.IsNullOrEmpty(exg.UserPass))
                            m.UserPass = exg.UserPass;

                        foreach (var c in exg.Candidates)
                            m.Candidates.Add(new Protos.Candidate
                            {
                                Address = PNetMeshUtils.MapToProtos(c.Address),
                                RelatedAddress = PNetMeshUtils.MapToProtos(c.Base),
                                ComponentId = c.ComponentId,
                                Foundation = c.Foundation,
                                Priority = c.Priority,
                                Protocol = (Protos.Candidate.Types.Protocol)c.Protocol,
                                Type = (Protos.Candidate.Types.Type)c.Type

                            });

                        item.CandidateExchange = m;
                    }

                    foreach (var r in packet.Route)
                    {
                        item.Route.Add(new Protos.MeshEndPoint
                        {
                            Hash = ByteString.CopyFrom(r)
                        });
                    }

                    item.Packet = ByteString.CopyFrom(packet.Payload.Span);

                    var itemIndex = _openPacket.Relay.Count;
                    var resultIndex = _openPacketResults.Count;
                    _openPacket.Relay.Add(item);
                    _openPacketResults.Add(result);

                    try
                    {
                        EnsurePacketSize(CalculatePNetFrameSize(_openPacket), nameof(packet));

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
                        if (_openPacket.Relay.Count > itemIndex)
                            _openPacket.Relay.RemoveAt(itemIndex);
                        if (_openPacketResults.Count > resultIndex)
                            _openPacketResults.RemoveAt(resultIndex);
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
                CompleteOpenPacketResults(results, resultException);
            }
        }

        void FlushOpenPacket()
        {
            TaskCompletionSource[] results = null;
            Exception resultException = null;
            try
            {
                lock (_openPacketLock)
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
            TaskCompletionSource[] results = null;
            Exception resultException = null;
            try
            {
                lock (_openPacketLock)
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
            TaskCompletionSource[] results = null;
            Exception resultException = null;
            try
            {
                lock (_openPacketLock)
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

        TaskCompletionSource[] WritePacket(string sizeParamName)
        {
            return WritePacket(sizeParamName, trackForRetransmit: true);
        }

        TaskCompletionSource[] WritePacket(string sizeParamName, bool trackForRetransmit)
        {
            return WritePacket(_openPacket.Clone(), clearOpenPacket: true, sizeParamName, trackForRetransmit);
        }

        void WriteControlPacket()
        {
            WritePacket(new Protos.Packet(), clearOpenPacket: false, "packet", trackForRetransmit: false);
        }

        TaskCompletionSource[] WritePacket(Protos.Packet packet, bool clearOpenPacket, string sizeParamName, bool trackForRetransmit = true)
        {
            ThrowIfDisposing();
            if (Status != PNetMeshSessionStatus.Open)
                throw new InvalidOperationException("session not open");

            var packetSize = PreparePacketForWrite(packet, sizeParamName, trackForRetransmit);
            TryGetDirectProbeEndpoint(DateTimeOffset.UtcNow, out var directProbeEndPoint);

            var frameSize = PNetMeshPayloadFraming.CalculatePNetFrameSize(packetSize);
            using var frameBuffer = MemoryPool<byte>.Shared.Rent(frameSize);

            var frame = frameBuffer.Memory.Span.Slice(0, frameSize);
            packet.WriteTo(frame.Slice(1, packetSize));
            if (!PNetMeshPayloadFraming.TryWritePNet(frame, packetSize, out var frameBytesWritten))
                throw new InvalidOperationException("Unable to write PNet frame.");
            Debug.Assert(frameBytesWritten == frameSize);
            frame = frame.Slice(0, frameBytesWritten);

            if (trackForRetransmit)
            {
                lock (_retransBufferLock)
                {
                    var rented = false;
                    try
                    {
                        var buffer = _retransBuffer.Rent(packetSize);
                        rented = true;
                        _transport.WriteMessage(frame, buffer.Span, out var byteWritten, out var counter);
                        Debug.Assert(counter == _retransBuffer.Current);
                        var item = new PNetMeshOutboundMessages.Packet
                        {
                            MemoryOwner = null,
                            MemoryBuffer = _retransBuffer.SliceCurrent(byteWritten),
                            LocalEndPoint = LocalEndPoint,
                            LocalAddress = LocalAddress,
                            RemoteEndPoint = RemoteEndPoint,
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
            }
            else
            {
                var bufferOwner = MemoryPool<byte>.Shared.Rent(packetSize + 48);
                try
                {
                    var buffer = bufferOwner.Memory;
                    _transport.WriteMessage(frame, buffer.Span, out var byteWritten, out var counter);

                    var item = new PNetMeshOutboundMessages.Packet
                    {
                        MemoryOwner = bufferOwner,
                        MemoryBuffer = buffer.Slice(0, byteWritten),
                        LocalEndPoint = LocalEndPoint,
                        LocalAddress = LocalAddress,
                        RemoteEndPoint = RemoteEndPoint,
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

                    lock (_retransBufferLock)
                    {
                        _retransBuffer.AddUntracked(counter);
                    }

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

        void TryWriteDirectProbe(Memory<byte> packet, EndPoint directProbeEndPoint)
        {
            if (directProbeEndPoint is null)
                return;

            var bufferOwner = MemoryPool<byte>.Shared.Rent(packet.Length);
            packet.CopyTo(bufferOwner.Memory);
            var item = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = bufferOwner,
                MemoryBuffer = bufferOwner.Memory.Slice(0, packet.Length),
                LocalEndPoint = LocalEndPoint,
                LocalAddress = LocalAddress,
                RemoteEndPoint = directProbeEndPoint,
                RemoteAddress = RemoteAddress
            };

            if (!_outboundWriter.TryWrite(item))
                bufferOwner.Dispose();
        }

        void WriteOpenPacketOrFail(
            string sizeParamName,
            bool failBatchOnSizeError,
            bool trackForRetransmit,
            out TaskCompletionSource[] results,
            out Exception resultException)
        {
            results = null;
            resultException = null;
            try
            {
                results = WritePacket(sizeParamName, trackForRetransmit);
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

        TaskCompletionSource[] CompleteOpenPacket()
        {
            _openPacket = new Protos.Packet();
            return DetachOpenPacketResults();
        }

        TaskCompletionSource[] FailOpenPacket()
        {
            _openPacket = new Protos.Packet();
            return DetachOpenPacketResults();
        }

        TaskCompletionSource[] DetachOpenPacketResults()
        {
            if (_openPacketResults.Count == 0)
                return null;

            var results = _openPacketResults.ToArray();
            _openPacketResults.Clear();
            return results;
        }

        static void CompleteOpenPacketResults(TaskCompletionSource[] results, Exception exception)
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
            if (_disposing || Status == PNetMeshSessionStatus.Disposed || Status == PNetMeshSessionStatus.Closed)
                throw new ObjectDisposedException(nameof(PNetMeshSession));
        }

        int PreparePacketForWrite(Protos.Packet packet, string sizeParamName)
        {
            return PreparePacketForWrite(packet, sizeParamName, trackForRetransmit: true);
        }

        int PreparePacketForWrite(Protos.Packet packet, string sizeParamName, bool trackForRetransmit)
        {
            lock (_receiveStateLock)
            {
                ulong? receiveAck = null;
                if (_receiveAck < _receiveAckRequired || _pendingPackets.Count > 0)
                {
                    var ackSeqNumber = _receiveCounter;

                    Span<byte> bitmap = stackalloc byte[16];
                    _transport.Tracker.GetBitmap(ackSeqNumber, bitmap, out var bytesWritten);
                    bitmap = bitmap.Slice(0, bytesWritten);

                    ackSeqNumber += PNetMeshPacketTracker.RightShift(bitmap, out bytesWritten);
                    bitmap = bitmap.Slice(0, bytesWritten);

                    packet.Ack = new Protos.Ack
                    {
                        AckSeqNumber = ackSeqNumber,
                        OutOfSeqPackets = ByteString.CopyFrom(bitmap)
                    };
                    receiveAck = ackSeqNumber;
                }

                packet.DoNotAck = !trackForRetransmit || (packet.Payload.Count == 0 && packet.Relay.Count == 0);

                var packetSize = packet.CalculateSize();
                var frameSize = PNetMeshPayloadFraming.CalculatePNetFrameSize(packetSize);
                EnsurePacketSize(frameSize, sizeParamName);

                if (receiveAck.HasValue)
                {
                    _receiveAck = receiveAck.Value;
                    _cumAckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }

                return packetSize;
            }
        }

        void RetransmitPackets()
        {
            RetransmitPackets(GetRemoteAckSnapshot(), expectedLatest: null);
        }

        void RetransmitPackets(Ack ack, ulong? expectedLatest)
        {
            lock (_openPacketLock)
            {
                if (_disposing || Status != PNetMeshSessionStatus.Open)
                    return;

                lock (_retransBufferLock)
                {
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
                            LocalEndPoint = LocalEndPoint,
                            RemoteEndPoint = RemoteEndPoint,
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

            if (Status == PNetMeshSessionStatus.Opening)
            {
                lock (_openPacketLock)
                {
                    if (_disposing || Status == PNetMeshSessionStatus.Disposed || Status == PNetMeshSessionStatus.Closed)
                        return false;

                    lock (_receiveStateLock)
                        result = ReadMessage(payload, buffer.Memory, openResponderSession: true, out payloadReceived);
                }
            }
            else
            {
                lock (_receiveStateLock)
                    result = ReadMessage(payload, buffer.Memory, openResponderSession: false, out payloadReceived);
            }

            if (result && payloadReceived)
                MessageReceived?.Invoke(this, EventArgs.Empty);

            return result;
        }

        bool ReadMessage(
            ReadOnlySpan<byte> payload,
            Memory<byte> buffer,
            bool openResponderSession,
            out bool payloadReceived)
        {
            payloadReceived = false;
            if (!_transport.TryReadMessage(payload, buffer.Span, out var bytesWritten, out var counter))
            {
                //buffer.Dispose();

                if (counter > 0)
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

            if (openResponderSession && Status == PNetMeshSessionStatus.Opening)
            {
                if (_disposing || Status == PNetMeshSessionStatus.Disposed || Status == PNetMeshSessionStatus.Closed)
                    return false;

                _openPacket = new Protos.Packet();
                Status = PNetMeshSessionStatus.Open;
            }

            if (!PNetMeshPayloadFraming.TryRead(buffer.Span.Slice(0, bytesWritten), out var frame, out _)
                || frame.Kind != PNetMeshPayloadFrameKind.PNet)
                return false;

            var packet = Protos.Packet.Parser.ParseFrom(frame.Payload);
            ProcessAck(packet);

            if (Status != PNetMeshSessionStatus.Open)
                return false;

            if (counter > _receiveCounter)
            {
                if (packet.DoNotAck && HasPayload(packet))
                {
                    payloadReceived |= ProcessPacket(packet);
                    UpdateAckTimer(packet);
                    return true;
                }

                MarkAckRequired(packet, counter);
                _pendingPackets[counter] = packet;
                _outOfOrder_count = _pendingPackets.Count;
                if (AllowOutOfOrderPayloadDelivery && HasPayload(packet))
                {
                    payloadReceived |= ProcessPacket(packet);
                    _deliveredOutOfOrderPackets.Add(counter);
                }
                UpdateAckTimer(packet);
                return true;
            }

            if (counter < _receiveCounter)
                return false;

            MarkAckRequired(packet, counter);
            payloadReceived |= ProcessPacket(packet);
            _receiveCounter++;

            while (_pendingPackets.TryGetValue(_receiveCounter, out var pendingPacket))
            {
                _pendingPackets.Remove(_receiveCounter);
                if (!_deliveredOutOfOrderPackets.Remove(_receiveCounter))
                    payloadReceived |= ProcessPacket(pendingPacket);
                _receiveCounter++;
            }

            _outOfOrder_count = _pendingPackets.Count;
            UpdateAckTimer(packet);
            return true;
        }

        void UpdateAckTimer(Protos.Packet packet)
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

        void MarkAckRequired(Protos.Packet packet, ulong counter)
        {
            if (!packet.DoNotAck)
                _receiveAckRequired = Math.Max(_receiveAckRequired, counter + 1);
        }

        bool ProcessPacket(Protos.Packet packet)
        {
            var payloadReceived = false;
            if (packet.Syn is not null)
            {
                var syn = packet.Syn;

                _cumAck_max = syn.MaxCumAck;
                _outOfOrder_max = syn.MaxOutOfSeq;
                if (syn.MaxOutstandingSeq > 0)
                    Volatile.Write(ref _outstanding_max, syn.MaxOutstandingSeq);
                if (syn.MaxPacketSize > 0)
                    Volatile.Write(ref _packetSize_max, syn.MaxPacketSize);

                if (syn.RetransmissionTimeout > 0)
                    _retrans_timeout = Math.Max(syn.RetransmissionTimeout, 100);
                if (syn.CumulativeAckTimeout > 0)
                    _cumAck_timeout = Math.Max(syn.CumulativeAckTimeout, 100);
            }

            if (packet.Probe is not null)
            {
                var probe = packet.Probe;
            }

            if (packet.ProbeReply is not null)
            {
                var reply = packet.ProbeReply;
            }

            foreach (var relay in packet.Relay)
            {
                var (lite, checkPacing, userPass) = (false, 0u, string.Empty);
                var candidates = ImmutableArray.CreateBuilder<PNetMeshCandidate>(relay.CandidateExchange?.Candidates.Count ?? 1);

                if (relay.CandidateExchange is not null)
                {
                    var ice = relay.CandidateExchange;
                    (lite, checkPacing, userPass) = (ice.Lite, ice.CheckPacing, ice.UserPass);

                    foreach (var candidate in ice.Candidates)
                        candidates.Add(new PNetMeshCandidate
                        {
                            Address = PNetMeshUtils.MapToItem(candidate.Address),
                            Protocol = (PNetMeshProtocolType)candidate.Protocol,
                            Type = (PNetMeshCandidateType)candidate.Type,
                            Base = PNetMeshUtils.MapToItem(candidate.RelatedAddress),
                            ComponentId = candidate.ComponentId > 0
                                ? (byte)candidate.ComponentId : (byte)1,
                            Foundation = candidate.Foundation,
                            Priority = candidate.Priority
                        });
                }

                if (relay.Route.Count == 1)
                {
                    //tmp add RemoteEndPoint as candidate

                    //todo sending peer need to add peer reflexive form learned from probe
                    if (!candidates.Any(n => n.Address.Equals(RemoteEndPoint)))
                    {
                        candidates.Add(new PNetMeshCandidate
                        {
                            Address = RemoteEndPoint,
                            Protocol = PNetMeshProtocolType.UDP,
                            Type = PNetMeshCandidateType.ServerReflexive,
                            Base = null, //todo incorrect
                            ComponentId = 1,
                            Foundation = $"srflx-no_base-{Convert.ToBase64String(LocalAddress)}-udp",
                            Priority = 100
                        });
                    }
                }

                var route = ImmutableArray.CreateBuilder<byte[]>(relay.Route.Count + 1);

                foreach (var r in relay.Route)
                    route.Add(r.Hash.ToByteArray());

                //add local address
                if (route.Count == 0 || !PNetMeshByteArrayComparer.Default.Equals(route[^1], LocalAddress))
                    route.Add(LocalAddress);

                var msg = new PNetMeshOutboundMessages.Relay
                {
                    Packet = new PNetMeshRelayPacket
                    {
                        Address = relay.Address.Hash.ToByteArray(),
                        SeqNumber = relay.SeqNumber,
                        HopCount = relay.HopCount > 0 ? (ushort)(relay.HopCount - 1) : (ushort)0,
                        Route = route.ToImmutable(),
                        Payload = relay.PayloadCase switch
                        {
                            Protos.Relay.PayloadOneofCase.Packet => relay.Packet.Memory,
                            _ => null //todo not supported error
                        },
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
            }

            if (packet.Rst)
            {

            }

            if (packet.CandidateExchange is not null)
            {
                var ice = packet.CandidateExchange;
            }

            foreach (var compress in packet.Compression)
            {

            }

            if (packet.Timestamp is not null)
            {
                var ts = packet.Timestamp.ToDateTime();
            }

            foreach (var p in packet.Payload)
            {
                //p.DictionaryId;
                //p.DataSize;

                switch (p.DataCase)
                {
                    case Protos.Payload.DataOneofCase.Raw:
                        if (_inboundWriter.TryWrite(p.Raw.Memory))
                        {
                            payloadReceived = true;
                        }
                        else
                        {
                            //log write inbound error
                        }
                        break;
                    default:
                        //not supported
                        break;
                }
            }

            return payloadReceived;
        }

        static bool HasPayload(Protos.Packet packet)
        {
            return packet.Payload.Count > 0;
        }

        void ProcessAck(Protos.Packet packet)
        {
            if (packet.Ack is null)
                return;

            var ack = packet.Ack;
            var outOfOrder = ack.OutOfSeqPackets.Memory;
            var remoteAck = new Ack
            {
                SeqNumber = ack.AckSeqNumber,
                OutOfOrder = outOfOrder
            };

            ulong retransmitBase;
            lock (_remoteAckLock)
            {
                var ackAdvanced = _remoteAck.SeqNumber < ack.AckSeqNumber;
                var outOfOrderChanged = _remoteAck.SeqNumber == ack.AckSeqNumber
                    && !_remoteAck.OutOfOrder.Span.SequenceEqual(outOfOrder.Span);

                if (!ackAdvanced && !outOfOrderChanged)
                    return;

                lock (_retransBufferLock)
                {
                    if (remoteAck.SeqNumber > _retransBuffer.Current + 1)
                        return;

                    if (remoteAck.SeqNumber > 0)
                        _retransBuffer.RemoveUntil(remoteAck.SeqNumber - 1);
                    if (remoteAck.OutOfOrder.Length > 0)
                        _retransBuffer.RemoveSequence(remoteAck.OutOfOrder);
                    retransmitBase = _retransBuffer.Latest;
                }

                _remoteAck = remoteAck;
            }

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
            if (_disposing || Status != PNetMeshSessionStatus.Open)
                return;

            var writer = _controlWriter;
            if (writer is null)
            {
                FailControlQueue(new InvalidOperationException("Control channel is not available."));
                return;
            }

            var startDrain = false;
            var overflow = false;
            lock (_controlQueueLock)
            {
                if (!_controlQueueDraining && _pendingControlCommands.Count == 0 && writer.TryWrite(command))
                    return;

                if (_pendingControlCommands.Count >= MaxPendingControlCommands)
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

            if (overflow)
            {
                FailControlQueue(new InvalidOperationException("Control channel backlog exceeded."));
                return;
            }

            if (startDrain)
                _ = DrainControlQueueAsync(writer);
        }

        async Task DrainControlQueueAsync(ChannelWriter<PNetMeshChannelCommands.Command> writer)
        {
            try
            {
                while (true)
                {
                    PNetMeshChannelCommands.Invoke command;
                    lock (_controlQueueLock)
                    {
                        if (_pendingControlCommands.Count == 0)
                        {
                            _controlQueueDraining = false;
                            return;
                        }

                        command = _pendingControlCommands.Peek();
                    }

                    await writer.WriteAsync(command);

                    lock (_controlQueueLock)
                    {
                        if (_pendingControlCommands.Count > 0 && ReferenceEquals(_pendingControlCommands.Peek(), command))
                            _pendingControlCommands.Dequeue();
                    }
                }
            }
            catch (Exception ex) when (ex is ChannelClosedException or InvalidOperationException)
            {
                lock (_controlQueueLock)
                {
                    _pendingControlCommands.Clear();
                    _controlQueueDraining = false;
                }

                FailControlQueue(ex);
            }
        }

        void FailControlQueue(Exception exception)
        {
            TaskCompletionSource[] results;
            lock (_openPacketLock)
            {
                if (_disposing || Status == PNetMeshSessionStatus.Disposed)
                    return;

                Status = PNetMeshSessionStatus.Closed;
                results = FailOpenPacket();
            }
            CompleteOpenPacketResults(results, exception);
        }

        Ack GetRemoteAckSnapshot()
        {
            lock (_remoteAckLock)
                return _remoteAck;
        }

        ulong GetRemoteAckSeqNumber()
        {
            lock (_remoteAckLock)
                return _remoteAck.SeqNumber;
        }

        bool HasSendWindow()
        {
            lock (_retransBufferLock)
                return _retransBuffer.Count < Volatile.Read(ref _outstanding_max);
        }

        bool HasOpenPacket()
        {
            return _openPacket.Payload.Count > 0 || _openPacket.Relay.Count > 0;
        }

        bool HasPendingAck()
        {
            lock (_receiveStateLock)
                return _receiveAck < _receiveAckRequired || _pendingPackets.Count > 0;
        }

        void UpdateRetransmissionTimer()
        {
            var dueTime = Timeout.Infinite;
            lock (_retransBufferLock)
            {
                if (!_disposing && Status == PNetMeshSessionStatus.Open && _retransBuffer.Count > 0)
                    dueTime = _retrans_timeout;
            }

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
            if (packetSize <= Volatile.Read(ref _packetSize_max))
                return;

            throw new ArgumentOutOfRangeException(paramName, "Packet exceeds negotiated max packet size.");
        }

        static int CalculatePNetFrameSize(Protos.Packet packet)
        {
            return PNetMeshPayloadFraming.CalculatePNetFrameSize(packet.CalculateSize());
        }
    }
}
