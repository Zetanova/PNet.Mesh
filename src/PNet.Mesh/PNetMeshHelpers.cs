using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PNet.Mesh
{
    public sealed class PNetMeshByteArrayComparer : IEqualityComparer<byte[]>
    {
        public readonly static PNetMeshByteArrayComparer Default = new PNetMeshByteArrayComparer();

        public bool Equals(byte[] x, byte[] y)
        {
            //todo benchmark
            return StructuralComparisons.StructuralEqualityComparer.Equals(x, y);
        }

        public int GetHashCode([DisallowNull] byte[] obj)
        {
            return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
        }
    }
}
