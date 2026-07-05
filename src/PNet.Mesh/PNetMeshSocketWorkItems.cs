using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Sockets;
using System.Threading.Channels;

namespace PNet.Mesh
{
    internal sealed class PNetMeshSocketReceiveWorkItem
    {
        public required PNetMeshProtocol Protocol { get; set; }

        public required IMemoryOwner<byte> MemoryOwner { get; set; }

        public required PNetMeshInboundDispatcher InboundDispatcher { get; set; }

        public required ILogger Logger { get; set; }
    }

    internal sealed class PNetMeshSocketSendWorkItem
    {
        public IMemoryOwner<byte>? MemoryOwner { get; set; }

        public required ChannelWriter<SocketAsyncEventArgs> Writer { get; set; }
    }
}
