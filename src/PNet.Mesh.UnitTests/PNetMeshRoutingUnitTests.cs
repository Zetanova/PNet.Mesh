using Google.Protobuf;
using Noise;
using PNet.Mesh;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        public void address_derivation_uses_first_ten_sha1_bytes_and_validates_lengths()
        {
            var publicKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            var address = new byte[10];

            PNetMeshUtils.GetAddressFromPublicKey(publicKey, address);

            Assert.Equal(new byte[] { 0xae, 0x5b, 0xd8, 0xef, 0xea, 0x53, 0x22, 0xc4, 0xd9, 0x98 }, address);
            Assert.Throws<ArgumentOutOfRangeException>(() => PNetMeshUtils.GetAddressFromPublicKey(publicKey.AsSpan(0, 31), address));
            Assert.Throws<ArgumentOutOfRangeException>(() => PNetMeshUtils.GetAddressFromPublicKey(publicKey, new byte[9]));
        }

        [Fact]
        public void session_handshake_derives_local_and_remote_addresses_from_public_keys()
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

            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            Assert.Equal(DeriveAddress(senderKey.PublicKey), sender.LocalAddress);
            Assert.Equal(DeriveAddress(receiverKey.PublicKey), sender.RemoteAddress);
            Assert.Equal(DeriveAddress(receiverKey.PublicKey), receiver.LocalAddress);
            Assert.Equal(DeriveAddress(senderKey.PublicKey), receiver.RemoteAddress);
        }

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
        public void router_span_overloads_match_existing_entries_and_create_new_entries()
        {
            var router = new PNetMeshRouter();
            var address = Address(3);
            var paddedAddress = new byte[address.Length + 2];
            address.CopyTo(paddedAddress, 1);
            var firstEndPoint = new IPEndPoint(IPAddress.Loopback, 10003);

            router.SetEntry(address, firstEndPoint);

            Assert.True(router.TryGetEntry(paddedAddress.AsSpan(1, address.Length), out var spanLookup));
            Assert.Equal(firstEndPoint, spanLookup.EndPoint);

            var existing = router.GetOrCreateEntry(paddedAddress.AsSpan(1, address.Length));

            Assert.Same(spanLookup, existing);

            var createdAddress = Address(4);
            var paddedCreatedAddress = new byte[createdAddress.Length + 4];
            createdAddress.CopyTo(paddedCreatedAddress, 2);

            var created = router.GetOrCreateEntry(paddedCreatedAddress.AsSpan(2, createdAddress.Length));

            Assert.True(router.TryGetEntry(createdAddress, out var createdLookup));
            Assert.Same(created, createdLookup);
            Assert.Equal(createdAddress, created.Address);
        }

        [Fact]
        public void server_endpoint_resolution_normalizes_ipv4_mapped_addresses_and_removes_duplicates()
        {
            var mapped = new IPEndPoint(IPAddress.Parse("::ffff:172.18.0.3"), 12402);
            var ipv4 = new IPEndPoint(IPAddress.Parse("172.18.0.3"), 12402);
            var other = new IPEndPoint(IPAddress.Parse("172.18.0.4"), 12402);

            var endpoints = PNetMeshServer.ResolveRemoteEndPoints(new EndPoint[] { mapped, ipv4, other }).ToArray();

            Assert.Equal(new EndPoint[] { ipv4, other }, endpoints);
        }

        [Fact]
        public void server_endpoint_normalization_maps_ipv4_mapped_endpoint_to_ipv4()
        {
            var mapped = new IPEndPoint(IPAddress.Parse("::ffff:172.18.0.3"), 12402);

            var endpoint = Assert.IsType<IPEndPoint>(PNetMeshServer.NormalizeRemoteEndPoint(mapped));

            Assert.Equal(AddressFamily.InterNetwork, endpoint.AddressFamily);
            Assert.Equal(IPAddress.Parse("172.18.0.3"), endpoint.Address);
            Assert.Equal(12402, endpoint.Port);
        }

        [Fact]
        public void endpoint_proto_mapping_preserves_ip_address_bytes()
        {
            var ipv4 = new IPEndPoint(IPAddress.Parse("172.18.0.3"), 12402);
            var ipv4Proto = Required(PNetMeshUtils.MapToProtos(ipv4));
            var ipv4RoundTrip = Assert.IsType<IPEndPoint>(PNetMeshUtils.MapToItem(ipv4Proto));

            Assert.Equal(ipv4.Address, ipv4RoundTrip.Address);
            Assert.Equal(ipv4.Port, ipv4RoundTrip.Port);

            var ipv6 = new IPEndPoint(IPAddress.Parse("2001:db8::5"), 12403);
            var ipv6Proto = Required(PNetMeshUtils.MapToProtos(ipv6));
            var ipv6RoundTrip = Assert.IsType<IPEndPoint>(PNetMeshUtils.MapToItem(ipv6Proto));

            Assert.Equal(ipv6.Address, ipv6RoundTrip.Address);
            Assert.Equal(ipv6.Port, ipv6RoundTrip.Port);
            Assert.Equal(ipv6.Address.GetAddressBytes(), ipv6Proto.Ip.V6.ToByteArray());
        }

        [Fact]
        public void server_receive_command_normalizes_ipv4_mapped_remote_endpoint()
        {
            var local = new IPEndPoint(IPAddress.Any, 12401);
            var mapped = new IPEndPoint(IPAddress.Parse("::ffff:172.18.0.3"), 12402);

            var command = PNetMeshServer.CreateReceiveCommand(
                memoryOwner: null,
                localEndPoint: local,
                remoteEndPoint: mapped,
                memoryBuffer: ReadOnlyMemory<byte>.Empty);
            var endpoint = Assert.IsType<IPEndPoint>(command.RemoteEndPoint);

            Assert.Equal(AddressFamily.InterNetwork, endpoint.AddressFamily);
            Assert.Equal(IPAddress.Parse("172.18.0.3"), endpoint.Address);
            Assert.Equal(12402, endpoint.Port);
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
        public void route_path_diagnostic_preserves_route_endpoint_and_destination()
        {
            var source = Address(36);
            var relay = Address(37);
            var destination = Address(38);
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12403);
            var packet = new PNetMeshRelayPacket
            {
                Address = destination,
                SeqNumber = 43,
                HopCount = 2,
                Route = ImmutableArray.Create(source, relay, destination),
                Payload = Encoding.UTF8.GetBytes("diagnostic")
            };

            var routePath = PNetMeshRoutePath.FromRelayPacket(packet, remoteEndPoint);

            Assert.Equal(remoteEndPoint, routePath.RemoteEndPoint);
            Assert.Equal((ushort)2, routePath.RemainingHopCount);
            Assert.Equal(destination, routePath.DestinationAddress);
            Assert.Equal(new[] { source, relay, destination }, routePath.Route);
            Assert.NotSame(destination, routePath.DestinationAddress);
            Assert.NotSame(source, routePath.Route[0]);
            Assert.Equal($"{Convert.ToHexString(source)} -> {Convert.ToHexString(relay)} -> {Convert.ToHexString(destination)}", routePath.ToString());

            source[0] = 0xff;
            relay[0] = 0xee;
            destination[0] = 0xdd;

            Assert.Equal(Address(36), routePath.Route[0]);
            Assert.Equal(Address(37), routePath.Route[1]);
            Assert.Equal(Address(38), routePath.DestinationAddress);
        }

        [Fact]
        public async Task channel_route_path_reader_exposes_written_diagnostics()
        {
            using var channel = new PNetMeshChannel();
            var routePath = new PNetMeshRoutePath
            {
                DestinationAddress = Address(39),
                Route = ImmutableArray.Create(Address(40), Address(41), Address(39)),
                RemainingHopCount = 1,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12404)
            };

            Assert.True(channel.TryWriteRoutePath(routePath));

            var ready = await channel.WaitToReadRoutePathAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.True(ready);
            Assert.True(channel.TryReadRoutePath(out var readRoutePath));
            Assert.Same(routePath, readRoutePath);
            Assert.False(channel.TryReadRoutePath(out _));
        }

        [Fact]
        public async Task server_local_relay_writes_route_path_diagnostic_to_target_channel()
        {
            using var localKey = KeyPair.Generate();
            using var remoteKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12405);
            var settings = new PNetMeshServerSettings
            {
                PublicKey = localKey.PublicKey,
                PrivateKey = localKey.PrivateKey,
                Psk = psk,
                BindTo = Array.Empty<string>()
            };
            var localAddress = DeriveAddress(settings.PublicKey);
            var remoteAddress = DeriveAddress(remoteKey.PublicKey);

            using var server = new PNetMeshServer(settings);
            server.Start();

            try
            {
                var channel = await server.ConnectToAsync(new PNetMeshPeer
                {
                    PublicKey = remoteKey.PublicKey,
                    EndPoints = Array.Empty<string>()
                }, TestContext.Current.CancellationToken);

                var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var packet = new PNetMeshRelayPacket
                {
                    Address = localAddress,
                    SeqNumber = 44,
                    HopCount = 1,
                    Route = ImmutableArray.Create(remoteAddress, localAddress),
                    Payload = new byte[] { 0xff },
                    CandidateExchange = new PNetMeshCandidateExchange
                    {
                        Candidates = ImmutableArray.Create(new PNetMeshCandidate
                        {
                            Address = remoteEndPoint,
                            Protocol = PNetMeshProtocolType.UDP,
                            Type = PNetMeshCandidateType.Host
                        })
                    }
                };

                Assert.True(GetServerControlChannel(server).Writer.TryWrite(new PNetMeshControlCommands.RelayPacket
                {
                    Packet = packet,
                    Result = result
                }));

                await result.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

                Assert.True(channel.TryReadRoutePath(out var routePath));
                Assert.Equal(remoteEndPoint, routePath.RemoteEndPoint);
                Assert.Equal((ushort)1, routePath.RemainingHopCount);
                Assert.Equal(localAddress, routePath.DestinationAddress);
                Assert.Equal(new[] { remoteAddress, localAddress }, routePath.Route);
                Assert.Equal($"{Convert.ToHexString(remoteAddress)} -> {Convert.ToHexString(localAddress)}", routePath.ToString());
            }
            finally
            {
                await server.ShutdownAsync(TestContext.Current.CancellationToken);
            }
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
            Assert.NotNull(exchange);
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
        public void session_pending_direct_candidate_sends_probe_while_preserving_relay_path()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var relayEndpoint = new IPEndPoint(IPAddress.Loopback, 25000);
            var directCandidate = new IPEndPoint(IPAddress.Loopback, 25002);

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25001),
                RemoteEndPoint = relayEndpoint
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25002),
                RemoteEndPoint = null
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            Assert.True(sender.TryApplyDirectEndpointHint(directCandidate, DateTimeOffset.UtcNow));

            sender.WritePayload(Encoding.UTF8.GetBytes("probe"));

            var directProbe = ReadPacket(senderOutbound);
            var relayPacket = ReadPacket(senderOutbound);

            Assert.Equal(directCandidate, directProbe.RemoteEndPoint);
            Assert.Equal(relayEndpoint, relayPacket.RemoteEndPoint);
            Assert.Equal(relayEndpoint, sender.RemoteEndPoint);

            InvokeRetransTimeout(sender);
            var retransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            retransmit.Handler();

            var retransmittedRelayPacket = ReadPacket(senderOutbound);
            Assert.Equal(relayEndpoint, retransmittedRelayPacket.RemoteEndPoint);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            InvokeRetransTimeout(sender);
            var laterRetransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            laterRetransmit.Handler();

            var laterRelayPacket = ReadPacket(senderOutbound);
            Assert.Equal(relayEndpoint, laterRelayPacket.RemoteEndPoint);
            Assert.False(senderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_try_read_message_reports_authenticated_replay_failure()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25102)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25101)
            };

            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var packet = WritePayloadPacket(sender, senderOutbound, "authenticated");

            Assert.True(receiver.TryReadMessage(packet.MemoryBuffer.Span));
            AssertPayload(receiverChannels.Inbound, "authenticated");
            Assert.False(receiver.TryReadMessage(packet.MemoryBuffer.Span));
        }

        [Fact]
        public void session_try_read_response_reports_mismatched_response_without_opening()
        {
            using var senderKey = KeyPair.Generate();
            using var otherSenderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var otherSenderProtocol = new PNetMeshProtocol(otherSenderKey.PrivateKey, otherSenderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var otherSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25202)
            };
            using var otherSender = new PNetMeshSession(otherSenderProtocol, otherSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25203),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25202)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25201)
            };

            sender.WriteInitialize(1, receiverKey.PublicKey);
            otherSender.WriteInitialize(1, receiverKey.PublicKey);
            var otherInitialize = ReadPacket(otherSenderOutbound);

            Assert.True(receiver.TryReadInitialize(2, otherInitialize.MemoryBuffer.Span));
            receiver.WriteResponse();
            var response = ReadPacket(receiverOutbound);

            Assert.False(sender.TryReadResponse(response.MemoryBuffer.Span));
            Assert.Equal(PNetMeshSessionStatus.Closed, sender.Status);
        }

        [Fact]
        public void responder_write_response_opens_session_for_outbound_payload()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25211),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25212)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25212),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25211)
            };

            AttachSession(receiver);
            sender.WriteInitialize(1, receiverKey.PublicKey);
            var initialize = ReadPacket(senderOutbound);
            Assert.True(receiver.TryReadInitialize(2, initialize.MemoryBuffer.Span));

            receiver.WriteResponse();
            Assert.Equal(PNetMeshSessionStatus.Open, receiver.Status);
            ReadPacket(receiverOutbound);

            receiver.WritePayload(Encoding.UTF8.GetBytes("response"));

            Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(receiverOutbound));
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
        public void session_out_of_order_payload_mode_delivers_datagrams_without_waiting_for_gap()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20311),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20312)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20312),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20311),
                AllowOutOfOrderPayloadDelivery = true
            };

            AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var first = WritePayloadPacket(sender, senderOutbound, "first");
            var second = WritePayloadPacket(sender, senderOutbound, "second");

            receiver.ReadMessage(second.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "second");

            receiver.ReadMessage(first.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "first");
            Assert.False(receiverChannels.Inbound.Reader.TryRead(out _));
        }

        [Fact]
        public void session_do_not_ack_payloads_do_not_emit_ack_work()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20321),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20322),
                UnreliablePayloadDelivery = true
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20322),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20321),
                AllowOutOfOrderPayloadDelivery = true
            };

            var senderChannels = AttachSession(sender);
            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var first = WritePayloadPacket(sender, senderOutbound, "first");
            receiver.ReadMessage(first.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "first");

            Assert.Equal(1UL, GetReceiveCounter(receiver));
            Assert.Equal(0UL, GetReceiveAck(receiver));

            InvokeCumAckTimeout(receiver);
            Assert.False(receiverChannels.Control.Reader.TryRead(out _));
            Assert.False(receiverOutbound.Reader.TryRead(out _));

            receiver.WritePayload(Encoding.UTF8.GetBytes("reply"));
            var reply = ReadPacket(receiverOutbound);
            sender.ReadMessage(reply.MemoryBuffer.Span);

            AssertPayload(senderChannels.Inbound, "reply");
            Assert.Equal(0UL, GetRemoteAckSeqNumber(sender));
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
        public void session_releases_selectively_acknowledged_packets_from_send_window()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20411),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20412)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20412),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20411)
            };

            AttachSession(sender);
            AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var syn = WriteSynPacket(receiver,
                maxOutstandingSeq: 2,
                maxPacketSize: 1280,
                maxCumAck: 100,
                retransmissionTimeout: 10000,
                cumulativeAckTimeout: 10000);
            sender.ReadMessage(syn.Span);

            WritePayloadPacket(sender, senderOutbound, "first");
            WritePayloadPacket(sender, senderOutbound, "second");
            Assert.Equal(2, GetRetransBufferCount(sender));

            var outOfSequenceAck = WriteAckPacket(receiver, ackSeqNumber: 0, outOfSeqPackets: new byte[] { 0x02 });
            sender.ReadMessage(outOfSequenceAck.Span);

            Assert.Equal(1, GetRetransBufferCount(sender));
            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            Assert.True(senderOutbound.Reader.TryRead(out _));
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
        public void session_default_send_window_allows_startup_burst_before_syn_negotiation()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20711),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20712)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20712),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20711)
            };

            AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            sender.WritePayload(Encoding.UTF8.GetBytes("second"));
            sender.WritePayload(Encoding.UTF8.GetBytes("third"));
            sender.WritePayload(Encoding.UTF8.GetBytes("fourth"));

            for (var i = 0; i < 4; i++)
                Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound));
            Assert.False(senderOutbound.Reader.TryRead(out _));
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

            receiver.ReadMessage(second.MemoryBuffer.Span);
            receiver.WritePacket();
            var outOfOrderAck = ReadPacket(receiverOutbound);
            sender.ReadMessage(outOfOrderAck.MemoryBuffer.Span);
            Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            var blockedFlush = Assert.IsType<PNetMeshChannelCommands.Invoke>(ReadControl(senderChannels));
            blockedFlush.Handler();
            var third = ReadPacket(senderOutbound);
            Assert.False(senderOutbound.Reader.TryRead(out _));

            receiver.ReadMessage(first.MemoryBuffer.Span);
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
            Assert.NotNull(queueControl);

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
            var exactPacketSize = PNetMeshPayloadFraming.CalculatePNetFrameSize(packet.CalculateSize());

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
        public async Task channel_relay_async_rejects_framed_packet_size_before_flush()
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
                maxPacketSize: PNetMeshPayloadFraming.CalculatePNetFrameSize(CalculateRelayPacketSize(secondPacket)) - 1,
                maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            var secondRelay = channel.RelayAsync(secondPacket, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => secondRelay);
            Assert.Equal(PNetMeshSessionStatus.Open, sender.Status);
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
            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

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
            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

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
        public async Task session_dispose_fails_pending_payload()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21711),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21712)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 21712),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 21711)
            };

            AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            var syn = WriteSynPacket(receiver, maxOutstandingSeq: 1, maxPacketSize: 1280, maxCumAck: 100);
            sender.ReadMessage(syn.Span);

            sender.WritePayload(Encoding.UTF8.GetBytes("first"));
            ReadPacket(senderOutbound);

            var pendingResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.WritePayload(Encoding.UTF8.GetBytes("second"), pendingResult);
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
        public async Task session_starts_retransmission_timer_after_tracked_send_without_ack()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22402)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22401)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            sender.WritePayload(Encoding.UTF8.GetBytes("lost"));
            ReadPacket(senderOutbound);

            var command = await senderChannels.Control.Reader
                .ReadAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var retransmit = Assert.IsType<PNetMeshChannelCommands.Invoke>(command);
            Assert.Equal("RetransmitPackets", retransmit.Handler.Method.Name);
        }

        [Fact]
        public async Task session_starts_cumulative_ack_timer_after_pending_ack()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22411),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22412)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22412),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22411)
            };

            var senderChannels = AttachSession(sender);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            var peerPayload = WriteAckPayloadPacket(receiver, ackSeqNumber: 0, payload: "peer");
            sender.ReadMessage(peerPayload.Span);

            var command = await senderChannels.Control.Reader
                .ReadAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var flush = Assert.IsType<PNetMeshChannelCommands.Invoke>(command);
            Assert.Equal("FlushAckTimeoutPacket", flush.Handler.Method.Name);
        }

        [Fact]
        public async Task session_status_changed_does_not_run_inline_during_owner_transition()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22421),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22422)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22422),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22421)
            };

            sender.WriteInitialize(1, receiverKey.PublicKey);
            var initialize = ReadPacket(senderOutbound);

            receiver.ReadInitialize(2, initialize.MemoryBuffer.Span);
            receiver.WriteResponse();
            var response = ReadPacket(receiverOutbound);

            var callerThreadId = Environment.CurrentManagedThreadId;
            var statusChangedThreadId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.StatusChanged += (_, _) => statusChangedThreadId.TrySetResult(Environment.CurrentManagedThreadId);

            sender.ReadResponse(response.MemoryBuffer.Span);

            var callbackThreadId = await statusChangedThreadId.Task
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.NotEqual(callerThreadId, callbackThreadId);
        }

        [Fact]
        public void session_status_read_waits_for_owner_lock()
        {
            using var senderKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer);
            var ownerLockField = typeof(PNetMeshSession).GetField("_sessionOwnerLock", BindingFlags.NonPublic | BindingFlags.Instance);
            var ownerLock = Assert.IsType<System.Threading.Lock>(ownerLockField?.GetValue(sender));
            using var completed = new ManualResetEventSlim();
            Exception? readException = null;

            using (ownerLock.EnterScope())
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        _ = sender.Status;
                    }
                    catch (Exception ex)
                    {
                        readException = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                })
                {
                    IsBackground = true
                };
                thread.Start();

                Assert.False(completed.Wait(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
            }

            Assert.True(completed.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            if (readException is not null)
                throw new InvalidOperationException("Status read failed.", readException);
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

        [Fact]
        public async Task channel_relay_waits_for_opening_session_to_become_routable()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var bootstrapSenderKey = KeyPair.Generate();
            using var bootstrapReceiverKey = KeyPair.Generate();
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var bootstrapSenderProtocol = new PNetMeshProtocol(bootstrapSenderKey.PrivateKey, bootstrapSenderKey.PublicKey, psk);
            var bootstrapReceiverProtocol = new PNetMeshProtocol(bootstrapReceiverKey.PrivateKey, bootstrapReceiverKey.PublicKey, psk);
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var bootstrapSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var bootstrapReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var bootstrapSender = new PNetMeshSession(bootstrapSenderProtocol, bootstrapSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22501),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22502)
            };
            using var bootstrapReceiver = new PNetMeshSession(bootstrapReceiverProtocol, bootstrapReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22502),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22501)
            };
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

            OpenSessionPair(bootstrapSender, bootstrapReceiver, bootstrapSenderOutbound, bootstrapReceiverOutbound, bootstrapReceiverKey.PublicKey);
            channel.AddSession(bootstrapSender);
            await WaitForStatusAsync(bootstrapSender, PNetMeshSessionStatus.Open);
            bootstrapSender.Dispose();

            sender.WriteInitialize(1, receiverKey.PublicKey);
            var initialize = ReadPacket(senderOutbound);
            receiver.ReadInitialize(2, initialize.MemoryBuffer.Span);
            receiver.WriteResponse();
            var response = ReadPacket(receiverOutbound);
            channel.AddSession(sender);
            Assert.Equal(PNetMeshSessionStatus.Opening, sender.Status);
            Assert.True(channel.HasRoutableSession);

            var packet = new PNetMeshRelayPacket
            {
                Address = Address(56),
                SeqNumber = 122,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(57)),
                Payload = Encoding.UTF8.GetBytes("queued relay")
            };

            var relayTask = channel.RelayAsync(packet, cancellationToken: TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(relayTask.IsCompleted);

            sender.ReadResponse(response.MemoryBuffer.Span);
            await relayTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var relayed = ReadPacket(senderOutbound);
            Assert.Equal(sender.RemoteEndPoint, relayed.RemoteEndPoint);
        }

        [Fact]
        public async Task channel_relay_wait_observes_later_added_routable_session()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var bootstrapSenderKey = KeyPair.Generate();
            using var bootstrapReceiverKey = KeyPair.Generate();
            using var staleSenderKey = KeyPair.Generate();
            using var staleReceiverKey = KeyPair.Generate();
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var bootstrapSenderProtocol = new PNetMeshProtocol(bootstrapSenderKey.PrivateKey, bootstrapSenderKey.PublicKey, psk);
            var bootstrapReceiverProtocol = new PNetMeshProtocol(bootstrapReceiverKey.PrivateKey, bootstrapReceiverKey.PublicKey, psk);
            var staleSenderProtocol = new PNetMeshProtocol(staleSenderKey.PrivateKey, staleSenderKey.PublicKey, psk);
            var staleReceiverProtocol = new PNetMeshProtocol(staleReceiverKey.PrivateKey, staleReceiverKey.PublicKey, psk);
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var bootstrapSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var bootstrapReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var staleSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var channel = new PNetMeshChannel();
            using var bootstrapSender = new PNetMeshSession(bootstrapSenderProtocol, bootstrapSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22511),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22512)
            };
            using var bootstrapReceiver = new PNetMeshSession(bootstrapReceiverProtocol, bootstrapReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22512),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22511)
            };
            using var staleCandidate = new PNetMeshSession(staleSenderProtocol, staleSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22521),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22522)
            };
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22531),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22532)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 22532),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 22531)
            };

            OpenSessionPair(bootstrapSender, bootstrapReceiver, bootstrapSenderOutbound, bootstrapReceiverOutbound, bootstrapReceiverKey.PublicKey);
            channel.AddSession(bootstrapSender);
            await WaitForStatusAsync(bootstrapSender, PNetMeshSessionStatus.Open);
            bootstrapSender.Dispose();

            staleCandidate.WriteInitialize(1, staleReceiverKey.PublicKey);
            DisposeRequired(Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(staleSenderOutbound)).MemoryOwner);
            channel.AddSession(staleCandidate);
            Assert.Equal(PNetMeshSessionStatus.Opening, staleCandidate.Status);

            var packet = new PNetMeshRelayPacket
            {
                Address = Address(58),
                SeqNumber = 123,
                HopCount = 2,
                Route = ImmutableArray.Create<byte[]>(Address(59)),
                Payload = Encoding.UTF8.GetBytes("later relay")
            };

            var relayTask = channel.RelayAsync(packet, cancellationToken: TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(relayTask.IsCompleted);

            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            channel.AddSession(sender);

            await relayTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var relayed = ReadPacket(senderOutbound);
            Assert.Equal(sender.RemoteEndPoint, relayed.RemoteEndPoint);
        }

        [Fact]
        public async Task channel_relay_async_cancels_while_queued_behind_pending_relay()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);

            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24531),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24532)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24532),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24531)
            };
            using var relayCandidate = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24533),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24534)
            };

            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            sender.RemoteEndPoint = null;

            relayCandidate.WriteInitialize(3, receiverKey.PublicKey);
            DisposeRequired(Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound)).MemoryOwner);

            using var channel = new PNetMeshChannel();
            channel.AddSession(sender);
            channel.AddSession(relayCandidate);

            Assert.True(channel.TryRelay(new PNetMeshRelayPacket
            {
                Address = Address(61),
                SeqNumber = 1,
                HopCount = 1,
                Route = ImmutableArray.Create<byte[]>(Address(62)),
                Payload = Encoding.UTF8.GetBytes("pending")
            }));

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var relayTask = channel.RelayAsync(new PNetMeshRelayPacket
            {
                Address = Address(63),
                SeqNumber = 2,
                HopCount = 1,
                Route = ImmutableArray.Create<byte[]>(Address(64)),
                Payload = Encoding.UTF8.GetBytes("cancel")
            }, cancellationToken: cancel.Token);

            await Task.Delay(50, TestContext.Current.CancellationToken);
            cancel.Cancel();

            var exception = await Record.ExceptionAsync(async () =>
                await relayTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
        }

        [Fact]
        public async Task channel_relay_wait_fails_when_channel_is_disposed()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var bootstrapSenderKey = KeyPair.Generate();
            using var bootstrapReceiverKey = KeyPair.Generate();
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var bootstrapSenderProtocol = new PNetMeshProtocol(bootstrapSenderKey.PrivateKey, bootstrapSenderKey.PublicKey, psk);
            var bootstrapReceiverProtocol = new PNetMeshProtocol(bootstrapReceiverKey.PrivateKey, bootstrapReceiverKey.PublicKey, psk);
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var bootstrapSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var bootstrapReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();

            using var bootstrapSender = new PNetMeshSession(bootstrapSenderProtocol, bootstrapSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24535),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24536)
            };
            using var bootstrapReceiver = new PNetMeshSession(bootstrapReceiverProtocol, bootstrapReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24536),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24535)
            };
            using var relayCandidate = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24541),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24542)
            };

            using var channel = new PNetMeshChannel();
            OpenSessionPair(bootstrapSender, bootstrapReceiver, bootstrapSenderOutbound, bootstrapReceiverOutbound, bootstrapReceiverKey.PublicKey);
            channel.AddSession(bootstrapSender);
            await WaitForStatusAsync(bootstrapSender, PNetMeshSessionStatus.Open);
            bootstrapSender.Dispose();

            relayCandidate.WriteInitialize(3, receiverKey.PublicKey);
            DisposeRequired(Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound)).MemoryOwner);
            channel.AddSession(relayCandidate);
            Assert.Equal(PNetMeshSessionStatus.Opening, relayCandidate.Status);
            Assert.True(channel.HasRoutableSession);

            var relayTask = channel.RelayAsync(new PNetMeshRelayPacket
            {
                Address = Address(65),
                SeqNumber = 3,
                HopCount = 1,
                Route = ImmutableArray.Create<byte[]>(Address(66)),
                Payload = Encoding.UTF8.GetBytes("dispose")
            }, cancellationToken: TestContext.Current.CancellationToken);

            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(relayTask.IsCompleted);

            channel.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                relayTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task channel_replies_use_session_that_delivered_recent_inbound_payload()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var firstSenderKey = KeyPair.Generate();
            using var firstReceiverKey = KeyPair.Generate();
            var firstSenderProtocol = new PNetMeshProtocol(firstSenderKey.PrivateKey, firstSenderKey.PublicKey, psk);
            var firstReceiverProtocol = new PNetMeshProtocol(firstReceiverKey.PrivateKey, firstReceiverKey.PublicKey, psk);
            var firstSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var firstReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var firstSender = new PNetMeshSession(firstSenderProtocol, firstSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24401),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24402)
            };
            using var firstReceiver = new PNetMeshSession(firstReceiverProtocol, firstReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24402),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24401)
            };

            using var secondSenderKey = KeyPair.Generate();
            using var secondReceiverKey = KeyPair.Generate();
            var secondSenderProtocol = new PNetMeshProtocol(secondSenderKey.PrivateKey, secondSenderKey.PublicKey, psk);
            var secondReceiverProtocol = new PNetMeshProtocol(secondReceiverKey.PrivateKey, secondReceiverKey.PublicKey, psk);
            var secondSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var secondReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var secondSender = new PNetMeshSession(secondSenderProtocol, secondSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24411),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24412)
            };
            using var secondReceiver = new PNetMeshSession(secondReceiverProtocol, secondReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24412),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24411)
            };

            OpenSessionPair(firstSender, firstReceiver, firstSenderOutbound, firstReceiverOutbound, firstReceiverKey.PublicKey);
            OpenSessionPair(secondSender, secondReceiver, secondSenderOutbound, secondReceiverOutbound, secondReceiverKey.PublicKey);

            using var channel = new PNetMeshChannel();
            channel.AddSession(firstSender);
            channel.AddSession(secondSender);

            firstSender.ReadMessage(WriteAckPayloadPacket(firstReceiver, ackSeqNumber: 0, payload: "request").Span);
            AssertPayload(GetInboundChannel(channel), "request");

            await channel.WriteAsync(Encoding.UTF8.GetBytes("reply"), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(firstSenderOutbound));
            Assert.False(secondSenderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task channel_ack_only_packets_do_not_steal_current_session()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var firstSenderKey = KeyPair.Generate();
            using var firstReceiverKey = KeyPair.Generate();
            var firstSenderProtocol = new PNetMeshProtocol(firstSenderKey.PrivateKey, firstSenderKey.PublicKey, psk);
            var firstReceiverProtocol = new PNetMeshProtocol(firstReceiverKey.PrivateKey, firstReceiverKey.PublicKey, psk);
            var firstSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var firstReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var firstSender = new PNetMeshSession(firstSenderProtocol, firstSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24421),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24422)
            };
            using var firstReceiver = new PNetMeshSession(firstReceiverProtocol, firstReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24422),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24421)
            };

            using var secondSenderKey = KeyPair.Generate();
            using var secondReceiverKey = KeyPair.Generate();
            var secondSenderProtocol = new PNetMeshProtocol(secondSenderKey.PrivateKey, secondSenderKey.PublicKey, psk);
            var secondReceiverProtocol = new PNetMeshProtocol(secondReceiverKey.PrivateKey, secondReceiverKey.PublicKey, psk);
            var secondSenderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var secondReceiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var secondSender = new PNetMeshSession(secondSenderProtocol, secondSenderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24431),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24432)
            };
            using var secondReceiver = new PNetMeshSession(secondReceiverProtocol, secondReceiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24432),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24431)
            };

            OpenSessionPair(firstSender, firstReceiver, firstSenderOutbound, firstReceiverOutbound, firstReceiverKey.PublicKey);
            OpenSessionPair(secondSender, secondReceiver, secondSenderOutbound, secondReceiverOutbound, secondReceiverKey.PublicKey);

            using var channel = new PNetMeshChannel();
            channel.AddSession(firstSender);
            channel.AddSession(secondSender);

            firstSender.ReadMessage(WriteAckPacket(firstReceiver, ackSeqNumber: 0, outOfSeqPackets: Array.Empty<byte>()).Span);

            await channel.WriteAsync(Encoding.UTF8.GetBytes("reply"), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(secondSenderOutbound));
            Assert.False(firstSenderOutbound.Reader.TryRead(out _));
        }

        [Fact]
        public async Task channel_unreliable_payload_delivery_does_not_track_payload_for_retransmit()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24501),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24502)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24502),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24501)
            };

            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            using var channel = new PNetMeshChannel();
            channel.AddSession(sender);

            await channel.EnqueueUnreliableWriteAsync(Encoding.UTF8.GetBytes("datagram"), TestContext.Current.CancellationToken);

            Assert.Equal(0, GetRetransBufferCount(sender));
            var packet = await ReadPacketAsync(senderOutbound);
            receiver.ReadMessage(packet.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "datagram");

            await channel.WriteAsync(Encoding.UTF8.GetBytes("reliable"), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Equal(1, GetRetransBufferCount(sender));
        }

        [Fact]
        public async Task channel_enqueue_write_copies_payload_before_returning()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24521),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24522)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24522),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24521)
            };

            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            using var channel = new PNetMeshChannel();
            channel.AddSession(sender);

            var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(GetControlChannel(channel).Writer.TryWrite(new PNetMeshChannelCommands.Invoke
            {
                Handler = () =>
                {
                    handlerStarted.SetResult();
                    releaseHandler.Task.GetAwaiter().GetResult();
                }
            }));
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var payload = Encoding.UTF8.GetBytes("original");
            await channel.EnqueueWriteAsync(payload, TestContext.Current.CancellationToken);
            payload[0] = (byte)'X';
            releaseHandler.SetResult();

            var packet = await ReadPacketAsync(senderOutbound);
            receiver.ReadMessage(packet.MemoryBuffer.Span);
            AssertPayload(receiverChannels.Inbound, "original");
        }

        [Fact]
        public async Task channel_enqueue_owned_write_queues_original_owner()
        {
            using var channel = new PNetMeshChannel();
            var owner = new TrackingMemoryOwner(16);
            Encoding.UTF8.GetBytes("original").CopyTo(owner.Memory.Span.Slice(4));

            await channel.EnqueueUnreliableWriteAsync(
                owner.Memory.Slice(4, 8),
                owner,
                TestContext.Current.CancellationToken);

            Assert.True(GetControlChannel(channel).Reader.TryRead(out var command));
            var send = Assert.IsType<PNetMeshChannelCommands.Send>(command);

            Assert.Same(owner, send.MemoryOwner);
            Assert.Equal("original", Encoding.UTF8.GetString(send.Payload.Span));
            Assert.Equal(0, owner.DisposeCount);

            DisposeRequired(send.MemoryOwner);
            Assert.Equal(1, owner.DisposeCount);
        }

        [Fact]
        public async Task channel_enqueue_owned_write_disposes_owner_after_send()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24523),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24524)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24524),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24523)
            };

            var receiverChannels = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);

            using var channel = new PNetMeshChannel();
            channel.AddSession(sender);

            var owner = new TrackingMemoryOwner(16);
            Encoding.UTF8.GetBytes("original").CopyTo(owner.Memory.Span);

            await channel.EnqueueUnreliableWriteAsync(
                owner.Memory.Slice(0, 8),
                owner,
                TestContext.Current.CancellationToken);

            var packet = await ReadPacketAsync(senderOutbound);
            receiver.ReadMessage(packet.MemoryBuffer.Span);

            AssertPayload(receiverChannels.Inbound, "original");
            Assert.True(SpinWait.SpinUntil(() => owner.DisposeCount == 1, TimeSpan.FromSeconds(2)));
            Assert.True(owner.IsCleared);
        }

        [Fact]
        public async Task channel_dispose_clears_queued_owned_write_before_control_loop_starts()
        {
            var channel = new PNetMeshChannel();
            var owner = new TrackingMemoryOwner(16);
            Encoding.UTF8.GetBytes("queued").CopyTo(owner.Memory.Span);

            await channel.EnqueueUnreliableWriteAsync(
                owner.Memory.Slice(0, 6),
                owner,
                TestContext.Current.CancellationToken);

            channel.Dispose();

            Assert.Equal(1, owner.DisposeCount);
            Assert.True(owner.IsCleared);
        }

        [Fact]
        public async Task channel_pending_relay_candidate_does_not_block_payload_sends()
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
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24511),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24512)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24512),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24511)
            };
            using var relayCandidate = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 24513),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 24514)
            };

            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            sender.RemoteEndPoint = null;

            relayCandidate.WriteInitialize(3, receiverKey.PublicKey);
            DisposeRequired(Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(senderOutbound)).MemoryOwner);

            using var channel = new PNetMeshChannel();
            channel.AddSession(sender);
            channel.AddSession(relayCandidate);

            Assert.True(channel.TryRelay(new PNetMeshRelayPacket
            {
                Address = Address(7),
                SeqNumber = 1,
                HopCount = 1,
                Route = ImmutableArray.Create<byte[]>(Address(1)),
                Payload = Encoding.UTF8.GetBytes("relay")
            }));

            await channel.EnqueueWriteAsync(
                Encoding.UTF8.GetBytes("payload"),
                TestContext.Current.CancellationToken);

            var outbound = await ReadPacketAsync(senderOutbound);
            Assert.Null(outbound.RemoteEndPoint);
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
            Assert.Equal(
                receiver.RemoteEndPoint is null ? PNetMeshSessionStatus.Opening : PNetMeshSessionStatus.Open,
                receiver.Status);
        }

        readonly struct AttachedSession
        {
            public Channel<ReadOnlyMemory<byte>> Inbound { get; init; }

            public Channel<PNetMeshChannelCommands.Command> Control { get; init; }
        }

        sealed class TrackingMemoryOwner : IMemoryOwner<byte>
        {
            readonly byte[] _buffer;

            public TrackingMemoryOwner(int length)
            {
                _buffer = new byte[length];
            }

            public Memory<byte> Memory => _buffer;

            public int DisposeCount { get; private set; }

            public bool IsCleared => _buffer.All(value => value == 0);

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        sealed class BlockingControlWriter : ChannelWriter<PNetMeshChannelCommands.Command>
        {
            readonly TaskCompletionSource _write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> _wait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool TryComplete(Exception? error = null)
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
            return WritePNetPacket(session, packet);
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

            return WritePNetPacket(session, packet);
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
            return WritePNetPacket(session, packet);
        }

        static ReadOnlyMemory<byte> WritePNetPacket(PNetMeshSession session, PNet.Actor.Mesh.Protos.Packet packet)
        {
            var payload = packet.ToByteArray();
            var frame = PNetMeshPayloadFraming.CreatePNet(payload);
            var buffer = new byte[frame.Length + 48];
            var transport = GetTransport(session);
            transport.WriteMessage(frame, buffer, out var bytesWritten, out _);
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

        static T Required<T>(T? value) where T : class
        {
            return value ?? throw new InvalidOperationException($"{typeof(T).Name} was expected to be non-null.");
        }

        static void DisposeRequired(IMemoryOwner<byte>? memoryOwner)
        {
            Required(memoryOwner).Dispose();
        }

        static T GetPrivateField<T>(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field '{name}' was not found on {instance.GetType().Name}.");
            var value = field.GetValue(instance);
            if (value is not T typed)
                throw new InvalidOperationException($"Field '{name}' was not a {typeof(T).Name}.");
            return typed;
        }

        static PropertyInfo GetRequiredProperty(Type type, string name)
        {
            return type.GetProperty(name)
                ?? throw new InvalidOperationException($"Property '{name}' was not found on {type.Name}.");
        }

        static MethodInfo GetRequiredMethod(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Method '{name}' was not found on {type.Name}.");
        }

        static PNetMeshTransport2 GetTransport(PNetMeshSession session)
        {
            return GetPrivateField<PNetMeshTransport2>(session, "_transport");
        }

        static ulong GetRemoteAckSeqNumber(PNetMeshSession session)
        {
            var ack = GetPrivateField<object>(session, "_remoteAck");
            var seqNumber = GetRequiredProperty(ack.GetType(), "SeqNumber");
            return Assert.IsType<ulong>(seqNumber.GetValue(ack));
        }

        static byte[] GetRemoteAckOutOfOrder(PNetMeshSession session)
        {
            var ack = GetPrivateField<object>(session, "_remoteAck");
            var outOfOrder = GetRequiredProperty(ack.GetType(), "OutOfOrder");
            return Assert.IsType<ReadOnlyMemory<byte>>(outOfOrder.GetValue(ack)).ToArray();
        }

        static ulong GetReceiveCounter(PNetMeshSession session)
        {
            return GetPrivateField<ulong>(session, "_receiveCounter");
        }

        static ulong GetReceiveAck(PNetMeshSession session)
        {
            return GetPrivateField<ulong>(session, "_receiveAck");
        }

        static int GetRetransBufferCount(PNetMeshSession session)
        {
            var buffer = GetPrivateField<PNetMeshPacketBuffer>(session, "_retransBuffer");
            return buffer.Count;
        }

        static int GetCumAckMax(PNetMeshSession session)
        {
            return GetPrivateField<int>(session, "_cumAck_max");
        }

        static int GetOutOfOrderMax(PNetMeshSession session)
        {
            return GetPrivateField<int>(session, "_outOfOrder_max");
        }

        static void InvokeCumAckTimeout(PNetMeshSession session)
        {
            var method = GetRequiredMethod(typeof(PNetMeshSession), "OnCumAckTimeout");
            method.Invoke(session, new object?[] { null });
        }

        static void InvokeRetransTimeout(PNetMeshSession session)
        {
            var method = GetRequiredMethod(typeof(PNetMeshSession), "OnRetransTimeout");
            method.Invoke(session, new object?[] { null });
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
            return GetPrivateField<Channel<PNetMeshChannelCommands.Command>>(channel, "_controlChannel");
        }

        static Channel<ReadOnlyMemory<byte>> GetInboundChannel(PNetMeshChannel channel)
        {
            return GetPrivateField<Channel<ReadOnlyMemory<byte>>>(channel, "_inboundChannel");
        }

        static Channel<PNetMeshControlCommands.Command> GetServerControlChannel(PNetMeshServer server)
        {
            return GetPrivateField<Channel<PNetMeshControlCommands.Command>>(server, "_controlChannel");
        }

        static PNetMeshOutboundMessages.Packet ReadPacket(Channel<PNetMeshOutboundMessages.Message> channel)
        {
            return Assert.IsType<PNetMeshOutboundMessages.Packet>(ReadMessage(channel));
        }

        static async Task<PNetMeshOutboundMessages.Packet> ReadPacketAsync(Channel<PNetMeshOutboundMessages.Message> channel)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (await channel.Reader.WaitToReadAsync(cts.Token))
            {
                if (channel.Reader.TryRead(out var message))
                    return Assert.IsType<PNetMeshOutboundMessages.Packet>(message);
            }

            throw new TimeoutException("Timed out waiting for outbound packet.");
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

        static byte[] DeriveAddress(byte[] publicKey)
        {
            var address = new byte[10];
            PNetMeshUtils.GetAddressFromPublicKey(publicKey, address);
            return address;
        }

        static void AssertDnsEndPoint(string host, int port, EndPoint? endPoint)
        {
            var dnsEndPoint = Assert.IsType<DnsEndPoint>(endPoint);
            Assert.Equal(host, dnsEndPoint.Host);
            Assert.Equal(port, dnsEndPoint.Port);
        }
    }
}
