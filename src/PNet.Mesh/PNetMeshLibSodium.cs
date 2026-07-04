using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PNet.Mesh
{
    internal static unsafe partial class PNetMeshLibSodium
    {
        public const int X25519KeySize = 32;
        public const int ChaCha20Poly1305IetfKeySize = 32;
        public const int ChaCha20Poly1305IetfNonceSize = 12;
        public const int ChaCha20Poly1305TagSize = 16;

        const string LibraryName = "libsodium";

        static PNetMeshLibSodium()
        {
            if (sodium_init() < 0)
                throw new CryptographicException("libsodium initialization failed.");
        }

        public static void EnsureInitialized()
        {
        }

        public static PNetMeshKeyPair GenerateKeyPair()
        {
            GenerateKeyPair(out var privateKey, out var publicKey);
            return new PNetMeshKeyPair(privateKey, publicKey);
        }

        internal static void GenerateKeyPair(out byte[] privateKey, out byte[] publicKey)
        {
            privateKey = new byte[X25519KeySize];
            publicKey = new byte[X25519KeySize];
            RandomNumberGenerator.Fill(privateKey);
            ClampWireGuardPrivateKey(privateKey);
            CreatePublicKey(privateKey, publicKey);
        }

        public static void CreatePublicKey(ReadOnlySpan<byte> privateKey, Span<byte> publicKey)
        {
            if (privateKey.Length != X25519KeySize) throw new ArgumentOutOfRangeException(nameof(privateKey));
            if (publicKey.Length != X25519KeySize) throw new ArgumentOutOfRangeException(nameof(publicKey));

            fixed (byte* q = publicKey)
            fixed (byte* n = privateKey)
            {
                if (crypto_scalarmult_curve25519_base(q, n) != 0)
                    throw new CryptographicException("X25519 public key derivation failed.");
            }
        }

        public static bool TryScalarMult(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey, Span<byte> sharedSecret)
        {
            if (privateKey.Length != X25519KeySize) throw new ArgumentOutOfRangeException(nameof(privateKey));
            if (publicKey.Length != X25519KeySize) throw new ArgumentOutOfRangeException(nameof(publicKey));
            if (sharedSecret.Length != X25519KeySize) throw new ArgumentOutOfRangeException(nameof(sharedSecret));

            fixed (byte* q = sharedSecret)
            fixed (byte* n = privateKey)
            fixed (byte* p = publicKey)
            {
                return crypto_scalarmult_curve25519(q, n, p) == 0;
            }
        }

        public static void EncryptChaCha20Poly1305Ietf(
            ReadOnlySpan<byte> key,
            ulong counter,
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> associatedData,
            Span<byte> ciphertext,
            out int bytesWritten)
        {
            if (key.Length != ChaCha20Poly1305IetfKeySize) throw new ArgumentOutOfRangeException(nameof(key));
            if (ciphertext.Length < plaintext.Length + ChaCha20Poly1305TagSize)
                throw new ArgumentOutOfRangeException(nameof(ciphertext));

            Span<byte> nonce = stackalloc byte[ChaCha20Poly1305IetfNonceSize];
            BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);

            ulong nativeBytesWritten;
            fixed (byte* c = ciphertext)
            fixed (byte* m = plaintext)
            fixed (byte* ad = associatedData)
            fixed (byte* n = nonce)
            fixed (byte* k = key)
            {
                if (crypto_aead_chacha20poly1305_ietf_encrypt(
                        c,
                        &nativeBytesWritten,
                        m,
                        (ulong)plaintext.Length,
                        ad,
                        (ulong)associatedData.Length,
                        null,
                        n,
                        k) != 0)
                    throw new CryptographicException("ChaCha20-Poly1305 encryption failed.");
            }

            bytesWritten = checked((int)nativeBytesWritten);
        }

        public static bool TryDecryptChaCha20Poly1305Ietf(
            ReadOnlySpan<byte> key,
            ulong counter,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> associatedData,
            Span<byte> plaintext,
            out int bytesWritten)
        {
            if (key.Length != ChaCha20Poly1305IetfKeySize) throw new ArgumentOutOfRangeException(nameof(key));
            if (ciphertext.Length < ChaCha20Poly1305TagSize) throw new ArgumentOutOfRangeException(nameof(ciphertext));
            if (plaintext.Length < ciphertext.Length - ChaCha20Poly1305TagSize)
                throw new ArgumentOutOfRangeException(nameof(plaintext));

            Span<byte> nonce = stackalloc byte[ChaCha20Poly1305IetfNonceSize];
            BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);

            ulong nativeBytesWritten;
            fixed (byte* m = plaintext)
            fixed (byte* c = ciphertext)
            fixed (byte* ad = associatedData)
            fixed (byte* n = nonce)
            fixed (byte* k = key)
            {
                if (crypto_aead_chacha20poly1305_ietf_decrypt(
                        m,
                        &nativeBytesWritten,
                        null,
                        c,
                        (ulong)ciphertext.Length,
                        ad,
                        (ulong)associatedData.Length,
                        n,
                        k) != 0)
                {
                    bytesWritten = 0;
                    return false;
                }
            }

            bytesWritten = checked((int)nativeBytesWritten);
            return true;
        }

        static void ClampWireGuardPrivateKey(Span<byte> privateKey)
        {
            privateKey[0] &= 248;
            privateKey[31] &= 127;
            privateKey[31] |= 64;
        }

        [LibraryImport(LibraryName)]
        private static partial int sodium_init();

        [LibraryImport(LibraryName)]
        private static partial int crypto_scalarmult_curve25519_base(byte* q, byte* n);

        [LibraryImport(LibraryName)]
        private static partial int crypto_scalarmult_curve25519(byte* q, byte* n, byte* p);

        [LibraryImport(LibraryName)]
        private static partial int crypto_aead_chacha20poly1305_ietf_encrypt(
            byte* c,
            ulong* clen_p,
            byte* m,
            ulong mlen,
            byte* ad,
            ulong adlen,
            byte* nsec,
            byte* npub,
            byte* k);

        [LibraryImport(LibraryName)]
        private static partial int crypto_aead_chacha20poly1305_ietf_decrypt(
            byte* m,
            ulong* mlen_p,
            byte* nsec,
            byte* c,
            ulong clen,
            byte* ad,
            ulong adlen,
            byte* npub,
            byte* k);
    }
}
