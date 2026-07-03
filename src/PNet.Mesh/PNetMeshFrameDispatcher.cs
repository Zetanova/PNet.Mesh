using System;

namespace PNet.Mesh
{
    internal interface IPNetMeshFrameHandler
    {
        bool TryHandleFrame(ReadOnlySpan<byte> frame, ulong counter, out bool payloadReceived);
    }

    internal sealed class PNetMeshFrameDispatcher
    {
        readonly IPNetMeshFrameHandler? _pnetFrameHandler;
        readonly IPNetMeshFrameHandler? _ipv4FrameHandler;
        readonly IPNetMeshFrameHandler? _ipv6FrameHandler;

        public PNetMeshFrameDispatcher(
            IPNetMeshFrameHandler? pnetFrameHandler = null,
            IPNetMeshFrameHandler? ipv4FrameHandler = null,
            IPNetMeshFrameHandler? ipv6FrameHandler = null)
        {
            _pnetFrameHandler = pnetFrameHandler;
            _ipv4FrameHandler = ipv4FrameHandler;
            _ipv6FrameHandler = ipv6FrameHandler;
        }

        public bool TryDispatch(
            ReadOnlySpan<byte> plaintextFrame,
            ulong counter,
            out bool payloadReceived,
            out PNetMeshPayloadFrameError error)
        {
            payloadReceived = false;
            if (!PNetMeshPayloadFraming.TryClassify(plaintextFrame, out var kind, out error))
                return false;

            var handler = kind switch
            {
                PNetMeshPayloadFrameKind.PNet => _pnetFrameHandler,
                PNetMeshPayloadFrameKind.IPv4 => _ipv4FrameHandler,
                PNetMeshPayloadFrameKind.IPv6 => _ipv6FrameHandler,
                _ => null
            };

            return handler is not null
                   && handler.TryHandleFrame(plaintextFrame, counter, out payloadReceived);
        }
    }
}
