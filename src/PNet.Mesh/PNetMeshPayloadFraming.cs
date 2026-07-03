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
            PNetMeshIpPacketHeader ipHeader)
        {
            _frame = frame;
            Kind = kind;
            HeaderByte = headerByte;
            HeaderLength = headerLength;
            TotalLength = totalLength;
            PaddingLength = paddingLength;
            IpHeader = ipHeader;
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

        public PNetMeshIpPacketHeader IpHeader { get; }

        public PNetMeshIpPacket IpPacket => IpHeader.ToPacket();

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
            if (!TryClassifyHeader(headerByte, out var kind, out error))
                return false;

            if (kind == PNetMeshPayloadFrameKind.IPv4 || kind == PNetMeshPayloadFrameKind.IPv6)
            {
                if (!PNetMeshIpPacket.TryReadHeader(payload, out var ipHeader)
                    || (kind == PNetMeshPayloadFrameKind.IPv4 && ipHeader.Version != PNetMeshIpPacketVersion.IPv4)
                    || (kind == PNetMeshPayloadFrameKind.IPv6 && ipHeader.Version != PNetMeshIpPacketVersion.IPv6))
                {
                    error = PNetMeshPayloadFrameError.InvalidIpPacket;
                    return false;
                }

                frame = new PNetMeshPayloadFrame(
                    payload,
                    kind,
                    headerByte,
                    ipHeader.HeaderLength,
                    ipHeader.TotalLength,
                    0,
                    ipHeader);
                return true;
            }

            if (kind == PNetMeshPayloadFrameKind.PNet)
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
                    kind,
                    headerByte,
                    1,
                    payload.Length,
                    paddingLength,
                    default);
                return true;
            }

            return false;
        }

        public static bool TryClassify(ReadOnlySpan<byte> payload, out PNetMeshPayloadFrameKind kind)
        {
            return TryClassify(payload, out kind, out _);
        }

        public static bool TryClassify(
            ReadOnlySpan<byte> payload,
            out PNetMeshPayloadFrameKind kind,
            out PNetMeshPayloadFrameError error)
        {
            kind = default;
            error = PNetMeshPayloadFrameError.None;
            if (payload.IsEmpty)
            {
                error = PNetMeshPayloadFrameError.Empty;
                return false;
            }

            return TryClassifyHeader(payload[0], out kind, out error);
        }

        public static bool TryClassifyHeader(byte headerByte, out PNetMeshPayloadFrameKind kind)
        {
            return TryClassifyHeader(headerByte, out kind, out _);
        }

        public static bool TryClassifyHeader(
            byte headerByte,
            out PNetMeshPayloadFrameKind kind,
            out PNetMeshPayloadFrameError error)
        {
            kind = default;
            error = PNetMeshPayloadFrameError.None;

            var version = headerByte >> 4;
            if (version == 4)
            {
                kind = PNetMeshPayloadFrameKind.IPv4;
                return true;
            }

            if (version == 6)
            {
                kind = PNetMeshPayloadFrameKind.IPv6;
                return true;
            }

            if (IsPNetHeader(headerByte))
            {
                kind = PNetMeshPayloadFrameKind.PNet;
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
            return CreatePNet(payload, CreatePNetHeaderByte(payload.Length, hasExtendedHeaderSignal));
        }

        public static byte[] CreatePNet(ReadOnlySpan<byte> payload, byte headerByte)
        {
            var frame = new byte[CalculatePNetFrameSize(payload.Length, headerByte)];
            TryWritePNet(payload, headerByte, frame, out _);
            return frame;
        }

        public static bool TryWritePNet(
            ReadOnlySpan<byte> payload,
            Span<byte> destination,
            out int bytesWritten,
            bool hasExtendedHeaderSignal = false)
        {
            return TryWritePNet(
                payload,
                CreatePNetHeaderByte(payload.Length, hasExtendedHeaderSignal),
                destination,
                out bytesWritten);
        }

        public static bool TryWritePNet(
            ReadOnlySpan<byte> payload,
            byte headerByte,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = CalculatePNetFrameSize(payload.Length, headerByte);
            if (destination.Length < bytesWritten)
                return false;

            payload.CopyTo(destination.Slice(1, payload.Length));
            destination[0] = headerByte;
            ClearPNetPadding(destination, payload.Length, headerByte);
            return true;
        }

        public static bool TryWritePNet(
            Span<byte> destination,
            int payloadLength,
            out int bytesWritten,
            bool hasExtendedHeaderSignal = false)
        {
            return TryWritePNet(
                destination,
                payloadLength,
                CreatePNetHeaderByte(payloadLength, hasExtendedHeaderSignal),
                out bytesWritten);
        }

        public static bool TryWritePNet(
            Span<byte> destination,
            int payloadLength,
            byte headerByte,
            out int bytesWritten)
        {
            bytesWritten = CalculatePNetFrameSize(payloadLength, headerByte);
            if (destination.Length < bytesWritten)
                return false;

            destination[0] = headerByte;
            ClearPNetPadding(destination, payloadLength, headerByte);
            return true;
        }

        internal static int CalculatePNetFrameSize(int payloadLength)
        {
            return payloadLength + 1 + CalculatePNetPaddingLength(payloadLength);
        }

        static int CalculatePNetFrameSize(int payloadLength, byte headerByte)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            if (!IsPNetHeader(headerByte))
                throw new ArgumentOutOfRangeException(nameof(headerByte));

            return checked(payloadLength + 1 + (headerByte & 0x0f));
        }

        internal static int CalculatePNetPaddingLength(int payloadLength)
        {
            return (16 - ((payloadLength + 1) & 0x0f)) & 0x0f;
        }

        static byte CreatePNetHeaderByte(int payloadLength, bool hasExtendedHeaderSignal)
        {
            var paddingLength = CalculatePNetPaddingLength(payloadLength);
            return (byte)(paddingLength | (hasExtendedHeaderSignal ? 0x80 : 0));
        }

        static void ClearPNetPadding(Span<byte> destination, int payloadLength, byte headerByte)
        {
            var paddingLength = headerByte & 0x0f;
            if (paddingLength == 0)
                return;

            destination.Slice(1 + payloadLength, paddingLength).Clear();
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
