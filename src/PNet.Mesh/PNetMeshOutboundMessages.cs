using System;
using System.Buffers;
using System.Net;

namespace PNet.Mesh
{
    public static class PNetMeshOutboundMessages
    {
        public abstract class Message
        {

        }
        public sealed class Packet : Message
        {
            public IMemoryOwner<byte>? MemoryOwner { get; set; }

            /// <summary>
            /// hash of remote public key
            /// </summary>
            public byte[]? RemoteAddress { get; set; }

            public EndPoint? RemoteEndPoint { get; set; }

            /// <summary>
            /// hash of local public key
            /// </summary>
            public byte[]? LocalAddress { get; set; }

            public EndPoint? LocalEndPoint { get; set; }

            public Memory<byte> MemoryBuffer { get; set; }
        }

        public sealed class Relay : Message
        {
            public required PNetMeshRelayPacket Packet { get; init; }
        }
    }
}
