using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.Tun.Linux
{
    public sealed class LinuxTunDevice : ITunDevice
    {
        readonly FileStream _stream;

        LinuxTunDevice(string name, int mtu, FileStream stream)
        {
            Name = name;
            Mtu = mtu;
            _stream = stream;
        }

        public string Name { get; }

        public int Mtu { get; }

        public static ValueTask<LinuxTunDevice> CreateAsync(
            string interfaceName,
            int mtu = 1280,
            bool exclusive = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mtu <= 0)
                throw new ArgumentOutOfRangeException(nameof(mtu));

            SafeFileHandle? handle = null;
            try
            {
                handle = File.OpenHandle(
                    LinuxTunInterop.DevicePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite,
                    FileOptions.Asynchronous);

                var actualName = LinuxTunInterop.ConfigureTun(handle, interfaceName, exclusive);
                // TUN is a packet device; buffering writes in FileStream batches packets until
                // the buffer fills, which turns small ping/iperf probes into delayed bursts.
                var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: true);
                handle = null;
                return ValueTask.FromResult(new LinuxTunDevice(actualName, mtu, stream));
            }
            finally
            {
                handle?.Dispose();
            }
        }

        public async ValueTask<int> ReadPacketAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await _stream.ReadAsync(buffer, cancellationToken);
        }

        public async ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
        {
            await _stream.WriteAsync(packet, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
        }
    }
}
