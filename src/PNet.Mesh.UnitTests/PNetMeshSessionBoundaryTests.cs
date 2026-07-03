using Google.Protobuf;
using Noise;
using PNet.Mesh;
using System;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshSessionBoundaryTests
    {
        [Fact]
        public void secure_frame_session_round_trips_raw_plaintext_without_protobuf_packet()
        {
            using var transports = CreateEstablishedTransports();
            var sender = new PNetMeshSecureFrameSession(transports.Initiator);
            var receiver = new PNetMeshSecureFrameSession(transports.Responder);
            var frame = new byte[] { 0x45, 0x00, 0x01, 0x02 };
            var encrypted = new byte[PNetMeshSecureFrameSession.CalculatePacketSize(frame.Length)];
            var plaintext = new byte[encrypted.Length];

            Assert.True(sender.TryWriteFrame(frame, encrypted, out var encryptedBytes, out var writtenCounter));
            Assert.True(receiver.TryReadFrame(
                encrypted.AsSpan(0, encryptedBytes),
                plaintext,
                out var decrypted));

            Assert.Equal(0ul, writtenCounter);
            Assert.Equal(writtenCounter, decrypted.Counter);
            Assert.Equal(16, decrypted.BytesWritten);
            Assert.Equal(frame, plaintext.AsSpan(0, frame.Length).ToArray());
            Assert.All(plaintext.AsSpan(frame.Length, decrypted.BytesWritten - frame.Length).ToArray(), value => Assert.Equal((byte)0, value));
        }

        [Fact]
        public void frame_dispatcher_routes_by_first_byte_before_pnet_control_handling()
        {
            var pnet = new RecordingFrameHandler();
            var ipv4 = new RecordingFrameHandler();
            var ipv6 = new RecordingFrameHandler();
            var dispatcher = new PNetMeshFrameDispatcher(
                pnetFrameHandler: pnet,
                ipv4FrameHandler: ipv4,
                ipv6FrameHandler: ipv6);
            var pnetFrame = PNetMeshPayloadFraming.CreatePNet(new byte[] { 0x01, 0x02 });
            var ipv4Frame = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                ReadOnlySpan<byte>.Empty,
                protocol: 17);
            var ipv6Frame = PNetMeshIpPacket.CreateIPv6(
                IPAddress.Parse("2001:db8::1"),
                IPAddress.Parse("2001:db8::2"),
                ReadOnlySpan<byte>.Empty,
                nextHeader: 59);

            Assert.True(dispatcher.TryDispatch(ipv4Frame, 7, out _, out var error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(0, pnet.Calls);
            Assert.Equal(1, ipv4.Calls);
            Assert.Equal(0, ipv6.Calls);

            Assert.True(dispatcher.TryDispatch(ipv6Frame, 8, out _, out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(0, pnet.Calls);
            Assert.Equal(1, ipv4.Calls);
            Assert.Equal(1, ipv6.Calls);

            Assert.True(dispatcher.TryDispatch(pnetFrame, 9, out _, out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(1, pnet.Calls);
            Assert.Equal(1, ipv4.Calls);
            Assert.Equal(1, ipv6.Calls);
            Assert.Equal(9ul, pnet.LastCounter);
        }

        [Fact]
        public void reliable_control_session_parses_only_pnet_frames()
        {
            var handler = new RecordingControlPacketHandler();
            var controlSession = new PNetMeshReliableControlSession(handler);
            var packet = new PNet.Actor.Mesh.Protos.Packet();
            packet.Payload.Add(new PNet.Actor.Mesh.Protos.Payload
            {
                Raw = ByteString.CopyFromUtf8("control")
            });
            var pnetFrame = PNetMeshPayloadFraming.CreatePNet(packet.ToByteArray());
            var ipv4Frame = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                ReadOnlySpan<byte>.Empty,
                protocol: 17);

            Assert.False(controlSession.TryHandleFrame(ipv4Frame, 41, out _, out var error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(0, handler.Calls);

            Assert.True(controlSession.TryHandleFrame(pnetFrame, 42, out var payloadReceived, out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.True(payloadReceived);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(42ul, handler.LastCounter);
            var payload = Assert.Single(handler.LastPacket!.Payload);
            Assert.Equal("control", payload.Raw.ToStringUtf8());
        }

        static EstablishedTransportPair CreateEstablishedTransports()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var initiatorStatic = KeyPair.Generate();
            var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(initiatorStatic.PrivateKey, initiatorStatic.PublicKey, psk);
            var responderProtocol = new PNetMeshProtocol(responderStatic.PrivateKey, responderStatic.PublicKey, psk);

            var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
            var responder = responderProtocol.CreateResponder(2);

            Span<byte> initiationBuffer = new byte[4098];
            Span<byte> responseBuffer = new byte[4098];

            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(initiationBuffer.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport));
            Assert.True(initiator.TryReadResponseMessage(responseBuffer.Slice(0, bytesWritten), out var initiatorTransport));

            return new EstablishedTransportPair(
                initiatorTransport,
                responderTransport,
                initiator,
                responder,
                initiatorStatic,
                responderStatic);
        }

        sealed class RecordingFrameHandler : IPNetMeshFrameHandler
        {
            public int Calls { get; private set; }

            public ulong LastCounter { get; private set; }

            public bool TryHandleFrame(ReadOnlySpan<byte> frame, ulong counter, out bool payloadReceived)
            {
                Calls++;
                LastCounter = counter;
                payloadReceived = false;
                return true;
            }
        }

        sealed class RecordingControlPacketHandler : IPNetMeshReliableControlPacketHandler
        {
            public int Calls { get; private set; }

            public ulong LastCounter { get; private set; }

            public PNet.Actor.Mesh.Protos.Packet? LastPacket { get; private set; }

            public bool TryHandlePacket(PNet.Actor.Mesh.Protos.Packet packet, ulong counter, out bool payloadReceived)
            {
                Calls++;
                LastPacket = packet;
                LastCounter = counter;
                payloadReceived = packet.Payload.Count > 0;
                return true;
            }
        }

        sealed record EstablishedTransportPair(
            PNetMeshTransport2 Initiator,
            PNetMeshTransport2 Responder,
            PNetMeshHandshake InitiatorHandshake,
            PNetMeshHandshake ResponderHandshake,
            KeyPair InitiatorStatic,
            KeyPair ResponderStatic) : IDisposable
        {
            public void Dispose()
            {
                Initiator.Dispose();
                Responder.Dispose();
                InitiatorHandshake.Dispose();
                ResponderHandshake.Dispose();
                InitiatorStatic.Dispose();
                ResponderStatic.Dispose();
            }
        }
    }
}
