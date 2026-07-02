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
        ReservedMarker,
        InvalidPNetPadding,
        NonZeroPNetPadding
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
            int paddingLength,
            PNetMeshIpPacket ipPacket)
        {
            _frame = frame;
            Kind = kind;
            HeaderByte = headerByte;
            HeaderLength = headerLength;
            TotalLength = totalLength;
            PaddingLength = paddingLength;
            IpPacket = ipPacket;
        }

        public PNetMeshPayloadFrameKind Kind { get; }

        public byte HeaderByte { get; }

        public byte PNetMarkerBits => (byte)(HeaderByte & 0x0f);

        public bool HasExtendedHeaderSignal => (HeaderByte & 0x80) != 0;

        public int HeaderLength { get; }

        public int PayloadOffset => HeaderLength;

        public int TotalLength { get; }

        public int PaddingLength { get; }

        public int PayloadLength => TotalLength - HeaderLength - PaddingLength;

        public PNetMeshIpPacket IpPacket { get; }

        public ReadOnlySpan<byte> Frame => _frame.Slice(0, TotalLength);

        public ReadOnlySpan<byte> Payload => _frame.Slice(HeaderLength, PayloadLength);

        public ReadOnlySpan<byte> Padding => PaddingLength == 0
            ? ReadOnlySpan<byte>.Empty
            : _frame.Slice(TotalLength - PaddingLength, PaddingLength);
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
                    0,
                    ipPacket);
                return true;
            }

            if (IsPNetHeader(headerByte))
            {
                var paddingLength = headerByte & 0x0f;
                if (payload.Length - 1 < paddingLength)
                {
                    error = PNetMeshPayloadFrameError.InvalidPNetPadding;
                    return false;
                }

                var padding = payload.Slice(payload.Length - paddingLength, paddingLength);
                foreach (var b in padding)
                {
                    if (b != 0)
                    {
                        error = PNetMeshPayloadFrameError.NonZeroPNetPadding;
                        return false;
                    }
                }

                frame = new PNetMeshPayloadFrame(
                    payload,
                    PNetMeshPayloadFrameKind.PNet,
                    headerByte,
                    1,
                    payload.Length,
                    paddingLength,
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

        public static byte[] CreatePNet(ReadOnlySpan<byte> payload, bool hasExtendedHeaderSignal = false)
        {
            var paddingLength = CalculatePNetPaddingLength(payload.Length);
            var headerByte = (byte)(paddingLength | (hasExtendedHeaderSignal ? 0x80 : 0));
            return CreatePNet(payload, headerByte);
        }

        public static byte[] CreatePNet(ReadOnlySpan<byte> payload, byte headerByte)
        {
            if (!IsPNetHeader(headerByte))
                throw new ArgumentOutOfRangeException(nameof(headerByte));

            var paddingLength = headerByte & 0x0f;
            var frame = new byte[payload.Length + 1 + paddingLength];
            frame[0] = headerByte;
            payload.CopyTo(frame.AsSpan(1));
            return frame;
        }

        internal static int CalculatePNetFrameSize(int payloadLength)
        {
            return payloadLength + 1 + CalculatePNetPaddingLength(payloadLength);
        }

        internal static int CalculatePNetPaddingLength(int payloadLength)
        {
            return (16 - ((payloadLength + 1) & 0x0f)) & 0x0f;
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
