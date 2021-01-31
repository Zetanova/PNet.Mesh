using Microsoft.Extensions.Logging.Abstractions;
using Noise;
using PNet.Actor.Mesh;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

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

            using var server1 = new PNetMeshServer(settings1, NullLogger<PNetMeshServer>.Instance);
            using var server2 = new PNetMeshServer(settings2, NullLogger<PNetMeshServer>.Instance);
            using var server3 = new PNetMeshServer(settings3, NullLogger<PNetMeshServer>.Instance);

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

            var channel2_1 = await server2.ConnectToAsync(peer1);

            var channel3_1 = await server3.ConnectToAsync(peer1);


            //var r = channel2_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 2"));
            //Assert.True(r);

            //r = channel3_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 3"));
            //Assert.True(r);


            //discover over peer1
            var channel3_2 = await server3.ConnectToAsync(peer2);

            //discover over pre connection
            var channel2_3 = await server2.ConnectToAsync(peer3);

            //await Task.Delay(500);

            string msg;
            bool r;
            ReadOnlyMemory<byte> payload;


            r = channel3_2.TryWrite(Encoding.UTF8.GetBytes("Hello World! 2 from 3"));
            Assert.True(r);

            r = channel2_3.TryWrite(Encoding.UTF8.GetBytes("Hello World! 3 from 2"));
            Assert.True(r);

            r = await channel3_2.WaitToReadAsync();
            Assert.True(r);

            r = channel3_2.TryRead(out payload);
            Assert.True(r);
            Assert.True(payload.Length > 0);
            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 3 from 2", msg);


            r = await channel2_3.WaitToReadAsync();
            Assert.True(r);

            r = channel2_3.TryRead(out payload);
            Assert.True(r);
            Assert.True(payload.Length > 0);
            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 2 from 3", msg);


            await Task.WhenAll(
                server1.ShutdownAsync(),
                server2.ShutdownAsync(),
                server3.ShutdownAsync()
            );
        }

        [Fact]
        public async Task bind_two_server_to_localhost_and_exchange()
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

            using var server1 = new PNetMeshServer(settings1, NullLogger<PNetMeshServer>.Instance);
            using var server2 = new PNetMeshServer(settings2, NullLogger<PNetMeshServer>.Instance);

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

            var channel2_1 = await server2.ConnectToAsync(peer1);

            var r = channel2_1.TryWrite(Encoding.UTF8.GetBytes("Hello World! 1 from 2"));
            Assert.True(r);

            var channel1_2 = await server1.ConnectToAsync(peer2);

            r = channel1_2.TryWrite(Encoding.UTF8.GetBytes("Hello World! 2 from 1"));
            Assert.True(r);

            r = await channel1_2.WaitToReadAsync();
            Assert.True(r);

            r = channel1_2.TryRead(out var payload);
            Assert.True(r);
            Assert.True(payload.Length > 0);

            var msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 1 from 2", msg);

            r = await channel2_1.WaitToReadAsync();
            Assert.True(r);

            r = channel2_1.TryRead(out payload);
            Assert.True(r);
            Assert.True(payload.Length > 0);

            msg = Encoding.UTF8.GetString(payload.Span);
            Assert.Equal("Hello World! 2 from 1", msg);

            for (int i = 0; i < 100; i++)
            {
                r = channel1_2.TryWrite(Encoding.UTF8.GetBytes($"Msg[{i}]"));
                Assert.True(r);
            }

            for (int i = 0; i < 100; i++)
            {
                r = channel2_1.TryRead(out payload);
                if (!r)
                {
                    await channel2_1.WaitToReadAsync();
                    r = channel2_1.TryRead(out payload);
                }
                Assert.True(r);
                msg = Encoding.UTF8.GetString(payload.Span);
                Assert.Equal($"Msg[{i}]", msg);
            }

            await Task.WhenAll(server1.ShutdownAsync(), server2.ShutdownAsync());
        }
    }
}
