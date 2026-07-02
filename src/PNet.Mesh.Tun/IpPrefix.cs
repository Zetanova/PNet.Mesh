using System;
using System.Net;
using System.Net.Sockets;

namespace PNet.Mesh.Tun
{
    public readonly struct IpPrefix : IEquatable<IpPrefix>
    {
        readonly byte[] _addressBytes;

        public IpPrefix(IPAddress address, int prefixLength)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));

            var maxPrefixLength = GetMaxPrefixLength(address.AddressFamily);
            if ((uint)prefixLength > maxPrefixLength)
                throw new ArgumentOutOfRangeException(nameof(prefixLength));

            PrefixLength = prefixLength;
            _addressBytes = address.GetAddressBytes();
        }

        public IPAddress Address { get; }

        public int PrefixLength { get; }

        public bool Contains(IPAddress address)
        {
            if (address == null)
                return false;
            if (address.AddressFamily != Address.AddressFamily)
                return false;

            Span<byte> candidate = stackalloc byte[_addressBytes.Length];
            if (!address.TryWriteBytes(candidate, out var bytesWritten) || bytesWritten != _addressBytes.Length)
                return false;
            var fullBytes = PrefixLength / 8;
            var remainingBits = PrefixLength % 8;

            for (var i = 0; i < fullBytes; i++)
            {
                if (_addressBytes[i] != candidate[i])
                    return false;
            }

            if (remainingBits == 0)
                return true;

            var mask = (byte)(0xff << (8 - remainingBits));
            return (_addressBytes[fullBytes] & mask) == (candidate[fullBytes] & mask);
        }

        public override string ToString()
        {
            return $"{Address}/{PrefixLength}";
        }

        public bool Equals(IpPrefix other)
        {
            return PrefixLength == other.PrefixLength
                   && Equals(Address, other.Address);
        }

        public override bool Equals(object obj)
        {
            return obj is IpPrefix other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Address, PrefixLength);
        }

        public static IpPrefix Parse(string value)
        {
            if (!TryParse(value, out var prefix))
                throw new FormatException($"Invalid IP prefix '{value}'.");

            return prefix;
        }

        public static bool TryParse(string value, out IpPrefix prefix)
        {
            prefix = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var slash = value.IndexOf('/');
            if (slash <= 0 || slash == value.Length - 1)
                return false;

            if (!IPAddress.TryParse(value[..slash], out var address))
                return false;
            if (!int.TryParse(value[(slash + 1)..], out var prefixLength))
                return false;

            var maxPrefixLength = GetMaxPrefixLength(address.AddressFamily);
            if ((uint)prefixLength > maxPrefixLength)
                return false;

            prefix = new IpPrefix(address, prefixLength);
            return true;
        }

        static int GetMaxPrefixLength(AddressFamily addressFamily)
        {
            return addressFamily switch
            {
                AddressFamily.InterNetwork => 32,
                AddressFamily.InterNetworkV6 => 128,
                _ => throw new ArgumentException("Address family must be IPv4 or IPv6.", nameof(addressFamily))
            };
        }
    }
}
