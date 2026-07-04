using Noise;
using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using CryptographicException = System.Security.Cryptography.CryptographicException;
using KeyPair = PNet.Mesh.PNetMeshKeyPair;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshWireGuardOracleTests
    {
        [Fact]
        public void direct_wireguard_implementation_interoperates_with_noise_net_oracle_matrix()
        {
            AssertPairing("local initiator to local responder", PairingMode.LocalInitiatorLocalResponder);
            AssertPairing("local initiator to Noise.NET responder", PairingMode.LocalInitiatorOracleResponder);
            AssertPairing("Noise.NET initiator to local responder", PairingMode.OracleInitiatorLocalResponder);
            AssertPairing("Noise.NET initiator to Noise.NET responder", PairingMode.OracleInitiatorOracleResponder);
        }

        [Fact]
        public void direct_wireguard_implementation_rejects_noise_net_oracle_wrong_psk_matrix()
        {
            AssertWrongPsk(PairingMode.LocalInitiatorOracleResponder);
            AssertWrongPsk(PairingMode.OracleInitiatorLocalResponder);
        }

        static void AssertPairing(string name, PairingMode mode)
        {
            Span<byte> initiationBuffer = stackalloc byte[PNetMeshHandshake.WireGuardInitiationMessageSize];
            Span<byte> responseBuffer = stackalloc byte[PNetMeshHandshake.ResponseMessageSize];

            var psk = CreatePsk(0x40);
            using var initiatorKey = KeyPair.Generate();
            using var responderKey = KeyPair.Generate();

            using var initiator = CreateInitiator(mode, initiatorKey, responderKey.PublicKey, psk);
            using var responder = CreateResponder(mode, responderKey, psk);

            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            var initiation = initiationBuffer[..bytesWritten].ToArray();
            Assert.Equal(PNetMeshHandshake.WireGuardInitiationMessageSize, bytesWritten);
            Assert.True(responder.Protocol.ValidatePacket(initiation), name);
            Assert.True(responder.TryReadInitiationMessage(initiation), name);

            Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport), name);
            using (responderTransport)
            {
                var response = responseBuffer[..bytesWritten].ToArray();
                Assert.Equal(PNetMeshHandshake.ResponseMessageSize, bytesWritten);
                Assert.True(initiator.Protocol.ValidatePacket(response), name);
                Assert.True(initiator.TryReadResponseMessage(response, out var initiatorTransport), name);
                using (initiatorTransport)
                {
                    AssertTransportBehavior(name, initiatorTransport!, responderTransport!);
                    AssertTransportBehavior(name, responderTransport!, initiatorTransport!);
                }
            }
        }

        static void AssertWrongPsk(PairingMode mode)
        {
            Span<byte> initiationBuffer = stackalloc byte[PNetMeshHandshake.WireGuardInitiationMessageSize];
            Span<byte> responseBuffer = stackalloc byte[PNetMeshHandshake.ResponseMessageSize];

            using var initiatorKey = KeyPair.Generate();
            using var responderKey = KeyPair.Generate();

            using var initiator = CreateInitiator(mode, initiatorKey, responderKey.PublicKey, CreatePsk(0x10));
            using var responder = CreateResponder(mode, responderKey, CreatePsk(0x80));

            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(initiationBuffer[..bytesWritten]));
            Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport));
            using (responderTransport)
            {
                Assert.False(initiator.TryReadResponseMessage(responseBuffer[..bytesWritten], out var initiatorTransport));
                Assert.Null(initiatorTransport);
            }
        }

        static void AssertTransportBehavior(string name, IWireGuardTestTransport sender, IWireGuardTestTransport receiver)
        {
            Span<byte> packet = stackalloc byte[256];
            Span<byte> plaintext = stackalloc byte[256];
            var firstPayload = Encoding.UTF8.GetBytes($"{name}: first payload");

            sender.WriteMessage(firstPayload, packet, out var bytesWritten, out var counter);
            var firstPacket = packet[..bytesWritten].ToArray();
            Assert.Equal(0ul, counter);
            Assert.True(receiver.TryReadMessage(firstPacket, plaintext, out var plaintextBytes, out var readCounter), name);
            Assert.Equal(counter, readCounter);
            Assert.True(firstPayload.AsSpan().SequenceEqual(plaintext[..firstPayload.Length]));
            Assert.All(plaintext.Slice(firstPayload.Length, plaintextBytes - firstPayload.Length).ToArray(), b => Assert.Equal((byte)0, b));
            Assert.False(receiver.TryReadMessage(firstPacket, plaintext, out _, out _));

            var secondPayload = Encoding.UTF8.GetBytes($"{name}: tamper target");
            sender.WriteMessage(secondPayload, packet, out bytesWritten, out counter);
            var secondPacket = packet[..bytesWritten].ToArray();
            var tampered = (byte[])secondPacket.Clone();
            tampered[^1] ^= 0x80;
            Assert.False(receiver.TryReadMessage(tampered, plaintext, out _, out _));
            Assert.True(receiver.TryReadMessage(secondPacket, plaintext, out plaintextBytes, out readCounter), name);
            Assert.Equal(counter, readCounter);
            Assert.True(secondPayload.AsSpan().SequenceEqual(plaintext[..secondPayload.Length]));

            sender.WriteMessage(secondPayload, packet, out bytesWritten, out _);
            var wrongReceiver = packet[..bytesWritten].ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(wrongReceiver.AsSpan(4, 4), 0xaabbccdd);
            Assert.False(receiver.TryReadMessage(wrongReceiver, plaintext, out _, out _));
        }

        static IWireGuardTestHandshake CreateInitiator(
            PairingMode mode,
            KeyPair keyPair,
            byte[] responderPublicKey,
            byte[] psk)
        {
            return mode is PairingMode.LocalInitiatorLocalResponder or PairingMode.LocalInitiatorOracleResponder
                ? LocalWireGuardTestHandshake.CreateInitiator(keyPair.PrivateKey, keyPair.PublicKey, responderPublicKey, psk)
                : NoiseNetWireGuardOracleHandshake.CreateInitiator(keyPair.PrivateKey, keyPair.PublicKey, responderPublicKey, psk);
        }

        static IWireGuardTestHandshake CreateResponder(PairingMode mode, KeyPair keyPair, byte[] psk)
        {
            return mode is PairingMode.LocalInitiatorLocalResponder or PairingMode.OracleInitiatorLocalResponder
                ? LocalWireGuardTestHandshake.CreateResponder(keyPair.PrivateKey, keyPair.PublicKey, psk)
                : NoiseNetWireGuardOracleHandshake.CreateResponder(keyPair.PrivateKey, keyPair.PublicKey, psk);
        }

        static byte[] CreatePsk(byte first)
        {
            var psk = new byte[32];
            for (var i = 0; i < psk.Length; i++)
                psk[i] = (byte)(first + i);
            return psk;
        }

        enum PairingMode
        {
            LocalInitiatorLocalResponder,
            LocalInitiatorOracleResponder,
            OracleInitiatorLocalResponder,
            OracleInitiatorOracleResponder
        }

        interface IWireGuardTestHandshake : IDisposable
        {
            PNetMeshProtocol Protocol { get; }

            void WriteInitiationMessage(Span<byte> buffer, out int bytesWritten);

            bool TryReadInitiationMessage(ReadOnlySpan<byte> payload);

            bool TryWriteResponseMessage(
                Span<byte> buffer,
                out int bytesWritten,
                out IWireGuardTestTransport? transport);

            bool TryReadResponseMessage(
                ReadOnlySpan<byte> payload,
                out IWireGuardTestTransport? transport);
        }

        interface IWireGuardTestTransport : IDisposable
        {
            void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter);

            bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter);
        }

        sealed class LocalWireGuardTestHandshake : IWireGuardTestHandshake
        {
            readonly PNetMeshHandshake _handshake;

            LocalWireGuardTestHandshake(PNetMeshProtocol protocol, PNetMeshHandshake handshake)
            {
                Protocol = protocol;
                _handshake = handshake;
            }

            public PNetMeshProtocol Protocol { get; }

            public static LocalWireGuardTestHandshake CreateInitiator(
                byte[] privateKey,
                byte[] publicKey,
                byte[] responderPublicKey,
                byte[] psk)
            {
                var protocol = new PNetMeshProtocol(privateKey, publicKey, psk);
                return new LocalWireGuardTestHandshake(protocol, protocol.CreateInitiator(0x1001, responderPublicKey));
            }

            public static LocalWireGuardTestHandshake CreateResponder(byte[] privateKey, byte[] publicKey, byte[] psk)
            {
                var protocol = new PNetMeshProtocol(privateKey, publicKey, psk);
                return new LocalWireGuardTestHandshake(protocol, protocol.CreateResponder(0x2002));
            }

            public void WriteInitiationMessage(Span<byte> buffer, out int bytesWritten)
            {
                _handshake.WriteInitiationMessage(buffer, out bytesWritten);
            }

            public bool TryReadInitiationMessage(ReadOnlySpan<byte> payload)
            {
                return _handshake.TryReadInitiationMessage(payload);
            }

            public bool TryWriteResponseMessage(
                Span<byte> buffer,
                out int bytesWritten,
                out IWireGuardTestTransport? transport)
            {
                transport = null;
                if (!_handshake.TryWriteResponseMessage(buffer, out bytesWritten, out var localTransport))
                    return false;

                transport = new LocalWireGuardTestTransport(localTransport);
                return true;
            }

            public bool TryReadResponseMessage(
                ReadOnlySpan<byte> payload,
                out IWireGuardTestTransport? transport)
            {
                transport = null;
                if (!_handshake.TryReadResponseMessage(payload, out var localTransport))
                    return false;

                transport = new LocalWireGuardTestTransport(localTransport);
                return true;
            }

            public void Dispose()
            {
                _handshake.Dispose();
            }
        }

        sealed class LocalWireGuardTestTransport : IWireGuardTestTransport
        {
            readonly PNetMeshTransport2 _transport;

            public LocalWireGuardTestTransport(PNetMeshTransport2 transport)
            {
                _transport = transport;
            }

            public void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
            {
                _transport.WriteMessage(payload, buffer, out bytesWritten, out counter);
            }

            public bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
            {
                return _transport.TryReadMessage(payload, buffer, out bytesWritten, out counter);
            }

            public void Dispose()
            {
                _transport.Dispose();
            }
        }

        sealed class NoiseNetWireGuardOracleHandshake : IWireGuardTestHandshake
        {
            static readonly Protocol NoiseProtocol = global::Noise.Protocol.Parse("Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s");
            static readonly byte[] Prologue = Encoding.UTF8.GetBytes("WireGuard v1 zx2c4 Jason@zx2c4.com");

            readonly HandshakeState _handshake;
            readonly uint _senderIndex;
            uint _receiverIndex;

            NoiseNetWireGuardOracleHandshake(
                PNetMeshProtocol protocol,
                HandshakeState handshake,
                uint senderIndex)
            {
                Protocol = protocol;
                _handshake = handshake;
                _senderIndex = senderIndex;
            }

            public PNetMeshProtocol Protocol { get; }

            byte[] RemotePublicKey { get; set; } = Array.Empty<byte>();

            public static NoiseNetWireGuardOracleHandshake CreateInitiator(
                byte[] privateKey,
                byte[] publicKey,
                byte[] responderPublicKey,
                byte[] psk)
            {
                var protocol = new PNetMeshProtocol(privateKey, publicKey, psk);
                var handshake = NoiseProtocol.Create(true, Prologue, privateKey, responderPublicKey, new[] { psk });
                return new NoiseNetWireGuardOracleHandshake(protocol, handshake, 0x1001)
                {
                    RemotePublicKey = responderPublicKey
                };
            }

            public static NoiseNetWireGuardOracleHandshake CreateResponder(byte[] privateKey, byte[] publicKey, byte[] psk)
            {
                var protocol = new PNetMeshProtocol(privateKey, publicKey, psk);
                var handshake = NoiseProtocol.Create(false, Prologue, privateKey, null!, new[] { psk });
                return new NoiseNetWireGuardOracleHandshake(protocol, handshake, 0x2002);
            }

            public void WriteInitiationMessage(Span<byte> buffer, out int bytesWritten)
            {
                if (buffer.Length < PNetMeshHandshake.WireGuardInitiationMessageSize)
                    throw new ArgumentOutOfRangeException(nameof(buffer));

                PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeInitiation);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);

                Span<byte> timestamp = stackalloc byte[PNetMeshTai64n.TimestampSize];
                Protocol.WriteInitiationTimestamp(timestamp);

                var mac1Offset = Protocol.HandshakeInitiationMac1Offset;
                var mac2Offset = Protocol.HandshakeInitiationMac2Offset;
                bytesWritten = _handshake.WriteMessage(timestamp, buffer[8..mac1Offset]).BytesWritten;
                Assert.Equal(mac1Offset - 8, bytesWritten);

                Protocol.GetPacketMac(_handshake.RemoteStaticPublicKey, buffer[..mac1Offset], buffer[mac1Offset..mac2Offset]);
                Protocol.GetCookieMac(buffer[..mac2Offset], buffer[mac2Offset..PNetMeshHandshake.WireGuardInitiationMessageSize]);

                bytesWritten = PNetMeshHandshake.WireGuardInitiationMessageSize;
            }

            public bool TryReadInitiationMessage(ReadOnlySpan<byte> payload)
            {
                if (payload.Length != PNetMeshHandshake.WireGuardInitiationMessageSize)
                    throw new ArgumentOutOfRangeException(nameof(payload));

                Span<byte> timestamp = stackalloc byte[PNetMeshTai64n.TimestampSize];
                int count;
                try
                {
                    (count, _, _) = _handshake.ReadMessage(payload[8..Protocol.HandshakeInitiationMac1Offset], timestamp);
                }
                catch (CryptographicException)
                {
                    return false;
                }

                if (count != PNetMeshTai64n.TimestampSize)
                    return false;

                RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();
                _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
                return Protocol.TryAddHandshakeInitiationTimestamp(RemotePublicKey, timestamp);
            }

            public bool TryWriteResponseMessage(
                Span<byte> buffer,
                out int bytesWritten,
                out IWireGuardTestTransport? transport)
            {
                if (buffer.Length < PNetMeshHandshake.ResponseMessageSize)
                    throw new ArgumentOutOfRangeException(nameof(buffer));

                transport = null;
                bytesWritten = 0;

                PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeResponse);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..12], _receiverIndex);

                var (count, _, noiseTransport) = _handshake.WriteMessage(default, buffer[12..60]);
                Assert.Equal(48, count);
                if (noiseTransport == null)
                    return false;

                Protocol.GetPacketMac(_handshake.RemoteStaticPublicKey, buffer[..60], buffer[60..76]);
                Protocol.GetCookieMac(buffer[..76], buffer[76..92]);

                transport = new NoiseNetWireGuardOracleTransport(_senderIndex, _receiverIndex, noiseTransport);
                bytesWritten = PNetMeshHandshake.ResponseMessageSize;
                return true;
            }

            public bool TryReadResponseMessage(
                ReadOnlySpan<byte> payload,
                out IWireGuardTestTransport? transport)
            {
                if (payload.Length != PNetMeshHandshake.ResponseMessageSize)
                    throw new ArgumentOutOfRangeException(nameof(payload));

                transport = null;
                _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
                if (BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]) != _senderIndex)
                    return false;

                Transport noiseTransport;
                try
                {
                    (_, _, noiseTransport) = _handshake.ReadMessage(payload[12..60], Span<byte>.Empty);
                }
                catch (CryptographicException)
                {
                    return false;
                }

                if (noiseTransport == null)
                    return false;

                transport = new NoiseNetWireGuardOracleTransport(_senderIndex, _receiverIndex, noiseTransport);
                return true;
            }

            public void Dispose()
            {
                _handshake.Dispose();
            }
        }

        sealed class NoiseNetWireGuardOracleTransport : IWireGuardTestTransport
        {
            static readonly ConcurrentDictionary<Type, TransportNonceLayout> NonceLayouts = new ConcurrentDictionary<Type, TransportNonceLayout>();
            static readonly ConcurrentDictionary<Type, MethodInfo> SetNonceMethods = new ConcurrentDictionary<Type, MethodInfo>();

            readonly Transport _transport;
            readonly PNetMeshPacketTracker _tracker = new PNetMeshPacketTracker();
            readonly uint _senderIndex;
            readonly uint _receiverIndex;
            readonly Action<ulong> _setWriteNonce;
            readonly Action<ulong> _setReadNonce;
            ulong _sendCounter;

            public NoiseNetWireGuardOracleTransport(uint senderIndex, uint receiverIndex, Transport transport)
            {
                _senderIndex = senderIndex;
                _receiverIndex = receiverIndex;
                _transport = transport;
                (_setWriteNonce, _setReadNonce) = CreateNonceSetters(transport);
            }

            public void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
            {
                var padding = (16 - (payload.Length % 16)) % 16;
                var paddedSize = payload.Length + padding;
                if (buffer.Length < PNetMeshPacketFraming.PacketDataHeaderSize + paddedSize + 16)
                    throw new ArgumentOutOfRangeException(nameof(buffer));

                PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.PacketData);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _receiverIndex);

                Span<byte> padded = stackalloc byte[paddedSize];
                payload.CopyTo(padded);
                padded[payload.Length..].Clear();

                counter = _sendCounter;
                _setWriteNonce(counter);
                bytesWritten = PNetMeshPacketFraming.PacketDataHeaderSize + _transport.WriteMessage(padded, buffer[16..]);
                BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);
                _sendCounter++;
            }

            public bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
            {
                if (payload.Length < 32)
                    throw new ArgumentOutOfRangeException(nameof(payload));
                if (buffer.Length < payload.Length)
                    throw new ArgumentOutOfRangeException(nameof(buffer));
                if (BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]) != _senderIndex)
                {
                    bytesWritten = 0;
                    counter = 0;
                    return false;
                }

                counter = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]);
                _setReadNonce(counter);
                try
                {
                    bytesWritten = _transport.ReadMessage(payload[16..], buffer);
                }
                catch (CryptographicException)
                {
                    bytesWritten = 0;
                    return false;
                }

                if (!_tracker.TryAdd(counter))
                {
                    buffer[..bytesWritten].Clear();
                    bytesWritten = 0;
                    return false;
                }

                return true;
            }

            public void Dispose()
            {
                _transport.Dispose();
                _tracker.Dispose();
            }

            static (Action<ulong> write, Action<ulong> read) CreateNonceSetters(Transport transport)
            {
                var layout = NonceLayouts.GetOrAdd(transport.GetType(), CreateNonceLayout);
                var initiator = (bool)layout.Initiator.GetValue(transport)!;
                var c1 = layout.C1.GetValue(transport)!;
                var c2 = layout.C2.GetValue(transport)!;
                return initiator
                    ? (CreateNonceSetter(c1), CreateNonceSetter(c2))
                    : (CreateNonceSetter(c2), CreateNonceSetter(c1));
            }

            static TransportNonceLayout CreateNonceLayout(Type transportType)
            {
                return new TransportNonceLayout(
                    GetRequiredField(transportType, "initiator"),
                    GetRequiredField(transportType, "c1"),
                    GetRequiredField(transportType, "c2"));
            }

            static FieldInfo GetRequiredField(Type type, string name)
            {
                return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? throw new NotSupportedException($"Noise transport field '{name}' was not found.");
            }

            static Action<ulong> CreateNonceSetter(object cipherState)
            {
                var setNonce = SetNonceMethods.GetOrAdd(
                    cipherState.GetType(),
                    type => type.GetMethod("SetNonce", new[] { typeof(ulong) })
                            ?? throw new NotSupportedException("Noise cipher state does not expose SetNonce(ulong)."));
                return (Action<ulong>)setNonce.CreateDelegate(typeof(Action<ulong>), cipherState);
            }

            readonly struct TransportNonceLayout
            {
                public TransportNonceLayout(FieldInfo initiator, FieldInfo c1, FieldInfo c2)
                {
                    Initiator = initiator;
                    C1 = c1;
                    C2 = c2;
                }

                public FieldInfo Initiator { get; }

                public FieldInfo C1 { get; }

                public FieldInfo C2 { get; }
            }
        }
    }
}
