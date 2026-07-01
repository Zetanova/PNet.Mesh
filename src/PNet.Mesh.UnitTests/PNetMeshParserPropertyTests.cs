using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshParserPropertyTests
    {
        [Fact]
        public void deterministic_random_byte_corpus_does_not_crash_parser_boundaries()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var registry = CreateRegistry((_, _) => false);
            registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now);
            var random = new Random(4919);

            for (var i = 0; i < 256; i++)
            {
                var payload = new byte[random.Next(0, 256)];
                random.NextBytes(payload);

                _ = PNetMeshPacketFraming.TryReadMessageType(payload, out _);
                _ = PNetMeshPayloadFraming.TryRead(payload, out _, out _);
                _ = registry.TryRoute(payload, endpoint, now.AddMilliseconds(i), out _, out _);
            }
        }

        [Fact]
        public void wireguard_control_packet_size_boundaries_are_rejected_before_demux()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var scans = 0;
            var registry = CreateRegistry((_, _) =>
            {
                scans++;
                return true;
            });
            registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now);

            foreach (var packet in new[]
            {
                Packet(PNetMeshMessageType.HandshakeInitiation, PNetMeshHandshake.WireGuardInitiationMessageSize - 1),
                Packet(PNetMeshMessageType.HandshakeInitiation, PNetMeshHandshake.WireGuardInitiationMessageSize + 1),
                Packet(PNetMeshMessageType.HandshakeResponse, PNetMeshHandshake.ResponseMessageSize - 1),
                Packet(PNetMeshMessageType.HandshakeResponse, PNetMeshHandshake.ResponseMessageSize + 1),
                Packet(PNetMeshMessageType.PacketData, PNetMeshPacketFraming.PacketDataHeaderSize - 1),
                Packet(PNetMeshMessageType.PacketCookieReply, PNetMeshPacketFraming.CookieReplyMessageSize),
                Packet((PNetMeshMessageType)99, PNetMeshPacketFraming.MessageTypeSize)
            })
            {
                Assert.False(registry.TryRoute(packet, endpoint, now, out _, out var result));
                Assert.Equal(PNetMeshWireGuardRelayRouteResult.MalformedPacket, result);
            }

            Assert.Equal(0, scans);
        }

        [Fact]
        public void pnet_marker_and_padding_properties_cover_all_low_nibbles()
        {
            var payload = new byte[] { 0x01, 0x02, 0x03 };

            for (var padding = 0; padding <= 15; padding++)
            {
                var frame = new byte[1 + payload.Length + padding];
                frame[0] = (byte)(0x80 | padding);
                payload.CopyTo(frame.AsSpan(1));

                Assert.True(PNetMeshPayloadFraming.TryRead(frame, out var parsed, out var error));
                Assert.Equal(PNetMeshPayloadFrameError.None, error);
                Assert.Equal(PNetMeshPayloadFrameKind.PNet, parsed.Kind);
                Assert.True(parsed.HasExtendedHeaderSignal);
                Assert.Equal(padding, parsed.PaddingLength);
                Assert.Equal(payload, parsed.Payload.ToArray());
                Assert.All(parsed.Padding.ToArray(), b => Assert.Equal((byte)0, b));
            }

            foreach (var marker in new byte[] { 0x10, 0x20, 0x30, 0x50, 0x70, 0x90, 0xff })
            {
                var frame = new byte[] { marker, 0x01, 0x02, 0x03 };

                Assert.False(PNetMeshPayloadFraming.TryRead(frame, out _, out var error));
                Assert.Equal(PNetMeshPayloadFrameError.ReservedMarker, error);
            }
        }

        [Fact]
        public void pnet_padding_rejects_truncated_and_non_zero_padding_for_all_declared_lengths()
        {
            for (var padding = 1; padding <= 15; padding++)
            {
                var truncated = new byte[padding];
                truncated[0] = (byte)padding;
                Assert.False(PNetMeshPayloadFraming.TryRead(truncated, out _, out var error));
                Assert.Equal(PNetMeshPayloadFrameError.InvalidPNetPadding, error);

                var nonZero = new byte[1 + padding];
                nonZero[0] = (byte)padding;
                nonZero[^1] = 0xff;
                Assert.False(PNetMeshPayloadFraming.TryRead(nonZero, out _, out error));
                Assert.Equal(PNetMeshPayloadFrameError.NonZeroPNetPadding, error);
            }
        }

        [Fact]
        public void relay_mac1_demux_properties_cover_zero_single_and_multiple_matches()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var endpoint = Endpoint(10001);
            var handshake = Handshake(senderIndex: 77);

            for (var matchCount = 0; matchCount <= 2; matchCount++)
            {
                var registry = CreateRegistry((_, key) => key[0] <= matchCount);
                registry.RegisterOrRenew(Key(1), Address(1), now.AddMinutes(1), now);
                registry.RegisterOrRenew(Key(2), Address(2), now.AddMinutes(1), now);

                var routed = registry.TryRoute(handshake, endpoint, now, out var lease, out var result);

                if (matchCount == 0)
                {
                    Assert.False(routed);
                    Assert.Null(lease);
                    Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
                }
                else if (matchCount == 1)
                {
                    Assert.True(routed);
                    Assert.Equal(Address(1), lease.ReturnAddress.ToArray());
                    Assert.Equal(PNetMeshWireGuardRelayRouteResult.Routed, result);
                }
                else
                {
                    Assert.False(routed);
                    Assert.Null(lease);
                    Assert.Equal(PNetMeshWireGuardRelayRouteResult.AmbiguousMac1Match, result);
                }
            }
        }

        [Fact]
        public void receiver_index_fast_path_properties_cover_endpoint_scope_and_expiry()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var options = new PNetMeshWireGuardRelayOptions
            {
                ReceiverIndexTtl = TimeSpan.FromSeconds(2)
            };

            foreach (var receiverIndex in new uint[] { 1, 42, uint.MaxValue })
            {
                var endpoint = Endpoint(10000 + (int)(receiverIndex % 1000));
                var registry = CreateRegistry((_, key) => key[0] == 1, options);
                var lease = registry.RegisterOrRenew(Key(1), Address((byte)(receiverIndex % 251)), now.AddMinutes(1), now);

                Assert.True(registry.TryRoute(Handshake(receiverIndex), endpoint, now, out _, out _));
                Assert.True(registry.TryRoute(Transport(receiverIndex), endpoint, now.AddSeconds(1), out var fastPath, out var result));
                Assert.Equal(PNetMeshWireGuardRelayRouteResult.Routed, result);
                Assert.Same(lease, fastPath);

                Assert.False(registry.TryRoute(Transport(receiverIndex), Endpoint(20000 + (int)(receiverIndex % 1000)), now.AddSeconds(1), out _, out result));
                Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);

                Assert.False(registry.TryRoute(Transport(receiverIndex), endpoint, now.AddSeconds(3), out _, out result));
                Assert.Equal(PNetMeshWireGuardRelayRouteResult.NoMatch, result);
            }
        }

        static PNetMeshWireGuardRelayRegistry CreateRegistry(
            PNetMeshWireGuardRelayMac1Matcher matcher,
            PNetMeshWireGuardRelayOptions options = null)
        {
            return new PNetMeshWireGuardRelayRegistry(options ?? new PNetMeshWireGuardRelayOptions(), matcher);
        }

        static byte[] Packet(PNetMeshMessageType messageType, int length)
        {
            var packet = new byte[length];
            if (length >= PNetMeshPacketFraming.MessageTypeSize)
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)messageType);
            return packet;
        }

        static byte[] Handshake(uint senderIndex)
        {
            var packet = Packet(PNetMeshMessageType.HandshakeInitiation, PNetMeshHandshake.WireGuardInitiationMessageSize);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), senderIndex);
            return packet;
        }

        static byte[] Transport(uint receiverIndex)
        {
            var packet = Packet(PNetMeshMessageType.PacketData, PNetMeshPacketFraming.PacketDataHeaderSize + 16);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), receiverIndex);
            return packet;
        }

        static IPEndPoint Endpoint(int port)
        {
            return new IPEndPoint(IPAddress.Parse("203.0.113.10"), port);
        }

        static byte[] Key(byte first)
        {
            var key = new byte[32];
            key[0] = first;
            return key;
        }

        static byte[] Address(byte value)
        {
            return Enumerable.Repeat(value, 10).ToArray();
        }
    }
}
