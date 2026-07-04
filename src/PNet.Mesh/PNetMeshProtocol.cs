using Noise;
using Org.BouncyCastle.Crypto.Digests;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using CryptographicException = System.Security.Cryptography.CryptographicException;

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

        readonly byte[][]? _psk;

        readonly byte[] _mac1KeyBytes;

        PNetMeshCookie _cookie = PNetMeshCookie.Empty;

        public string ProtocolName => _profile.ProtocolName;

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
            _localStaticPubKey = localStaticPubKey ?? throw new ArgumentNullException(nameof(localStaticPrivKey));
            _psk = psk != null ? new byte[][] { psk } : null;

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
                    if (!payload[^32..^16].SequenceEqual(mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    ComputeMac(_cookie.Value, payload[0..^16], mac);
                    return payload[^16..].SequenceEqual(mac);
                case PNetMeshMessageType.HandshakeResponse
                    when payload.Length == PNetMeshHandshake.ResponseMessageSize:

                    ComputeMac(_mac1KeyBytes, payload[0..^32], mac);
                    if (!payload[^32..^16].SequenceEqual(mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    ComputeMac(_cookie.Value, payload[0..^16], mac);
                    return payload[^16..].SequenceEqual(mac);
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
            return payload[mac1Offset..mac2Offset].SequenceEqual(mac);
        }

        internal bool TryValidatePacketMac2(ReadOnlySpan<byte> payload, PNetMeshCookie cookie)
        {
            if (!cookie.IsValid || !TryGetHandshakeMacOffsets(payload, out _, out var mac2Offset))
                return false;

            Span<byte> mac = stackalloc byte[16];
            ComputeMac(cookie.Value, payload[..mac2Offset], mac);
            return payload[mac2Offset..(mac2Offset + 16)].SequenceEqual(mac);
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
            var handshake = _profile.Protocol.Create(initiator, _profile.Prologue, _localStaticPrivKey, remoteStaticPubKey!, _psk);

            return new PNetMeshHandshake(this, senderIndex, _localStaticPubKey, handshake);
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

        void ComputeHash(ReadOnlySpan<byte> payload, Span<byte> hash)
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

        void ComputeMac(byte[] key, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            var digest = new Blake2sDigest(key, mac.Length, null, null);
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
            Prologue = Encoding.UTF8.GetBytes(prologue);
            Protocol = Protocol.Parse(protocolName);
            HandshakeInitiationMessageSize = handshakeInitiationMessageSize;
            HandshakeInitiationTimestampSize = handshakeInitiationTimestampSize;
        }

        public string ProtocolName { get; }

        public byte[] Prologue { get; }

        public Protocol Protocol { get; }

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
        readonly static TimeSpan CookieTimeout = TimeSpan.FromMinutes(2);

        //public const int MinBufferSize = 116;

        public const int WireGuardInitiationMessageSize = 148;

        public const int InitiationMessageSize = WireGuardInitiationMessageSize;

        public const int ResponseMessageSize = 92;

        readonly PNetMeshProtocol _protocol;

        readonly uint _senderIndex;

        readonly byte[] _localStaticPubKey;

        readonly HandshakeState _handshake;

        public byte[] LocalPublicKey => _localStaticPubKey;

        public byte[] RemotePublicKey { get; private set; } = Array.Empty<byte>();

        public long Timestamp { get; private set; }

        public uint SenderIndex => _senderIndex;

        uint _receiverIndex;

        public PNetMeshHandshake(PNetMeshProtocol protocol, uint senderIndex, byte[] localStaticPubKey, HandshakeState handshake)
        {
            _protocol = protocol;
            _senderIndex = senderIndex;
            _localStaticPubKey = localStaticPubKey;
            _handshake = handshake;
        }

        public void WriteInitiationMessage(Span<byte> buffer, out int bytesWritten)
        {
            var initiationMessageSize = _protocol.HandshakeInitiationMessageSize;
            if (buffer.Length < initiationMessageSize) throw new ArgumentOutOfRangeException(nameof(buffer));

            /* 
           msg = handshake_initiation {
           u8 message_type
           u8 reserved_zero[3]
           u32 sender_index
           u8 unencrypted_ephemeral[32]
           u8 encrypted_static[AEAD_LEN(32)]
           u8 encrypted_timestamp[AEAD_LEN(8)]
           u8 mac1[16]
           u8 mac2[16]
           }
            */

            PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeInitiation);

            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);

            //cipher and payload
            Span<byte> temp = stackalloc byte[_protocol.HandshakeInitiationTimestampSize];

            _protocol.WriteInitiationTimestamp(temp);

            var mac1Offset = _protocol.HandshakeInitiationMac1Offset;
            var mac2Offset = _protocol.HandshakeInitiationMac2Offset;
            bytesWritten = _handshake.WriteMessage(temp, buffer[8..mac1Offset]).BytesWritten;
            Debug.Assert(bytesWritten == mac1Offset - 8);

            _protocol.GetPacketMac(_handshake.RemoteStaticPublicKey, buffer[0..mac1Offset], buffer[mac1Offset..mac2Offset]);
            _protocol.GetCookieMac(buffer[0..mac2Offset], buffer[mac2Offset..initiationMessageSize]); //todo set cookie

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

            //timestamp
            Span<byte> temp = stackalloc byte[_protocol.HandshakeInitiationTimestampSize];
            int c;
            try
            {
                (c, _, _) = _handshake.ReadMessage(payload[8.._protocol.HandshakeInitiationMac1Offset], temp);
            }
            catch (CryptographicException)
            {
                return false;
            }

            if (c != _protocol.HandshakeInitiationTimestampSize)
                return false;

            RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();
            _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
            Timestamp = _protocol.ReadInitiationTimestamp(temp);

            return _protocol.TryAddHandshakeInitiationTimestamp(RemotePublicKey, temp);
        }

        public bool TryWriteResponseMessage(Span<byte> buffer, out int bytesWritten, [NotNullWhen(true)] out PNetMeshTransport2? transport)
        {
            if (buffer.Length < ResponseMessageSize)
                throw new ArgumentOutOfRangeException(nameof(buffer));
            if (_receiverIndex == 0)
                throw new InvalidOperationException("invalid receiver index");

            bytesWritten = 0;
            transport = null;

            /*
            msg = handshake_response {
            u8 message_type
            u8 reserved_zero[3]
            u32 sender_index
            u32 receiver_index
            u8 unencrypted_ephemeral[32]
            u8 encrypted_nothing[AEAD_LEN(0)]
            u8 mac1[16]
            u8 mac2[16]
            }
            */

            PNetMeshPacketFraming.WriteMessageType(buffer, PNetMeshMessageType.HandshakeResponse);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..12], _receiverIndex);

            var (c, _, t) = _handshake.WriteMessage(default, buffer[12..60]);
            Debug.Assert(c == 48);

            if (t == null)
                return false;

            //mac1 = MAC(HASH(LABEL_MAC1 || initiator.static_public), msg[0:offsetof(msg.mac1)])
            _protocol.GetPacketMac(_handshake.RemoteStaticPublicKey, buffer[0..60], buffer[60..76]);
            _protocol.GetCookieMac(buffer[0..76], buffer[76..92]);

            transport = new PNetMeshTransport2(_senderIndex, _receiverIndex, t);
            bytesWritten = ResponseMessageSize;

            RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();
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

            /*
            msg = handshake_response {
            u8 message_type
            u8 reserved_zero[3]
            u32 sender_index
            u32 receiver_index
            u8 unencrypted_ephemeral[32]
            u8 encrypted_nothing[AEAD_LEN(0)]
            u8 mac1[16]
            u8 mac2[16]
            }
            */

            transport = null;

            //swaped
            _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
            var senderIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);

            if (senderIndex != _senderIndex)
                return false;

            //empty payload response
            Transport t;
            try
            {
                (_, _, t) = _handshake.ReadMessage(payload[12..60], Span<byte>.Empty);
            }
            catch (CryptographicException)
            {
                return false;
            }

            if (t == null)
                return false;

            transport = new PNetMeshTransport2(_senderIndex, _receiverIndex, t);

            RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();
            _protocol.RegisterWireGuardKeypair(RemotePublicKey, transport, PNetMeshWireGuardKeypairRole.Initiator);

            return true;
        }

        public void Dispose()
        {
            _handshake.Dispose();
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
        static readonly ConcurrentDictionary<Type, TransportNonceLayout> NonceLayouts = new ConcurrentDictionary<Type, TransportNonceLayout>();

        static readonly ConcurrentDictionary<Type, MethodInfo> SetNonceMethods = new ConcurrentDictionary<Type, MethodInfo>();

        readonly Transport _transport;

        readonly PNetMeshPacketTracker _tracker;

        readonly uint _senderIndex;

        readonly uint _receiverIndex;

        readonly Action<ulong> _setWriteNonce;

        readonly Action<ulong> _setReadNonce;

        PNetMeshWireGuardKeypair? _wireGuardKeypair;

        ulong _sendCounter;

        public uint SenderIndex => _senderIndex;

        public uint ReceiverIndex => _receiverIndex;

        public PNetMeshPacketTracker Tracker => _tracker;

        public PNetMeshWireGuardKeypair? WireGuardKeypair => _wireGuardKeypair;

        public PNetMeshTransport2(uint senderIndex, uint receiverIndex, Transport transport)
        {
            _senderIndex = senderIndex;
            _receiverIndex = receiverIndex;
            _transport = transport;
            _tracker = new PNetMeshPacketTracker();
            (_setWriteNonce, _setReadNonce) = CreateNonceSetters(transport);
        }

        internal void SetWireGuardKeypair(PNetMeshWireGuardKeypair keypair)
        {
            _wireGuardKeypair = keypair;
        }

        public void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            var padding = (16 - (payload.Length % 16)) % 16;
            var paddedSize = payload.Length + padding;

            if (buffer.Length < (8 + 8 + 16 + paddedSize))
                throw new ArgumentOutOfRangeException(nameof(buffer));

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

                counter = _sendCounter;
                _setWriteNonce(counter);
                bytesWritten += _transport.WriteMessage(padded_payload.Slice(0, paddedSize), buffer[16..]);
                RecordWireGuardSent(counter);
                _sendCounter++;

                if (rented_padded_payload != null)
                {
                    // todo think carefully about clearing or not clearing the array.
                    ArrayPool<byte>.Shared.Return(rented_padded_payload, clearArray: true);
                }
            }
            else
            {
                counter = _sendCounter;
                _setWriteNonce(counter);
                bytesWritten += _transport.WriteMessage(payload, buffer[16..]);
                RecordWireGuardSent(counter);
                _sendCounter++;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);
        }

        public bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            return TryReadRawMessage(payload, buffer, out bytesWritten, out counter);
        }

        public bool TryReadPlaintext(
            ReadOnlySpan<byte> payload,
            Span<byte> buffer,
            out PNetMeshTransportPlaintext plaintext)
        {
            plaintext = default;
            if (!TryReadRawMessage(payload, buffer, out var bytesWritten, out var counter))
                return false;

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
                //duplicate message
                buffer.Slice(0, bytesWritten).Clear();
                bytesWritten = 0;
                return false;
            }

            RecordWireGuardReceived(counter);

            return true;
        }

        public void Dispose()
        {
            _transport.Dispose();
            _tracker.Dispose();
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

        static (Action<ulong> write, Action<ulong> read) CreateNonceSetters(Transport transport)
        {
            var layout = NonceLayouts.GetOrAdd(transport.GetType(), CreateNonceLayout);
            var initiator = (bool)(layout.Initiator.GetValue(transport)
                ?? throw new NotSupportedException("Noise transport field 'initiator' was null."));
            var c1 = layout.C1.GetValue(transport)
                ?? throw new NotSupportedException("Noise transport field 'c1' was null.");
            var c2 = layout.C2.GetValue(transport)
                ?? throw new NotSupportedException("Noise transport field 'c2' was null.");

            var writeState = initiator ? c1 : c2;
            var readState = initiator ? c2 : c1;

            return (CreateNonceSetter(writeState), CreateNonceSetter(readState));
        }

        static TransportNonceLayout CreateNonceLayout(Type transportType)
        {
            return new TransportNonceLayout(
                GetRequiredField(transportType, "initiator"),
                GetRequiredField(transportType, "c1"),
                GetRequiredField(transportType, "c2"));
        }

        static FieldInfo GetRequiredField(Type type, string name)
            => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new NotSupportedException($"Noise transport field '{name}' was not found.");

        static Action<ulong> CreateNonceSetter(object cipherState)
        {
            var setNonce = SetNonceMethods.GetOrAdd(cipherState.GetType(), GetRequiredSetNonceMethod);
            return (Action<ulong>)setNonce.CreateDelegate(typeof(Action<ulong>), cipherState);
        }

        static MethodInfo GetRequiredSetNonceMethod(Type cipherStateType)
            => cipherStateType.GetMethod("SetNonce", new[] { typeof(ulong) })
               ?? throw new NotSupportedException("Noise cipher state does not expose SetNonce(ulong).");

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
