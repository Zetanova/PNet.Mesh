using System;
using System.Collections.Generic;

namespace PNet.Mesh
{
    public sealed class PNetMeshByteArrayComparer :
        IEqualityComparer<byte[]>,
        IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public readonly static PNetMeshByteArrayComparer Default = new PNetMeshByteArrayComparer();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            return GetHashCode(obj.AsSpan());
        }

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other)
        {
            return alternate.SequenceEqual(other);
        }

        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            var hash = new HashCode();
            hash.AddBytes(alternate);
            return hash.ToHashCode();
        }

        public byte[] Create(ReadOnlySpan<byte> alternate)
        {
            return alternate.ToArray();
        }
    }
}
