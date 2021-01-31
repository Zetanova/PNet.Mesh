using Noise;
using NSec.Cryptography;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace PNet.Actor.Mesh
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

        readonly static string Mac2Label = "cookie--"; //8 bytes

        //"Noise_IKpsk2_25519_ChaChaPoly_BLAKE2b"
        //static readonly Protocol Protocol = new Protocol(
        //  HandshakePattern.IK,
        //  CipherFunction.ChaChaPoly,
        //  HashFunction.Blake2b,
        //  PatternModifiers.Psk2
        //);
        static readonly Protocol Protocol = Protocol.Parse("Noise_IKpsk2_25519_ChaChaPoly_BLAKE2b");

        //todo ActorSystem name
        static readonly byte[] Identifier = Encoding.UTF8.GetBytes("PNet.Actor.Mesh v1 akka");

        readonly byte[] _localStaticPrivKey;
        readonly byte[] _localStaticPubKey;

        readonly byte[][] _psk;

        readonly Key _mac1Key; //packet mac

        Key _mac2Key; //cookie mac

        PNetMeshCookie _cookie = PNetMeshCookie.Empty;
        public PNetMeshCookie Cookie
        {
            get => _cookie;
            set
            {
                //todo cookie random every 2min
                //cookie key is with remote endpoint hashed
                //proof of EP ownership

                _cookie = value;
                _mac2Key?.Dispose();

                if (_cookie.IsValid)
                    _mac2Key = Key.Import(MacAlgorithm.Blake2b_128, _cookie.Value, KeyBlobFormat.RawSymmetricKey);
            }
        }

        public PNetMeshProtocol(byte[] localStaticPrivKey, byte[] localStaticPubKey, byte[] psk = null)
        {
            _localStaticPrivKey = localStaticPrivKey ?? throw new ArgumentNullException(nameof(localStaticPrivKey));
            _localStaticPubKey = localStaticPubKey ?? throw new ArgumentNullException(nameof(localStaticPrivKey));
            _psk = psk != null ? new byte[][] { psk } : null;

            //init mac1 hash
            //mac1 = MAC(HASH(LABEL_MAC1 || responder.static_public), msg[0:offsetof(msg.mac1)])
            Span<byte> temp = stackalloc byte[72];
            Encoding.UTF8.GetBytes(Mac1Label, temp[0..8]);
            _localStaticPubKey.CopyTo(temp[8..40]);
            HashAlgorithm.Blake2b_256.Hash(temp[0..40], temp[40..72]);

            _mac1Key = Key.Import(MacAlgorithm.Blake2b_128, temp[40..72], KeyBlobFormat.RawSymmetricKey);
        }

        public void GetPacketMac(ReadOnlySpan<byte> remotePublicKey, ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            if (payload.Length < 32) throw new ArgumentOutOfRangeException(nameof(payload));
            if (mac.Length != 16) throw new ArgumentOutOfRangeException(nameof(mac));

            //maybe store key in handshake
            Span<byte> temp = stackalloc byte[72];
            Encoding.UTF8.GetBytes(Mac1Label, temp[0..8]);
            remotePublicKey.CopyTo(temp[8..40]);
            HashAlgorithm.Blake2b_256.Hash(temp[0..40], temp[40..72]);
            using var key = Key.Import(MacAlgorithm.Blake2b_128, temp[40..72], KeyBlobFormat.RawSymmetricKey);

            MacAlgorithm.Blake2b_128.Mac(key, payload, mac);
        }

        public bool ValidatePacket(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 32)
                return false;

            Span<byte> mac = stackalloc byte[16];

            //mac1 = MAC(HASH(LABEL_MAC1 || responder.static_public), msg[0:offsetof(msg.mac1)])

            switch ((PNetMeshMessageType)payload[0])
            {
                case PNetMeshMessageType.PacketData:
                    return true;
                case PNetMeshMessageType.HandshakeInitiation
                    when payload.Length == PNetMeshHandshake.InitiationMessageSize:

                    MacAlgorithm.Blake2b_128.Mac(_mac1Key, payload[0..^32], mac);
                    if (!payload[^32..^16].SequenceEqual(mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    MacAlgorithm.Blake2b_128.Mac(_mac2Key, payload[0..^16], mac);
                    return payload[^16..].SequenceEqual(mac);
                case PNetMeshMessageType.HandshakeResponse
                    when payload.Length == PNetMeshHandshake.ResponseMessageSize:

                    MacAlgorithm.Blake2b_128.Mac(_mac1Key, payload[0..^32], mac);
                    if (!payload[^32..^16].SequenceEqual(mac))
                        return false;
                    if (!_cookie.IsValid)
                        return true;
                    MacAlgorithm.Blake2b_128.Mac(_mac2Key, payload[0..^16], mac);
                    return payload[^16..].SequenceEqual(mac);
                case PNetMeshMessageType.PacketCookieReply:
                    //todo
                    return true;
                default:
                    //ignore unknown or invalid
                    return false;
            }
        }

        public void GetCookieMac(ReadOnlySpan<byte> payload, Span<byte> mac)
        {
            if (payload.Length < 32) throw new ArgumentOutOfRangeException(nameof(payload));
            if (mac.Length != 16) throw new ArgumentOutOfRangeException(nameof(mac));

            //mac2 = MAC(responder.last_received_cookie, msg[0:offsetof(msg.mac2)])
            if (_cookie.IsValid)
                MacAlgorithm.Blake2b_128.Mac(_mac2Key, payload, mac);
            else
                mac.Clear();
        }

        public uint GetReceiverIndex(ReadOnlySpan<byte> payload)
        {
            switch ((PNetMeshMessageType)payload[0])
            {
                case PNetMeshMessageType.PacketData:
                    return BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
                case PNetMeshMessageType.HandshakeResponse:
                    return BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);
                default:
                    throw new InvalidOperationException("invalid message type");
            }
        }

        public PNetMeshHandshake Create(uint senderIndex, bool initiator, byte[] remoteStaticPubKey)
        {
            var handshake = Protocol.Create(initiator, Identifier, _localStaticPrivKey, remoteStaticPubKey, _psk);

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

        public const int InitiationMessageSize = 144;

        public const int ResponseMessageSize = 92;

        readonly PNetMeshProtocol _protocol;

        readonly uint _senderIndex;

        readonly byte[] _localStaticPubKey;

        readonly HandshakeState _handshake;

        public byte[] LocalPublicKey => _localStaticPubKey;

        public byte[] RemotePublicKey { get; private set; }

        public long Timestamp { get; private set; }

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
            if (buffer.Length < InitiationMessageSize) throw new ArgumentOutOfRangeException(nameof(buffer));

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

            buffer[0] = (byte)PNetMeshMessageType.HandshakeInitiation;
            buffer[1..4].Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _senderIndex);

            //cipher and payload
            Span<byte> temp = stackalloc byte[8];

            //maybe use Stopwatch.GetTimestamp()
            BinaryPrimitives.WriteUInt64LittleEndian(temp, PNetMeshUtils.GetTimestamp());

            bytesWritten = _handshake.WriteMessage(temp, buffer[8..112]).BytesWritten;
            Debug.Assert(bytesWritten == 104);

            _protocol.GetPacketMac(_handshake.RemoteStaticPublicKey, buffer[0..112], buffer[112..128]);
            _protocol.GetCookieMac(buffer[0..128], buffer[128..144]); //todo set cookie

            bytesWritten = InitiationMessageSize;
        }

        public bool TryReadInitiationMessage(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < InitiationMessageSize)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (payload[0] != (byte)PNetMeshMessageType.HandshakeInitiation)
                throw new InvalidOperationException("invalid message type");

            //timestamp
            Span<byte> temp = stackalloc byte[8];
            var (c, _, _) = _handshake.ReadMessage(payload[8..112], temp);
            if (c != 8)
                return false;

            RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();
            _receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
            Timestamp = BinaryPrimitives.ReadInt64LittleEndian(temp[0..8]);

            return true;
        }

        public bool TryWriteResponseMessage(Span<byte> buffer, out int bytesWritten, out PNetMeshTransport2 transport)
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

            buffer[0] = (byte)PNetMeshMessageType.HandshakeResponse;
            buffer[1..4].Clear();
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

            return true;
        }

        public bool TryReadResponseMessage(ReadOnlySpan<byte> payload, out PNetMeshTransport2 transport)
        {
            if (payload.Length < ResponseMessageSize)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (payload[0] != (byte)PNetMeshMessageType.HandshakeResponse)
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
            var (_, _, t) = _handshake.ReadMessage(payload[12..60], Span<byte>.Empty);
            if (t == null)
                return false;

            transport = new PNetMeshTransport2(_senderIndex, _receiverIndex, t);

            RemotePublicKey = _handshake.RemoteStaticPublicKey.ToArray();

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

        public bool IsValid => Value?.Length == 32 && DateTime.UtcNow < ValidTo;

        public PNetMeshCookie(byte[] value, DateTime validTo)
        {
            if (value != null && value.Length != 0 && value.Length != 32)
                throw new ArgumentException(nameof(value));

            Value = value ?? Array.Empty<byte>();
            ValidTo = validTo;
        }

        //todo generate cookie from static key rotation and endpoint address
    }

    public sealed class PNetMeshTransport2 : IDisposable
    {
        readonly Transport _transport;

        readonly PNetMeshPacketTracker _tracker;

        readonly uint _senderIndex;

        readonly uint _receiverIndex;

        public uint SenderIndex => _senderIndex;

        public uint ReceiverIndex => _receiverIndex;

        public PNetMeshPacketTracker Tracker => _tracker;

        public PNetMeshTransport2(uint senderIndex, uint receiverIndex, Transport transport)
        {
            _senderIndex = senderIndex;
            _receiverIndex = receiverIndex;
            _transport = transport;
            _tracker = new PNetMeshPacketTracker();
        }

        public void WriteMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            var padding = 16 - (payload.Length % 16);
            var paddedSize = payload.Length + padding;
            if (padding == 0 && !payload.IsEmpty && payload[^1] < 16)
            {
                //require padding length encoding
                padding = 16;
                paddedSize += 16;
            }

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

            buffer[0] = (byte)PNetMeshMessageType.PacketData;
            buffer[1..3].Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..8], _receiverIndex);
            //placeholder BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);            
            bytesWritten = 16;

            //zero padding to 16            
            if (padding > 0)
            {
                byte[] rented_padded_payload = null;
                Span<byte> padded_payload = paddedSize >= 1468
                    ? (rented_padded_payload = ArrayPool<byte>.Shared.Rent(paddedSize))
                    : stackalloc byte[paddedSize];

                payload.CopyTo(padded_payload);
                padded_payload.Slice(payload.Length, padding - 1).Clear();
                padded_payload[paddedSize - 1] = (byte)(padding - 1); //padding length

                bytesWritten += _transport.WriteMessage(padded_payload.Slice(0, paddedSize), buffer[16..], out counter);

                if (rented_padded_payload != null)
                {
                    // todo think carefully about clearing or not clearing the array.
                    ArrayPool<byte>.Shared.Return(rented_padded_payload, clearArray: true);
                }
            }
            else
            {
                bytesWritten += _transport.WriteMessage(payload, buffer[16..], out counter);
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..16], counter);
        }

        public bool TryReadMessage(ReadOnlySpan<byte> payload, Span<byte> buffer, out int bytesWritten, out ulong counter)
        {
            if (payload.Length < 32)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (buffer.Length < payload.Length)
                throw new ArgumentOutOfRangeException(nameof(buffer));
            if (payload[0] != (byte)PNetMeshMessageType.PacketData)
                throw new InvalidOperationException("invalid message type");

            if (BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]) != _senderIndex)
            {
                bytesWritten = 0;
                counter = 0;
                return false; //maybe throw error
            }

            counter = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]);

            if (!_tracker.TryAdd(counter))
            {
                //duplicate message
                bytesWritten = 0;
                return false;
            }

            bytesWritten = _transport.ReadMessage(counter, payload[16..], buffer);

            if (bytesWritten > 1)
            {
                //padding length
                int padding = buffer[bytesWritten - 1];

                if (padding < 16)
                {
                    bytesWritten -= padding + 1;
                }
            }

            return true;
        }

        public void Dispose()
        {
            _transport.Dispose();
            _tracker.Dispose();
        }
    }

}
