using System;

namespace PNet.Mesh
{
    public sealed class PNetMeshSecureFrameSession
    {
        readonly PNetMeshTransport2 _transport;

        public PNetMeshSecureFrameSession(PNetMeshTransport2 transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        internal PNetMeshPacketTracker Tracker => _transport.Tracker;

        public static int CalculatePacketSize(int plaintextFrameLength)
        {
            if (plaintextFrameLength < 0)
                throw new ArgumentOutOfRangeException(nameof(plaintextFrameLength));

            var padding = (16 - (plaintextFrameLength % 16)) % 16;
            return checked(PNetMeshPacketFraming.PacketDataHeaderSize + plaintextFrameLength + padding + 16);
        }

        public bool TryWriteFrame(
            ReadOnlySpan<byte> plaintextFrame,
            Span<byte> packet,
            out int bytesWritten,
            out ulong counter)
        {
            bytesWritten = CalculatePacketSize(plaintextFrame.Length);
            counter = 0;
            if (packet.Length < bytesWritten)
                return false;

            _transport.WriteMessage(plaintextFrame, packet, out bytesWritten, out counter);
            return true;
        }

        public bool TryReadFrame(
            ReadOnlySpan<byte> packet,
            Span<byte> plaintextFrame,
            out PNetMeshTransportPlaintext plaintext)
        {
            return _transport.TryReadPlaintext(packet, plaintextFrame, out plaintext);
        }
    }
}
