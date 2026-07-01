using PNet.Mesh;
using System;
using System.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshPayloadFramingTests
    {
        [Fact]
        public void channel_trywrite_preserves_raw_payload_boundary()
        {
            using var channel = new PNetMeshChannel();
            var payload = new byte[] { 0x70, 0x01, 0x02 };

            Assert.True(channel.TryWrite(payload));
        }

        [Theory]
        [InlineData(0x0a, false)]
        [InlineData(0x8f, true)]
        public void pnet_frame_detects_one_byte_marker_header(byte marker, bool hasExtendedHeaderSignal)
        {
            var raw = new byte[] { marker, 0x01, 0x02 };

            Assert.True(PNetMeshPayloadFraming.TryRead(raw, out var frame, out var error));

            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(PNetMeshPayloadFrameKind.PNet, frame.Kind);
            Assert.Equal(marker, frame.HeaderByte);
            Assert.Equal(marker & 0x0f, frame.PNetMarkerBits);
            Assert.Equal(hasExtendedHeaderSignal, frame.HasExtendedHeaderSignal);
            Assert.Equal(1, frame.HeaderLength);
            Assert.Equal(raw.Length, frame.TotalLength);
            Assert.Equal(new byte[] { 0x01, 0x02 }, frame.Payload.ToArray());
        }

        [Fact]
        public void classifies_ipv4_and_uses_ip_total_length_before_padding()
        {
            var payload = Encoding.ASCII.GetBytes("hello");
            var packet = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                payload,
                protocol: 17);
            var padded = packet.Concat(new byte[7]).ToArray();

            Assert.True(PNetMeshPayloadFraming.TryRead(padded, out var frame));

            Assert.Equal(PNetMeshPayloadFrameKind.IPv4, frame.Kind);
            Assert.Equal(PNetMeshIpPacketVersion.IPv4, frame.IpPacket.Version);
            Assert.Equal(20, frame.HeaderLength);
            Assert.Equal(packet.Length, frame.TotalLength);
            Assert.Equal(payload, frame.Payload.ToArray());
            Assert.Equal(packet, frame.Frame.ToArray());
        }

        [Fact]
        public void classifies_ipv6_and_uses_ip_payload_length_before_padding()
        {
            var payload = Encoding.ASCII.GetBytes("hello-ipv6");
            var packet = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                payload,
                nextHeader: 59);
            var padded = packet.Concat(new byte[5]).ToArray();

            Assert.True(PNetMeshPayloadFraming.TryRead(padded, out var frame));

            Assert.Equal(PNetMeshPayloadFrameKind.IPv6, frame.Kind);
            Assert.Equal(PNetMeshIpPacketVersion.IPv6, frame.IpPacket.Version);
            Assert.Equal(40, frame.HeaderLength);
            Assert.Equal(packet.Length, frame.TotalLength);
            Assert.Equal(payload, frame.Payload.ToArray());
            Assert.Equal(packet, frame.Frame.ToArray());
        }

        [Fact]
        public void create_helpers_copy_pnet_and_valid_ip_frames()
        {
            var pnetPayload = Encoding.ASCII.GetBytes("protobuf");
            var pnet = PNetMeshPayloadFraming.CreatePNet(pnetPayload, headerByte: 0x8a);

            Assert.Equal(new byte[] { 0x8a }.Concat(pnetPayload), pnet);

            var ipv4 = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                Encoding.ASCII.GetBytes("v4"),
                protocol: 17);
            Assert.Equal(ipv4, PNetMeshPayloadFraming.CreateIPv4(ipv4.Concat(new byte[4]).ToArray()));

            var ipv6 = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                Encoding.ASCII.GetBytes("v6"),
                nextHeader: 59);
            Assert.Equal(ipv6, PNetMeshPayloadFraming.CreateIPv6(ipv6.Concat(new byte[4]).ToArray()));
        }

        [Fact]
        public void invalid_payloads_return_explicit_parse_failures()
        {
            Assert.False(PNetMeshPayloadFraming.TryRead(Array.Empty<byte>(), out _, out var error));
            Assert.Equal(PNetMeshPayloadFrameError.Empty, error);

            Assert.False(PNetMeshPayloadFraming.TryRead(new byte[] { 0x70, 0x01 }, out _, out error));
            Assert.Equal(PNetMeshPayloadFrameError.ReservedMarker, error);

            Assert.False(PNetMeshPayloadFraming.TryRead(new byte[] { 0x45, 0x00, 0x00, 0x13 }, out _, out error));
            Assert.Equal(PNetMeshPayloadFrameError.InvalidIpPacket, error);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PNetMeshPayloadFraming.CreatePNet(ReadOnlySpan<byte>.Empty, headerByte: 0x70));
            Assert.Throws<ArgumentException>(() =>
                PNetMeshPayloadFraming.CreateIPv4(new byte[] { 0x60 }));
            Assert.Throws<ArgumentException>(() =>
                PNetMeshPayloadFraming.CreateIPv6(new byte[] { 0x45 }));
        }
    }
}
