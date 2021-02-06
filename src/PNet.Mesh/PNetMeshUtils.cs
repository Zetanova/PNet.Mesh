using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace PNet.Actor.Mesh
{
    public static class PNetMeshUtils
    {
        public static EndPoint ParseEndPoint(string s)
        {
            if (IPEndPoint.TryParse(s, out var ep))
                return ep;

            //dns
            int i = s.IndexOf(':');
            if(i > 0 && int.TryParse(s.Substring(i + 1), out var port))
                return new DnsEndPoint(s.Substring(0, i), port);

            throw new FormatException("unknown endpoint format");
        }

        public static void GetAddressFromPublicKey(ReadOnlySpan<byte> publicKey, Span<byte> address)
        {
            if (publicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(publicKey));
            if (address.Length != 10) throw new ArgumentOutOfRangeException(nameof(address));

            Span<byte> dest = stackalloc byte[20];

            var c = SHA1.HashData(publicKey, dest);
            Debug.Assert(c == 20);

            dest.Slice(0, 10).CopyTo(address);
        }

        public static ulong GetTimestamp()
        {
            return (ulong)(DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks);
        }

        public static EndPoint MapToItem(Protos.EndPoint item)
        {
            return item.ValueCase switch
            {
                Protos.EndPoint.ValueOneofCase.Ip => item.Ip.IpCase switch
                {
                    Protos.IPEndPoint.IpOneofCase.V4 => new IPEndPoint(item.Ip.V4, (ushort)item.Ip.Port),
                    Protos.IPEndPoint.IpOneofCase.V6 => new IPEndPoint(new IPAddress(item.Ip.V6.Span), (ushort)item.Ip.Port),
                    _ => null,//throw new NotSupportedException($"ip case '{item.Ip.IpCase}' not supported");
                },
                Protos.EndPoint.ValueOneofCase.Dns => new DnsEndPoint(item.Dns.Hostname, (ushort)item.Dns.Port),
                Protos.EndPoint.ValueOneofCase.Mesh => null,
                _ => null,//throw new NotSupportedException($"endpoint case '{item.ValueCase}' not supported");
            };
        }

        public static Protos.EndPoint MapToProtos(EndPoint item)
        {
            return item switch
            {
                IPEndPoint ep => ep.AddressFamily switch
                {
                    AddressFamily.InterNetwork => new Protos.EndPoint
                    {
                        Ip = new Protos.IPEndPoint
                        {
                            V4 = BitConverter.ToUInt32(ep.Address.GetAddressBytes()),
                            Port = (uint)ep.Port
                        }
                    },
                    AddressFamily.InterNetworkV6 => new Protos.EndPoint
                    {
                        Ip = new Protos.IPEndPoint
                        {
                            V6 = Google.Protobuf.ByteString.CopyFrom(ep.Address.GetAddressBytes()),
                            Port = (uint)ep.Port
                        }
                    }
                },
                DnsEndPoint ep => new Protos.EndPoint
                {
                    Dns = new Protos.DnsEndPoint
                    {
                        Hostname = ep.Host,
                        Port = (uint)ep.Port
                    }
                },
                _ => null //maybe throw error
            };
        }


    }
}
