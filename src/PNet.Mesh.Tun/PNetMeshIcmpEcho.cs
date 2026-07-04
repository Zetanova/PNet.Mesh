using PNet.Mesh;
using System;
using System.Buffers.Binary;

namespace PNet.Mesh.Tun
{
    internal static class PNetMeshIcmpEcho
    {
        const byte IcmpProtocol = 1;
        const byte IcmpEchoRequest = 8;
        const byte IcmpEchoReply = 0;

        public static bool TryCreateIPv4EchoReply(ReadOnlySpan<byte> request, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            if (!PNetMeshIpPacket.TryReadHeader(request, out var header)
                || header.Version != PNetMeshIpPacketVersion.IPv4
                || header.HeaderLength != 20
                || header.TotalLength > destination.Length
                || header.PayloadLength < 8)
            {
                return false;
            }

            var packet = request[..header.TotalLength];
            var fragment = BinaryPrimitives.ReadUInt16BigEndian(packet[6..8]);
            if ((fragment & 0x3fff) != 0 || packet[9] != IcmpProtocol)
                return false;

            var icmp = packet[header.PayloadOffset..header.TotalLength];
            if (icmp[0] != IcmpEchoRequest || icmp[1] != 0)
                return false;

            packet.CopyTo(destination);
            var reply = destination[..header.TotalLength];

            packet[12..16].CopyTo(reply[16..20]);
            packet[16..20].CopyTo(reply[12..16]);
            reply[8] = 64;
            reply[10] = 0;
            reply[11] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(reply[10..12], ComputeChecksum(reply[..header.HeaderLength]));

            var replyIcmp = reply[header.PayloadOffset..header.TotalLength];
            replyIcmp[0] = IcmpEchoReply;
            replyIcmp[2] = 0;
            replyIcmp[3] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(replyIcmp[2..4], ComputeChecksum(replyIcmp));

            bytesWritten = header.TotalLength;
            return true;
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
