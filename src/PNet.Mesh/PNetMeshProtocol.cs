using Org.BouncyCastle.Crypto.Digests;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace PNet.Mesh
{

    /*
    msg = packet_cookie_reply {
    u8 message_type
    u8 reserved_zero[3]
    u32 receiver_index
    u8 nonce[24]
    u8 encrypted_cookie[AEAD_LEN(16)]
    }
    

    //todo 
    msg = handshake_relay {
    u8 message_type
    u8 reserved_zero[3]

    //relay payload
    u32 sender_index
    u8 unencrypted_ephemeral[32]
    u8 encrypted_static[AEAD_LEN(32)]
    u8 encrypted_timestamp[AEAD_LEN(8)]
    u8 mac1[16]
    u8 mac2[16]

    //todo encrypted sender address is missing

    //relayer
    u8 receiver_hash[32]
    u8 relayer_mac1[16]
    u8 relayer_mac2[16]
    }

    //todo implement PLPMTUD
    //https://tools.ietf.org/html/rfc4821
    msg = packet_probe {
    u8 message_type
    u8 reserved_zero[3]
    u32 sender_index
    u32 tag
    u8 sender_timestamp[8]
    u8 receiver_hash[32]
    u8 sender_endpoint[24]    
    u8 mac1[16]
    u8 mac2[16]
    }

    //todo implement PLPMTUD
    msg = packet_probe_reply {
    u8 message_type
    u8 reserved_zero[3]
    u32 receiver_index
    u8 nonce[24]
    u8 encrypted_data[AEAD_LEN(44)]:
        u32 tag
        u8 sender_timestamp[8]
        u8 receiver_hash[32]
        u8 reciever_timestamp[8]
        u8 sender_endpoint[24]
        //maybe other attributes
    }
     */

    public sealed class PNetMeshProtocol
    {
        readonly static string Mac1Label = "mac1----"; //8 bytes

        static readonly PNetMeshProtocolProfile WireGuardProfile = new PNetMeshProtocolProfile(
            "Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s",
            "WireGuard v1 zx2c4 Jason@zx2c4.com",
            PNetMeshHandshake.WireGuardInitiationMessageSize,
            PNetMeshTai64n.TimestampSize);

        readonly PNetMeshProtocolProfile _profile = WireGuardProfile;

        readonly PNetMeshHandshakeReplayTracker _handshakeReplayTracker = new PNetMeshHandshakeReplayTracker();

        readonly PNetMeshWireGuardPeerTable _wireGuardPeers = new PNetMeshWireGuardPeerTable();

        readonly PNetMeshCookieGate _cookieGate;

        readonly byte[] _localStaticPrivKey;
        readonly byte[] _localStaticPubKey;

        readonly byte[] _psk;

        readonly byte[] _mac1KeyBytes;

        PNetMeshCookie _cookie = PNetMeshCookie.Empty;

        public string ProtocolName => _profile.ProtocolName;

        internal byte[] ProtocolNameBytes => _profile.ProtocolNameBytes;

        public ReadOnlySpan<byte> Prologue => _profile.Prologue;

        public int HandshakeInitiationMessageSize => _profile.HandshakeInitiationMessageSize;

        public int HandshakeResponseMessageSize => PNetMeshHandshake.ResponseMessageSize;

        public PNetMeshWireGuardPeerTable WireGuardPeers => _wireGuardPeers;

        public PNetMeshCookieGate CookieGate => _cookieGate;

        public bool RequireCookieMac2 { get; set; }

        internal ReadOnlySpan<byte> LocalStaticPublicKey => _localStaticPubKey;

        internal int HandshakeInitiationTimestampSize => _profile.HandshakeInitiationTimestampSize;

        internal int HandshakeInitiationMac1Offset => _profile.HandshakeInitiationMessageSize - 32;

        internal int HandshakeInitiationMac2Offset => _profile.HandshakeInitiationMessageSize - 16;

        public PNetMeshCookie Cookie
        {
            get => _cookie;
            set
            {
                //todo cookie random every 2min
                //cookie key is with remote endpoint hashed
                //proof of EP ownership

                _cookie = value;
            }
        }

        public PNetMeshProtocol(
            byte[] localStaticPrivKey,
            byte[] localStaticPubKey,
            byte[]? psk = null)
        {
            _localStaticPrivKey = localStaticPrivKey ?? throw new ArgumentNullException(nameof(localStaticPrivKey));
            _localStaticPubKey = localStaticPubKey ?? throw new ArgumentNullException(nameof(localStaticPubKey));
            if (_localStaticPrivKey.Length != PNetMeshLibSodium.X25519KeySize)
                throw new ArgumentOutOfRangeException(nameof(localStaticPrivKey));
            if (_localStaticPubKey.Length != PNetMeshLibSodium.X25519KeySize)
                throw new ArgumentOutOfRangeException(nameof(localStaticPubKey));
            if (psk != null && psk.Length != 32)
                throw new ArgumentOutOfRangeException(nameof(psk));

            _psk = psk != null ? (byte[])psk.Clone() : new byte[32];

            //init mac1 hash
            //mac1 = MAC(HASH(LABEL_MAC1 || responder.static_public), msg[0:offsetof(msg.mac1)])
            _mac1KeyBytes = CreateMac1Key(_localStaticPubKey);

            _cookieGate = new PNetMeshCookieGate(this);
        }

        public void GetPacketMac(ReadOnlySpan<byte> remotePublicKey, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            if (payload.Length < 32) throw new ArgumentOutOfRangeException(nameof(payload));
            if (mac.Length != 16) throw new ArgumentOutOfRangeException(nameof(mac));

            //maybe store key in handshake
            var mac1Key = CreateMac1Key(remotePublicKey);

            ComputeMac(mac1Key, payload, mac);
        }

        public bool ValidatePacket(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 32)
                return false;

            Span<byte> mac = stackalloc byte[16];

            //mac1 = MAC(HASH(LABEL_MAC1 || responder.static_public), msg[0:offsetof(msg.mac1)])
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType))
                return false;

            switch (messageType)
            {
                case PNetMeshMessageType.PacketData:
                    return payload.Length >= PNetMeshPacketFraming.PacketDataHeaderSize;
                case PNetMeshMessageType.HandshakeInitiation
                    when payload.Length == _profile.HandshakeInitiationMessageSize:

                    ComputeMac(_mac1KeyBytes, payload[0..^32], mac);
                    if (!CryptographicOperations.FixedTimeEquals(payload[^32..^16], mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    ComputeMac(_cookie.Value, payload[0..^16], mac);
                    return CryptographicOperations.FixedTimeEquals(payload[^16..], mac);
                case PNetMeshMessageType.HandshakeResponse
                    when payload.Length == PNetMeshHandshake.ResponseMessageSize:

                    ComputeMac(_mac1KeyBytes, payload[0..^32], mac);
                    if (!CryptographicOperations.FixedTimeEquals(payload[^32..^16], mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    ComputeMac(_cookie.Value, payload[0..^16], mac);
                    return CryptographicOperations.FixedTimeEquals(payload[^16..], mac);
                case PNetMeshMessageType.PacketCookieReply:
                    return payload.Length == PNetMeshPacketFraming.CookieReplyMessageSize;
                default:
                    //ignore unknown or invalid
                    return false;
            }
        }

        internal bool TryGetHandshakeMacOffsets(ReadOnlySpan<byte> payload, out int mac1Offset, out int mac2Offset)
        {
            mac1Offset = 0;
            mac2Offset = 0;
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType))
                return false;

            switch (messageType)
            {
                case PNetMeshMessageType.HandshakeInitiation
                    when payload.Length == _profile.HandshakeInitiationMessageSize:
                    mac1Offset = HandshakeInitiationMac1Offset;
                    mac2Offset = HandshakeInitiationMac2Offset;
                    return true;
                case PNetMeshMessageType.HandshakeResponse
                    when payload.Length == PNetMeshHandshake.ResponseMessageSize:
                    mac1Offset = 60;
                    mac2Offset = 76;
                    return true;
                default:
                    return false;
            }
        }

        internal bool TryValidatePacketMac1(ReadOnlySpan<byte> payload)
        {
            if (!TryGetHandshakeMacOffsets(payload, out var mac1Offset, out var mac2Offset))
                return false;

            Span<byte> mac = stackalloc byte[16];
            ComputeMac(_mac1KeyBytes, payload[..mac1Offset], mac);
            return CryptographicOperations.FixedTimeEquals(payload[mac1Offset..mac2Offset], mac);
        }

        internal bool TryValidatePacketMac2(ReadOnlySpan<byte> payload, PNetMeshCookie cookie)
        {
            if (!cookie.IsValid || !TryGetHandshakeMacOffsets(payload, out _, out var mac2Offset))
                return false;

            Span<byte> mac = stackalloc byte[16];
            ComputeMac(cookie.Value, payload[..mac2Offset], mac);
            return CryptographicOperations.FixedTimeEquals(payload[mac2Offset..(mac2Offset + 16)], mac);
        }

        public void GetCookieMac(ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            if (payload.Length < 32) throw new ArgumentOutOfRangeException(nameof(payload));
            if (mac.Length != 16) throw new ArgumentOutOfRangeException(nameof(mac));

            //mac2 = MAC(responder.last_received_cookie, msg[0:offsetof(msg.mac2)])
            if (_cookie.IsValid)
                ComputeMac(_cookie.Value, payload, mac);
            else
                mac.Clear();
        }

        public uint GetReceiverIndex(ReadOnlySpan<byte> payload)
        {
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType))
                throw new InvalidOperationException("invalid message type");

            switch (messageType)
            {
                case PNetMeshMessageType.PacketData:
                    return BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
                case PNetMeshMessageType.HandshakeResponse:
                    return BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);
                default:
                    throw new InvalidOperationException("invalid message type");
            }
        }

        public PNetMeshHandshake Create(uint senderIndex, bool initiator, byte[]? remoteStaticPubKey)
        {
            if (initiator && (remoteStaticPubKey == null || remoteStaticPubKey.Length != PNetMeshLibSodium.X25519KeySize))
                throw new ArgumentOutOfRangeException(nameof(remoteStaticPubKey));

            return new PNetMeshHandshake(
                this,
                senderIndex,
                _localStaticPrivKey,
                _localStaticPubKey,
                initiator,
                remoteStaticPubKey,
                _psk);
        }

        public PNetMeshHandshake CreateInitiator(uint senderIndex, byte[] remoteStaticPubKey)
        {
            return Create(senderIndex, true, remoteStaticPubKey);
        }

        public PNetMeshHandshake CreateResponder(uint senderIndex)
        {
            return Create(senderIndex, false, null);
        }

        internal void WriteInitiationTimestamp(Span<byte> destination)
        {
            if (destination.Length != _profile.HandshakeInitiationTimestampSize)
                throw new ArgumentOutOfRangeException(nameof(destination));

            PNetMeshTai64n.WriteNow(destination);
        }

        internal long ReadInitiationTimestamp(ReadOnlySpan<byte> timestamp)
        {
            if (timestamp.Length != _profile.HandshakeInitiationTimestampSize)
                throw new ArgumentOutOfRangeException(nameof(timestamp));

            return PNetMeshTai64n.ToUnixNanoseconds(timestamp);
        }

        internal bool TryAddHandshakeInitiationTimestamp(ReadOnlySpan<byte> peerPublicKey, ReadOnlySpan<byte> timestamp)
        {
            return _handshakeReplayTracker.TryAdd(peerPublicKey, timestamp);
        }

        internal void RegisterWireGuardKeypair(
            ReadOnlySpan<byte> remotePublicKey,
            PNetMeshTransport2 transport,
            PNetMeshWireGuardKeypairRole role)
        {
            var keypair = _wireGuardPeers.SetCurrentKeypair(
                remotePublicKey,
                new PNetMeshWireGuardKeypair(
                    transport.SenderIndex,
                    transport.ReceiverIndex,
                    role,
                    DateTimeOffset.UtcNow));
            transport.SetWireGuardKeypair(keypair);
        }

        byte[] CreateMac1Key(ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(publicKey));

            Span<byte> input = stackalloc byte[40];
            Encoding.UTF8.GetBytes(Mac1Label, input[0..8]);
            publicKey.CopyTo(input[8..40]);

            var output = new byte[32];
            ComputeHash(input, output);
            return output;
        }

        internal void ComputeHash(ReadOnlySpan<byte> payload, Span<byte> hash)
        {
            var digest = new Blake2sDigest(hash.Length * 8);
            digest.BlockUpdate(payload);
            digest.DoFinal(hash);
        }

        internal void ComputeHash(ReadOnlySpan<byte> payload, byte[] hash)
        {
            ComputeHash(payload, hash.AsSpan());
        }

        internal void ComputeKeyedMac(byte[] key, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            ComputeMac(key, payload, mac);
        }

        internal void ComputeKeyedMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            ComputeMac(key, payload, mac);
        }

        void ComputeMac(byte[] key, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            var digest = new Blake2sDigest(key, mac.Length, null, null);
            digest.BlockUpdate(payload);
            digest.DoFinal(mac);
        }

        void ComputeMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            var digest = new Blake2sDigest(key.ToArray(), mac.Length, null, null);
            digest.BlockUpdate(payload);
            digest.DoFinal(mac);
        }
    }

    sealed class PNetMeshProtocolProfile
    {
        public PNetMeshProtocolProfile(
            string protocolName,
            string prologue,
            int handshakeInitiationMessageSize,
            int handshakeInitiationTimestampSize)
        {
            ProtocolName = protocolName;
            ProtocolNameBytes = Encoding.UTF8.GetBytes(protocolName);
            Prologue = Encoding.UTF8.GetBytes(prologue);
            HandshakeInitiationMessageSize = handshakeInitiationMessageSize;
            HandshakeInitiationTimestampSize = handshakeInitiationTimestampSize;
        }

        public string ProtocolName { get; }

        public byte[] ProtocolNameBytes { get; }

        public byte[] Prologue { get; }

        public int HandshakeInitiationMessageSize { get; }

        public int HandshakeInitiationTimestampSize { get; }
    }

    public readonly struct PNetMeshTai64nTimestamp
    {
        public PNetMeshTai64nTimestamp(ulong unixSeconds, uint nanoseconds)
        {
            UnixSeconds = unixSeconds;
            Nanoseconds = nanoseconds;
        }

        public ulong UnixSeconds { get; }

        public uint Nanoseconds { get; }
    }

    public static class PNetMeshTai64n
    {
        public const int TimestampSize = 12;

        const ulong Base = 0x400000000000000a;

        const uint WhitenerMask = 0x00ffffff;

        public static void Write(ulong unixSeconds, uint nanoseconds, Span<byte> destination)
        {
            if (destination.Length < TimestampSize) throw new ArgumentOutOfRangeException(nameof(destination));
            if (nanoseconds >= 1_000_000_000) throw new ArgumentOutOfRangeException(nameof(nanoseconds));

            BinaryPrimitives.WriteUInt64BigEndian(destination[..8], Base + unixSeconds);
            BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], nanoseconds);
        }

        public static void WriteNow(Span<byte> destination)
        {
            var now = DateTimeOffset.UtcNow;
            var unixSeconds = now.ToUnixTimeSeconds();
            var nanoseconds = (uint)((now.Ticks % TimeSpan.TicksPerSecond) * 100);

            Write((ulong)unixSeconds, nanoseconds & ~WhitenerMask, destination);
        }

        public static PNetMeshTai64nTimestamp Read(ReadOnlySpan<byte> timestamp)
        {
            if (timestamp.Length < TimestampSize) throw new ArgumentOutOfRangeException(nameof(timestamp));

            var encodedSeconds = BinaryPrimitives.ReadUInt64BigEndian(timestamp[..8]);
            if (encodedSeconds < Base)
                throw new ArgumentOutOfRangeException(nameof(timestamp));

            return new PNetMeshTai64nTimestamp(
                encodedSeconds - Base,
                BinaryPrimitives.ReadUInt32BigEndian(timestamp[8..12]));
        }

        public static long ToUnixNanoseconds(ReadOnlySpan<byte> timestamp)
        {
            var parsed = Read(timestamp);
            checked
            {
                return (long)(parsed.UnixSeconds * 1_000_000_000ul + parsed.Nanoseconds);
            }
        }

        public static int Compare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length < TimestampSize) throw new ArgumentOutOfRangeException(nameof(left));
            if (right.Length < TimestampSize) throw new ArgumentOutOfRangeException(nameof(right));

            return left[..TimestampSize].SequenceCompareTo(right[..TimestampSize]);
        }
    }

    public sealed class PNetMeshHandshakeReplayTracker
    {
        readonly Dictionary<byte[], byte[]> _timestamps = new Dictionary<byte[], byte[]>(PNetMeshByteArrayComparer.Default);

        public bool TryAdd(ReadOnlySpan<byte> peerPublicKey, ReadOnlySpan<byte> timestamp)
        {
            if (peerPublicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(peerPublicKey));
            if (timestamp.Length < PNetMeshTai64n.TimestampSize) throw new ArgumentOutOfRangeException(nameof(timestamp));

            var lookup = _timestamps.GetAlternateLookup<ReadOnlySpan<byte>>();
            var currentTimestamp = timestamp[..PNetMeshTai64n.TimestampSize];
            if (lookup.TryGetValue(peerPublicKey, out var previous)
                && PNetMeshTai64n.Compare(currentTimestamp, previous) <= 0)
                return false;

            var peer = peerPublicKey.ToArray();
            var value = currentTimestamp.ToArray();
            _timestamps[peer] = value;
            return true;
        }
    }

    public static class PNetMeshPacketFraming
    {
        public const int MessageTypeSize = 4;

        public const int CookieReplyMessageSize = 64;

        public const int PacketDataHeaderSize = 16;

        public static bool TryReadMessageType(ReadOnlySpan<byte> payload, out PNetMeshMessageType messageType)
        {
            messageType = PNetMeshMessageType.Unknown;
            if (payload.Length < MessageTypeSize)
                return false;

            var rawType = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
            messageType = rawType switch
            {
                1 => PNetMeshMessageType.HandshakeInitiation,
                2 => PNetMeshMessageType.HandshakeResponse,
                3 => PNetMeshMessageType.PacketCookieReply,
                4 => PNetMeshMessageType.PacketData,
                _ => PNetMeshMessageType.Unknown
            };

            return messageType != PNetMeshMessageType.Unknown;
        }

        internal static void WriteMessageType(Span<byte> buffer, PNetMeshMessageType messageType)
        {
            if (buffer.Length < MessageTypeSize) throw new ArgumentOutOfRangeException(nameof(buffer));

            BinaryPrimitives.WriteUInt32LittleEndian(buffer[..4], (uint)messageType);
        }
    }

    public enum PNetMeshMessageType : byte
    {
        Unknown = 0x00,
        HandshakeInitiation = 0x1,
        HandshakeResponse = 0x2,
        PacketCookieReply = 0x3,
        PacketData = 0x4
    }

    public sealed class PNetMeshHandshake : IDisposable
    {
        //public const int MinBufferSize = 116;

        public const int WireGuardInitiationMessageSize = 148;

        public const int InitiationMessageSize = WireGuardInitiationMessageSize;

        public const int ResponseMessageSize = 92;

        readonly PNetMeshProtocol _protocol;

        readonly uint _senderIndex;

        readonly byte[] _localStaticPrivKey;

        readonly byte[] _localStaticPubKey;

        readonly byte[] _psk;

        readonly byte[] _chainingKey = new byte[32];

        readonly byte[] _hash = new byte[32];

        readonly Blake2sDigest _hashDigest = new Blake2sDigest(256);

        byte[]? _localEphemeralPrivKey;

        byte[]? _localEphemeralPubKey;

        byte[]? _remoteEphemeralPubKey;

        public byte[] LocalPublicKey => _localStaticPubKey;

        public byte[] RemotePublicKey { get; private set; } = Array.Empty<byte>();

        public long Timestamp { get; private set; }

        public uint SenderIndex => _senderIndex;

        uint _receiverIndex;

        public PNetMeshHandshake(
            PNetMeshProtocol protocol,
            uint senderIndex,
            byte[] localStaticPrivKey,
            byte[] localStaticPubKey,
            bool initiator,
            byte[]? remoteStaticPubKey,
            byte[] psk)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _senderIndex = senderIndex;
            _localStaticPrivKey = localStaticPrivKey ?? throw new ArgumentNullException(nameof(localStaticPrivKey));
            _localStaticPubKey = localStaticPubKey ?? throw new ArgumentNullException(nameof(localStaticPubKey));
            _psk = psk ?? throw new ArgumentNullException(nameof(psk));

            if (initiator)
                RemotePublicKey = (byte[])remoteStaticPubKey!.Clone();

            InitializeSymmetricState(initiator ? RemotePublicKey : _localStaticPubKey);
        }

        public void WriteInitiationMessage(Span<byte> buffer, out int bytesWritten)
        {
            var initiationMessageSize = _protocol.HandshakeInitiationMessageSize;
            if (buffer.Length < initiationMessageSize) throw new ArgumentOutOfRangeException(nameof(buffer));

            PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeInitiation);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);

            Span<byte> timestamp = stackalloc byte[_protocol.HandshakeInitiationTimestampSize];
            _protocol.WriteInitiationTimestamp(timestamp);

            var mac1Offset = _protocol.HandshakeInitiationMac1Offset;
            var mac2Offset = _protocol.HandshakeInitiationMac2Offset;
            bytesWritten = WriteInitiationPayload(timestamp, buffer[8..mac1Offset]);
            Debug.Assert(bytesWritten == mac1Offset - 8);

            _protocol.GetPacketMac(RemotePublicKey, buffer[0..mac1Offset], buffer[mac1Offset..mac2Offset]);
            _protocol.GetCookieMac(buffer[0..mac2Offset], buffer[mac2Offset..initiationMessageSize]);

            bytesWritten = initiationMessageSize;
        }

        public bool TryReadInitiationMessage(ReadOnlySpan<byte> payload)
        {
            var initiationMessageSize = _protocol.HandshakeInitiationMessageSize;
            if (payload.Length != initiationMessageSize)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType)
                || messageType != PNetMeshMessageType.HandshakeInitiation)
                throw new InvalidOperationException("invalid message type");

            Span<byte> timestamp = stackalloc byte[_protocol.HandshakeInitiationTimestampSize];
            if (!TryReadInitiationPayload(payload[8.._protocol.HandshakeInitiationMac1Offset], timestamp))
                return false;

            _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
            Timestamp = _protocol.ReadInitiationTimestamp(timestamp);

            return _protocol.TryAddHandshakeInitiationTimestamp(RemotePublicKey, timestamp);
        }

        public bool TryWriteResponseMessage(Span<byte> buffer, out int bytesWritten, [NotNullWhen(true)] out PNetMeshTransport2? transport)
        {
            if (buffer.Length < ResponseMessageSize)
                throw new ArgumentOutOfRangeException(nameof(buffer));
            if (_receiverIndex == 0)
                throw new InvalidOperationException("invalid receiver index");

            bytesWritten = 0;
            transport = null;

            PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeResponse);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..12], _receiverIndex);

            Span<byte> writeKey = stackalloc byte[32];
            Span<byte> readKey = stackalloc byte[32];
            if (!TryWriteResponsePayload(buffer[12..60], writeKey, readKey))
                return false;

            _protocol.GetPacketMac(RemotePublicKey, buffer[0..60], buffer[60..76]);
            _protocol.GetCookieMac(buffer[0..76], buffer[76..92]);

            transport = new PNetMeshTransport2(_senderIndex, _receiverIndex, writeKey, readKey);
            bytesWritten = ResponseMessageSize;

            _protocol.RegisterWireGuardKeypair(RemotePublicKey, transport, PNetMeshWireGuardKeypairRole.Responder);

            return true;
        }

        public bool TryReadResponseMessage(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out PNetMeshTransport2? transport)
        {
            if (payload.Length != ResponseMessageSize)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType)
                || messageType != PNetMeshMessageType.HandshakeResponse)
                throw new InvalidOperationException("invalid message type");

            transport = null;

            _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
            var senderIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);

            if (senderIndex != _senderIndex)
                return false;

            Span<byte> writeKey = stackalloc byte[32];
            Span<byte> readKey = stackalloc byte[32];
            if (!TryReadResponsePayload(payload[12..60], writeKey, readKey))
                return false;

            transport = new PNetMeshTransport2(_senderIndex, _receiverIndex, writeKey, readKey);
            _protocol.RegisterWireGuardKeypair(RemotePublicKey, transport, PNetMeshWireGuardKeypairRole.Initiator);

            return true;
        }

        public void Dispose()
        {
            if (_localEphemeralPrivKey != null)
                CryptographicOperations.ZeroMemory(_localEphemeralPrivKey);
            if (_localEphemeralPubKey != null)
                CryptographicOperations.ZeroMemory(_localEphemeralPubKey);
            if (_remoteEphemeralPubKey != null)
                CryptographicOperations.ZeroMemory(_remoteEphemeralPubKey);
            CryptographicOperations.ZeroMemory(_chainingKey);
            CryptographicOperations.ZeroMemory(_hash);
        }

        int WriteInitiationPayload(ReadOnlySpan<byte> timestamp, Span<byte> destination)
        {
            if (destination.Length != 108)
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (timestamp.Length != PNetMeshTai64n.TimestampSize)
                throw new ArgumentOutOfRangeException(nameof(timestamp));

            GenerateLocalEphemeral();

            _localEphemeralPubKey!.CopyTo(destination[..32]);
            MixKey(_localEphemeralPubKey);
            MixHash(destination[..32]);

            Span<byte> sharedSecret = stackalloc byte[32];
            Span<byte> key = stackalloc byte[32];

            if (!PNetMeshLibSodium.TryScalarMult(_localEphemeralPrivKey!, RemotePublicKey, sharedSecret))
                throw new CryptographicException("WireGuard initiation ephemeral-static DH failed.");
            MixKey(sharedSecret, key);
            EncryptAndHash(key, _localStaticPubKey, destination[32..80]);

            if (!PNetMeshLibSodium.TryScalarMult(_localStaticPrivKey, RemotePublicKey, sharedSecret))
                throw new CryptographicException("WireGuard initiation static-static DH failed.");
            MixKey(sharedSecret, key);
            EncryptAndHash(key, timestamp, destination[80..108]);

            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(key);
            return destination.Length;
        }

        bool TryReadInitiationPayload(ReadOnlySpan<byte> payload, Span<byte> timestamp)
        {
            if (payload.Length != 108)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (timestamp.Length != PNetMeshTai64n.TimestampSize)
                throw new ArgumentOutOfRangeException(nameof(timestamp));

            _remoteEphemeralPubKey = payload[..32].ToArray();
            MixKey(_remoteEphemeralPubKey);
            MixHash(payload[..32]);

            Span<byte> sharedSecret = stackalloc byte[32];
            Span<byte> key = stackalloc byte[32];
            Span<byte> remoteStaticPublicKey = stackalloc byte[32];

            if (!PNetMeshLibSodium.TryScalarMult(_localStaticPrivKey, _remoteEphemeralPubKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);
            if (!TryDecryptAndHash(key, payload[32..80], remoteStaticPublicKey))
                return false;

            RemotePublicKey = remoteStaticPublicKey.ToArray();

            if (!PNetMeshLibSodium.TryScalarMult(_localStaticPrivKey, RemotePublicKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);
            if (!TryDecryptAndHash(key, payload[80..108], timestamp))
                return false;

            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(remoteStaticPublicKey);
            return true;
        }

        bool TryWriteResponsePayload(Span<byte> destination, Span<byte> writeKey, Span<byte> readKey)
        {
            if (destination.Length != 48)
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (writeKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(writeKey));
            if (readKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(readKey));
            if (_remoteEphemeralPubKey == null || RemotePublicKey.Length != 32)
                return false;

            GenerateLocalEphemeral();

            _localEphemeralPubKey!.CopyTo(destination[..32]);
            MixKey(_localEphemeralPubKey);
            MixHash(destination[..32]);

            Span<byte> sharedSecret = stackalloc byte[32];
            Span<byte> key = stackalloc byte[32];

            if (!PNetMeshLibSodium.TryScalarMult(_localEphemeralPrivKey!, _remoteEphemeralPubKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);

            if (!PNetMeshLibSodium.TryScalarMult(_localEphemeralPrivKey, RemotePublicKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);

            MixKeyAndHash(_psk, key);
            EncryptAndHash(key, ReadOnlySpan<byte>.Empty, destination[32..48]);

            Split(readKey, writeKey);

            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(key);
            return true;
        }

        bool TryReadResponsePayload(ReadOnlySpan<byte> payload, Span<byte> writeKey, Span<byte> readKey)
        {
            if (payload.Length != 48)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (writeKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(writeKey));
            if (readKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(readKey));
            if (_localEphemeralPrivKey == null)
                return false;

            _remoteEphemeralPubKey = payload[..32].ToArray();
            MixKey(_remoteEphemeralPubKey);
            MixHash(payload[..32]);

            Span<byte> sharedSecret = stackalloc byte[32];
            Span<byte> key = stackalloc byte[32];

            if (!PNetMeshLibSodium.TryScalarMult(_localEphemeralPrivKey, _remoteEphemeralPubKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);

            if (!PNetMeshLibSodium.TryScalarMult(_localStaticPrivKey, _remoteEphemeralPubKey, sharedSecret))
                return false;
            MixKey(sharedSecret, key);

            MixKeyAndHash(_psk, key);
            if (!TryDecryptAndHash(key, payload[32..48], Span<byte>.Empty))
                return false;

            Split(writeKey, readKey);

            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(key);
            return true;
        }

        void InitializeSymmetricState(ReadOnlySpan<byte> responderStaticPublicKey)
        {
            ComputeHash(_protocol.ProtocolNameBytes, _chainingKey);
            _chainingKey.CopyTo(_hash, 0);
            MixHash(_protocol.Prologue);
            MixHash(responderStaticPublicKey);
        }

        void GenerateLocalEphemeral()
        {
            PNetMeshLibSodium.GenerateKeyPair(out var privateKey, out var publicKey);
            _localEphemeralPrivKey = privateKey;
            _localEphemeralPubKey = publicKey;
        }

        void MixHash(ReadOnlySpan<byte> data)
        {
            Span<byte> input = stackalloc byte[32 + data.Length];
            _hash.CopyTo(input[..32]);
            data.CopyTo(input[32..]);
            ComputeHash(input, _hash);
        }

        void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
        {
            Span<byte> nextChainingKey = stackalloc byte[32];
            Kdf(inputKeyMaterial, nextChainingKey);
            nextChainingKey.CopyTo(_chainingKey);
            CryptographicOperations.ZeroMemory(nextChainingKey);
        }

        void MixKey(ReadOnlySpan<byte> inputKeyMaterial, Span<byte> key)
        {
            Span<byte> nextChainingKey = stackalloc byte[32];
            Kdf(inputKeyMaterial, nextChainingKey, key);
            nextChainingKey.CopyTo(_chainingKey);
            CryptographicOperations.ZeroMemory(nextChainingKey);
        }

        void MixKeyAndHash(ReadOnlySpan<byte> inputKeyMaterial, Span<byte> key)
        {
            Span<byte> nextChainingKey = stackalloc byte[32];
            Span<byte> tempHash = stackalloc byte[32];
            Kdf(inputKeyMaterial, nextChainingKey, tempHash, key);
            nextChainingKey.CopyTo(_chainingKey);
            MixHash(tempHash);
            CryptographicOperations.ZeroMemory(nextChainingKey);
            CryptographicOperations.ZeroMemory(tempHash);
        }

        void EncryptAndHash(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, Span<byte> destination)
        {
            PNetMeshLibSodium.EncryptChaCha20Poly1305Ietf(key, 0, plaintext, _hash, destination, out var bytesWritten);
            if (bytesWritten != plaintext.Length + PNetMeshLibSodium.ChaCha20Poly1305TagSize)
                throw new CryptographicException("WireGuard handshake encryption wrote an unexpected length.");
            MixHash(destination[..bytesWritten]);
        }

        bool TryDecryptAndHash(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
        {
            if (!PNetMeshLibSodium.TryDecryptChaCha20Poly1305Ietf(key, 0, ciphertext, _hash, plaintext, out var bytesWritten))
                return false;
            if (bytesWritten != plaintext.Length)
                return false;
            MixHash(ciphertext);
            return true;
        }

        void Split(Span<byte> firstKey, Span<byte> secondKey)
        {
            if (firstKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(firstKey));
            if (secondKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(secondKey));

            Kdf(ReadOnlySpan<byte>.Empty, firstKey, secondKey);
        }

        void Kdf(ReadOnlySpan<byte> inputKeyMaterial, Span<byte> output1)
        {
            Span<byte> tempKey = stackalloc byte[32];
            ComputeHmac(_chainingKey, inputKeyMaterial, tempKey);
            Span<byte> input = stackalloc byte[1] { 1 };
            ComputeHmac(tempKey, input, output1);
            CryptographicOperations.ZeroMemory(tempKey);
        }

        void Kdf(ReadOnlySpan<byte> inputKeyMaterial, Span<byte> output1, Span<byte> output2)
        {
            Span<byte> tempKey = stackalloc byte[32];
            ComputeHmac(_chainingKey, inputKeyMaterial, tempKey);

            Span<byte> input = stackalloc byte[33];
            input[0] = 1;
            ComputeHmac(tempKey, input[..1], output1);
            output1.CopyTo(input[..32]);
            input[32] = 2;
            ComputeHmac(tempKey, input, output2);

            CryptographicOperations.ZeroMemory(tempKey);
            CryptographicOperations.ZeroMemory(input);
        }

        void Kdf(ReadOnlySpan<byte> inputKeyMaterial, Span<byte> output1, Span<byte> output2, Span<byte> output3)
        {
            Span<byte> tempKey = stackalloc byte[32];
            ComputeHmac(_chainingKey, inputKeyMaterial, tempKey);

            Span<byte> input = stackalloc byte[33];
            input[0] = 1;
            ComputeHmac(tempKey, input[..1], output1);
            output1.CopyTo(input[..32]);
            input[32] = 2;
            ComputeHmac(tempKey, input, output2);
            output2.CopyTo(input[..32]);
            input[32] = 3;
            ComputeHmac(tempKey, input, output3);

            CryptographicOperations.ZeroMemory(tempKey);
            CryptographicOperations.ZeroMemory(input);
        }

        void ComputeHmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, Span<byte> output)
        {
            if (output.Length != 32)
                throw new ArgumentOutOfRangeException(nameof(output));

            Span<byte> hmacKey = stackalloc byte[64];
            if (key.Length > 64)
                ComputeHash(key, hmacKey[..32]);
            else
                key.CopyTo(hmacKey);

            Span<byte> innerInput = stackalloc byte[64 + data.Length];
            for (var i = 0; i < 64; i++)
                innerInput[i] = 0x36;
            for (var i = 0; i < 64; i++)
                innerInput[i] ^= hmacKey[i];
            data.CopyTo(innerInput[64..]);

            Span<byte> innerHash = stackalloc byte[32];
            ComputeHash(innerInput, innerHash);

            Span<byte> outerInput = stackalloc byte[96];
            for (var i = 0; i < 64; i++)
                outerInput[i] = 0x5c;
            for (var i = 0; i < 64; i++)
                outerInput[i] ^= hmacKey[i];
            innerHash.CopyTo(outerInput[64..]);

            ComputeHash(outerInput, output);

            CryptographicOperations.ZeroMemory(hmacKey);
            CryptographicOperations.ZeroMemory(innerInput);
            CryptographicOperations.ZeroMemory(innerHash);
            CryptographicOperations.ZeroMemory(outerInput);
        }

        void ComputeHash(ReadOnlySpan<byte> payload, Span<byte> hash)
        {
            _hashDigest.BlockUpdate(payload);
            _hashDigest.DoFinal(hash);
        }
    }

    public readonly struct PNetMeshCookie
    {
        public static readonly PNetMeshCookie Empty = new PNetMeshCookie(Array.Empty<byte>(), DateTime.MinValue);

        public readonly byte[] Value;

        public readonly DateTime ValidTo;

        public bool IsValid => Value?.Length == 16 && DateTime.UtcNow < ValidTo;

        public PNetMeshCookie(byte[] value, DateTime validTo)
        {
            if (value != null && value.Length != 0 && value.Length != 16)
                throw new ArgumentException(nameof(value));

            Value = value ?? Array.Empty<byte>();
            ValidTo = validTo;
        }

        //todo generate cookie from static key rotation and endpoint address
    }

    public readonly struct PNetMeshTransportPlaintext
    {
        public PNetMeshTransportPlaintext(
            int bytesWritten,
            ulong counter,
            PNetMeshWireGuardKeypair? keypair)
        {
            BytesWritten = bytesWritten;
            Counter = counter;
            Keypair = keypair;
        }

        public int BytesWritten { get; }

        public ulong Counter { get; }

        public PNetMeshWireGuardKeypair? Keypair { get; }

        public PNetMeshWireGuardPeer? Peer => Keypair?.Peer;
    }

    public sealed class PNetMeshTransport2 : IDisposable
    {
        readonly uint _senderIndex;

        readonly uint _receiverIndex;

        readonly byte[] _writeKey;

        readonly byte[] _readKey;

        readonly PNetMeshPacketTracker _legacyReplayTracker;

        PNetMeshWireGuardKeypair? _wireGuardKeypair;

        ulong _sendCounter;

        public uint SenderIndex => _senderIndex;

        public uint ReceiverIndex => _receiverIndex;

        [Obsolete("Session-owned replay tracking should pass an explicit PNetMeshPacketTracker to read methods.")]
        public PNetMeshPacketTracker Tracker => _legacyReplayTracker;

        public PNetMeshWireGuardKeypair? WireGuardKeypair => _wireGuardKeypair;

        internal PNetMeshTransport2(uint senderIndex, uint receiverIndex, ReadOnlySpan<byte> writeKey, ReadOnlySpan<byte> readKey)
        {
            if (writeKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(writeKey));
            if (readKey.Length != PNetMeshLibSodium.ChaCha20Poly1305IetfKeySize)
                throw new ArgumentOutOfRangeException(nameof(readKey));

            _senderIndex = senderIndex;
            _receiverIndex = receiverIndex;
            _writeKey = writeKey.ToArray();
            _readKey = readKey.ToArray();
            _legacyReplayTracker = new PNetMeshPacketTracker();
        }

        internal void SetWireGuardKeypair(PNetMeshWireGuardKeypair keypair)
        {
            _wireGuardKeypair = keypair;
        }

        public void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            if (buffer.Length < CalculatePacketSize(payload.Length))
                throw new ArgumentOutOfRangeException(nameof(buffer));

            var reservation = ReserveWrite();
            counter = reservation.Counter;
            WriteMessageWithCounter(payload, counter, buffer, out bytesWritten);
            reservation.RecordWritten();
        }

        internal WriteReservation ReserveWrite()
        {
            var counter = _sendCounter;
            ThrowIfWriteKeypairExpired(counter);
            _sendCounter = counter + 1;
            return new WriteReservation(this, counter);
        }

        void WriteMessageWithCounter(ReadOnlySpan<byte> payload, ulong counter, Span<byte> buffer, out int bytesWritten)
        {
            var padding = (16 - (payload.Length % 16)) % 16;
            var paddedSize = payload.Length + padding;

            /*
                msg = packet_data {
                u8 message_type
                u8 reserved_zero[3]
                u32 receiver_index
                u64 counter
                u8 encrypted_encapsulated_packet[]
                }             
            */

            PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.PacketData);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _receiverIndex);
            //placeholder BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);            
            bytesWritten = 16;

            //zero padding to 16            
            if (padding > 0)
            {
                byte[]? rented_padded_payload = null;
                Span<byte> padded_payload = paddedSize >= 1468
                    ? (rented_padded_payload = ArrayPool<byte>.Shared.Rent(paddedSize))
                    : stackalloc byte[paddedSize];

                payload.CopyTo(padded_payload);
                padded_payload.Slice(payload.Length, padding).Clear();

                PNetMeshLibSodium.EncryptChaCha20Poly1305Ietf(
                    _writeKey,
                    counter,
                    padded_payload.Slice(0, paddedSize),
                    ReadOnlySpan<byte>.Empty,
                    buffer[16..],
                    out var encryptedBytes);
                bytesWritten += encryptedBytes;

                if (rented_padded_payload != null)
                {
                    // todo think carefully about clearing or not clearing the array.
                    ArrayPool<byte>.Shared.Return(rented_padded_payload, clearArray: true);
                }
            }
            else
            {
                PNetMeshLibSodium.EncryptChaCha20Poly1305Ietf(
                    _writeKey,
                    counter,
                    payload,
                    ReadOnlySpan<byte>.Empty,
                    buffer[16..],
                    out var encryptedBytes);
                bytesWritten += encryptedBytes;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);
        }

        public static int CalculatePacketSize(int plaintextFrameLength)
        {
            if (plaintextFrameLength < 0)
                throw new ArgumentOutOfRangeException(nameof(plaintextFrameLength));

            var padding = (16 - (plaintextFrameLength % 16)) % 16;
            return checked(PNetMeshPacketFraming.PacketDataHeaderSize + plaintextFrameLength + padding + 16);
        }

        public bool TryWriteFrame(
            ReadOnlySpan<byte> plaintextFrame,
            Span<byte> packet,
            out int bytesWritten,
            out ulong counter)
        {
            bytesWritten = CalculatePacketSize(plaintextFrame.Length);
            counter = 0;
            if (packet.Length < bytesWritten)
                return false;

            WriteMessage(plaintextFrame, packet, out bytesWritten, out counter);
            return true;
        }

        internal readonly struct WriteReservation
        {
            readonly PNetMeshTransport2 _transport;

            internal WriteReservation(PNetMeshTransport2 transport, ulong counter)
            {
                _transport = transport;
                Counter = counter;
            }

            public ulong Counter { get; }

            internal bool TryWriteFrame(
                ReadOnlySpan<byte> plaintextFrame,
                Span<byte> packet,
                out int bytesWritten)
            {
                bytesWritten = CalculatePacketSize(plaintextFrame.Length);
                if (packet.Length < bytesWritten)
                    return false;

                _transport.WriteMessageWithCounter(plaintextFrame, Counter, packet, out bytesWritten);
                return true;
            }

            internal void RecordWritten()
            {
                _transport.RecordWireGuardSent(Counter);
            }
        }

        public bool TryReadFrame(
            ReadOnlySpan<byte> packet,
            Span<byte> plaintextFrame,
            out PNetMeshTransportPlaintext plaintext)
        {
            return TryReadPlaintext(packet, plaintextFrame, out plaintext);
        }

        public bool TryReadFrame(
            ReadOnlySpan<byte> packet,
            Span<byte> plaintextFrame,
            PNetMeshPacketTracker tracker,
            out PNetMeshTransportPlaintext plaintext)
        {
            return TryReadPlaintext(packet, plaintextFrame, tracker, out plaintext);
        }

        public bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            return TryReadMessage(payload, buffer, _legacyReplayTracker, out bytesWritten, out counter);
        }

        public bool TryReadMessage(
            ReadOnlySpan<byte> payload,
            Span<byte> buffer,
            PNetMeshPacketTracker tracker,
            out int bytesWritten,
            out ulong counter)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));
            if (!TryReadRawMessage(payload, buffer, out bytesWritten, out counter))
                return false;

            if (!TryTrackReceivedCounter(tracker, counter, buffer, ref bytesWritten))
                return false;

            RecordWireGuardReceived(counter);
            return true;
        }

        public bool TryReadPlaintext(
            ReadOnlySpan<byte> payload,
            Span<byte> buffer,
            out PNetMeshTransportPlaintext plaintext)
        {
            return TryReadPlaintext(payload, buffer, _legacyReplayTracker, out plaintext);
        }

        public bool TryReadPlaintext(
            ReadOnlySpan<byte> payload,
            Span<byte> buffer,
            PNetMeshPacketTracker tracker,
            out PNetMeshTransportPlaintext plaintext)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));

            plaintext = default;
            if (!TryReadRawMessage(payload, buffer, out var bytesWritten, out var counter))
                return false;

            if (!TryTrackReceivedCounter(tracker, counter, buffer, ref bytesWritten))
                return false;

            RecordWireGuardReceived(counter);
            plaintext = new PNetMeshTransportPlaintext(bytesWritten, counter, _wireGuardKeypair);
            return true;
        }

        bool TryReadRawMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            if (payload.Length < 32)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (buffer.Length < payload.Length)
                throw new ArgumentOutOfRangeException(nameof(buffer));
            if (!PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType)
                || messageType != PNetMeshMessageType.PacketData)
                throw new InvalidOperationException("invalid message type");

            if (BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]) != _senderIndex)
            {
                bytesWritten = 0;
                counter = 0;
                return false; //maybe throw error
            }

            counter = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]);
            if (ShouldRejectReadKeypair(counter))
            {
                bytesWritten = 0;
                return false;
            }

            if (!PNetMeshLibSodium.TryDecryptChaCha20Poly1305Ietf(
                    _readKey,
                    counter,
                    payload[16..],
                    ReadOnlySpan<byte>.Empty,
                    buffer,
                    out bytesWritten))
            {
                bytesWritten = 0;
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_writeKey);
            CryptographicOperations.ZeroMemory(_readKey);
            _legacyReplayTracker.Dispose();
        }

        static bool TryTrackReceivedCounter(
            PNetMeshPacketTracker tracker,
            ulong counter,
            Span<byte> buffer,
            ref int bytesWritten)
        {
            if (tracker.TryAdd(counter))
                return true;

            buffer.Slice(0, bytesWritten).Clear();
            bytesWritten = 0;
            return false;
        }

        void RecordWireGuardSent(ulong counter)
        {
            _wireGuardKeypair?.RecordSentCounter(counter);
            _wireGuardKeypair?.RecordTransportActivity(DateTimeOffset.UtcNow);
        }

        void RecordWireGuardReceived(ulong counter)
        {
            _wireGuardKeypair?.RecordReceivedCounter(counter);
            _wireGuardKeypair?.RecordTransportActivity(DateTimeOffset.UtcNow);
        }

        void ThrowIfWriteKeypairExpired(ulong counter)
        {
            var keypair = _wireGuardKeypair;
            if (keypair == null)
                return;

            var now = DateTimeOffset.UtcNow;
            if (counter >= PNetMeshWireGuardLifecycle.RejectAfterMessages || keypair.ShouldReject(now))
                throw new InvalidOperationException("WireGuard keypair is expired.");
            if (counter >= PNetMeshWireGuardLifecycle.RekeyAfterMessages || keypair.ShouldRekey(now))
                throw new InvalidOperationException("WireGuard keypair requires rekey.");
        }

        bool ShouldRejectReadKeypair(ulong counter)
        {
            if (counter >= PNetMeshWireGuardLifecycle.RejectAfterMessages)
                return true;

            return _wireGuardKeypair?.ShouldReject(DateTimeOffset.UtcNow) == true;
        }
    }

}
