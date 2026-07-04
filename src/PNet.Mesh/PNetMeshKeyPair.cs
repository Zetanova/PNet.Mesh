using System;
using System.Security.Cryptography;

namespace PNet.Mesh
{
    public sealed class PNetMeshKeyPair : IDisposable
    {
        readonly byte[] _privateKey;
        readonly byte[] _publicKey;
        bool _disposed;

        internal PNetMeshKeyPair(byte[] privateKey, byte[] publicKey)
        {
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            if (privateKey.Length != PNetMeshLibSodium.X25519KeySize) throw new ArgumentOutOfRangeException(nameof(privateKey));
            if (publicKey.Length != PNetMeshLibSodium.X25519KeySize) throw new ArgumentOutOfRangeException(nameof(publicKey));

            _privateKey = privateKey;
            _publicKey = publicKey;
        }

        public byte[] PrivateKey
        {
            get
            {
                ThrowIfDisposed();
                return (byte[])_privateKey.Clone();
            }
        }

        public byte[] PublicKey
        {
            get
            {
                ThrowIfDisposed();
                return (byte[])_publicKey.Clone();
            }
        }

        public static PNetMeshKeyPair Generate()
        {
            return PNetMeshLibSodium.GenerateKeyPair();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CryptographicOperations.ZeroMemory(_privateKey);
            _disposed = true;
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PNetMeshKeyPair));
        }
    }
}
