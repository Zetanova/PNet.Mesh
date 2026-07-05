using Microsoft.Extensions.Logging.Abstractions;
using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using PNet.Mesh;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    [Trait("Type", "Integration")]
    public sealed class PNetMeshServerTests
    {
        readonly ITestOutputHelper _output;

        public PNetMeshServerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task bind_three_server_to_localhost_and_relay_exchange()
        {
            using var key1 = KeyPair.Generate();
            using var key2 = KeyPair.Generate();
            using var key3 = KeyPair.Generate();

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var settings1 = new PNetMeshServerSettings
            {
                PublicKey = key1.PublicKey,
                PrivateKey = key1.PrivateKey,
                Psk = psk,
                BindTo = new[] { "127.0.0.1:5811" }
            };

            var settings2 = new PNetMeshServerSettings
            {
                PublicKey = key2.PublicKey,
                PrivateKey = key2.PrivateKey,
                Psk = psk,
                BindTo = new[] { "127.0.0.1:5812" },
                Peers = new[]
                {
                    new PNetMeshPeer
                    {
                        PublicKey = settings1.PublicKey,
                        EndPoints = settings1.BindTo
                    }
                }
            };

            var settings3 = new PNetMeshServerSettings
            {
                PublicKey = key3.PublicKey,
                PrivateKey = key3.PrivateKey,
                Psk = psk,
                BindTo = new[] { "127.0.0.1:5813" },
                Peers = new[]
                {
                    new PNetMeshPeer
                    {
                        PublicKey = settings1.PublicKey,
                        EndPoints = settings1.BindTo
                    }
                }
            };

            using var server1 = new PNetMeshServer(settings1);
            using var server2 = new PNetMeshServer(settings2);
            using var server3 = new PNetMeshServer(settings3);

            server1.Start();
            server2.Start();
            server3.Start();

            var peer1 = new PNetMeshPeer
            {
                PublicKey = settings1.PublicKey,
                EndPoints = Array.Empty<string>()
            };

            var peer2 = new PNetMeshPeer
            {
                PublicKey = settings2.PublicKey,
                EndPoints = Array.Empty<string>(),
                //EndPoints = settings2.BindTo
            };

            var peer3 = new PNetMeshPeer
            {
                PublicKey = settings3.PublicKey,
                EndPoints = Array.Empty<string>(),
                //EndPoints = settings3.BindTo
            };

            var channel2_1 = await server2.ConnectToAsync(peer1, TestContext.Current.CancellationToken);

            var channel3_1 = await server3.ConnectToAsync(peer1, TestContext.Current.CancellationToken);


            //var r = channel2_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 2"));
            //Assert.True(r);

            //r = channel3_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 3"));
            //Assert.True(r);


            //discover over peer1
            var channel3_2 = await server3.ConnectToAsync(peer2, TestContext.Current.CancellationToken);

            //discover over pre connection
            var channel2_3 = await server2.ConnectToAsync(peer3, TestContext.Current.CancellationToken);

            //await Task.Delay(500);

            string msg;
            bool r;
            ReadOnlyMemory<byte> payload;


            r = channel3_2.TryWrite(Encoding.UTF8.GetBytes("Hello World! 2 from 3"));
            Assert.True(r);

            r = channel2_3.TryWrite(Encoding.UTF8.GetBytes("Hello World! 3 from 2"));
            Assert.True(r);

            payload = await ReadPayloadAsync(channel3_2, "node3 waiting for node2 relay");
            Assert.True(payload.Length > 0);
            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 3 from 2", msg);


            payload = await ReadPayloadAsync(channel2_3, "node2 waiting for node3 relay");
            Assert.True(payload.Length > 0);
            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 2 from 3", msg);


            await Task.WhenAll(
                server1.ShutdownAsync(TestContext.Current.CancellationToken),
                server2.ShutdownAsync(TestContext.Current.CancellationToken),
                server3.ShutdownAsync(TestContext.Current.CancellationToken)
            );
        }

        [Fact]
        public async Task direct_peers_exchange_payloads_in_both_directions_over_public_api()
        {
            using var key1 = KeyPair.Generate();
            using var key2 = KeyPair.Generate();

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var settings1 = new PNetMeshServerSettings
            {
                PublicKey = key1.PublicKey,
                PrivateKey = key1.PrivateKey,
                Psk = psk,
                BindTo = new[] { "127.0.0.1:5801" }
            };

            var settings2 = new PNetMeshServerSettings
            {
                PublicKey = key2.PublicKey,
                PrivateKey = key2.PrivateKey,
                Psk = psk,
                BindTo = new[] { "127.0.0.1:5802" },
                Peers = new[]
                {
                    new PNetMeshPeer
                    {
                        PublicKey = settings1.PublicKey,
                        EndPoints = settings1.BindTo
                    }
                }
            };

            using var server1 = new PNetMeshServer(settings1);
            using var server2 = new PNetMeshServer(settings2);

            server1.Start();
            server2.Start();

            var peer1 = new PNetMeshPeer
            {
                PublicKey = settings1.PublicKey,
                //EndPoints = settings1.BindTo
                EndPoints = Array.Empty<string>()
            };

            var peer2 = new PNetMeshPeer
            {
                PublicKey = settings2.PublicKey,
                EndPoints = Array.Empty<string>() //no endpoint
            };

            var channel2_1 = await server2.ConnectToAsync(peer1, TestContext.Current.CancellationToken);

            var r = channel2_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 2"));
            Assert.True(r);

            var channel1_2 = await server1.ConnectToAsync(peer2, TestContext.Current.CancellationToken);

            r = channel1_2.TryWrite(Encoding.UTF8.GetBytes("Hello World! 2 from 1"));
            Assert.True(r);

            var payload = await ReadPayloadAsync(channel1_2, "node1 waiting for node2 message");
            Assert.True(payload.Length > 0);

            var msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 1 from 2", msg);

            payload = await ReadPayloadAsync(channel2_1, "node2 waiting for node1 message");
            Assert.True(payload.Length > 0);

            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 2 from 1", msg);

            const int burstMessageCount = 2;

            for (int i = 0; i < burstMessageCount; i++)
            {
                r = channel1_2.TryWrite(Encoding.UTF8.GetBytes($"Msg[{i}]"));
                Assert.True(r);
            }

            var receivedMessages = new HashSet<string>();

            for (int i = 0; i < burstMessageCount; i++)
            {
                payload = await ReadPayloadAsync(channel2_1, $"node2 waiting for burst message {i}");
                msg = Encoding.UTF8.GetString(payload.Span);
                Assert.True(receivedMessages.Add(msg));
            }

            for (int i = 0; i < burstMessageCount; i++)
                Assert.Contains($"Msg[{i}]", receivedMessages);

            await Task.WhenAll(
                server1.ShutdownAsync(TestContext.Current.CancellationToken),
                server2.ShutdownAsync(TestContext.Current.CancellationToken)
            );
        }

        [Fact]
        public void disposed_receive_socket_local_endpoint_disposes_active_buffer()
        {
            using var key = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var protocol = new PNetMeshProtocol(key.PrivateKey, key.PublicKey, psk);
            var control = Channel.CreateUnbounded<PNetMeshControlCommands.Command>();
            var inboundDispatcher = new PNetMeshInboundDispatcher(
                protocol,
                new PNetMeshSessionTable(),
                new PNetMeshEndpointUpdater(new PNetMeshRouter(), NullLogger.Instance),
                control.Writer,
                NullLogger.Instance);
            using var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            var memoryOwner = new TrackingMemoryOwner(256);
            var item = new PNetMeshSocketReceiveWorkItem
            {
                Protocol = protocol,
                MemoryOwner = memoryOwner,
                InboundDispatcher = inboundDispatcher,
                Logger = NullLogger.Instance
            };
            socket.Dispose();

            var accepted = PNetMeshServer.TryGetReceiveLocalEndPoint(socket, item, out var localEndPoint);

            Assert.False(accepted);
            Assert.Null(localEndPoint);
            Assert.True(memoryOwner.Disposed);
        }

        static async Task<ReadOnlyMemory<byte>> ReadPayloadAsync(PNetMeshChannel channel, string operation)
        {
            if (channel.TryRead(out var payload))
                return payload;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                var ready = await channel.WaitToReadAsync(timeout.Token);
                Assert.True(ready, $"{operation}: channel closed before a payload was available");
            }
            catch (OperationCanceledException ex) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{operation}: timed out waiting for a payload", ex);
            }

            Assert.True(channel.TryRead(out payload), $"{operation}: signaled readable but no payload was available");
            return payload;
        }

        sealed class TrackingMemoryOwner : IMemoryOwner<byte>
        {
            readonly byte[] _buffer;

            public TrackingMemoryOwner(int length)
            {
                _buffer = new byte[length];
            }

            public bool Disposed { get; private set; }

            public Memory<byte> Memory => _buffer;

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
