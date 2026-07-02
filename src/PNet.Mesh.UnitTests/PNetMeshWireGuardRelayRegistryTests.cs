using Noise;
using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshWireGuardRelayRegistryTests
    {
        [Fact]
        public void register_renew_release_and_expire_shared_port_lease()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var registry = CreateRegistry((_, _) => false);
            var endpoint = Endpoint(10001);
            var key = Key(1);
            var paddedKey = new byte[key.Length + 2];
            key.CopyTo(paddedKey, 1);

            var lease = registry.RegisterOrRenew(
                key,
                Address(10),
                now.AddMinutes(1),
                now,
                new[] { endpoint },
                ImmutableArray.Create<byte[]>(Address(20)));

            Assert.Equal(1, registry.Count);
            Assert.Equal(key, lease.PublicKey.ToArray());
            Assert.Equal(Address(10), lease.ReturnAddress.ToArray());
            Assert.Equal(Address(20), Assert.Single(lease.ReturnRoute));
            Assert.Equal(endpoint, Assert.Single(lease.AllowedRemoteEndpoints));

            var renewed = registry.RegisterOrRenew(paddedKey.AsSpan(1, key.Length), Address(11), now.AddMinutes(2), now);

            Assert.Equal(1, registry.Count);
            Assert.Equal(Address(11), renewed.ReturnAddress.ToArray());
            Assert.Empty(renewed.AllowedRemoteEndpoints);
            Assert.True(registry.Release(paddedKey.AsSpan(1, key.Length)));
            Assert.False(registry.Release(paddedKey.AsSpan(1, key.Length)));

            registry.RegisterOrRenew(key, Address(12), now.AddMilliseconds(1), now);

            Assert.False(registry.TryRoute(Handshake(senderIndex: 7), endpoint, now.AddSeconds(1), out _, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void handshake_demux_routes_exact_mac1_match_and_rejects_zero_or_ambiguous_matches()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var handshake = Handshake(senderIndex: 10);
            var registry = CreateRegistry((_, key) => key[0] == 1);
            var first = registry.RegisterOrRenew(Key(1, 1), Address(1), now.AddMinutes(1), now);
            registry.RegisterOrRenew(Key(2), Address(2), now.AddMinutes(1), now);

            Assert.True(registry.TryRoute(handshake, endpoint, now, out var routed, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.Routed, result);
            Assert.Same(first, routed);

            var zeroMatch = CreateRegistry((_, _) => false);
            zeroMatch.RegisterOrRenew(Key(3), Address(3), now.AddMinutes(1), now);

            Assert.False(zeroMatch.TryRoute(handshake, endpoint, now, out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);

            registry.RegisterOrRenew(Key(1, 2), Address(4), now.AddMinutes(1), now);

            Assert.False(registry.TryRoute(handshake, endpoint, now, out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.AmbiguousMac1Match, result);
        }

        [Fact]
        public void real_mac1_match_routes_wireguard_handshake_without_decrypting()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            using var registeredStatic = KeyPair.Generate();
            using var remoteStatic = KeyPair.Generate();
            using var relayStatic = KeyPair.Generate();

            var remoteProtocol = new PNetMeshProtocol(
                remoteStatic.PrivateKey,
                remoteStatic.PublicKey,
                psk);
            var relayProtocol = new PNetMeshProtocol(
                relayStatic.PrivateKey,
                relayStatic.PublicKey,
                psk);
            using var initiator = remoteProtocol.CreateInitiator(42, registeredStatic.PublicKey);
            Span<byte> packet = stackalloc byte[PNetMeshHandshake.WireGuardInitiationMessageSize];
            initiator.WriteInitiationMessage(packet, out var bytesWritten);

            var registry = new PNetMeshWireGuardRelayRegistry(relayProtocol);
            var lease = registry.RegisterOrRenew(registeredStatic.PublicKey, Address(1), now.AddMinutes(1), now);

            Assert.True(registry.TryRoute(packet.Slice(0, bytesWritten), endpoint, now, out var routed, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.Routed, result);
            Assert.Same(lease, routed);
        }

        [Fact]
        public void receiver_index_fast_path_routes_transport_without_mac1_scan()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var options = new PNetMeshWireGuardRelayOptions
            {
                ReceiverIndexTtl = TimeSpan.FromSeconds(5)
            };
            var scans = 0;
            var registry = CreateRegistry((_, key) =>
            {
                scans++;
                return key[0] == 1;
            }, options);
            var lease = registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now);

            Assert.True(registry.TryRoute(Handshake(senderIndex: 99), endpoint, now, out _, out _));
            Assert.Equal(1, scans);

            Assert.True(registry.TryRoute(Transport(receiverIndex: 99), endpoint, now.AddSeconds(1), out var routed, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.Routed, result);
            Assert.Same(lease, routed);
            Assert.Equal(1, scans);

            Assert.False(registry.TryRoute(Transport(receiverIndex: 99), Endpoint(10002), now.AddSeconds(1), out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);

            Assert.False(registry.TryRoute(Transport(receiverIndex: 99), endpoint, now.AddSeconds(6), out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
        }

        [Fact]
        public void endpoint_filters_and_rate_limits_protect_mac1_scans()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var allowed = Endpoint(10001);
            var blocked = Endpoint(10002);
            var registry = CreateRegistry((_, _) => true);
            registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now, new[] { allowed });

            Assert.False(registry.TryRoute(Handshake(senderIndex: 1), blocked, now, out _, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.EndpointRejected, result);

            var limited = CreateRegistry((_, _) => false, new PNetMeshWireGuardRelayOptions
            {
                MaxMac1ScansPerEndpoint = 1,
                Mac1ScanWindow = TimeSpan.FromSeconds(1)
            });
            limited.RegisterOrRenew(Key(2), Address(2), now.AddMinutes(1), now);

            Assert.False(limited.TryRoute(Handshake(senderIndex: 2), allowed, now, out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
            Assert.False(limited.TryRoute(Handshake(senderIndex: 3), allowed, now.AddMilliseconds(100), out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.RateLimited, result);
            Assert.False(limited.TryRoute(Handshake(senderIndex: 4), allowed, now.AddSeconds(2), out _, out result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
        }

        [Fact]
        public void registration_cap_and_malformed_packets_fail_before_mac1_scan()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var scans = 0;
            var registry = CreateRegistry((_, _) =>
            {
                scans++;
                return true;
            }, new PNetMeshWireGuardRelayOptions { MaxLeases = 1 });

            registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now);
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOrRenew(Key(2), Address(2), now.AddMinutes(1), now));

            Assert.False(registry.TryRoute(new byte[] { 1, 0, 0 }, Endpoint(10001), now, out _, out var result));
            Assert.Equal(PNetMeshWireGuardRelayRouteResult.MalformedPacket, result);
            Assert.Equal(0, scans);
        }

        static PNetMeshWireGuardRelayRegistry CreateRegistry(
            PNetMeshWireGuardRelayMac1Matcher matcher,
            PNetMeshWireGuardRelayOptions options = null)
        {
            return new PNetMeshWireGuardRelayRegistry(options ?? new PNetMeshWireGuardRelayOptions(), matcher);
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

        static IPEndPoint Endpoint(int port)
        {
            return new IPEndPoint(IPAddress.Parse("203.0.113.10"), port);
        }

        static byte[] Key(byte first, byte second = 0)
        {
            var key = new byte[32];
            key[0] = first;
            key[1] = second;
            return key;
        }

        static byte[] Address(byte value)
        {
            return Enumerable.Repeat(value, 10).ToArray();
        }
    }
}
