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
        [InlineData(0x00, false)]
        [InlineData(0x80, true)]
        public void pnet_frame_detects_one_byte_marker_header(byte marker, bool hasExtendedHeaderSignal)
        {
            var raw = new byte[] { marker, 0x01, 0x02 };

            Assert.True(PNetMeshPayloadFraming.TryRead(raw, out var frame, out var error));

            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(PNetMeshPayloadFrameKind.PNet, frame.Kind);
            Assert.Equal(marker, frame.HeaderByte);
            Assert.Equal(marker & 0x0f, frame.PNetMarkerBits);
            Assert.Equal(0, frame.PaddingLength);
            Assert.Equal(hasExtendedHeaderSignal, frame.HasExtendedHeaderSignal);
            Assert.Equal(1, frame.HeaderLength);
            Assert.Equal(raw.Length, frame.TotalLength);
            Assert.Equal(new byte[] { 0x01, 0x02 }, frame.Payload.ToArray());
        }

        [Fact]
        public void pnet_frame_decodes_padding_count_and_slices_default_payload()
        {
            var raw = new byte[] { 0x83, 0x01, 0x02, 0x00, 0x00, 0x00 };

            Assert.True(PNetMeshPayloadFraming.TryRead(raw, out var frame, out var error));

            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(PNetMeshPayloadFrameKind.PNet, frame.Kind);
            Assert.Equal(3, frame.PaddingLength);
            Assert.Equal(new byte[] { 0x01, 0x02 }, frame.Payload.ToArray());
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, frame.Padding.ToArray());
        }

        [Fact]
        public void pnet_empty_default_payload_uses_fifteen_zero_padding_bytes()
        {
            var raw = new byte[16];
            raw[0] = 0x0f;

            Assert.True(PNetMeshPayloadFraming.TryRead(raw, out var frame));

            Assert.Equal(15, frame.PaddingLength);
            Assert.True(frame.Payload.IsEmpty);
            Assert.Equal(15, frame.Padding.Length);
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
            var pnet = PNetMeshPayloadFraming.CreatePNet(pnetPayload, headerByte: 0x80);

            Assert.Equal(new byte[] { 0x80 }.Concat(pnetPayload), pnet);

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

        [Fact]
        public void pnet_parser_rejects_invalid_or_non_zero_padding()
        {
            Assert.False(PNetMeshPayloadFraming.TryRead(new byte[] { 0x03, 0x01, 0x00 }, out _, out var error));
            Assert.Equal(PNetMeshPayloadFrameError.InvalidPNetPadding, error);

            Assert.False(PNetMeshPayloadFraming.TryRead(new byte[] { 0x02, 0x01, 0x00, 0x01 }, out _, out error));
            Assert.Equal(PNetMeshPayloadFrameError.NonZeroPNetPadding, error);
        }

        [Fact]
        public void create_pnet_appends_declared_zero_padding()
        {
            var payload = Encoding.ASCII.GetBytes("protobuf");

            var frame = PNetMeshPayloadFraming.CreatePNet(payload, headerByte: 0x8a);

            Assert.Equal(new byte[] { 0x8a }.Concat(payload).Concat(new byte[10]), frame);
        }

        [Fact]
        public void create_pnet_defaults_to_canonical_sixteen_byte_plaintext_frame()
        {
            var empty = PNetMeshPayloadFraming.CreatePNet(ReadOnlySpan<byte>.Empty);

            Assert.Equal(new byte[] { 0x0f }.Concat(new byte[15]), empty);

            var payload = Encoding.ASCII.GetBytes("protobuf");
            var frame = PNetMeshPayloadFraming.CreatePNet(payload, hasExtendedHeaderSignal: true);

            Assert.Equal(new byte[] { 0x87 }.Concat(payload).Concat(new byte[7]), frame);
        }

        [Fact]
        public void try_write_pnet_copies_payload_and_clears_padding()
        {
            var payload = Encoding.ASCII.GetBytes("protobuf");
            var buffer = Enumerable.Repeat((byte)0xcc, 32).ToArray();

            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                payload,
                buffer,
                out var bytesWritten,
                hasExtendedHeaderSignal: true));

            Assert.Equal(16, bytesWritten);
            Assert.Equal(new byte[] { 0x87 }.Concat(payload).Concat(new byte[7]), buffer.AsSpan(0, bytesWritten).ToArray());
            Assert.All(buffer.Skip(bytesWritten), b => Assert.Equal(0xcc, b));
        }

        [Fact]
        public void try_write_pnet_reports_required_size_without_writing_short_destination()
        {
            var buffer = Enumerable.Repeat((byte)0xcc, 15).ToArray();

            Assert.False(PNetMeshPayloadFraming.TryWritePNet(
                ReadOnlySpan<byte>.Empty,
                buffer,
                out var bytesWritten));

            Assert.Equal(16, bytesWritten);
            Assert.All(buffer, b => Assert.Equal(0xcc, b));
        }

        [Fact]
        public void try_write_pnet_in_place_preserves_payload_and_clears_padding()
        {
            var payload = Encoding.ASCII.GetBytes("protobuf");
            var buffer = Enumerable.Repeat((byte)0xcc, 32).ToArray();
            payload.CopyTo(buffer.AsSpan(1));

            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                buffer,
                payload.Length,
                out var bytesWritten,
                hasExtendedHeaderSignal: true));

            Assert.Equal(16, bytesWritten);
            Assert.Equal(new byte[] { 0x87 }.Concat(payload).Concat(new byte[7]), buffer.AsSpan(0, bytesWritten).ToArray());
            Assert.All(buffer.Skip(bytesWritten), b => Assert.Equal(0xcc, b));
        }
    }
}
