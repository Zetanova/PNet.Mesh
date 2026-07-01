using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PNet.Mesh
{
    public enum PNetMeshIpPacketVersion
    {
        IPv4 = 4,
        IPv6 = 6
    }

    public readonly struct PNetMeshIpPacket
    {
        public PNetMeshIpPacket(
            PNetMeshIpPacketVersion version,
            int headerLength,
            int totalLength,
            IPAddress sourceAddress,
            IPAddress destinationAddress)
        {
            Version = version;
            HeaderLength = headerLength;
            TotalLength = totalLength;
            SourceAddress = sourceAddress;
            DestinationAddress = destinationAddress;
        }

        public PNetMeshIpPacketVersion Version { get; }

        public int HeaderLength { get; }

        public int TotalLength { get; }

        public int PayloadLength => TotalLength - HeaderLength;

        public IPAddress SourceAddress { get; }

        public IPAddress DestinationAddress { get; }

        public static bool TryRead(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacket packet)
        {
            packet = default;
            if (plaintext.IsEmpty)
                return false;

            return (plaintext[0] >> 4) switch
            {
                4 => TryReadIPv4(plaintext, out packet),
                6 => TryReadIPv6(plaintext, out packet),
                _ => false
            };
        }

        public static byte[] CreateIPv4(
            IPAddress sourceAddress,
            IPAddress destinationAddress,
            ReadOnlySpan<byte> payload,
            byte protocol = 0)
        {
            ValidateAddressFamily(sourceAddress, AddressFamily.InterNetwork, nameof(sourceAddress));
            ValidateAddressFamily(destinationAddress, AddressFamily.InterNetwork, nameof(destinationAddress));

            var totalLength = 20 + payload.Length;
            if (totalLength > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var packet = new byte[totalLength];
            packet[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)totalLength);
            packet[8] = 64;
            packet[9] = protocol;
            sourceAddress.GetAddressBytes().CopyTo(packet, 12);
            destinationAddress.GetAddressBytes().CopyTo(packet, 16);
            payload.CopyTo(packet.AsSpan(20));
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10, 2), ComputeIPv4HeaderChecksum(packet.AsSpan(0, 20)));
            return packet;
        }

        public static byte[] CreateIPv6(
            IPAddress sourceAddress,
            IPAddress destinationAddress,
            ReadOnlySpan<byte> payload,
            byte nextHeader = 59)
        {
            ValidateAddressFamily(sourceAddress, AddressFamily.InterNetworkV6, nameof(sourceAddress));
            ValidateAddressFamily(destinationAddress, AddressFamily.InterNetworkV6, nameof(destinationAddress));
            if (payload.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var packet = new byte[40 + payload.Length];
            packet[0] = 0x60;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4, 2), (ushort)payload.Length);
            packet[6] = nextHeader;
            packet[7] = 64;
            sourceAddress.GetAddressBytes().CopyTo(packet, 8);
            destinationAddress.GetAddressBytes().CopyTo(packet, 24);
            payload.CopyTo(packet.AsSpan(40));
            return packet;
        }

        static bool TryReadIPv4(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacket packet)
        {
            packet = default;
            if (plaintext.Length < 20)
                return false;

            var headerLength = (plaintext[0] & 0x0f) * 4;
            if (headerLength < 20 || headerLength > plaintext.Length)
                return false;

            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(2, 2));
            if (totalLength < headerLength || totalLength > plaintext.Length)
                return false;

            packet = new PNetMeshIpPacket(
                PNetMeshIpPacketVersion.IPv4,
                headerLength,
                totalLength,
                new IPAddress(plaintext.Slice(12, 4)),
                new IPAddress(plaintext.Slice(16, 4)));
            return true;
        }

        static bool TryReadIPv6(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacket packet)
        {
            packet = default;
            if (plaintext.Length < 40)
                return false;

            var totalLength = 40 + BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(4, 2));
            if (totalLength > plaintext.Length)
                return false;

            packet = new PNetMeshIpPacket(
                PNetMeshIpPacketVersion.IPv6,
                40,
                totalLength,
                new IPAddress(plaintext.Slice(8, 16)),
                new IPAddress(plaintext.Slice(24, 16)));
            return true;
        }

        static void ValidateAddressFamily(IPAddress address, AddressFamily expected, string paramName)
        {
            if (address == null) throw new ArgumentNullException(paramName);
            if (address.AddressFamily != expected)
                throw new ArgumentException("invalid address family", paramName);
        }

        static ushort ComputeIPv4HeaderChecksum(ReadOnlySpan<byte> header)
        {
            uint sum = 0;
            for (var i = 0; i < header.Length; i += 2)
                sum += BinaryPrimitives.ReadUInt16BigEndian(header.Slice(i, 2));

            while ((sum >> 16) != 0)
                sum = (sum & 0xffff) + (sum >> 16);

            return (ushort)~sum;
        }
    }
}
