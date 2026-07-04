using PNet.Mesh;
using PNet.Mesh.Tun;
using System;
using System.Buffers.Binary;
using System.Net;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh.Tun
{
    public sealed class PNetMeshIcmpEchoTests
    {
        [Fact]
        public void creates_ipv4_echo_reply_with_swapped_addresses_and_valid_checksums()
        {
            var request = CreateIpv4EchoRequest();
            var destination = new byte[request.Length + 16];

            var created = PNetMeshIcmpEcho.TryCreateIPv4EchoReply(request, destination, out var bytesWritten);

            Assert.True(created);
            Assert.Equal(request.Length, bytesWritten);
            var reply = destination.AsSpan(0, bytesWritten);
            Assert.Equal(0x45, reply[0]);
            Assert.Equal(1, reply[9]);
            Assert.Equal(IPAddress.Parse("10.80.0.2").GetAddressBytes(), reply[12..16].ToArray());
            Assert.Equal(IPAddress.Parse("10.80.0.1").GetAddressBytes(), reply[16..20].ToArray());
            Assert.Equal(0, reply[20]);
            Assert.Equal(0, reply[21]);
            Assert.Equal(request.AsSpan(24).ToArray(), reply[24..].ToArray());
            Assert.Equal(0, ComputeChecksum(reply[..20]));
            Assert.Equal(0, ComputeChecksum(reply[20..]));
        }

        [Fact]
        public void rejects_non_icmp_ipv4_packet()
        {
            var request = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.80.0.1"),
                IPAddress.Parse("10.80.0.2"),
                new byte[8],
                protocol: 17);

            Assert.False(PNetMeshIcmpEcho.TryCreateIPv4EchoReply(request, new byte[request.Length], out _));
        }

        [Fact]
        public void rejects_fragmented_ipv4_packet()
        {
            var request = CreateIpv4EchoRequest();
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), 0x2000);

            Assert.False(PNetMeshIcmpEcho.TryCreateIPv4EchoReply(request, new byte[request.Length], out _));
        }

        static byte[] CreateIpv4EchoRequest()
        {
            var icmp = new byte[16];
            icmp[0] = 8;
            BinaryPrimitives.WriteUInt16BigEndian(icmp.AsSpan(4, 2), 0x1234);
            BinaryPrimitives.WriteUInt16BigEndian(icmp.AsSpan(6, 2), 1);
            for (var i = 8; i < icmp.Length; i++)
                icmp[i] = (byte)i;

            BinaryPrimitives.WriteUInt16BigEndian(icmp.AsSpan(2, 2), ComputeChecksum(icmp));
            return PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.80.0.1"),
                IPAddress.Parse("10.80.0.2"),
                icmp,
                protocol: 1);
        }

        static ushort ComputeChecksum(ReadOnlySpan<byte> payload)
        {
            uint sum = 0;
            var offset = 0;
            while (offset + 1 < payload.Length)
            {
                sum += BinaryPrimitives.ReadUInt16BigEndian(payload[offset..(offset + 2)]);
                offset += 2;
            }

            if (offset < payload.Length)
                sum += (uint)(payload[offset] << 8);

            while ((sum >> 16) != 0)
                sum = (sum & 0xffff) + (sum >> 16);

            return (ushort)~sum;
        }
    }
}
