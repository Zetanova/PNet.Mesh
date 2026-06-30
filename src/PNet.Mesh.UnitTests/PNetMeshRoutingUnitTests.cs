using Noise;
using PNet.Mesh;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
            AttachSession(receiver);
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
            Assert.False(secondSenderOutbound.Reader.TryRead(out _));
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

        static void AttachSession(PNetMeshSession session)
        {
            var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var control = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
            session.AttachTo(inbound.Writer, control.Writer);
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
