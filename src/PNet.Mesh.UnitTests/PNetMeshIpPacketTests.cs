using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshIpPacketTests
    {
        [Fact]
        public void ipv4_packet_create_and_read_uses_total_length_before_wireguard_padding()
        {
            var payload = Encoding.ASCII.GetBytes("hello");
            var packetBytes = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                payload,
                protocol: 17);
            var padded = packetBytes.Concat(new byte[11]).ToArray();

            Assert.True(PNetMeshIpPacket.TryRead(padded, out var packet));

            Assert.Equal(PNetMeshIpPacketVersion.IPv4, packet.Version);
            Assert.Equal(20, packet.HeaderLength);
            Assert.Equal(packetBytes.Length, packet.TotalLength);
            Assert.Equal(payload.Length, packet.PayloadLength);
            Assert.Equal(IPAddress.Parse("10.0.0.1"), packet.SourceAddress);
            Assert.Equal(IPAddress.Parse("10.0.0.2"), packet.DestinationAddress);
            Assert.Equal(new byte[] { 10, 0, 0, 1 }, packetBytes.AsSpan(12, 4).ToArray());
            Assert.Equal(new byte[] { 10, 0, 0, 2 }, packetBytes.AsSpan(16, 4).ToArray());
        }

        [Fact]
        public void ipv4_header_read_exposes_payload_bounds_without_materializing_addresses()
        {
            var payload = Encoding.ASCII.GetBytes("hello");
            var packetBytes = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                payload,
                protocol: 17);
            var padded = packetBytes.Concat(new byte[11]).ToArray();

            Assert.True(PNetMeshIpPacket.TryReadHeader(padded, out var header));

            Assert.Equal(PNetMeshIpPacketVersion.IPv4, header.Version);
            Assert.Equal(20, header.HeaderLength);
            Assert.Equal(packetBytes.Length, header.TotalLength);
            Assert.Equal(20, header.PayloadOffset);
            Assert.Equal(payload.Length, header.PayloadLength);
        }

        [Fact]
        public void ipv6_packet_create_and_read_uses_payload_length_before_wireguard_padding()
        {
            var payload = Encoding.ASCII.GetBytes("hello-ipv6");
            var packetBytes = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                payload,
                nextHeader: 59);
            var padded = packetBytes.Concat(new byte[6]).ToArray();

            Assert.True(PNetMeshIpPacket.TryRead(padded, out var packet));

            Assert.Equal(PNetMeshIpPacketVersion.IPv6, packet.Version);
            Assert.Equal(40, packet.HeaderLength);
            Assert.Equal(packetBytes.Length, packet.TotalLength);
            Assert.Equal(payload.Length, packet.PayloadLength);
            Assert.Equal(IPAddress.Parse("2001:db8::1"), packet.SourceAddress);
            Assert.Equal(IPAddress.Parse("2001:db8::2"), packet.DestinationAddress);
            Assert.Equal(IPAddress.Parse("2001:db8::1").GetAddressBytes(), packetBytes.AsSpan(8, 16).ToArray());
            Assert.Equal(IPAddress.Parse("2001:db8::2").GetAddressBytes(), packetBytes.AsSpan(24, 16).ToArray());
        }

        [Fact]
        public void ipv6_header_read_exposes_payload_bounds_without_materializing_addresses()
        {
            var payload = Encoding.ASCII.GetBytes("hello-ipv6");
            var packetBytes = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                payload,
                nextHeader: 59);
            var padded = packetBytes.Concat(new byte[6]).ToArray();

            Assert.True(PNetMeshIpPacket.TryReadHeader(padded, out var header));

            Assert.Equal(PNetMeshIpPacketVersion.IPv6, header.Version);
            Assert.Equal(40, header.HeaderLength);
            Assert.Equal(packetBytes.Length, header.TotalLength);
            Assert.Equal(40, header.PayloadOffset);
            Assert.Equal(payload.Length, header.PayloadLength);
        }

        [Fact]
        public void ipv4_header_read_rejects_malformed_ihl_and_total_length()
        {
            var packetBytes = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                Encoding.ASCII.GetBytes("hello"),
                protocol: 17);

            var invalidIhl = packetBytes.ToArray();
            invalidIhl[0] = 0x44;
            Assert.False(PNetMeshIpPacket.TryReadHeader(invalidIhl, out _));

            var totalLengthBeforeHeader = packetBytes.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(totalLengthBeforeHeader.AsSpan(2, 2), 19);
            Assert.False(PNetMeshIpPacket.TryReadHeader(totalLengthBeforeHeader, out _));

            var totalLengthPastBuffer = packetBytes.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(totalLengthPastBuffer.AsSpan(2, 2), (ushort)(packetBytes.Length + 1));
            Assert.False(PNetMeshIpPacket.TryReadHeader(totalLengthPastBuffer, out _));
        }

        [Fact]
        public void ipv6_header_read_rejects_malformed_payload_length()
        {
            var packetBytes = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                ReadOnlySpan<byte>.Empty,
                nextHeader: 59);
            BinaryPrimitives.WriteUInt16BigEndian(packetBytes.AsSpan(4, 2), 1);

            Assert.False(PNetMeshIpPacket.TryReadHeader(packetBytes, out _));
        }

        [Fact]
        public void header_read_rejects_short_packets()
        {
            var ipv4Short = new byte[19];
            ipv4Short[0] = 0x45;
            Assert.False(PNetMeshIpPacket.TryReadHeader(ipv4Short, out _));

            var ipv6Short = new byte[39];
            ipv6Short[0] = 0x60;
            Assert.False(PNetMeshIpPacket.TryReadHeader(ipv6Short, out _));
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0x70, 0, 0, 0 })]
        [InlineData(new byte[] { 0x45, 0, 0, 19, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
        [InlineData(new byte[] { 0x60, 0, 0, 0, 0, 1 })]
        public void read_rejects_unsupported_families_and_invalid_lengths(byte[] plaintext)
        {
            Assert.False(PNetMeshIpPacket.TryRead(plaintext, out _));
        }

        [Fact]
        public void create_rejects_wrong_address_family()
        {
            Assert.Throws<ArgumentException>(() =>
                PNetMeshIpPacket.CreateIPv4(IPAddress.IPv6Loopback, IPAddress.IPv6Loopback, ReadOnlySpan<byte>.Empty));
            Assert.Throws<ArgumentException>(() =>
                PNetMeshIpPacket.CreateIPv6(IPAddress.Loopback, IPAddress.Loopback, ReadOnlySpan<byte>.Empty));
        }
    }
}
