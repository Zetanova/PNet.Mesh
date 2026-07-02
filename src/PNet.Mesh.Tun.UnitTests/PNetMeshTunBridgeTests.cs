using Noise;
using PNet.Mesh;
using PNet.Mesh.Tun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh.Tun
{
    [Trait("Type", "Integration")]
    public sealed class PNetMeshTunBridgeTests
    {
        static int _nextPort = Random.Shared.Next(21000, 51000);

        [Fact]
        public async Task bridge_exchanges_ipv4_and_ipv6_packets_between_fake_tun_devices()
        {
            using var key1 = KeyPair.Generate();
            using var key2 = KeyPair.Generate();

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var bind1 = $"127.0.0.1:{NextPort()}";
            var bind2 = $"127.0.0.1:{NextPort()}";

            var settings1 = CreateSettings(key1.PublicKey, key1.PrivateKey, psk, bind1, key2.PublicKey, bind2);
            var settings2 = CreateSettings(key2.PublicKey, key2.PrivateKey, psk, bind2, key1.PublicKey, bind1);

            using var server1 = new PNetMeshServer(settings1);
            using var server2 = new PNetMeshServer(settings2);
            await using var tun1 = new FakeTunDevice("pnet-test1");
            await using var tun2 = new FakeTunDevice("pnet-test2");

            server1.Start();
            server2.Start();

            var bridge1 = new PNetMeshTunBridge(server1, tun1, new[]
            {
                CreateRoute("node2", key2.PublicKey, bind2, "10.80.0.2/32", "fd80::2/128")
            });
            var bridge2 = new PNetMeshTunBridge(server2, tun2, new[]
            {
                CreateRoute("node1", key1.PublicKey, bind1, "10.80.0.1/32", "fd80::1/128")
            });

            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var bridge1Task = bridge1.RunAsync(runCancellation.Token);
            var bridge2Task = bridge2.RunAsync(runCancellation.Token);

            try
            {
                var ipv4 = PNetMeshIpPacket.CreateIPv4(
                    IPAddress.Parse("10.80.0.1"),
                    IPAddress.Parse("10.80.0.2"),
                    new byte[] { 1, 2, 3, 4 },
                    protocol: 17);

                tun1.QueueRead(ipv4);
                var receivedIpv4 = await tun2.ReadWrittenAsync("node2 waiting for IPv4 packet", TestContext.Current.CancellationToken);
                Assert.Equal(ipv4, receivedIpv4);

                var ipv6 = PNetMeshIpPacket.CreateIPv6(
                    IPAddress.Parse("fd80::2"),
                    IPAddress.Parse("fd80::1"),
                    new byte[] { 5, 6, 7, 8, 9 },
                    nextHeader: 59);

                tun2.QueueRead(ipv6);
                var receivedIpv6 = await tun1.ReadWrittenAsync("node1 waiting for IPv6 packet", TestContext.Current.CancellationToken);
                Assert.Equal(ipv6, receivedIpv6);
            }
            finally
            {
                runCancellation.Cancel();
                await IgnoreCancellationAsync(bridge1Task);
                await IgnoreCancellationAsync(bridge2Task);
                await Task.WhenAll(
                    server1.ShutdownAsync(TestContext.Current.CancellationToken),
                    server2.ShutdownAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task bridge_preserves_tun_packet_bursts()
        {
            using var key1 = KeyPair.Generate();
            using var key2 = KeyPair.Generate();

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var bind1 = $"127.0.0.1:{NextPort()}";
            var bind2 = $"127.0.0.1:{NextPort()}";

            var settings1 = CreateSettings(key1.PublicKey, key1.PrivateKey, psk, bind1, key2.PublicKey, bind2);
            var settings2 = CreateSettings(key2.PublicKey, key2.PrivateKey, psk, bind2, key1.PublicKey, bind1);

            using var server1 = new PNetMeshServer(settings1);
            using var server2 = new PNetMeshServer(settings2);
            await using var tun1 = new FakeTunDevice("pnet-test1");
            await using var tun2 = new FakeTunDevice("pnet-test2");

            server1.Start();
            server2.Start();

            var bridge1 = new PNetMeshTunBridge(server1, tun1, new[]
            {
                CreateRoute("node2", key2.PublicKey, bind2, "10.80.0.2/32")
            });
            var bridge2 = new PNetMeshTunBridge(server2, tun2, new[]
            {
                CreateRoute("node1", key1.PublicKey, bind1, "10.80.0.1/32")
            });

            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var bridge1Task = bridge1.RunAsync(runCancellation.Token);
            var bridge2Task = bridge2.RunAsync(runCancellation.Token);

            try
            {
                const int packetCount = 128;
                var packets = Enumerable.Range(0, packetCount)
                    .Select(index => PNetMeshIpPacket.CreateIPv4(
                        IPAddress.Parse("10.80.0.1"),
                        IPAddress.Parse("10.80.0.2"),
                        BitConverter.GetBytes(index),
                        protocol: 17))
                    .ToArray();

                foreach (var packet in packets)
                    tun1.QueueRead(packet);

                var received = await tun2.ReadWrittenAsync(packetCount, "node2 waiting for IPv4 packet burst", TestContext.Current.CancellationToken);

                Assert.Equal(packetCount, received.Length);
                for (var i = 0; i < packetCount; i++)
                    Assert.Equal(packets[i], received[i]);
            }
            finally
            {
                runCancellation.Cancel();
                await IgnoreCancellationAsync(bridge1Task);
                await IgnoreCancellationAsync(bridge2Task);
                await Task.WhenAll(
                    server1.ShutdownAsync(TestContext.Current.CancellationToken),
                    server2.ShutdownAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public void ip_prefix_matches_ipv4_and_ipv6_cidr_boundaries()
        {
            Assert.True(IpPrefix.Parse("10.80.0.0/24").Contains(IPAddress.Parse("10.80.0.42")));
            Assert.False(IpPrefix.Parse("10.80.0.0/24").Contains(IPAddress.Parse("10.80.1.42")));
            Assert.True(IpPrefix.Parse("fd80::/64").Contains(IPAddress.Parse("fd80::abcd")));
            Assert.False(IpPrefix.Parse("fd80::/64").Contains(IPAddress.Parse("fd80:1::abcd")));
            Assert.False(IpPrefix.Parse("10.80.0.0/24").Contains(IPAddress.Parse("fd80::1")));
        }

        static PNetMeshServerSettings CreateSettings(
            byte[] publicKey,
            byte[] privateKey,
            byte[] psk,
            string bindTo,
            byte[] peerPublicKey,
            string peerEndpoint)
        {
            return new PNetMeshServerSettings
            {
                PublicKey = publicKey,
                PrivateKey = privateKey,
                Psk = psk,
                BindTo = new[] { bindTo },
                Peers = new[]
                {
                    new PNetMeshPeer
                    {
                        PublicKey = peerPublicKey,
                        EndPoints = new[] { peerEndpoint }
                    }
                }
            };
        }

        static PNetMeshTunPeerRoute CreateRoute(string name, byte[] publicKey, string endpoint, params string[] prefixes)
        {
            return new PNetMeshTunPeerRoute
            {
                Name = name,
                Peer = new PNetMeshPeer
                {
                    PublicKey = publicKey,
                    EndPoints = new[] { endpoint }
                },
                AllowedIPs = prefixes.Select(IpPrefix.Parse).ToArray()
            };
        }

        static int NextPort()
        {
            return Interlocked.Increment(ref _nextPort);
        }

        static async Task IgnoreCancellationAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        sealed class FakeTunDevice : ITunDevice
        {
            readonly Channel<byte[]> _reads = Channel.CreateUnbounded<byte[]>();
            readonly Channel<byte[]> _writes = Channel.CreateUnbounded<byte[]>();

            public FakeTunDevice(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public int Mtu { get; } = 1280;

            public void QueueRead(byte[] packet)
            {
                if (packet.Length > Mtu)
                    throw new ArgumentOutOfRangeException(nameof(packet));

                Assert.True(_reads.Writer.TryWrite(packet));
            }

            public async Task<byte[]> ReadWrittenAsync(string operation, CancellationToken cancellationToken)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    return await _writes.Reader.ReadAsync(timeout.Token);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"{operation}: timed out waiting for a TUN write", ex);
                }
            }

            public async Task<byte[][]> ReadWrittenAsync(int count, string operation, CancellationToken cancellationToken)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                var packets = new byte[count][];
                try
                {
                    for (var i = 0; i < packets.Length; i++)
                        packets[i] = await _writes.Reader.ReadAsync(timeout.Token);
                    return packets;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"{operation}: timed out waiting for {count} TUN writes", ex);
                }
            }

            public async ValueTask<int> ReadPacketAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var packet = await _reads.Reader.ReadAsync(cancellationToken);
                packet.CopyTo(buffer);
                return packet.Length;
            }

            public ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
            {
                Assert.True(_writes.Writer.TryWrite(packet.ToArray()));
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                _reads.Writer.TryComplete();
                _writes.Writer.TryComplete();
                return ValueTask.CompletedTask;
            }
        }
    }
}
