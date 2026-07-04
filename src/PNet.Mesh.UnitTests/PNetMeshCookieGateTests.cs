using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshCookieGateTests
    {
        [Fact]
        public void wireguard_cookie_secret_rotates_and_binds_cookie_to_endpoint()
        {
            using var localStatic = KeyPair.Generate();
            var protocol = new PNetMeshProtocol(
                localStatic.PrivateKey,
                localStatic.PublicKey);
            var now = DateTimeOffset.UnixEpoch;
            var endpoint1 = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 51820);
            var endpoint2 = new IPEndPoint(IPAddress.Parse("203.0.113.11"), 51820);

            var first = protocol.CookieGate.CreateCookie(endpoint1, now);
            var samePeriod = protocol.CookieGate.CreateCookie(endpoint1, now.AddSeconds(30));
            var otherEndpoint = protocol.CookieGate.CreateCookie(endpoint2, now.AddSeconds(30));
            var rotated = protocol.CookieGate.CreateCookie(endpoint1, now.Add(PNetMeshWireGuardLifecycle.CookieRefreshTime).AddTicks(1));

            Assert.Equal(16, first.Value.Length);
            Assert.Equal(first.Value, samePeriod.Value);
            Assert.NotEqual(first.Value, otherEndpoint.Value);
            Assert.NotEqual(first.Value, rotated.Value);
        }

        [Fact]
        public void wireguard_cookie_reply_round_trips_and_allows_mac2_under_load()
        {
            Span<byte> initiationBuffer = new byte[4098];
            Span<byte> cookieReply = new byte[PNetMeshPacketFraming.CookieReplyMessageSize];
            var endpoint = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 51820);
            var now = DateTimeOffset.UnixEpoch;
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiatorStatic = KeyPair.Generate();
            using var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(
                initiatorStatic.PrivateKey,
                initiatorStatic.PublicKey,
                psk);
            var responderProtocol = new PNetMeshProtocol(
                responderStatic.PrivateKey,
                responderStatic.PublicKey,
                psk);

            using var initiator = initiatorProtocol.CreateInitiator(11, responderStatic.PublicKey);
            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            var initiation = initiationBuffer.Slice(0, bytesWritten).ToArray();
            var mac1Offset = PNetMeshHandshake.WireGuardInitiationMessageSize - 32;
            var mac1 = initiation.AsSpan(mac1Offset, 16).ToArray();

            Assert.Equal(
                PNetMeshCookieGateResult.CookieRequired,
                responderProtocol.CookieGate.EvaluateHandshake(initiation, endpoint, now, requireCookie: true));

            Assert.True(responderProtocol.CookieGate.TryWriteCookieReply(initiation, endpoint, now, cookieReply, out bytesWritten));
            Assert.Equal(PNetMeshPacketFraming.CookieReplyMessageSize, bytesWritten);
            Assert.Equal((uint)PNetMeshMessageType.PacketCookieReply, BinaryPrimitives.ReadUInt32LittleEndian(cookieReply[..4]));
            Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(cookieReply[4..8]));

            Assert.True(initiatorProtocol.CookieGate.TryReadCookieReply(
                cookieReply,
                responderStatic.PublicKey,
                mac1,
                out var cookie));
            Assert.Equal(16, cookie.Value.Length);

            initiatorProtocol.Cookie = cookie;
            using var retry = initiatorProtocol.CreateInitiator(12, responderStatic.PublicKey);
            retry.WriteInitiationMessage(initiationBuffer, out bytesWritten);

            Assert.Equal(
                PNetMeshCookieGateResult.Accepted,
                responderProtocol.CookieGate.EvaluateHandshake(initiationBuffer.Slice(0, bytesWritten), endpoint, now, requireCookie: true));
        }

        [Fact]
        public void cookie_gate_rate_limits_valid_mac1_before_handshake_allocation()
        {
            Span<byte> initiationBuffer = new byte[4098];
            var endpoint = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 51820);
            var now = DateTimeOffset.UnixEpoch;
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiatorStatic = KeyPair.Generate();
            using var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(
                initiatorStatic.PrivateKey,
                initiatorStatic.PublicKey,
                psk);
            var responderProtocol = new PNetMeshProtocol(
                responderStatic.PrivateKey,
                responderStatic.PublicKey,
                psk);

            responderProtocol.CookieGate.RateLimitPacketCount = 1;
            responderProtocol.CookieGate.RateLimitWindow = TimeSpan.FromSeconds(10);

            using var initiator = initiatorProtocol.CreateInitiator(11, responderStatic.PublicKey);
            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            var initiation = initiationBuffer.Slice(0, bytesWritten).ToArray();

            Assert.Equal(
                PNetMeshCookieGateResult.Accepted,
                responderProtocol.CookieGate.EvaluateHandshake(initiation, endpoint, now, requireCookie: false));
            Assert.Equal(
                PNetMeshCookieGateResult.RateLimited,
                responderProtocol.CookieGate.EvaluateHandshake(initiation, endpoint, now.AddSeconds(1), requireCookie: false));
            Assert.Equal(
                PNetMeshCookieGateResult.Accepted,
                responderProtocol.CookieGate.EvaluateHandshake(initiation, endpoint, now.AddSeconds(11), requireCookie: false));
        }
    }
}
