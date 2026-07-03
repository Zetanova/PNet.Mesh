using System;

using Protos = PNet.Actor.Mesh.Protos;

namespace PNet.Mesh
{
    internal interface IPNetMeshReliableControlPacketHandler
    {
        bool TryHandlePacket(Protos.Packet packet, ulong counter, out bool payloadReceived);
    }

    internal sealed class PNetMeshReliableControlSession : IPNetMeshFrameHandler
    {
        readonly IPNetMeshReliableControlPacketHandler _packetHandler;

        public PNetMeshReliableControlSession(IPNetMeshReliableControlPacketHandler packetHandler)
        {
            _packetHandler = packetHandler ?? throw new ArgumentNullException(nameof(packetHandler));
        }

        public bool TryHandleFrame(ReadOnlySpan<byte> frame, ulong counter, out bool payloadReceived)
        {
            return TryHandleFrame(frame, counter, out payloadReceived, out _);
        }

        public bool TryHandleFrame(
            ReadOnlySpan<byte> frame,
            ulong counter,
            out bool payloadReceived,
            out PNetMeshPayloadFrameError error)
        {
            payloadReceived = false;
            if (!PNetMeshPayloadFraming.TryClassify(frame, out var kind, out error))
                return false;
            if (kind != PNetMeshPayloadFrameKind.PNet)
                return false;
            if (!PNetMeshPayloadFraming.TryRead(frame, out var pnetFrame, out error)
                || pnetFrame.Kind != PNetMeshPayloadFrameKind.PNet)
                return false;

            var packet = Protos.Packet.Parser.ParseFrom(pnetFrame.Payload);
            return _packetHandler.TryHandlePacket(packet, counter, out payloadReceived);
        }
    }
}
