using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    internal static class PNetMeshControlCommands
    {
        public abstract class Command
        {
        }

        public sealed class Receive : Command
        {
            public IMemoryOwner<byte>? MemoryOwner { get; set; }

            public EndPoint? RemoteEndPoint { get; set; }

            public EndPoint? RelayCandidateEndPoint { get; set; }

            public EndPoint? LocalEndPoint { get; set; }

            public ReadOnlyMemory<byte> MemoryBuffer { get; set; }

#if PNET_MESH_PACKET_TRACE
            public long PacketTraceReceiveTimestamp { get; set; }
#endif
        }

        public sealed class OpenChannel : Command
        {
            public required PNetMeshPeer Peer { get; init; }

            public required TaskCompletionSource<PNetMeshChannel> Result { get; init; }
        }

        //public sealed class PulseChannel : Command
        //{
        //    public PNetMeshPeer Peer { get; init; }
        //}

        public sealed class RelayPacket : Command
        {
            public required PNetMeshRelayPacket Packet { get; init; }

            public IMemoryOwner<byte>? MemoryOwner { get; set; }

            public TaskCompletionSource? Result { get; init; }
        }
    }
}
