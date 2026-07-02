using PNet.Mesh;
using System;
using System.Collections.Generic;

namespace PNet.Mesh.Tun
{
    public sealed class PNetMeshTunOptions
    {
        public string InterfaceName { get; init; } = "pnet0";

        public int Mtu { get; init; } = 1280;

        public PNetMeshServerSettings Mesh { get; init; }

        public IReadOnlyList<PNetMeshTunPeerRoute> Peers { get; init; } = Array.Empty<PNetMeshTunPeerRoute>();
    }
}
