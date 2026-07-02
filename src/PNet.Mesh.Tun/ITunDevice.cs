using System;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.Tun
{
    public interface ITunDevice : IAsyncDisposable
    {
        string Name { get; }

        int Mtu { get; }

        ValueTask<int> ReadPacketAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

        ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);
    }
}
