using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PNet.Mesh.Tun.Linux
{
    static class LinuxTunInterop
    {
        public const string DevicePath = "/dev/net/tun";

        const ulong TunSetIff = 0x400454ca;
        const ushort IffTun = 0x0001;
        const ushort IffNoPi = 0x1000;
        const ushort IffTunExcl = 0x8000;
        const int IfNameSize = 16;
        const int IfReqSize = 40;

        public static string ConfigureTun(SafeFileHandle handle, string interfaceName, bool exclusive)
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Linux TUN devices are only supported on Linux.");
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));

            var request = new byte[IfReqSize];
            if (!string.IsNullOrWhiteSpace(interfaceName))
            {
                var nameBytes = Encoding.ASCII.GetBytes(interfaceName);
                if (nameBytes.Length >= IfNameSize)
                    throw new ArgumentOutOfRangeException(nameof(interfaceName), "Linux interface names must be shorter than 16 bytes.");

                nameBytes.CopyTo(request, 0);
            }

            var flags = (ushort)(IffTun | IffNoPi);
            if (exclusive)
                flags |= IffTunExcl;

            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(IfNameSize, 2), flags);

            if (Ioctl(handle, TunSetIff, request) < 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"TUNSETIFF failed for {DevicePath}: {new Win32Exception(error).Message}", error);
            }

            var actualNameLength = Array.IndexOf(request, (byte)0, 0, IfNameSize);
            if (actualNameLength < 0)
                actualNameLength = IfNameSize;

            return Encoding.ASCII.GetString(request, 0, actualNameLength);
        }

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        static extern int Ioctl(SafeFileHandle fd, ulong request, byte[] argp);
    }
}
