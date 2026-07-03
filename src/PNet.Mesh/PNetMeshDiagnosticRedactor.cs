using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PNet.Mesh
{
    internal static class PNetMeshDiagnosticRedactor
    {
        public static string AddressId(ReadOnlySpan<byte> address)
        {
            return Hash("addr", address);
        }

        public static string AddressId(byte[]? address)
        {
            return address is null ? "addr:none" : AddressId(address.AsSpan());
        }

        public static string EndpointId(EndPoint? endpoint)
        {
            if (endpoint is null)
                return "ep:none";

            return EndpointKeyId(PNetMeshWireGuardRelayRegistry.GetEndpointKey(endpoint));
        }

        public static string EndpointKeyId(string endpointKey)
        {
            if (endpointKey is null)
                return "ep:none";

            return Hash("ep", Encoding.UTF8.GetBytes(endpointKey));
        }

        public static string PublicKeyId(ReadOnlySpan<byte> publicKey)
        {
            return Hash("peer", publicKey);
        }

        static string Hash(string prefix, ReadOnlySpan<byte> value)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(value, hash);
            return $"{prefix}:{Convert.ToHexString(hash[..6])}";
        }
    }
}
