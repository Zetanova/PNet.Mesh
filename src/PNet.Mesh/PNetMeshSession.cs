using Google.Protobuf;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;

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

            public byte[] OutOfOrder { get; init; }
        }

        readonly PNetMeshProtocol _protocol;

        readonly ChannelWriter<PNetMeshOutboundMessages.Message> _outboundWriter;

        readonly PNetMeshPacketBuffer _retransBuffer;

        PNetMeshHandshake _handshake;

        PNetMeshTransport2 _transport;

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


        public event EventHandler StatusChanged;

        //public PNetMeshChannel Channel { get; set; }

        ChannelWriter<ReadOnlyMemory<byte>> _inboundWriter;
        ChannelWriter<PNetMeshChannelCommands.Command> _controlWriter;

        Protos.Packet _openPacket;

        Ack _remoteAck = new Ack { SeqNumber = 0, OutOfOrder = Array.Empty<byte>() };

        ulong _receiveCounter = 0;
        ulong _receiveAck = 0;

        int _cumAck_max = 2;
        //int _cumAck_count = 0;
        int _cumAck_timeout = 100;

        int _outOfOrder_max = 2;
        int _outOfOrder_count = 0;
        ushort _outstanding_max = 1;
        ushort _packetSize_max = ushort.MaxValue - 8;

        int _retrans_timeout = 250;

        Timer _retransTimer;
        Timer _cumAckTimer;

        const ushort PayloadSizeToTarget = 1280;

        internal PNetMeshSession(PNetMeshProtocol protocol, ChannelWriter<PNetMeshOutboundMessages.Message> writer)
        {
            _protocol = protocol;
            _outboundWriter = writer;
            _retransBuffer = new PNetMeshPacketBuffer();
        }

        public void Dispose()
        {
            Status = PNetMeshSessionStatus.Disposed;

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
            _controlWriter.TryWrite(new PNetMeshChannelCommands.Invoke
            {
                Handler = RetransmitPackets
            });
        }

        void OnCumAckTimeout(object state)
        {
            //var writer = state as ChannelWriter<PNetMeshChannelCommands.Command>;
            _controlWriter.TryWrite(new PNetMeshChannelCommands.Invoke
            {
                Handler = WritePacket
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

            var buffer = MemoryPool<byte>.Shared.Rent(PNetMeshHandshake.InitiationMessageSize);

            _handshake.WriteInitiationMessage(buffer.Memory.Span, out var bytesWritten);
            Debug.Assert(bytesWritten == PNetMeshHandshake.InitiationMessageSize);

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
            _handshake = _protocol.CreateResponder(senderIndex);

            var localAddress = new byte[10]; //todo set local address external
            PNetMeshUtils.GetAddressFromPublicKey(LocalPublicKey, localAddress);
            LocalAddress = localAddress;

            if (!_handshake.TryReadInitiationMessage(payload))
            {
                //invalid handshake
                Status = PNetMeshSessionStatus.Closed;
                return;
            }

            var remoteAddress = new byte[10];
            PNetMeshUtils.GetAddressFromPublicKey(RemotePublicKey, remoteAddress);
            RemoteAddress = remoteAddress;
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
        }

        public void ReadResponse(ReadOnlySpan<byte> payload)
        {
            if (!_handshake.TryReadResponseMessage(payload, out _transport))
            {
                //invalid session
                Status = PNetMeshSessionStatus.Closed;
                return;
            }

            _openPacket = new Protos.Packet();

            Status = PNetMeshSessionStatus.Open;
        }

        public void WritePayload(ReadOnlySpan<byte> payload)
        {
            var item = new Protos.Payload();
            item.Raw = ByteString.CopyFrom(payload);

            _openPacket.Payload.Add(item);

            var packetSize = _openPacket.CalculateSize();

            if (packetSize > PayloadSizeToTarget || (_retransBuffer.Current - _outstanding_max > _remoteAck.SeqNumber))
            {
                WritePacket();
            }
        }

        public void WriteRelay(PNetMeshRelayPacket packet)
        {
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

            _openPacket.Relay.Add(item);

            var packetSize = _openPacket.CalculateSize();

            if (packetSize > PayloadSizeToTarget || (_retransBuffer.Current - _outstanding_max > _remoteAck.SeqNumber))
            {
                WritePacket();
            }
        }

        void FlushPayload()
        {
            if (_openPacket.Payload.Count > 0)
                WritePacket();
        }

        public void WritePacket()
        {
            if (Status != PNetMeshSessionStatus.Open)
                throw new InvalidOperationException("session not open");

            var packet = _openPacket;
            _openPacket = new Protos.Packet();

            if (_receiveAck < _receiveCounter)
            {
                _receiveAck = _receiveCounter;

                _cumAckTimer.Change(Timeout.Infinite, Timeout.Infinite);

                Span<byte> bitmap = stackalloc byte[16];
                _transport.Tracker.GetBitmap(_receiveAck, bitmap, out var bytesWritten);
                bitmap = bitmap.Slice(0, bytesWritten);

                _receiveAck += PNetMeshPacketTracker.RightShift(bitmap, out bytesWritten);
                bitmap = bitmap.Slice(0, bytesWritten);

                packet.Ack = new Protos.Ack
                {
                    AckSeqNumber = _receiveAck,
                    OutOfSeqPackets = ByteString.CopyFrom(bitmap)
                };
            }

            packet.DoNotAck = packet.Payload.Count == 0;

            var packetSize = packet.CalculateSize();

            using var packetBuffer = MemoryPool<byte>.Shared.Rent(packetSize);

            var payload = packetBuffer.Memory.Span.Slice(0, packetSize);

            packet.WriteTo(payload);

            var buffer = _retransBuffer.Rent(packetSize);

            _transport.WriteMessage(payload, buffer.Span, out var byteWritten, out var counter);
            Debug.Assert(counter == _retransBuffer.Current);

            //send message
            var item = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = null,
                MemoryBuffer = _retransBuffer.SliceCurrent(byteWritten),
                LocalEndPoint = LocalEndPoint,
                LocalAddress = LocalAddress,
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

        void RetransmitPackets()
        {
            var ack = _remoteAck;
            if (ack.SeqNumber > 0) _retransBuffer.RemoveUntil(ack.SeqNumber - 1);
            foreach (var packet in _retransBuffer.GetSequence(ack.OutOfOrder))
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
        }

        public void ReadMessage(ReadOnlySpan<byte> payload)
        {
            using var buffer = MemoryPool<byte>.Shared.Rent(payload.Length);

            if (!_transport.TryReadMessage(payload, buffer.Memory.Span, out var bytesWritten, out var counter))
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

                return;
            }

            //sequence counter
            if (counter == _receiveCounter + 1)
            {
                _receiveCounter = counter;
                _outOfOrder_count = Math.Max(_outOfOrder_count - 1, 0);
            }
            else
                _outOfOrder_count++;

            if (Status == PNetMeshSessionStatus.Opening)
            {
                //open responder session
                _openPacket = new Protos.Packet();
                Status = PNetMeshSessionStatus.Open;
            }

            var bufferSeq = new ReadOnlySequence<byte>(buffer.Memory.Slice(0, bytesWritten));

            var packet = Protos.Packet.Parser.ParseFrom(bufferSeq);

            if (packet.Syn is not null)
            {
                var syn = packet.Syn;

                _cumAck_max = syn.MaxCumAck;
                _outOfOrder_max = syn.MaxOutOfSeq;
                _outstanding_max = (ushort)syn.MaxOutstandingSeq;
                _packetSize_max = (ushort)syn.MaxPacketSize;

                if (syn.RetransmissionTimeout > 0)
                    _retrans_timeout = Math.Max(syn.RetransmissionTimeout, 100);
                if (syn.CumulativeAckTimeout > 0)
                    _cumAck_timeout = Math.Max(syn.CumulativeAckTimeout, 100);
            }

            if (packet.Ack is not null)
            {
                var ack = packet.Ack;

                if (_remoteAck.SeqNumber < ack.AckSeqNumber)
                {
                    _remoteAck = new Ack
                    {
                        SeqNumber = ack.AckSeqNumber,
                        OutOfOrder = !ack.OutOfSeqPackets.IsEmpty
                            ? ack.OutOfSeqPackets.ToByteArray()
                            : Array.Empty<byte>()
                    };

                    //if _ackOutOfSeqPackets not empty then queue retrans
                    var retransTimeout = _retrans_timeout;
                    if (_remoteAck.OutOfOrder.Length > 0)
                        retransTimeout = 0;

                    //todo start retransTimer on send and disable when no unconfirmed packets
                    _retransTimer.Change(retransTimeout, Timeout.Infinite);

                    if (_remoteAck.SeqNumber == _retransBuffer.Current)
                        _controlWriter.TryWrite(new PNetMeshChannelCommands.Invoke
                        {
                            Handler = FlushPayload
                        });
                }
            }

            var ackTimeout = _cumAck_timeout;
            //todo use donotack field
            if (packet.DoNotAck && _receiveCounter - _receiveAck <= 1)
                ackTimeout = Timeout.Infinite;
            if ((int)(_receiveCounter - _receiveAck) >= _cumAck_max)
                ackTimeout = 0;
            if (_outOfOrder_count >= _outOfOrder_max)
                ackTimeout = 0;

            _cumAckTimer.Change(ackTimeout, Timeout.Infinite);

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
                                ? (byte)candidate.ComponentId : 1,
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
                        HopCount = relay.HopCount > 0 ? (ushort)(relay.HopCount - 1) : 0,
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
                        if (!_inboundWriter.TryWrite(p.Raw.Memory))
                        {
                            //log write inbound error
                        }
                        break;
                    default:
                        //not supported
                        break;
                }
            }
        }

    }
}
