using System;

namespace PNet.Mesh
{
    [Obsolete("Use PNetMeshTransport2 directly.")]
    public sealed class PNetMeshSecureFrameSession
    {
        readonly PNetMeshTransport2 _transport;

        public PNetMeshSecureFrameSession(PNetMeshTransport2 transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public static int CalculatePacketSize(int plaintextFrameLength)
        {
            return PNetMeshTransport2.CalculatePacketSize(plaintextFrameLength);
        }

        public bool TryWriteFrame(
            ReadOnlySpan<byte> plaintextFrame,
            Span<byte> packet,
            out int bytesWritten,
            out ulong counter)
        {
            return _transport.TryWriteFrame(plaintextFrame, packet, out bytesWritten, out counter);
        }

        public bool TryReadFrame(
            ReadOnlySpan<byte> packet,
            Span<byte> plaintextFrame,
            out PNetMeshTransportPlaintext plaintext)
        {
            return _transport.TryReadFrame(packet, plaintextFrame, out plaintext);
        }
    }
}
