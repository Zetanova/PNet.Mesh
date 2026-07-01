using PNet.Mesh;
using System;
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
