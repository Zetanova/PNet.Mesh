using Microsoft.Extensions.Logging;
using Noise;
using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshDiagnosticsTests
    {
        [Fact]
        public void redactor_hashes_diagnostic_ids_without_raw_values()
        {
            var endpoint = Endpoint("203.0.113.77", 51820);
            var publicKey = Enumerable.Repeat((byte)0xab, 32).ToArray();
            var address = Encoding.ASCII.GetBytes("secretaddr");

            var endpointId = PNetMeshDiagnosticRedactor.EndpointId(endpoint);
            var publicKeyId = PNetMeshDiagnosticRedactor.PublicKeyId(publicKey);
            var addressId = PNetMeshDiagnosticRedactor.AddressId(address);
            var joined = string.Join(" ", endpointId, publicKeyId, addressId);

            Assert.Matches("^ep:[0-9A-F]{12}$", endpointId);
            Assert.Matches("^peer:[0-9A-F]{12}$", publicKeyId);
            Assert.Matches("^addr:[0-9A-F]{12}$", addressId);
            Assert.DoesNotContain("203.0.113.77", joined);
            Assert.DoesNotContain("51820", joined);
            Assert.DoesNotContain(Convert.ToHexString(publicKey), joined);
            Assert.DoesNotContain("secretaddr", joined);
        }

        [Fact]
        public void wireguard_relay_registry_emits_redacted_lease_demux_fast_path_and_expiry_events()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint("203.0.113.77", 51820);
            var logger = new CaptureLogger<PNetMeshWireGuardRelayRegistry>();
            var registry = new PNetMeshWireGuardRelayRegistry(
                new PNetMeshWireGuardRelayOptions { ReceiverIndexTtl = TimeSpan.FromSeconds(5) },
                (_, key) => key[0] == 0xab,
                logger);
            var publicKey = Key(0xab);
            var expiringKey = Key(0xcd);
            var returnAddress = Encoding.ASCII.GetBytes("secretaddr");

            registry.RegisterOrRenew(publicKey, returnAddress, now.AddMinutes(1), now, new[] { endpoint });
            registry.RegisterOrRenew(expiringKey, Address(2), now.AddMilliseconds(1), now);

            Assert.True(registry.TryRoute(Handshake(senderIndex: 99), endpoint, now, out _, out _));
            Assert.True(registry.TryRoute(Transport(receiverIndex: 99), endpoint, now.AddSeconds(1), out _, out _));
            Assert.True(registry.Release(publicKey));
            Assert.False(registry.TryRoute(Handshake(senderIndex: 100), endpoint, now.AddSeconds(1), out _, out _));

            var joined = logger.JoinedEntries;
            Assert.Contains("event=wireguard_relay_lease_registered", joined);
            Assert.Contains("event=wireguard_relay_demux", joined);
            Assert.Contains("event=wireguard_relay_receiver_index_learned", joined);
            Assert.Contains("event=wireguard_relay_fast_path", joined);
            Assert.Contains("event=wireguard_relay_lease_released", joined);
            Assert.Contains("event=wireguard_relay_lease_expired", joined);
            Assert.DoesNotContain("203.0.113.77", joined);
            Assert.DoesNotContain("51820", joined);
            Assert.DoesNotContain(Convert.ToHexString(publicKey), joined);
            Assert.DoesNotContain("secretaddr", joined);
        }

        [Fact]
        public void wireguard_direct_probe_start_log_is_redacted()
        {
            using var localKey = KeyPair.Generate();
            using var remoteKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var logger = new CaptureLogger<PNetMeshSession>();
            var outbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var protocol = new PNetMeshProtocol(
                localKey.PrivateKey,
                localKey.PublicKey,
                psk,
                PNetMeshTransportMode.WireGuard);
            var candidate = Endpoint("203.0.113.77", 51820);

            using var session = new PNetMeshSession(protocol, outbound.Writer, logger)
            {
                RemoteEndPoint = Endpoint("203.0.113.78", 51821)
            };

            session.WriteInitialize(42, remoteKey.PublicKey);
            Assert.True(outbound.Reader.TryRead(out var initialize));
            Assert.IsType<PNetMeshOutboundMessages.Packet>(initialize).MemoryOwner.Dispose();

            Assert.True(session.TryApplyDirectEndpointHint(candidate, DateTimeOffset.Parse("2026-07-01T00:00:00Z")));
            Assert.True(session.TryGetDirectProbeEndpoint(DateTimeOffset.Parse("2026-07-01T00:00:01Z"), out var probeEndpoint));
            Assert.Equal(candidate, probeEndpoint);

            var joined = logger.JoinedEntries;
            Assert.Contains("event=wireguard_direct_probe_started", joined);
            Assert.Contains("session=42", joined);
            Assert.DoesNotContain("203.0.113.77", joined);
            Assert.DoesNotContain("51820", joined);
        }

        [Fact]
        public void server_endpoint_hint_and_promotion_logs_are_redacted()
        {
            using var serverKey = KeyPair.Generate();
            using var remoteKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var logger = new CaptureLogger<PNetMeshServer>();
            var outbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var protocol = new PNetMeshProtocol(
                serverKey.PrivateKey,
                serverKey.PublicKey,
                psk,
                PNetMeshTransportMode.WireGuard);
            var settings = new PNetMeshServerSettings
            {
                PublicKey = serverKey.PublicKey,
                PrivateKey = serverKey.PrivateKey,
                Psk = psk,
                BindTo = Array.Empty<string>()
            };
            using var server = new PNetMeshServer(settings, logger: logger);
            using var session = new PNetMeshSession(protocol, outbound.Writer, logger)
            {
                RemoteEndPoint = Endpoint("203.0.113.78", 51821)
            };
            var candidate = Endpoint("203.0.113.77", 51820);

            session.WriteInitialize(7, remoteKey.PublicKey);
            Assert.True(outbound.Reader.TryRead(out var initialize));
            Assert.IsType<PNetMeshOutboundMessages.Packet>(initialize).MemoryOwner.Dispose();

            ApplyAuthenticatedEndpointUpdate(server, session, new PNetMeshControlCommands.Receive
            {
                RelayCandidateEndPoint = candidate
            });
            Assert.True(session.TryGetDirectProbeEndpoint(DateTimeOffset.UtcNow, out _));
            ApplyAuthenticatedEndpointUpdate(server, session, new PNetMeshControlCommands.Receive
            {
                RemoteEndPoint = candidate
            });

            var joined = logger.JoinedEntries;
            Assert.Contains("event=wireguard_endpoint_hint_queued", joined);
            Assert.Contains("event=wireguard_direct_promoted", joined);
            Assert.DoesNotContain("203.0.113.77", joined);
            Assert.DoesNotContain("51820", joined);
        }

        [Fact]
        public async Task server_relay_delivery_log_is_redacted()
        {
            using var localKey = KeyPair.Generate();
            using var remoteKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var logger = new CaptureLogger<PNetMeshServer>();
            var relayEndPoint = Endpoint("203.0.113.77", 51820);
            var settings = new PNetMeshServerSettings
            {
                PublicKey = localKey.PublicKey,
                PrivateKey = localKey.PrivateKey,
                Psk = psk,
                BindTo = Array.Empty<string>()
            };
            var localAddress = DeriveAddress(localKey.PublicKey);
            var remoteAddress = DeriveAddress(remoteKey.PublicKey);

            using var server = new PNetMeshServer(settings, logger: logger);
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
                    SeqNumber = 9001,
                    HopCount = 1,
                    Route = ImmutableArray.Create(remoteAddress, localAddress),
                    Payload = new byte[] { 0xde, 0xad, 0xbe, 0xef },
                    CandidateExchange = new PNetMeshCandidateExchange
                    {
                        Candidates = ImmutableArray.Create(new PNetMeshCandidate
                        {
                            Address = relayEndPoint,
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
                Assert.True(channel.TryReadRoutePath(out _));
            }
            finally
            {
                await server.ShutdownAsync(TestContext.Current.CancellationToken);
            }

            var joined = logger.JoinedEntries;
            Assert.Contains("event=relay_packet_delivered", joined);
            Assert.DoesNotContain("203.0.113.77", joined);
            Assert.DoesNotContain("51820", joined);
            Assert.DoesNotContain(Convert.ToHexString(localAddress), joined);
            Assert.DoesNotContain(Convert.ToHexString(remoteAddress), joined);
            Assert.DoesNotContain("DEADBEEF", joined);
        }

        static byte[] Handshake(uint senderIndex)
        {
            var packet = new byte[PNetMeshHandshake.WireGuardInitiationMessageSize];
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)PNetMeshMessageType.HandshakeInitiation);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), senderIndex);
            return packet;
        }

        static byte[] Transport(uint receiverIndex)
        {
            var packet = new byte[PNetMeshPacketFraming.PacketDataHeaderSize + 16];
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)PNetMeshMessageType.PacketData);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), receiverIndex);
            return packet;
        }

        static IPEndPoint Endpoint(string address, int port)
        {
            return new IPEndPoint(IPAddress.Parse(address), port);
        }

        static byte[] Key(byte value)
        {
            return Enumerable.Repeat(value, 32).ToArray();
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

        static void ApplyAuthenticatedEndpointUpdate(
            PNetMeshServer server,
            PNetMeshSession session,
            PNetMeshControlCommands.Receive command)
        {
            var method = typeof(PNetMeshServer).GetMethod(
                "ApplyAuthenticatedEndpointUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(server, new object[] { session, command });
        }

        static Channel<PNetMeshControlCommands.Command> GetServerControlChannel(PNetMeshServer server)
        {
            var field = typeof(PNetMeshServer).GetField(
                "_controlChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsAssignableFrom<Channel<PNetMeshControlCommands.Command>>(field.GetValue(server));
        }

        sealed class CaptureLogger<T> : ILogger<T>
        {
            readonly List<string> _entries = new List<string>();

            public string JoinedEntries => string.Join("\n", _entries);

            public IDisposable BeginScope<TState>(TState state)
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _entries.Add(formatter(state, exception));
            }
        }

        sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            public void Dispose()
            {
            }
        }
    }
}
