using Google.Protobuf;
using Noise;
using PNet.Mesh;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshRoutingUnitTests
    {
        [Fact]
        public void router_insert_update_lookup_and_rejects_invalid_addresses()
        {
            var router = new PNetMeshRouter();
            var address = Address(1);
            var firstEndPoint = new IPEndPoint(IPAddress.Loopback, 10001);
            var secondEndPoint = new IPEndPoint(IPAddress.Loopback, 10002);

            Assert.False(router.TryGetEntry(address, out _));

            router.SetEntry(address, firstEndPoint);

            Assert.True(router.TryGetEntry(address, out var entry));
            Assert.NotSame(address, entry.Address);
            Assert.Equal(address, entry.Address);
            Assert.Equal(firstEndPoint, entry.EndPoint);

            entry.LastSeen = DateTime.UtcNow.AddMinutes(-5);
            var staleSeen = entry.LastSeen;

            router.SetEntry(address, secondEndPoint);

            Assert.True(router.TryGetEntry(address, out var updated));
            Assert.Same(entry, updated);
            Assert.Equal(secondEndPoint, updated.EndPoint);
            Assert.True(updated.LastSeen > staleSeen);

            Assert.False(router.TryGetEntry(Address(2), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => router.SetEntry(new byte[9], firstEndPoint));
        }

        [Fact]
        public void server_relay_packet_tracking_suppresses_duplicate_route_sequence()
        {
            var router = new PNetMeshRouter();
            var senderAddress = Address(10);
            var packet = new PNetMeshRelayPacket
            {
                Address = Address(20),
                SeqNumber = 42,
                HopCount = 1,
                Route = ImmutableArray.Create(senderAddress, Address(11)),
                Payload = Encoding.UTF8.GetBytes("relay")
            };

            Assert.True(PNetMeshServer.TryAcceptRelayPacket(router, packet));
            Assert.False(PNetMeshServer.TryAcceptRelayPacket(router, packet));

            Assert.True(router.TryGetEntry(senderAddress, out var entry));
            Assert.NotNull(entry.Tracker);

            var directPacket = new PNetMeshRelayPacket
            {
                Address = Address(21),
                SeqNumber = 42,
                HopCount = 1,
                Route = ImmutableArray.Create<byte[]>(senderAddress),
                Payload = Encoding.UTF8.GetBytes("direct")
            };

            Assert.True(PNetMeshServer.TryAcceptRelayPacket(router, directPacket));
        }

        [Fact]
        public void server_relay_target_filter_skips_peers_already_in_route()
        {
            var firstHop = Address(30);
            var secondHop = Address(31);
            var routeSet = PNetMeshServer.CreateRelayRouteSet(ImmutableArray.Create(firstHop, secondHop));

            Assert.False(PNetMeshServer.ShouldRelayToPeer(Address(30), routeSet));
            Assert.False(PNetMeshServer.ShouldRelayToPeer(secondHop, routeSet));
            Assert.True(PNetMeshServer.ShouldRelayToPeer(Address(32), routeSet));
        }

        [Fact]
        public void server_relay_candidate_selection_prefers_known_route_then_reflexive_endpoint()
        {
            var router = new PNetMeshRouter();
            var remoteAddress = Address(35);
            var anyHost = new IPEndPoint(IPAddress.Any, 12402);
            var validHost = new IPEndPoint(IPAddress.Parse("172.18.0.7"), 12402);
            var reflexive = new IPEndPoint(IPAddress.Parse("172.18.0.8"), 12402);
            var candidates = ImmutableArray.Create(
                new PNetMeshCandidate
                {
                    Address = anyHost,
                    Protocol = PNetMeshProtocolType.UDP,
                    Type = PNetMeshCandidateType.Host
                },
                new PNetMeshCandidate
                {
                    Address = reflexive,
                    Protocol = PNetMeshProtocolType.UDP,
                    Type = PNetMeshCandidateType.ServerReflexive
                });

            Assert.Equal(reflexive, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates));

            candidates = candidates.Insert(0, new PNetMeshCandidate
            {
                Address = validHost,
                Protocol = PNetMeshProtocolType.UDP,
                Type = PNetMeshCandidateType.Host
            });

            Assert.Equal(reflexive, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates));
            Assert.Equal(reflexive, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates, routeLength: 3));
            Assert.Null(PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates, routeLength: 4));

            var knownRoute = new IPEndPoint(IPAddress.Loopback, 12402);
            router.SetEntry(remoteAddress, knownRoute);
            candidates = candidates.Add(new PNetMeshCandidate
            {
                Address = knownRoute,
                Protocol = PNetMeshProtocolType.UDP,
                Type = PNetMeshCandidateType.Host
            });

            Assert.Equal(knownRoute, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates));
            Assert.Equal(knownRoute, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates, routeLength: 3));
            Assert.Equal(knownRoute, PNetMeshServer.SelectRelayRemoteEndPoint(router, remoteAddress, candidates, routeLength: 4));
        }

        [Fact]
        public void session_relay_roundtrips_route_hop_payload_and_candidate_exchange()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20001),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20002)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20002),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20001)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var originalRoute = ImmutableArray.Create(Address(30), Address(31));
            var payload = Encoding.UTF8.GetBytes("relay payload");
            var packet = new PNetMeshRelayPacket
            {
                Address = Address(40),
                SeqNumber = 99,
                HopCount = 5,
                Route = originalRoute,
                Payload = payload,
                CandidateExchange = new PNetMeshCandidateExchange
                {
                    Lite = true,
                    CheckPacing = 250,
                    UserPass = "user:pass",
                    Candidates = ImmutableArray.Create(
                        new PNetMeshCandidate
                        {
                            Address = new DnsEndPoint("relay.example", 3478),
                            Base = new DnsEndPoint("base.example", 3479),
                            ComponentId = 2,
                            Foundation = "foundation-1",
                            Priority = 12345,
                            Protocol = PNetMeshProtocolType.TCP,
                            Type = PNetMeshCandidateType.Relayed
                        },
                        new PNetMeshCandidate
                        {
                            Address = new IPEndPoint(IPAddress.Loopback, 3480),
                            Base = null,
                            ComponentId = 1,
                            Foundation = "foundation-null-base",
                            Priority = 54321,
                            Protocol = PNetMeshProtocolType.UDP,
                            Type = PNetMeshCandidateType.ServerReflexive
                        })
                }
            };

            sender.WriteRelay(packet);
            sender.WritePacket();

            var encrypted = ReadPacket(senderOutbound);
            receiver.ReadMessage(encrypted.MemoryBuffer.Span);

            var relayed = Assert.IsType<PNetMeshOutboundMessages.Relay>(ReadMessage(receiverOutbound));
            Assert.Equal(packet.Address, relayed.Packet.Address);
            Assert.Equal(packet.SeqNumber, relayed.Packet.SeqNumber);
            Assert.Equal((ushort)4, relayed.Packet.HopCount);
            Assert.Equal(payload, relayed.Packet.Payload.ToArray());

            Assert.Equal(3, relayed.Packet.Route.Length);
            Assert.Equal(originalRoute[0], relayed.Packet.Route[0]);
            Assert.Equal(originalRoute[1], relayed.Packet.Route[1]);
            Assert.Equal(receiver.LocalAddress, relayed.Packet.Route[2]);

            var exchange = relayed.Packet.CandidateExchange;
            Assert.True(exchange.Lite);
            Assert.Equal(250u, exchange.CheckPacing);
            Assert.Equal("user:pass", exchange.UserPass);

            Assert.Equal(2, exchange.Candidates.Length);

            var candidate = exchange.Candidates.Single(n => n.Foundation == "foundation-1");
            AssertDnsEndPoint("relay.example", 3478, candidate.Address);
            AssertDnsEndPoint("base.example", 3479, candidate.Base);
            Assert.Equal((byte)2, candidate.ComponentId);
            Assert.Equal("foundation-1", candidate.Foundation);
            Assert.Equal(12345u, candidate.Priority);
            Assert.Equal(PNetMeshProtocolType.TCP, candidate.Protocol);
            Assert.Equal(PNetMeshCandidateType.Relayed, candidate.Type);

            var nullBaseCandidate = exchange.Candidates.Single(n => n.Foundation == "foundation-null-base");
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 3480), nullBaseCandidate.Address);
            Assert.Null(nullBaseCandidate.Base);
            Assert.Equal((byte)1, nullBaseCandidate.ComponentId);
            Assert.Equal(54321u, nullBaseCandidate.Priority);
            Assert.Equal(PNetMeshProtocolType.UDP, nullBaseCandidate.Protocol);
            Assert.Equal(PNetMeshCandidateType.ServerReflexive, nullBaseCandidate.Type);
        }

        [Fact]
        public void session_buffers_reordered_payload_until_missing_packet_arrives()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20301),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20302)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20302),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20301)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var first = WritePayloadPacket(sender, senderOutbound, "first");
            var second = WritePayloadPacket(sender, senderOutbound, "second");

            receiver.ReadMessage(second.MemoryBuffer.Span);

            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));

            receiver.ReadMessage(first.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "first");
            AssertPayload(receiverChannels.Inbound, "second");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_retransmits_missing_packet_from_out_of_sequence_ack()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20402)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20401)
            };

            var senderChannels = AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var first = WritePayloadPacket(sender, senderOutbound, "first");
            var second = WritePayloadPacket(sender, senderOutbound, "second");

            receiver.ReadMessage(second.MemoryBuffer.Span);
            receiver.WritePacket();

            var ack = ReadPacket(receiverOutbound);
            sender.ReadMessage(ack.MemoryBuffer.Span);

            var retransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            retransmit.Handler();

            var retransmittedFirst = ReadPacket(senderOutbound);
            Assert.False(senderOutbound.Reader.TryRead(out _));
            receiver.ReadMessage(retransmittedFirst.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "first");
            AssertPayload(receiverChannels.Inbound, "second");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_clears_stale_out_of_sequence_bitmap_on_same_cumulative_ack()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20501),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20502)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20502),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20501)
            };

            var senderChannels = AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver,
                maxOutstandingSeq: 4,
                maxPacketSize: 1280,
                maxCumAck: 100,
                retransmissionTimeout: 10000,
                cumulativeAckTimeout: 10000);
            sender.ReadMessage(syn.Span);

            WritePayloadPacket(sender, senderOutbound, "first");
            WritePayloadPacket(sender, senderOutbound, "second");

            var outOfSequenceAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: new byte[] { 0x02 });
            sender.ReadMessage(outOfSequenceAck.Span);
            var retransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            var firstFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            Assert.Equal(new byte[] { 0x02 }, GetRemoteAckOutOfOrder(sender));

            var emptySameSequenceAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(emptySameSequenceAck.Span);

            Assert.Empty(GetRemoteAckOutOfOrder(sender));
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            while (senderChannels.Control.Reader.TryRead(out var command))
            {
                var invoke = Assert.IsType<PNetMeshChannelCommands.Invoke>(command);
                Assert.Equal("FlushOpenPacket", invoke.Handler.Method.Name);
                invoke.Handler();
            }
            Assert.False(senderOutbound.Reader.TryRead(out _));

            retransmit.Handler();
            firstFlush.Handler();
            flush.Handler();

            Assert.True(senderOutbound.Reader.TryRead(out _));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_drops_queued_retransmit_when_later_ack_advances_buffer_head()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22501),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22502)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22502),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22501)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver,
                maxOutstandingSeq: 4,
                maxPacketSize: 1280,
                maxCumAck: 100,
                retransmissionTimeout: 10000,
                cumulativeAckTimeout: 10000);
            sender.ReadMessage(syn.Span);

            WritePayloadPacket(sender, senderOutbound, "first");
            WritePayloadPacket(sender, senderOutbound, "second");

            var outOfSequenceAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: new byte[] { 0x02 });
            sender.ReadMessage(outOfSequenceAck.Span);
            var staleRetransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            var firstFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            var advancingAck = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(advancingAck.Span);
            var secondFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            staleRetransmit.Handler();
            firstFlush.Handler();
            secondFlush.Handler();

            Assert.False(senderOutbound.Reader.TryRead(out _));
            Assert.False(senderChannels.Control.Reader.TryRead(out _));
        }

        [Fact]
        public void session_handles_interleaved_ack_generation_and_reordered_reads()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20701),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20702)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20702),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20701)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            const int packetCount = 16;
            var packets = new PNetMeshOutboundMessages.Packet[packetCount];
            for (var i = 0; i < packetCount; i++)
                packets[i] = WritePayloadPacket(sender, senderOutbound, $"msg-{i}");

            receiver.ReadMessage(packets[0].MemoryBuffer.Span);

            for (var i = packetCount - 1; i > 0; i--)
            {
                receiver.WritePacket();
                receiver.ReadMessage(packets[i].MemoryBuffer.Span);
            }

            for (var i = 0; i < packetCount; i++)
                AssertPayload(receiverChannels.Inbound, $"msg-{i}");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));

            receiver.WritePacket();
            for (var i = 0; i <= packetCount && GetRemoteAckSeqNumber(sender) < (ulong)packetCount; i++)
                sender.ReadMessage(ReadPacket(receiverOutbound).MemoryBuffer.Span);

            Assert.Equal((ulong)packetCount, GetRemoteAckSeqNumber(sender));
        }

        [Fact]
        public void session_honors_negotiated_max_outstanding_packets()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20801),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20802)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20802),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20801)
            };

            var senderChannels = AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 2, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            var first = ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            var second = ReadPacket(senderOutbound);

            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            Assert.False(senderOutbound.Reader.TryRead(out _));

            var outOfOrderAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: new byte[] { 0x02 });
            sender.ReadMessage(outOfOrderAck.Span);
            Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            var blockedFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            blockedFlush.Handler();
            Assert.False(senderOutbound.Reader.TryRead(out _));

            receiver.ReadMessage(first.MemoryBuffer.Span);
            receiver.ReadMessage(second.MemoryBuffer.Span);

            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);

            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            flush.Handler();
            var third = ReadPacket(senderOutbound);

            receiver.ReadMessage(third.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "first");
            AssertPayload(receiverChannels.Inbound, "second");
            AssertPayload(receiverChannels.Inbound, "third");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_ignores_future_ack_that_exceeds_sent_packet_cursor()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23202)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23201)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 2, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            Assert.False(senderOutbound.Reader.TryRead(out _));
            Assert.Equal(2, GetRetransBufferCount(sender));

            var futureAck = WriteAckPacket(receiver, ackSeqNumber: 100, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(futureAck.Span);

            Assert.Equal(2, GetRetransBufferCount(sender));
            Assert.False(senderChannels.Control.Reader.TryRead(out _));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task session_ack_frees_send_window_even_when_control_channel_is_full()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var senderControl = Channel.CreateBounded<PNetMeshChannelCommands.Command>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22402)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22401)
            };

            sender.AttachTo(senderInbound.Writer, senderControl.Writer);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            var first = ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            Assert.False(senderOutbound.Reader.TryRead(out _));

            Assert.True(senderControl.Writer.TryWrite(new PNetMeshChannelCommands.Invoke
            {
                Handler = static () => { }
            }));

            receiver.ReadMessage(first.MemoryBuffer.Span);
            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);

            Assert.False(senderOutbound.Reader.TryRead(out _));
            Assert.True(senderControl.Reader.TryRead(out var dummyCommand));
            var dummy = Assert.IsType<PNetMeshChannelCommands.Invoke>(dummyCommand);
            dummy.Handler();
            Assert.True(await senderControl.Reader.WaitToReadAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.True(senderControl.Reader.TryRead(out var flushCommand));
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(flushCommand);
            flush.Handler();

            var second = ReadPacket(senderOutbound);
            receiver.ReadMessage(second.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "first");
            AssertPayload(receiverChannels.Inbound, "second");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task session_closes_when_control_channel_rejects_ack_flush()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var senderControl = Channel.CreateBounded<PNetMeshChannelCommands.Command>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22601),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22602)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22602),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22601)
            };

            sender.AttachTo(senderInbound.Writer, senderControl.Writer);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            var first = ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            Assert.False(senderOutbound.Reader.TryRead(out _));

            Assert.True(senderControl.Writer.TryWrite(new PNetMeshChannelCommands.Invoke
            {
                Handler = static () => { }
            }));
            senderControl.Writer.Complete();

            receiver.ReadMessage(first.MemoryBuffer.Span);
            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);

            await WaitForStatusAsync(sender, PNetMeshSessionStatus.Closed);
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_drops_payload_when_control_queue_failure_closes_during_ack_processing()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23402)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23401)
            };

            sender.AttachTo(senderInbound.Writer, null);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var ackPayload = WriteAckPayloadPacket(
                receiver,
                ackSeqNumber: 0,
                outOfSeqPackets: new byte[] { 0x01 },
                payload: "must not deliver");

            sender.ReadMessage(ackPayload.Span);

            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
            Assert.False(senderInbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task session_completes_send_results_without_reentering_open_packet_batch()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22701),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22702)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22702),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22701)
            };

            AttachSession(sender);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            var firstResult = new TaskCompletionSource();
            var secondResult = new TaskCompletionSource();
            var continuation = firstResult.Task.ContinueWith(_ =>
            {
                sender.WritePayload(Encoding.UTF8.GetBytes("second"), secondResult);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"), firstResult);

            await continuation.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await firstResult.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await secondResult.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound));
            Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_closes_when_control_overflow_exceeds_bound()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var senderControl = new BlockingControlWriter();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22801),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22802)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22802),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22801)
            };

            sender.AttachTo(senderInbound.Writer, senderControl);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            var queueControl = typeof(PNetMeshSession).GetMethod("QueueControl", BindingFlags.Instance | BindingFlags.NonPublic);

            for (var i = 0; i < 40 && sender.Status == PNetMeshSessionStatus.Open; i++)
            {
                queueControl.Invoke(sender, new object[]
                {
                    new PNetMeshChannelCommands.Invoke
                    {
                        Handler = static () => { }
                    }
                });
            }

            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
        }

        [Fact]
        public void session_cumulative_ack_timeout_does_not_flush_when_send_window_is_full()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21202)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21201)
            };

            var senderChannels = AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 2, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            var first = ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            var second = ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            Assert.False(senderOutbound.Reader.TryRead(out _));

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderChannels.Inbound, "peer");

            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            flush.Handler();

            var ackOnly = ReadPacket(senderOutbound);
            receiver.ReadMessage(ackOnly.MemoryBuffer.Span);

            Assert.False(senderOutbound.Reader.TryRead(out _));
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));

            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);
            var queuedFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            queuedFlush.Handler();
            var third = ReadPacket(senderOutbound);

            receiver.ReadMessage(first.MemoryBuffer.Span);
            receiver.ReadMessage(second.MemoryBuffer.Span);
            receiver.ReadMessage(third.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "first");
            AssertPayload(receiverChannels.Inbound, "second");
            AssertPayload(receiverChannels.Inbound, "third");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_cumulative_ack_timeout_without_open_packet_does_not_consume_send_window()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21801),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21802)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21802),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21801)
            };

            var senderChannels = AttachSession(sender);
            var senderInbound = senderChannels.Inbound;
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);
            Assert.Equal(1, GetRetransBufferCount(sender));

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderInbound, "peer");

            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            flush.Handler();

            ReadPacket(senderOutbound);
            Assert.Equal(1, GetRetransBufferCount(sender));
        }

        [Fact]
        public void session_cumulative_ack_timeout_does_not_rearm_after_ack_is_flushed()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23102)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23101)
            };

            var senderChannels = AttachSession(sender);
            var senderInbound = senderChannels.Inbound;
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1280, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderInbound, "peer");

            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            flush.Handler();
            ReadPacket(senderOutbound);

            InvokeCumAckTimeout(sender);
            Assert.False(senderChannels.Control.Reader.TryRead(out _));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_rejects_payload_larger_than_negotiated_packet_size()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20901),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20902)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20902),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20901)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 2, maxPacketSize: 24);
            sender.ReadMessage(syn.Span);

            var oversizedPayload = Encoding.UTF8.GetBytes("payload larger than negotiated limit");
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => sender.WritePayload(oversizedPayload));

            Assert.Equal("payload", exception.ParamName);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            sender.WritePayload(Encoding.UTF8.GetBytes("ok"));
            var recovered = ReadPacket(senderOutbound);
            receiver.ReadMessage(recovered.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "ok");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_enforces_exact_negotiated_packet_size_boundary()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23001),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23002)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23002),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23001)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var payload = Encoding.UTF8.GetBytes("x");
            var packet = new PNet.Actor.Mesh.Protos.Packet();
            packet.Payload.Add(new PNet.Actor.Mesh.Protos.Payload
            {
                Raw = ByteString.CopyFrom(payload)
            });
            packet.Ack = new PNet.Actor.Mesh.Protos.Ack
            {
                AckSeqNumber = GetReceiveCounter(sender) + 1,
                OutOfSeqPackets = ByteString.Empty
            };
            var exactPacketSize = packet.CalculateSize();

            var exactLimit = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: exactPacketSize);
            sender.ReadMessage(exactLimit.Span);

            sender.WritePayload(payload);
            var accepted = ReadPacket(senderOutbound);
            receiver.ReadMessage(accepted.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "x");

            var lowerLimit = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: exactPacketSize - 1);
            sender.ReadMessage(lowerLimit.Span);

            Assert.Throws<ArgumentOutOfRangeException>(() => sender.WritePayload(payload));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task session_faults_direct_send_result_when_payload_exceeds_negotiated_packet_size()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22901),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22902)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22902),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22901)
            };

            AttachSession(sender);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 2, maxPacketSize: 24);
            sender.ReadMessage(syn.Span);

            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var oversizedPayload = Encoding.UTF8.GetBytes("payload larger than negotiated limit");

            Assert.Throws<ArgumentOutOfRangeException>(() => sender.WritePayload(oversizedPayload, result));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                result.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task session_faults_all_pending_results_when_open_packet_flush_fails()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23301),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23302)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 23302),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 23301)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);

            var secondResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thirdResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"), secondResult);
            sender.WritePayload(Encoding.UTF8.GetBytes("third"), thirdResult);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            senderOutbound.Writer.Complete();
            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            Assert.Throws<InvalidOperationException>(flush.Handler);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                secondResult.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                thirdResult.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void session_rejects_payload_when_ack_header_exceeds_negotiated_packet_size()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21001),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21002)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21002),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21001)
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var payload = Encoding.UTF8.GetBytes("x");
            var payloadOnlyPacket = new PNet.Actor.Mesh.Protos.Packet();
            payloadOnlyPacket.Payload.Add(new PNet.Actor.Mesh.Protos.Payload
            {
                Raw = ByteString.CopyFrom(payload)
            });

            var negotiatedPacketSize = payloadOnlyPacket.CalculateSize();
            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: negotiatedPacketSize);
            sender.ReadMessage(syn.Span);

            Assert.True(GetReceiveCounter(sender) > GetReceiveAck(sender));

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => sender.WritePayload(payload));

            Assert.Equal("payload", exception.ParamName);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            var largerLimit = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1280);
            sender.ReadMessage(largerLimit.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("ok"));
            var recovered = ReadPacket(senderOutbound);
            receiver.ReadMessage(recovered.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "ok");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_honors_zero_syn_ack_thresholds()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21102)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21101)
            };

            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var negotiated = WriteSynPacket(sender, maxOutstandingSeq: 3, maxPacketSize: 128, maxCumAck: 5, maxOutOfSeq: 6);
            receiver.ReadMessage(negotiated.Span);
            Assert.Equal(5, GetCumAckMax(receiver));
            Assert.Equal(6, GetOutOfOrderMax(receiver));

            var syn = WriteSynPacket(sender, maxOutstandingSeq: 0, maxPacketSize: 0, maxCumAck: 0, maxOutOfSeq: 0);
            receiver.ReadMessage(syn.Span);

            Assert.Equal(0, GetCumAckMax(receiver));
            Assert.Equal(0, GetOutOfOrderMax(receiver));

            receiver.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(receiverOutbound);
            receiver.WritePayload(Encoding.UTF8.GetBytes("second"));
            ReadPacket(receiverOutbound);
            receiver.WritePayload(Encoding.UTF8.GetBytes("third"));
            ReadPacket(receiverOutbound);
            receiver.WritePayload(Encoding.UTF8.GetBytes("fourth"));
            Assert.False(receiverOutbound.Reader.TryRead(out _));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                receiver.WritePayload(Encoding.UTF8.GetBytes(new string('x', 256))));
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_processes_out_of_order_ack_before_missing_packet_arrives()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20601),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20602)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20602),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20601)
            };

            AttachSession(sender);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            WritePayloadPacket(sender, senderOutbound, "first");

            var firstAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: Array.Empty<byte>());
            var secondAck = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());

            sender.ReadMessage(secondAck.Span);

            Assert.Equal(1ul, GetRemoteAckSeqNumber(sender));

            sender.ReadMessage(firstAck.Span);

            Assert.Equal(1ul, GetRemoteAckSeqNumber(sender));
        }

        [Fact]
        public async Task channel_relay_uses_open_routable_session_when_current_session_is_disposed()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var firstSenderKey = KeyPair.Generate();
            using var firstReceiverKey = KeyPair.Generate();
            var firstSenderProtocol = new PNetMeshProtocol(firstSenderKey.PrivateKey, firstSenderKey.PublicKey, psk);
            var firstReceiverProtocol = new PNetMeshProtocol(firstReceiverKey.PrivateKey, firstReceiverKey.PublicKey, psk);
            var firstSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var firstReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var secondSenderKey = KeyPair.Generate();
            using var secondReceiverKey = KeyPair.Generate();
            var secondSenderProtocol = new PNetMeshProtocol(secondSenderKey.PrivateKey, secondSenderKey.PublicKey, psk);
            var secondReceiverProtocol = new PNetMeshProtocol(secondReceiverKey.PrivateKey, secondReceiverKey.PublicKey, psk);
            var secondSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var secondReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var firstSender = new PNetMeshSession(firstSenderProtocol, firstSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20102)
            };
            using var firstReceiver = new PNetMeshSession(firstReceiverProtocol, firstReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20101)
            };
            using var secondSender = new PNetMeshSession(secondSenderProtocol, secondSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20202)
            };
            using var secondReceiver = new PNetMeshSession(secondReceiverProtocol, secondReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20201)
            };

            AttachSession(firstReceiver);
            OpenSessionPair(firstSender, firstReceiver, firstSenderOutbound, firstReceiverOutbound, firstReceiverKey.PublicKey);
            channel.AddSession(firstSender);

            AttachSession(secondReceiver);
            OpenSessionPair(secondSender, secondReceiver, secondSenderOutbound, secondReceiverOutbound, secondReceiverKey.PublicKey);
            channel.AddSession(secondSender);

            secondSender.Dispose();

            var packet = new PNetMeshRelayPacket
            {
                Address = Address(41),
                SeqNumber = 100,
                HopCount = 4,
                Route = ImmutableArray.Create<byte[]>(Address(42)),
                Payload = Encoding.UTF8.GetBytes("fallback relay")
            };

            await channel.RelayAsync(packet, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var relayed = ReadPacket(firstSenderOutbound);
            Assert.Equal(firstSender.RemoteEndPoint, relayed.RemoteEndPoint);
            Assert.False(firstSenderOutbound.Reader.TryRead(out _));
            Assert.False(secondSenderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task channel_relay_async_propagates_negotiated_packet_size_error()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21301),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21302)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21302),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21301)
            };

            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            channel.AddSession(sender);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 4);
            sender.ReadMessage(syn.Span);

            var packet = new PNetMeshRelayPacket
            {
                Address = Address(50),
                SeqNumber = 110,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(51)),
                Payload = Encoding.UTF8.GetBytes("oversized relay")
            };

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                channel.RelayAsync(packet, cancellationToken: TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task channel_relay_async_propagates_deferred_packet_size_error()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21501),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21502)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21502),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21501)
            };

            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            channel.AddSession(sender);

            var firstPacket = new PNetMeshRelayPacket
            {
                Address = Address(60),
                SeqNumber = 130,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(61)),
                Payload = Encoding.UTF8.GetBytes("first")
            };
            var secondPacket = new PNetMeshRelayPacket
            {
                Address = Address(62),
                SeqNumber = 131,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(63)),
                Payload = Encoding.UTF8.GetBytes("second")
            };

            await channel.RelayAsync(firstPacket, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var first = ReadPacket(senderOutbound);
            receiver.ReadMessage(first.MemoryBuffer.Span);

            var syn = WriteSynPacket(receiver,
                maxOutstandingSeq: 1,
                maxPacketSize: CalculateRelayPacketSize(secondPacket),
                maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            var secondRelay = channel.RelayAsync(secondPacket, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            await Task.Yield();
            Assert.False(secondRelay.IsCompleted);

            var ack = WriteAckPacket(receiver, ackSeqNumber: 1, outOfSeqPackets: Array.Empty<byte>());
            sender.ReadMessage(ack.Span);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => secondRelay);
            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_fails_pending_relay_when_ack_only_flush_closes_session()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21601),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21602)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21602),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21601)
            };

            var senderChannels = AttachSession(sender);
            var senderInbound = senderChannels.Inbound;
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            sender.WriteRelay(CreateRelayPacket(Address(70), 140, "first"));
            ReadPacket(senderOutbound);
            Assert.Equal(1, GetRetransBufferCount(sender));

            var pendingResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.WriteRelay(CreateRelayPacket(Address(71), 141, "second"), pendingResult);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderInbound, "peer");

            senderOutbound.Writer.Complete();
            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            Assert.Throws<InvalidOperationException>(flush.Handler);
            var exception = Assert.IsType<InvalidOperationException>(pendingResult.Task.Exception?.InnerException);
            Assert.Equal("Unable to queue outbound packet.", exception.Message);
            Assert.Equal(1, GetRetransBufferCount(sender));
        }

        [Fact]
        public async Task session_dispose_fails_pending_relay()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21701),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21702)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21702),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21701)
            };

            AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            sender.WriteRelay(CreateRelayPacket(Address(72), 142, "first"));
            ReadPacket(senderOutbound);

            var pendingResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.WriteRelay(CreateRelayPacket(Address(73), 143, "second"), pendingResult);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            sender.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                pendingResult.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void session_rolls_back_retransmit_entry_when_tracked_send_cannot_queue()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22001),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22002)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22002),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22001)
            };

            AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            senderOutbound.Writer.Complete();

            Assert.Throws<InvalidOperationException>(() => sender.WritePayload(Encoding.UTF8.GetBytes("queued failure")));
            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
            Assert.Equal(0, GetRetransBufferCount(sender));
        }

        [Fact]
        public void session_dispose_suppresses_queued_ack_timeout_flush()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21901),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21902)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21902),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21901)
            };

            var senderChannels = AttachSession(sender);
            var senderInbound = senderChannels.Inbound;
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderInbound, "peer");

            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            sender.Dispose();
            flush.Handler();

            Assert.Equal(PNetMeshSessionStatus.Disposed, sender.Status);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            InvokeCumAckTimeout(sender);
            Assert.False(senderChannels.Control.Reader.TryRead(out _));
        }

        [Fact]
        public void session_dispose_suppresses_queued_retransmit_flush()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22301),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22302)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22302),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22301)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);
            Assert.Equal(1, GetRetransBufferCount(sender));

            InvokeRetransTimeout(sender);
            var retransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            sender.Dispose();
            retransmit.Handler();

            Assert.Equal(PNetMeshSessionStatus.Disposed, sender.Status);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            InvokeRetransTimeout(sender);
            Assert.False(senderChannels.Control.Reader.TryRead(out _));
        }

        [Fact]
        public void session_closes_when_ack_only_timeout_packet_exceeds_negotiated_size()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22102)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22101)
            };

            var senderChannels = AttachSession(sender);
            var senderInbound = senderChannels.Inbound;
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);
            AssertPayload(senderInbound, "peer");

            InvokeCumAckTimeout(sender);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));

            Assert.Throws<ArgumentOutOfRangeException>(flush.Handler);
            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_zero_syn_preserves_negotiated_send_window_and_packet_size()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22202)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22201)
            };

            AttachSession(sender);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var negotiated = WriteSynPacket(receiver, maxOutstandingSeq: 3, maxPacketSize: 128);
            sender.ReadMessage(negotiated.Span);
            var zeroSyn = WriteSynPacket(receiver, maxOutstandingSeq: 0, maxPacketSize: 0);
            sender.ReadMessage(zeroSyn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            ReadPacket(senderOutbound);
            sender.WritePayload(Encoding.UTF8.GetBytes("fourth"));
            Assert.False(senderOutbound.Reader.TryRead(out _));

            var oversizedPayload = Encoding.UTF8.GetBytes(new string('x', 256));
            Assert.Throws<ArgumentOutOfRangeException>(() => sender.WritePayload(oversizedPayload));
        }

        [Fact]
        public async Task channel_control_loop_survives_canceled_relay_failure()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21402)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21401)
            };

            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            channel.AddSession(sender);

            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 4);
            sender.ReadMessage(syn.Span);

            var failedRelayResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            failedRelayResult.TrySetCanceled(TestContext.Current.CancellationToken);

            Assert.True(GetControlChannel(channel).Writer.TryWrite(new PNetMeshChannelCommands.Relay
            {
                Packet = new PNetMeshRelayPacket
                {
                    Address = Address(52),
                    SeqNumber = 120,
                    HopCount = 2,
                    Route = ImmutableArray.Create<byte[]>(Address(53)),
                    Payload = Encoding.UTF8.GetBytes("oversized relay")
                },
                Result = failedRelayResult
            }));

            var usableSyn = WriteSynPacket(receiver, maxOutstandingSeq: 4, maxPacketSize: 1280);
            sender.ReadMessage(usableSyn.Span);

            var packet = new PNetMeshRelayPacket
            {
                Address = Address(54),
                SeqNumber = 121,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(55)),
                Payload = Encoding.UTF8.GetBytes("healthy relay")
            };

            await channel.RelayAsync(packet, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var relayed = ReadPacket(senderOutbound);
            Assert.Equal(sender.RemoteEndPoint, relayed.RemoteEndPoint);
        }

        static void OpenSessionPair(
            PNetMeshSession sender,
            PNetMeshSession receiver,
            Channel<PNetMeshOutboundMessages.Message> senderOutbound,
            Channel<PNetMeshOutboundMessages.Message> receiverOutbound,
            byte[] receiverPublicKey)
        {
            sender.WriteInitialize(1, receiverPublicKey);
            var initialize = ReadPacket(senderOutbound);

            receiver.ReadInitialize(2, initialize.MemoryBuffer.Span);
            receiver.WriteResponse();
            var response = ReadPacket(receiverOutbound);

            sender.ReadResponse(response.MemoryBuffer.Span);

            Assert.Equal(PNetMeshSessionStatus.Open, sender.Status);
            Assert.Equal(PNetMeshSessionStatus.Opening, receiver.Status);
        }

        readonly struct AttachedSession
        {
            public Channel<ReadOnlyMemory<byte>> Inbound { get; init; }

            public Channel<PNetMeshChannelCommands.Command> Control { get; init; }
        }

        sealed class BlockingControlWriter : ChannelWriter<PNetMeshChannelCommands.Command>
        {
            readonly TaskCompletionSource _write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> _wait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool TryComplete(Exception error = null)
            {
                return false;
            }

            public override bool TryWrite(PNetMeshChannelCommands.Command item)
            {
                return false;
            }

            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(_wait.Task);
            }

            public override ValueTask WriteAsync(PNetMeshChannelCommands.Command item, CancellationToken cancellationToken = default)
            {
                return new ValueTask(_write.Task);
            }
        }

        static AttachedSession AttachSession(PNetMeshSession session)
        {
            var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var control = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
            session.AttachTo(inbound.Writer, control.Writer);
            return new AttachedSession
            {
                Inbound = inbound,
                Control = control
            };
        }

        static PNetMeshOutboundMessages.Packet WritePayloadPacket(
            PNetMeshSession session,
            Channel<PNetMeshOutboundMessages.Message> outbound,
            string payload)
        {
            session.WritePayload(Encoding.UTF8.GetBytes(payload));
            if (!outbound.Reader.TryRead(out var message))
            {
                session.WritePacket();
                Assert.True(outbound.Reader.TryRead(out message));
            }

            return Assert.IsType<PNetMeshOutboundMessages.Packet>(message);
        }

        static void AssertPayload(Channel<ReadOnlyMemory<byte>> channel, string expected)
        {
            Assert.True(channel.Reader.TryRead(out var payload));
            Assert.Equal(expected, Encoding.UTF8.GetString(payload.Span));
        }

        static PNetMeshChannelCommands.Command ReadControl(AttachedSession session)
        {
            Assert.True(session.Control.Reader.TryRead(out var command));
            return command;
        }

        static ReadOnlyMemory<byte> WriteAckPacket(
            PNetMeshSession session,
            ulong ackSeqNumber,
            byte[] outOfSeqPackets)
        {
            var packet = new PNet.Actor.Mesh.Protos.Packet
            {
                Ack = new PNet.Actor.Mesh.Protos.Ack
                {
                    AckSeqNumber = ackSeqNumber,
                    OutOfSeqPackets = ByteString.CopyFrom(outOfSeqPackets)
                }
            };
            var payload = packet.ToByteArray();
            var buffer = new byte[payload.Length + 64];
            var transport = GetTransport(session);
            transport.WriteMessage(payload, buffer, out var bytesWritten, out _);
            return buffer.AsMemory(0, bytesWritten);
        }

        static ReadOnlyMemory<byte> WriteAckPayloadPacket(PNetMeshSession session, ulong ackSeqNumber, string payload)
        {
            return WriteAckPayloadPacket(session, ackSeqNumber, ByteString.Empty, payload);
        }

        static ReadOnlyMemory<byte> WriteAckPayloadPacket(
            PNetMeshSession session,
            ulong ackSeqNumber,
            byte[] outOfSeqPackets,
            string payload)
        {
            return WriteAckPayloadPacket(session, ackSeqNumber, ByteString.CopyFrom(outOfSeqPackets), payload);
        }

        static ReadOnlyMemory<byte> WriteAckPayloadPacket(
            PNetMeshSession session,
            ulong ackSeqNumber,
            ByteString outOfSeqPackets,
            string payload)
        {
            var packet = new PNet.Actor.Mesh.Protos.Packet
            {
                Ack = new PNet.Actor.Mesh.Protos.Ack
                {
                    AckSeqNumber = ackSeqNumber,
                    OutOfSeqPackets = outOfSeqPackets
                }
            };
            packet.Payload.Add(new PNet.Actor.Mesh.Protos.Payload
            {
                Raw = ByteString.CopyFrom(Encoding.UTF8.GetBytes(payload))
            });

            var bytes = packet.ToByteArray();
            var buffer = new byte[bytes.Length + 64];
            var transport = GetTransport(session);
            transport.WriteMessage(bytes, buffer, out var bytesWritten, out _);
            return buffer.AsMemory(0, bytesWritten);
        }

        static ReadOnlyMemory<byte> WriteSynPacket(
            PNetMeshSession session,
            int maxOutstandingSeq,
            int maxPacketSize,
            int maxCumAck = 2,
            int maxOutOfSeq = 2,
            int retransmissionTimeout = 0,
            int cumulativeAckTimeout = 0)
        {
            var packet = new PNet.Actor.Mesh.Protos.Packet
            {
                Syn = new PNet.Actor.Mesh.Protos.Syn
                {
                    MaxOutstandingSeq = maxOutstandingSeq,
                    MaxPacketSize = maxPacketSize,
                    MaxCumAck = maxCumAck,
                    MaxOutOfSeq = maxOutOfSeq,
                    RetransmissionTimeout = retransmissionTimeout,
                    CumulativeAckTimeout = cumulativeAckTimeout
                }
            };
            var payload = packet.ToByteArray();
            var buffer = new byte[payload.Length + 64];
            var transport = GetTransport(session);
            transport.WriteMessage(payload, buffer, out var bytesWritten, out _);
            return buffer.AsMemory(0, bytesWritten);
        }

        static int CalculateRelayPacketSize(PNetMeshRelayPacket packet)
        {
            var relay = new PNet.Actor.Mesh.Protos.Relay
            {
                Address = new PNet.Actor.Mesh.Protos.MeshEndPoint
                {
                    Hash = ByteString.CopyFrom(packet.Address)
                },
                SeqNumber = packet.SeqNumber,
                HopCount = packet.HopCount,
                Packet = ByteString.CopyFrom(packet.Payload.Span)
            };

            foreach (var route in packet.Route)
            {
                relay.Route.Add(new PNet.Actor.Mesh.Protos.MeshEndPoint
                {
                    Hash = ByteString.CopyFrom(route)
                });
            }

            var protoPacket = new PNet.Actor.Mesh.Protos.Packet();
            protoPacket.Relay.Add(relay);
            return protoPacket.CalculateSize();
        }

        static PNetMeshRelayPacket CreateRelayPacket(byte[] address, ulong sequence, string payload)
        {
            return new PNetMeshRelayPacket
            {
                Address = address,
                SeqNumber = sequence,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address((byte)((sequence % 200) + 1))),
                Payload = Encoding.UTF8.GetBytes(payload)
            };
        }

        static PNetMeshTransport2 GetTransport(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<PNetMeshTransport2>(field.GetValue(session));
        }

        static ulong GetRemoteAckSeqNumber(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_remoteAck", BindingFlags.Instance | BindingFlags.NonPublic);
            var ack = field.GetValue(session);
            var seqNumber = ack.GetType().GetProperty("SeqNumber");
            return (ulong)seqNumber.GetValue(ack);
        }

        static byte[] GetRemoteAckOutOfOrder(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_remoteAck", BindingFlags.Instance | BindingFlags.NonPublic);
            var ack = field.GetValue(session);
            var outOfOrder = ack.GetType().GetProperty("OutOfOrder");
            return (byte[])outOfOrder.GetValue(ack);
        }

        static ulong GetReceiveCounter(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_receiveCounter", BindingFlags.Instance | BindingFlags.NonPublic);
            return (ulong)field.GetValue(session);
        }

        static ulong GetReceiveAck(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_receiveAck", BindingFlags.Instance | BindingFlags.NonPublic);
            return (ulong)field.GetValue(session);
        }

        static int GetRetransBufferCount(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_retransBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            var buffer = (PNetMeshPacketBuffer)field.GetValue(session);
            return buffer.Count;
        }

        static int GetCumAckMax(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_cumAck_max", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)field.GetValue(session);
        }

        static int GetOutOfOrderMax(PNetMeshSession session)
        {
            var field = typeof(PNetMeshSession).GetField("_outOfOrder_max", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)field.GetValue(session);
        }

        static void InvokeCumAckTimeout(PNetMeshSession session)
        {
            var method = typeof(PNetMeshSession).GetMethod("OnCumAckTimeout", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(session, new object[] { null });
        }

        static void InvokeRetransTimeout(PNetMeshSession session)
        {
            var method = typeof(PNetMeshSession).GetMethod("OnRetransTimeout", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(session, new object[] { null });
        }

        static async Task WaitForStatusAsync(PNetMeshSession session, PNetMeshSessionStatus expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (session.Status != expected)
            {
                await Task.Delay(10, cts.Token);
            }
        }

        static Channel<PNetMeshChannelCommands.Command> GetControlChannel(PNetMeshChannel channel)
        {
            var field = typeof(PNetMeshChannel).GetField("_controlChannel", BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsAssignableFrom<Channel<PNetMeshChannelCommands.Command>>(field.GetValue(channel));
        }

        static PNetMeshOutboundMessages.Packet ReadPacket(Channel<PNetMeshOutboundMessages.Message> channel)
        {
            return Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(channel));
        }

        static PNetMeshOutboundMessages.Message ReadMessage(Channel<PNetMeshOutboundMessages.Message> channel)
        {
            Assert.True(channel.Reader.TryRead(out var message));
            return message;
        }

        static byte[] Address(byte value)
        {
            return Enumerable.Repeat(value, 10).ToArray();
        }

        static void AssertDnsEndPoint(string host, int port, EndPoint endPoint)
        {
            var dnsEndPoint = Assert.IsType<DnsEndPoint>(endPoint);
            Assert.Equal(host, dnsEndPoint.Host);
            Assert.Equal(port, dnsEndPoint.Port);
        }
    }
}
