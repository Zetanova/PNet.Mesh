using System;

namespace PNet.Mesh
{
    public enum PNetMeshPayloadFrameKind
    {
        PNet = 1,
        IPv4 = 4,
        IPv6 = 6
    }

    public enum PNetMeshPayloadFrameError
    {
        None = 0,
        Empty,
        InvalidIpPacket,
        ReservedMarker
    }

    public readonly ref struct PNetMeshPayloadFrame
    {
        readonly ReadOnlySpan<byte> _frame;

        internal PNetMeshPayloadFrame(
            ReadOnlySpan<byte> frame,
            PNetMeshPayloadFrameKind kind,
            byte headerByte,
            int headerLength,
            int totalLength,
            PNetMeshIpPacket ipPacket)
        {
            _frame = frame;
            Kind = kind;
            HeaderByte = headerByte;
            HeaderLength = headerLength;
            TotalLength = totalLength;
            IpPacket = ipPacket;
        }

        public PNetMeshPayloadFrameKind Kind { get; }

        public byte HeaderByte { get; }

        public byte PNetMarkerBits => (byte)(HeaderByte & 0x0f);

        public bool HasExtendedHeaderSignal => (HeaderByte & 0x80) != 0;

        public int HeaderLength { get; }

        public int PayloadOffset => HeaderLength;

        public int TotalLength { get; }

        public int PayloadLength => TotalLength - HeaderLength;

        public PNetMeshIpPacket IpPacket { get; }

        public ReadOnlySpan<byte> Frame => _frame.Slice(0, TotalLength);

        public ReadOnlySpan<byte> Payload => _frame.Slice(HeaderLength, PayloadLength);
    }

    public static class PNetMeshPayloadFraming
    {
        public const byte PNetMarkerMask = 0x70;

        public static bool TryRead(ReadOnlySpan<byte> payload, out PNetMeshPayloadFrame frame)
        {
            return TryRead(payload, out frame, out _);
        }

        public static bool TryRead(
            ReadOnlySpan<byte> payload,
            out PNetMeshPayloadFrame frame,
            out PNetMeshPayloadFrameError error)
        {
            frame = default;
            error = PNetMeshPayloadFrameError.None;

            if (payload.IsEmpty)
            {
                error = PNetMeshPayloadFrameError.Empty;
                return false;
            }

            var headerByte = payload[0];
            var version = headerByte >> 4;
            if (version == 4 || version == 6)
            {
                if (!PNetMeshIpPacket.TryRead(payload, out var ipPacket)
                    || (version == 4 && ipPacket.Version != PNetMeshIpPacketVersion.IPv4)
                    || (version == 6 && ipPacket.Version != PNetMeshIpPacketVersion.IPv6))
                {
                    error = PNetMeshPayloadFrameError.InvalidIpPacket;
                    return false;
                }

                frame = new PNetMeshPayloadFrame(
                    payload,
                    version == 4 ? PNetMeshPayloadFrameKind.IPv4 : PNetMeshPayloadFrameKind.IPv6,
                    headerByte,
                    ipPacket.HeaderLength,
                    ipPacket.TotalLength,
                    ipPacket);
                return true;
            }

            if (IsPNetHeader(headerByte))
            {
                frame = new PNetMeshPayloadFrame(
                    payload,
                    PNetMeshPayloadFrameKind.PNet,
                    headerByte,
                    1,
                    payload.Length,
                    default);
                return true;
            }

            error = PNetMeshPayloadFrameError.ReservedMarker;
            return false;
        }

        public static bool IsPNetHeader(byte headerByte)
        {
            return (headerByte & PNetMarkerMask) == 0;
        }

        public static byte[] CreatePNet(ReadOnlySpan<byte> payload, byte headerByte = 0)
        {
            if (!IsPNetHeader(headerByte))
                throw new ArgumentOutOfRangeException(nameof(headerByte));

            var frame = new byte[payload.Length + 1];
            frame[0] = headerByte;
            payload.CopyTo(frame.AsSpan(1));
            return frame;
        }

        public static byte[] CreateIPv4(ReadOnlySpan<byte> packet)
        {
            return CreateIp(packet, PNetMeshIpPacketVersion.IPv4, nameof(packet));
        }

        public static byte[] CreateIPv6(ReadOnlySpan<byte> packet)
        {
            return CreateIp(packet, PNetMeshIpPacketVersion.IPv6, nameof(packet));
        }

        static byte[] CreateIp(ReadOnlySpan<byte> packet, PNetMeshIpPacketVersion version, string paramName)
        {
            if (!PNetMeshIpPacket.TryRead(packet, out var ipPacket) || ipPacket.Version != version)
                throw new ArgumentException("invalid IP packet", paramName);

            return packet.Slice(0, ipPacket.TotalLength).ToArray();
        }
    }
}
