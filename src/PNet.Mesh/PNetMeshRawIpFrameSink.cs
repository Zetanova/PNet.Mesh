using System;

namespace PNet.Mesh
{
    internal enum PNetMeshRawIpFrameSinkResult
    {
        Delivered,
        Consumed,
        FallbackToChannel
    }

    internal interface IPNetMeshRawIpFrameSink
    {
        PNetMeshRawIpFrameSinkResult TryReceiveIPv4(ReadOnlySpan<byte> packet);

        PNetMeshRawIpFrameSinkResult TryReceiveIPv6(ReadOnlySpan<byte> packet);
    }
}
