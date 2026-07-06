namespace PNet.Mesh
{
    public sealed class PNetMeshServerSettings
    {
        public const int WireGuardSocketBufferBytes = 7 << 20;

        public required byte[] PublicKey { get; init; }

        public required byte[] PrivateKey { get; init; }

        public byte[]? Psk { get; init; }

        public required string[] BindTo { get; init; }

        public PNetMeshPeer[]? Peers { get; init; }

        public int? UdpSocketBufferBytes { get; init; }
    }
}
