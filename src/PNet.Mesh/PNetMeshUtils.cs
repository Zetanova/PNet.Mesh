using System;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using Org.BouncyCastle.Crypto.Digests;

using MeshProtos = global::PNet.Actor.Mesh.Protos;

namespace PNet.Mesh
{
    public static class PNetMeshUtils
    {
        public static EndPoint ParseEndPoint(string s)
        {
            if (IPEndPoint.TryParse(s, out var ep))
                return ep;

            //dns
            int i = s.IndexOf(':');
            if (i > 0 && int.TryParse(s.Substring(i + 1), out var port))
                return new DnsEndPoint(s.Substring(0, i), port);

            throw new FormatException("unknown endpoint format");
        }

        public static void GetAddressFromPublicKey(ReadOnlySpan<byte> publicKey, Span<byte> address)
        {
            if (publicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(publicKey));
            if (address.Length != 10) throw new ArgumentOutOfRangeException(nameof(address));

            ReadOnlySpan<byte> domain = "pnet.mesh.address.v1"u8;
            Span<byte> input = stackalloc byte[domain.Length + 32];
            domain.CopyTo(input);
            publicKey.CopyTo(input[domain.Length..]);

            Span<byte> dest = stackalloc byte[32];
            var digest = new Blake2sDigest(256);
            digest.BlockUpdate(input);
            digest.DoFinal(dest);

            dest[..10].CopyTo(address);
        }

        public static ulong GetTimestamp()
        {
            return (ulong)(DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks);
        }

        /// <summary>
        /// Reads a Varint32 from the specified span, returning the value and the number of bytes read.
        /// Throws if the varint is malformed or truncated.
        /// </summary>
        public static int ReadVarint32(ReadOnlySpan<byte> buffer, out int bytesRead)
        {
            uint value = 0;
            int shift = 0;
            bytesRead = 0;

            for (; bytesRead < buffer.Length && bytesRead < 5; bytesRead++)
            {
                byte b = buffer[bytesRead];
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    bytesRead++;
                    return unchecked((int)value);
                }
                shift += 7;
            }

            throw new InvalidOperationException("Malformed Varint32, too many bytes or incomplete.");
        }

        /// <summary>
        /// Writes a Varint32 to the span. Returns the number of bytes written.
        /// Throws if the span is too small.
        /// </summary>
        public static int WriteVarint32(int value, Span<byte> buffer)
        {
            uint uValue = unchecked((uint)value);

            var c = GetVarint32Size(uValue) - 1;
            if (c >= buffer.Length)
                throw new ArgumentException("Span too small for Varint32", nameof(buffer));

            int i;
            for (i = 0; i < c; i++)
            {
                buffer[i] = (byte)((uValue & 0x7F) | 0x80);
                uValue >>= 7;
            }
            buffer[i++] = (byte)uValue;

            return i;
        }

        /// <summary>
        /// Returns the byte size of the encoded integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarint32Size(uint value)
        {
            var c = (sizeof(uint) * 8) - BitOperations.LeadingZeroCount(value);
            return Math.Max(1, (c + 6) / 7);
        }

        public static EndPoint? MapToItem(MeshProtos.Endpoint? item)
        {
            if (item is null)
                return null;

            return item.ValueCase switch
            {
                MeshProtos.Endpoint.ValueOneofCase.Ip => item.Ip.AddressCase switch
                {
                    MeshProtos.IpEndpoint.AddressOneofCase.Ipv4 => new IPEndPoint(new IPAddress(item.Ip.Ipv4.Span), (ushort)item.Ip.Port),
                    MeshProtos.IpEndpoint.AddressOneofCase.Ipv6 => new IPEndPoint(new IPAddress(item.Ip.Ipv6.Span), (ushort)item.Ip.Port),
                    _ => null,//throw new NotSupportedException($"ip case '{item.Ip.IpCase}' not supported");
                },
                MeshProtos.Endpoint.ValueOneofCase.Dns => new DnsEndPoint(item.Dns.Hostname, (ushort)item.Dns.Port),
                MeshProtos.Endpoint.ValueOneofCase.Mesh => null,
                _ => null,//throw new NotSupportedException($"endpoint case '{item.ValueCase}' not supported");
            };
        }

        public static MeshProtos.Endpoint? MapToProtos(EndPoint? item)
        {
            return item switch
            {
                IPEndPoint ep => ep.AddressFamily switch
                {
                    AddressFamily.InterNetwork => new MeshProtos.Endpoint
                    {
                        Ip = new MeshProtos.IpEndpoint
                        {
                            Ipv4 = MapIPv4Address(ep.Address),
                            Port = (uint)ep.Port
                        }
                    },
                    AddressFamily.InterNetworkV6 => new MeshProtos.Endpoint
                    {
                        Ip = new MeshProtos.IpEndpoint
                        {
                            Ipv6 = MapIPv6Address(ep.Address),
                            Port = (uint)ep.Port
                        }
                    },
                    _ => null
                },
                DnsEndPoint ep => new MeshProtos.Endpoint
                {
                    Dns = new MeshProtos.DnsEndpoint
                    {
                        Hostname = ep.Host,
                        Port = (uint)ep.Port
                    }
                },
                _ => null //maybe throw error
            };
        }

        static Google.Protobuf.ByteString MapIPv4Address(IPAddress address)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!address.TryWriteBytes(bytes, out var bytesWritten) || bytesWritten != bytes.Length)
                throw new InvalidOperationException("IPv4 address byte length mismatch.");

            return Google.Protobuf.ByteString.CopyFrom(bytes);
        }

        static Google.Protobuf.ByteString MapIPv6Address(IPAddress address)
        {
            Span<byte> bytes = stackalloc byte[16];
            if (!address.TryWriteBytes(bytes, out var bytesWritten) || bytesWritten != bytes.Length)
                throw new InvalidOperationException("IPv6 address byte length mismatch.");

            return Google.Protobuf.ByteString.CopyFrom(bytes);
        }
    }
}
