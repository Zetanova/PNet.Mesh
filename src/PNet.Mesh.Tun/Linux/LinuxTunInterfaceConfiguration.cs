using System;
using System.Collections.Generic;

namespace PNet.Mesh.Tun.Linux
{
    public sealed class LinuxTunInterfaceConfiguration
    {
        public required string InterfaceName { get; init; }

        public int Mtu { get; init; } = 1280;

        public IReadOnlyList<IpPrefix> Addresses { get; init; } = Array.Empty<IpPrefix>();

        public IReadOnlyList<IpPrefix> Routes { get; init; } = Array.Empty<IpPrefix>();
    }
}
