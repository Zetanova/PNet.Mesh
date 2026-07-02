using NSec.Cryptography;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PNet.Mesh
{
    public enum PNetMeshCookieGateResult
    {
        Invalid = 0,
        Accepted = 1,
        CookieRequired = 2,
        RateLimited = 3
    }

    public sealed class PNetMeshCookieGate
    {
        const int WireGuardCookieSize = 16;
        const string CookieLabel = "cookie--";

        readonly PNetMeshProtocol _protocol;
        readonly Dictionary<string, Queue<DateTimeOffset>> _rateLimits = new Dictionary<string, Queue<DateTimeOffset>>();

        byte[] _secret;
        DateTimeOffset _secretCreatedAt;

        public PNetMeshCookieGate(PNetMeshProtocol protocol)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        }

        public int RateLimitPacketCount { get; set; } = 50;

        public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromSeconds(1);

        public PNetMeshCookie CreateCookie(EndPoint remoteEndPoint, DateTimeOffset now)
        {
            if (remoteEndPoint == null) throw new ArgumentNullException(nameof(remoteEndPoint));

            var endpoint = GetEndpointBytes(remoteEndPoint);
            var cookie = new byte[WireGuardCookieSize];
            _protocol.ComputeKeyedMac(GetSecret(now), endpoint, cookie);
            return new PNetMeshCookie(cookie, DateTime.UtcNow.Add(PNetMeshWireGuardLifecycle.CookieRefreshTime));
        }

        public PNetMeshCookieGateResult EvaluateHandshake(
            ReadOnlySpan<byte> payload,
            EndPoint remoteEndPoint,
            DateTimeOffset now,
            bool requireCookie)
        {
            if (!_protocol.TryGetHandshakeMacOffsets(payload, out _, out _)
                || !_protocol.TryValidatePacketMac1(payload))
                return PNetMeshCookieGateResult.Invalid;

            if (!TryConsumeRateLimit(remoteEndPoint, now))
                return PNetMeshCookieGateResult.RateLimited;

            if (requireCookie && !_protocol.TryValidatePacketMac2(payload, CreateCookie(remoteEndPoint, now)))
                return PNetMeshCookieGateResult.CookieRequired;

            return PNetMeshCookieGateResult.Accepted;
        }

        public bool TryWriteCookieReply(
            ReadOnlySpan<byte> triggeringPacket,
            EndPoint remoteEndPoint,
            DateTimeOffset now,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < PNetMeshPacketFraming.CookieReplyMessageSize)
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (!_protocol.TryGetHandshakeMacOffsets(triggeringPacket, out var mac1Offset, out _)
                || !_protocol.TryValidatePacketMac1(triggeringPacket))
                return false;

            var cookie = CreateCookie(remoteEndPoint, now);
            var aead = AeadAlgorithm.XChaCha20Poly1305;
            using var key = Key.Import(aead, CreateCookieReplyKey(_protocol.LocalStaticPublicKey), KeyBlobFormat.RawSymmetricKey);

            PNetMeshPacketFraming.WriteMessageType(destination, PNetMeshMessageType.PacketCookieReply);
            triggeringPacket[4..8].CopyTo(destination[4..8]);
            RandomNumberGenerator.Fill(destination[8..32]);
            aead.Encrypt(
                key,
                destination[8..32],
                triggeringPacket.Slice(mac1Offset, 16),
                cookie.Value,
                destination[32..64]);
            bytesWritten = PNetMeshPacketFraming.CookieReplyMessageSize;
            return true;
        }

        public bool TryReadCookieReply(
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> responderStaticPublicKey,
            ReadOnlySpan<byte> lastSentMac1,
            out PNetMeshCookie cookie)
        {
            cookie = PNetMeshCookie.Empty;
            if (payload.Length != PNetMeshPacketFraming.CookieReplyMessageSize
                || responderStaticPublicKey.Length != 32
                || lastSentMac1.Length != 16
                || !PNetMeshPacketFraming.TryReadMessageType(payload, out var messageType)
                || messageType != PNetMeshMessageType.PacketCookieReply)
                return false;

            Span<byte> cookieValue = stackalloc byte[WireGuardCookieSize];
            var aead = AeadAlgorithm.XChaCha20Poly1305;
            using var key = Key.Import(aead, CreateCookieReplyKey(responderStaticPublicKey), KeyBlobFormat.RawSymmetricKey);
            if (!aead.Decrypt(key, payload[8..32], lastSentMac1, payload[32..64], cookieValue))
                return false;

            cookie = new PNetMeshCookie(cookieValue.ToArray(), DateTime.UtcNow.Add(PNetMeshWireGuardLifecycle.CookieRefreshTime));
            return true;
        }

        byte[] GetSecret(DateTimeOffset now)
        {
            if (_secret == null || now - _secretCreatedAt >= PNetMeshWireGuardLifecycle.CookieRefreshTime)
            {
                _secret = new byte[32];
                RandomNumberGenerator.Fill(_secret);
                _secretCreatedAt = now;
            }

            return _secret;
        }

        bool TryConsumeRateLimit(EndPoint remoteEndPoint, DateTimeOffset now)
        {
            if (RateLimitPacketCount <= 0)
                return true;

            var key = GetEndpointKey(remoteEndPoint);
            if (!_rateLimits.TryGetValue(key, out var seen))
            {
                seen = new Queue<DateTimeOffset>();
                _rateLimits.Add(key, seen);
            }

            while (seen.Count > 0 && now - seen.Peek() >= RateLimitWindow)
                seen.Dequeue();

            if (seen.Count >= RateLimitPacketCount)
                return false;

            seen.Enqueue(now);
            return true;
        }

        byte[] CreateCookieReplyKey(ReadOnlySpan<byte> staticPublicKey)
        {
            Span<byte> input = stackalloc byte[40];
            Encoding.UTF8.GetBytes(CookieLabel, input[..8]);
            staticPublicKey.CopyTo(input[8..]);

            var key = new byte[32];
            _protocol.ComputeHash(input, key);
            return key;
        }

        static byte[] GetEndpointBytes(EndPoint remoteEndPoint)
        {
            switch (remoteEndPoint)
            {
                case IPEndPoint ip:
                    var address = ip.Address.GetAddressBytes();
                    var value = new byte[address.Length + sizeof(ushort)];
                    address.CopyTo(value, 0);
                    BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(address.Length), (ushort)ip.Port);
                    return value;
                case DnsEndPoint dns:
                    return Encoding.UTF8.GetBytes($"{dns.Host.ToLowerInvariant()}:{dns.Port}");
                default:
                    return Encoding.UTF8.GetBytes(remoteEndPoint.ToString());
            }
        }

        static string GetEndpointKey(EndPoint remoteEndPoint)
        {
            return Convert.ToHexString(GetEndpointBytes(remoteEndPoint));
        }
    }
}
