using Microsoft.Extensions.Logging.Abstractions;
using PNet.Mesh;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Xunit;
using KeyPair = PNet.Mesh.PNetMeshKeyPair;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshInboundDispatcherTests
    {
        [Fact]
        public void dispatch_queues_non_packet_data_for_control_processing()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = CreatePsk();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20002)
            };
            var fallback = Channel.CreateUnbounded<PNetMeshControlCommands.Command>();
            var dispatcher = CreateDispatcher(receiverProtocol, new PNetMeshSessionTable(), fallback.Writer);

            sender.WriteInitialize(1, receiverKey.PublicKey);
            var initialize = ReadPacket(senderOutbound);
            var receive = new PNetMeshControlCommands.Receive
            {
                MemoryOwner = initialize.MemoryOwner,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20001),
                MemoryBuffer = initialize.MemoryBuffer
            };

            dispatcher.Dispatch(receive);

            Assert.True(fallback.Reader.TryRead(out var queued));
            Assert.Same(receive, queued);
            receive.MemoryOwner?.Dispose();
        }

        [Fact]
        public void dispatch_queues_packet_data_without_known_session()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = CreatePsk();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20101),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20102)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20102),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20101)
            };
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            var fallback = Channel.CreateUnbounded<PNetMeshControlCommands.Command>();
            var dispatcher = CreateDispatcher(receiverProtocol, new PNetMeshSessionTable(), fallback.Writer);
            var packet = WritePayloadPacket(sender, senderOutbound, "queued");
            var receive = new PNetMeshControlCommands.Receive
            {
                MemoryOwner = packet.MemoryOwner,
                RemoteEndPoint = sender.LocalEndPoint,
                LocalEndPoint = receiver.LocalEndPoint,
                MemoryBuffer = packet.MemoryBuffer
            };

            dispatcher.Dispatch(receive);

            Assert.True(fallback.Reader.TryRead(out var queued));
            Assert.Same(receive, queued);
            receive.MemoryOwner?.Dispose();
        }

        [Fact]
        public void dispatch_decrypts_known_packet_data_directly_without_fallback_queue()
        {
            using var senderKey = KeyPair.Generate();
            using var receiverKey = KeyPair.Generate();
            var psk = CreatePsk();
            var senderProtocol = new PNetMeshProtocol(senderKey.PrivateKey, senderKey.PublicKey, psk);
            var receiverProtocol = new PNetMeshProtocol(receiverKey.PrivateKey, receiverKey.PublicKey, psk);
            var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            using var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20201),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20202)
            };
            using var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
            {
                LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 20202),
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20201)
            };
            var receiverInbound = AttachSession(receiver);
            OpenSessionPair(sender, receiver, senderOutbound, receiverOutbound, receiverKey.PublicKey);
            var sessions = new PNetMeshSessionTable();
            sessions.Add(receiver.SenderIndex, receiver);
            var fallback = Channel.CreateUnbounded<PNetMeshControlCommands.Command>();
            var dispatcher = CreateDispatcher(receiverProtocol, sessions, fallback.Writer);
            var packet = WritePayloadPacket(sender, senderOutbound, "direct");
            var promotedEndpoint = new IPEndPoint(IPAddress.Loopback, 20999);
            var receive = new PNetMeshControlCommands.Receive
            {
                MemoryOwner = packet.MemoryOwner,
                RemoteEndPoint = promotedEndpoint,
                LocalEndPoint = receiver.LocalEndPoint,
                MemoryBuffer = packet.MemoryBuffer
            };

            dispatcher.Dispatch(receive);

            Assert.False(fallback.Reader.TryRead(out _));
            Assert.True(receiverInbound.Reader.TryRead(out var payload));
            Assert.Equal("direct", Encoding.UTF8.GetString(payload.Span));
            Assert.Equal(promotedEndpoint, receiver.RemoteEndPoint);
        }

        static PNetMeshInboundDispatcher CreateDispatcher(
            PNetMeshProtocol protocol,
            PNetMeshSessionTable sessions,
            ChannelWriter<PNetMeshControlCommands.Command> fallbackWriter)
        {
            return new PNetMeshInboundDispatcher(
                protocol,
                sessions,
                new PNetMeshEndpointUpdater(new PNetMeshRouter(), NullLogger.Instance),
                fallbackWriter,
                NullLogger.Instance);
        }

        static byte[] CreatePsk()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            return psk;
        }

        static Channel<ReadOnlyMemory<byte>> AttachSession(PNetMeshSession session)
        {
            var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            var control = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
            session.AttachTo(inbound.Writer, control.Writer);
            return inbound;
        }

        static void OpenSessionPair(
            PNetMeshSession sender,
            PNetMeshSession receiver,
            Channel<PNetMeshOutboundMessages.Message> senderOutbound,
            Channel<PNetMeshOutboundMessages.Message> receiverOutbound,
            byte[] receiverPublicKey)
        {
            sender.WriteInitialize(1, receiverPublicKey);
            var initialize = ReadPacket(senderOutbound);
            Assert.True(receiver.TryReadInitialize(2, initialize.MemoryBuffer.Span));
            initialize.MemoryOwner?.Dispose();

            receiver.WriteResponse();
            var response = ReadPacket(receiverOutbound);
            Assert.True(sender.TryReadResponse(response.MemoryBuffer.Span));
            response.MemoryOwner?.Dispose();
        }

        static PNetMeshOutboundMessages.Packet WritePayloadPacket(
            PNetMeshSession session,
            Channel<PNetMeshOutboundMessages.Message> outbound,
            string payload)
        {
            session.WritePayload(Encoding.UTF8.GetBytes(payload));
            if (!outbound.Reader.TryRead(out var message))
            {
                session.WritePacket();
                Assert.True(outbound.Reader.TryRead(out message));
            }

            return Assert.IsType<PNetMeshOutboundMessages.Packet>(message);
        }

        static PNetMeshOutboundMessages.Packet ReadPacket(Channel<PNetMeshOutboundMessages.Message> channel)
        {
            Assert.True(channel.Reader.TryRead(out var message));
            return Assert.IsType<PNetMeshOutboundMessages.Packet>(message);
        }
    }
}
