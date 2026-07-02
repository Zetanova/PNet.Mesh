using PNet.Mesh;
using System;
using System.Collections.Generic;

namespace PNet.Mesh.Tun
{
    public sealed class PNetMeshTunPeerRoute
    {
        public string Name { get; init; }

        public PNetMeshPeer Peer { get; init; }

        public IReadOnlyList<IpPrefix> AllowedIPs { get; init; } = Array.Empty<IpPrefix>();
    }
}
