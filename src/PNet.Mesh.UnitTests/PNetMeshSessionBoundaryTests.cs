using Google.Protobuf;
using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using PNet.Mesh;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

using EnvelopeBody = PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Body;
using Forward = PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Body.Types.Forward;
using MeshEndpoint = PNet.Actor.Mesh.Protos.MeshEndpoint;
using ProtoMetadata = PNet.Protos.ProtoMetadata;
using ReliableEnvelope = PNet.Actor.Mesh.Protos.ReliableEnvelope;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshSessionBoundaryTests
    {
        [Fact]
        public void secure_frame_session_round_trips_raw_plaintext_without_protobuf_packet()
        {
            using var transports = CreateEstablishedTransports();
            var sender = transports.Initiator;
            var receiver = transports.Responder;
            var frame = new byte[] { 0x45, 0x00, 0x01, 0x02 };
            var encrypted = new byte[PNetMeshTransport2.CalculatePacketSize(frame.Length)];
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
            var reliableEnvelope = new ReliableEnvelope();
            var payloadEnvelope = new ReliableEnvelope();
            payloadEnvelope.Bodies.Add(CreateBody(7));
            var payload = Encoding.UTF8.GetBytes("control");
            var pnetFrame = PNetMeshPayloadFraming.CreatePNet(payloadEnvelope.ToByteArray());
            var typedPNetFrame = new byte[64];
            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                PNetMeshPNetFrameType.ReliableEnvelope,
                reliableEnvelope.ToByteArray(),
                typedPNetFrame,
                out var typedPNetFrameBytes));
            var reliableBodiesPNetFrame = CreateReliableBodiesFrame(payloadEnvelope, payload);
            var forwardBody = CreateBody(7);
            forwardBody.Forward = new Forward
            {
                Destination = new MeshEndpoint
                {
                    Address = ByteString.CopyFrom(new byte[] { 0x01 })
                },
                SequenceNumber = 1
            };
            var bodyPNetFrame = CreateBodyFrame(forwardBody, payload);
            var unknownPNetFrame = new byte[64];
            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                (PNetMeshPNetFrameType)4,
                payloadEnvelope.ToByteArray(),
                unknownPNetFrame,
                out var unknownPNetFrameBytes));
            var ipv4Frame = PNetMeshIpPacket.CreateIPv4(
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                ReadOnlySpan<byte>.Empty,
                protocol: 17);

            Assert.False(controlSession.TryHandleFrame(ipv4Frame, 41, out _, out var error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.Equal(0, handler.Calls);

            Assert.False(controlSession.TryHandleFrame(pnetFrame, 42, out var payloadReceived, out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.False(payloadReceived);
            Assert.Equal(0, handler.Calls);

            Assert.True(controlSession.TryHandleFrame(
                typedPNetFrame.AsSpan(0, typedPNetFrameBytes),
                43,
                out payloadReceived,
                out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.False(payloadReceived);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(43ul, handler.LastCounter);

            Assert.True(controlSession.TryHandleFrame(
                reliableBodiesPNetFrame,
                44,
                out payloadReceived,
                out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.True(payloadReceived);
            Assert.Equal(2, handler.Calls);
            Assert.Equal(44ul, handler.LastCounter);
            var metadata = Assert.Single(handler.LastEnvelope!.Bodies).Metadata;
            Assert.Equal(7u, metadata.PayloadSize);
            Assert.Equal("control", Encoding.UTF8.GetString(handler.LastReliableEnvelope.BodyTail.Span));

            Assert.True(controlSession.TryHandleFrame(
                bodyPNetFrame,
                45,
                out payloadReceived,
                out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.True(payloadReceived);
            Assert.Equal(3, handler.Calls);
            Assert.Equal(45ul, handler.LastCounter);
            Assert.Equal("control", Encoding.UTF8.GetString(handler.LastBodyPayload.Span));
            Assert.NotNull(handler.LastBody!.Forward);

            Assert.False(controlSession.TryHandleFrame(
                unknownPNetFrame.AsSpan(0, unknownPNetFrameBytes),
                46,
                out payloadReceived,
                out error));
            Assert.Equal(PNetMeshPayloadFrameError.None, error);
            Assert.False(payloadReceived);
            Assert.Equal(3, handler.Calls);
        }

        static EnvelopeBody CreateBody(int payloadLength)
        {
            return new EnvelopeBody
            {
                Metadata = new ProtoMetadata
                {
                    PayloadSize = (uint)payloadLength,
                    CompressedSize = (uint)payloadLength
                }
            };
        }

        static byte[] CreateReliableBodiesFrame(ReliableEnvelope envelope, ReadOnlySpan<byte> bodyTail)
        {
            var envelopeBytes = envelope.ToByteArray();
            var reliableBodies = new byte[PNetMeshUtils.GetVarint32Size((uint)envelopeBytes.Length) + envelopeBytes.Length + bodyTail.Length];
            var offset = PNetMeshUtils.WriteVarint32(envelopeBytes.Length, reliableBodies);
            envelopeBytes.CopyTo(reliableBodies.AsSpan(offset));
            offset += envelopeBytes.Length;
            bodyTail.CopyTo(reliableBodies.AsSpan(offset));

            var frame = new byte[64];
            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                PNetMeshPNetFrameType.ReliableBodies,
                reliableBodies,
                frame,
                out var frameBytes));

            return frame.AsSpan(0, frameBytes).ToArray();
        }

        static byte[] CreateBodyFrame(EnvelopeBody body, ReadOnlySpan<byte> payload)
        {
            var bodyBytes = body.ToByteArray();
            var bodyPayload = new byte[PNetMeshUtils.GetVarint32Size((uint)bodyBytes.Length) + bodyBytes.Length + payload.Length];
            var offset = PNetMeshUtils.WriteVarint32(bodyBytes.Length, bodyPayload);
            bodyBytes.CopyTo(bodyPayload.AsSpan(offset));
            offset += bodyBytes.Length;
            payload.CopyTo(bodyPayload.AsSpan(offset));

            var frame = new byte[64];
            Assert.True(PNetMeshPayloadFraming.TryWritePNet(
                PNetMeshPNetFrameType.Body,
                bodyPayload,
                frame,
                out var frameBytes));

            return frame.AsSpan(0, frameBytes).ToArray();
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

            public ReliableEnvelope? LastEnvelope { get; private set; }

            public PNetMeshReliableEnvelope LastReliableEnvelope { get; private set; }

            public EnvelopeBody? LastBody { get; private set; }

            public ReadOnlyMemory<byte> LastBodyPayload { get; private set; }

            public bool TryHandleEnvelope(PNetMeshReliableEnvelope envelope, ulong counter, out bool bodyReceived)
            {
                Calls++;
                LastReliableEnvelope = envelope;
                LastEnvelope = envelope.Envelope;
                LastCounter = counter;
                bodyReceived = envelope.Envelope.Bodies.Count > 0 || !envelope.BodyTail.IsEmpty;
                return true;
            }

            public bool TryHandleBody(
                EnvelopeBody body,
                ReadOnlyMemory<byte> bodyPayload,
                ulong counter,
                out bool bodyReceived)
            {
                Calls++;
                LastBody = body;
                LastCounter = counter;
                LastBodyPayload = bodyPayload;
                bodyReceived = !bodyPayload.IsEmpty;
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
