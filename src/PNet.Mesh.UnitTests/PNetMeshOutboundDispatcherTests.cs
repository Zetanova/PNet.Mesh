using Microsoft.Extensions.Logging.Abstractions;
using PNet.Mesh;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshOutboundDispatcherTests
    {
        [Fact]
        public void writer_sends_endpoint_packet_directly_without_queueing()
        {
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.ReceiveTimeout = 3000;

            var dispatcher = new PNetMeshOutboundDispatcher(
                () => ImmutableArray.Create(sender),
                NullLogger.Instance);
            var payload = new byte[] { 1, 2, 3, 4 };
            var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(owner.Memory.Span);
            var packet = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = owner,
                MemoryBuffer = owner.Memory.Slice(0, payload.Length),
                LocalEndPoint = sender.LocalEndPoint,
                RemoteEndPoint = receiver.LocalEndPoint
            };

            Assert.True(dispatcher.Writer.TryWrite(packet));

            var buffer = new byte[payload.Length];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            var received = receiver.ReceiveFrom(buffer, ref remote);
            Assert.Equal(payload.Length, received);
            Assert.Equal(payload, buffer);
            Assert.False(dispatcher.Reader.TryRead(out _));
            dispatcher.Complete();
        }

        [Fact]
        public void writer_queues_packet_without_remote_endpoint()
        {
            var dispatcher = new PNetMeshOutboundDispatcher(
                () => ImmutableArray<Socket>.Empty,
                NullLogger.Instance);
            var owner = MemoryPool<byte>.Shared.Rent(1);
            var packet = new PNetMeshOutboundMessages.Packet
            {
                MemoryOwner = owner,
                MemoryBuffer = owner.Memory.Slice(0, 1)
            };

            Assert.True(dispatcher.Writer.TryWrite(packet));

            Assert.True(dispatcher.Reader.TryRead(out var queued));
            Assert.Same(packet, queued);
            packet.MemoryOwner?.Dispose();
            dispatcher.Complete();
        }

        [Fact]
        public void writer_queues_relay_messages()
        {
            var dispatcher = new PNetMeshOutboundDispatcher(
                () => ImmutableArray<Socket>.Empty,
                NullLogger.Instance);
            var relay = new PNetMeshOutboundMessages.Relay
            {
                Packet = new PNetMeshRelayPacket
                {
                    Address = Array.Empty<byte>(),
                    Route = ImmutableArray<byte[]>.Empty,
                    Payload = ReadOnlyMemory<byte>.Empty
                }
            };

            Assert.True(dispatcher.Writer.TryWrite(relay));

            Assert.True(dispatcher.Reader.TryRead(out var queued));
            Assert.Same(relay, queued);
            dispatcher.Complete();
        }
    }
}
