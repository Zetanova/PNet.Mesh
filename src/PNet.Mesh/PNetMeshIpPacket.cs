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

    public readonly ref struct PNetMeshIpPacketHeader
    {
        readonly ReadOnlySpan<byte> _packet;

        internal PNetMeshIpPacketHeader(
            ReadOnlySpan<byte> packet,
            PNetMeshIpPacketVersion version,
            int headerLength,
            int totalLength)
        {
            _packet = packet;
            Version = version;
            HeaderLength = headerLength;
            TotalLength = totalLength;
        }

        public PNetMeshIpPacketVersion Version { get; }

        public int HeaderLength { get; }

        public int TotalLength { get; }

        public int PayloadOffset => HeaderLength;

        public int PayloadLength => TotalLength - PayloadOffset;

        public PNetMeshIpPacket ToPacket()
        {
            return Version switch
            {
                PNetMeshIpPacketVersion.IPv4 when _packet.Length >= TotalLength => new PNetMeshIpPacket(
                    Version,
                    HeaderLength,
                    TotalLength,
                    new IPAddress(_packet.Slice(12, 4)),
                    new IPAddress(_packet.Slice(16, 4))),
                PNetMeshIpPacketVersion.IPv6 when _packet.Length >= TotalLength => new PNetMeshIpPacket(
                    Version,
                    HeaderLength,
                    TotalLength,
                    new IPAddress(_packet.Slice(8, 16)),
                    new IPAddress(_packet.Slice(24, 16))),
                _ => default
            };
        }
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
            if (!TryReadHeader(plaintext, out var header))
                return false;

            packet = header.ToPacket();
            return true;
        }

        public static bool TryReadHeader(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacketHeader header)
        {
            header = default;
            if (plaintext.IsEmpty)
                return false;

            return (plaintext[0] >> 4) switch
            {
                4 => TryReadIPv4Header(plaintext, out header),
                6 => TryReadIPv6Header(plaintext, out header),
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
            WriteAddressBytes(sourceAddress, packet.AsSpan(12, 4));
            WriteAddressBytes(destinationAddress, packet.AsSpan(16, 4));
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
            WriteAddressBytes(sourceAddress, packet.AsSpan(8, 16));
            WriteAddressBytes(destinationAddress, packet.AsSpan(24, 16));
            payload.CopyTo(packet.AsSpan(40));
            return packet;
        }

        static bool TryReadIPv4Header(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacketHeader header)
        {
            header = default;
            if (plaintext.Length < 20)
                return false;

            var headerLength = (plaintext[0] & 0x0f) * 4;
            if (headerLength < 20 || headerLength > plaintext.Length)
                return false;

            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(2, 2));
            if (totalLength < headerLength || totalLength > plaintext.Length)
                return false;

            header = new PNetMeshIpPacketHeader(
                plaintext,
                PNetMeshIpPacketVersion.IPv4,
                headerLength,
                totalLength);
            return true;
        }

        static bool TryReadIPv6Header(ReadOnlySpan<byte> plaintext, out PNetMeshIpPacketHeader header)
        {
            header = default;
            if (plaintext.Length < 40)
                return false;

            var totalLength = 40 + BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(4, 2));
            if (totalLength > plaintext.Length)
                return false;

            header = new PNetMeshIpPacketHeader(
                plaintext,
                PNetMeshIpPacketVersion.IPv6,
                40,
                totalLength);
            return true;
        }

        static void ValidateAddressFamily(IPAddress address, AddressFamily expected, string paramName)
        {
            if (address == null) throw new ArgumentNullException(paramName);
            if (address.AddressFamily != expected)
                throw new ArgumentException("invalid address family", paramName);
        }

        static void WriteAddressBytes(IPAddress address, Span<byte> destination)
        {
            if (!address.TryWriteBytes(destination, out var bytesWritten) || bytesWritten != destination.Length)
                throw new InvalidOperationException("Address byte length did not match the destination length.");
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
