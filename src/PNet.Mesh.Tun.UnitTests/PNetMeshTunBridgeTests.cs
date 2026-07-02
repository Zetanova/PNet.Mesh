using Noise;
using PNet.Mesh;
using PNet.Mesh.Tun;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
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
            Assert.True(IpPrefix.Parse("10.80.0.128/25").Contains(IPAddress.Parse("10.80.0.255")));
            Assert.False(IpPrefix.Parse("10.80.0.128/25").Contains(IPAddress.Parse("10.80.0.127")));
            Assert.True(IpPrefix.Parse("fd80::/64").Contains(IPAddress.Parse("fd80::abcd")));
            Assert.False(IpPrefix.Parse("fd80::/64").Contains(IPAddress.Parse("fd80:1::abcd")));
            Assert.False(IpPrefix.Parse("10.80.0.0/24").Contains(IPAddress.Parse("fd80::1")));
        }

        [Fact]
        public async Task peer_state_memoizes_one_channel_connect_task_for_concurrent_callers()
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
            await using var tun = new FakeTunDevice("pnet-test");

            server1.Start();
            server2.Start();

            var bridge = new PNetMeshTunBridge(server1, tun, new[]
            {
                CreateRoute("node2", key2.PublicKey, bind2, "10.80.0.2/32")
            });

            try
            {
                var peerState = GetPeerState(bridge, "node2");
                var connectTaskField = GetRequiredField(peerState.GetType(), "_connectTask");
                Assert.Null(connectTaskField.GetValue(peerState));

                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var requests = Enumerable.Range(0, 32)
                    .Select(_ => Task.Run(async () =>
                    {
                        await start.Task;
                        return StartPeerChannelRequest(peerState, server1, TestContext.Current.CancellationToken);
                    }, TestContext.Current.CancellationToken))
                    .ToArray();

                start.TrySetResult();

                var snapshots = await Task.WhenAll(requests);
                var channels = await Task.WhenAll(snapshots.Select(snapshot => snapshot.ChannelTask));
                var memoizedTask = Assert.IsType<Task<PNetMeshChannel>>(connectTaskField.GetValue(peerState));

                Assert.All(channels, channel => Assert.Same(channels[0], channel));
                Assert.All(snapshots, snapshot => Assert.Same(memoizedTask, snapshot.ConnectTask));
                Assert.True(memoizedTask.IsCompletedSuccessfully);
                Assert.Same(channels[0], await memoizedTask);

                var cachedChannel = await InvokeGetChannelAsync(peerState, server1, TestContext.Current.CancellationToken);
                Assert.Same(channels[0], cachedChannel);
            }
            finally
            {
                await Task.WhenAll(
                    server1.ShutdownAsync(TestContext.Current.CancellationToken),
                    server2.ShutdownAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task peer_state_canceled_waiter_does_not_cancel_shared_connect_task()
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
            await using var tun = new FakeTunDevice("pnet-test");

            server1.Start();
            server2.Start();

            var bridge = new PNetMeshTunBridge(server1, tun, new[]
            {
                CreateRoute("node2", key2.PublicKey, bind2, "10.80.0.2/32")
            });

            try
            {
                var peerState = GetPeerState(bridge, "node2");
                var connectTaskField = GetRequiredField(peerState.GetType(), "_connectTask");

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();

                var canceledSnapshot = StartPeerChannelRequest(peerState, server1, canceled.Token);
                var canceledWaitException = await Record.ExceptionAsync(async () => await canceledSnapshot.ChannelTask);
                Assert.True(canceledWaitException is null or OperationCanceledException);

                var channel = await InvokeGetChannelAsync(peerState, server1, TestContext.Current.CancellationToken);
                var memoizedTask = Assert.IsType<Task<PNetMeshChannel>>(connectTaskField.GetValue(peerState));

                Assert.Same(canceledSnapshot.ConnectTask, memoizedTask);
                Assert.True(memoizedTask.IsCompletedSuccessfully);
                Assert.Same(channel, await memoizedTask);
            }
            finally
            {
                await Task.WhenAll(
                    server1.ShutdownAsync(TestContext.Current.CancellationToken),
                    server2.ShutdownAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public void peer_state_queues_packet_owner_without_copy()
        {
            using var key2 = KeyPair.Generate();

            var peerState = CreatePeerState(key2, "node2");
            var owner = new TrackingMemoryOwner(16);
            var expected = new byte[] { 1, 2, 3, 4 };
            expected.CopyTo(owner.Memory.Span.Slice(3));

            Assert.True(InvokeTryQueuePacket(peerState, owner.Memory.Slice(3, expected.Length), owner));
            Assert.True(InvokeTryTakePacket(peerState, out var queuedPacket));

            var payload = GetQueuedPayload(queuedPacket);
            var memoryOwner = GetQueuedMemoryOwner(queuedPacket);

            Assert.Same(owner, memoryOwner);
            Assert.Equal(expected, payload.ToArray());

            owner.Dispose();
        }

        [Fact]
        public void peer_state_disposes_queued_packet_owners_when_drained()
        {
            using var key2 = KeyPair.Generate();

            var peerState = CreatePeerState(key2, "node2");
            var owner = new TrackingMemoryOwner(16);
            new byte[] { 1, 2, 3, 4 }.CopyTo(owner.Memory.Span);

            Assert.True(InvokeTryQueuePacket(peerState, owner.Memory.Slice(0, 4), owner));
            InvokeDisposeQueuedPackets(peerState);

            Assert.Equal(1, owner.DisposeCount);
            Assert.True(owner.IsCleared);
            Assert.False(InvokeTryTakePacket(peerState, out _));

            var lateOwner = new TrackingMemoryOwner(16);
            Assert.False(InvokeTryQueuePacket(peerState, lateOwner.Memory.Slice(0, 4), lateOwner));
            lateOwner.Dispose();
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

        static object CreatePeerState(KeyPair peerKey, string peerName)
        {
            var bind2 = $"127.0.0.1:{NextPort()}";
            var route = CreateRoute(peerName, peerKey.PublicKey, bind2, "10.80.0.2/32");
            var peerStateType = typeof(PNetMeshTunBridge).GetNestedType("PeerState", BindingFlags.NonPublic);
            Assert.NotNull(peerStateType);

            return Activator.CreateInstance(peerStateType, route);
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

        static object GetPeerState(PNetMeshTunBridge bridge, string peerName)
        {
            var peersField = GetRequiredField(typeof(PNetMeshTunBridge), "_peers");
            var peers = Assert.IsAssignableFrom<Array>(peersField.GetValue(bridge));

            return Assert.Single(
                peers.Cast<object>(),
                peerState => GetRoute(peerState).Name == peerName);
        }

        static (Task<PNetMeshChannel> ChannelTask, Task<PNetMeshChannel> ConnectTask) StartPeerChannelRequest(
            object peerState,
            PNetMeshServer server,
            CancellationToken cancellationToken)
        {
            var channelTask = InvokeGetChannelAsync(peerState, server, cancellationToken);
            var connectTask = Assert.IsType<Task<PNetMeshChannel>>(
                GetRequiredField(peerState.GetType(), "_connectTask").GetValue(peerState));
            return (channelTask, connectTask);
        }

        static Task<PNetMeshChannel> InvokeGetChannelAsync(
            object peerState,
            PNetMeshServer server,
            CancellationToken cancellationToken)
        {
            var getChannelAsync = peerState.GetType().GetMethod("GetChannelAsync", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(getChannelAsync);

            var valueTask = Assert.IsType<ValueTask<PNetMeshChannel>>(
                getChannelAsync.Invoke(peerState, new object[] { server, cancellationToken }));
            return valueTask.AsTask();
        }

        static PNetMeshTunPeerRoute GetRoute(object peerState)
        {
            var routeProperty = peerState.GetType().GetProperty("Route", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(routeProperty);
            return Assert.IsType<PNetMeshTunPeerRoute>(routeProperty.GetValue(peerState));
        }

        static bool InvokeTryQueuePacket(
            object peerState,
            ReadOnlyMemory<byte> packet,
            IMemoryOwner<byte> memoryOwner)
        {
            var tryQueuePacket = peerState.GetType().GetMethod("TryQueuePacket", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(tryQueuePacket);
            return Assert.IsType<bool>(tryQueuePacket.Invoke(peerState, new object[] { packet, memoryOwner }));
        }

        static bool InvokeTryTakePacket(object peerState, out object queuedPacket)
        {
            var tryTakePacket = peerState.GetType().GetMethod("TryTakePacket", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(tryTakePacket);

            var args = new object[] { null };
            var result = Assert.IsType<bool>(tryTakePacket.Invoke(peerState, args));
            queuedPacket = args[0];
            return result;
        }

        static void InvokeDisposeQueuedPackets(object peerState)
        {
            var disposeQueuedPackets = peerState.GetType().GetMethod("CompleteSendQueueAndDisposeQueuedPackets", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(disposeQueuedPackets);
            disposeQueuedPackets.Invoke(peerState, Array.Empty<object>());
        }

        static ReadOnlyMemory<byte> GetQueuedPayload(object queuedPacket)
        {
            var payloadProperty = queuedPacket.GetType().GetProperty("Payload", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(payloadProperty);
            return Assert.IsType<ReadOnlyMemory<byte>>(payloadProperty.GetValue(queuedPacket));
        }

        static IMemoryOwner<byte> GetQueuedMemoryOwner(object queuedPacket)
        {
            var memoryOwnerProperty = queuedPacket.GetType().GetProperty("MemoryOwner", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(memoryOwnerProperty);
            return Assert.IsAssignableFrom<IMemoryOwner<byte>>(memoryOwnerProperty.GetValue(queuedPacket));
        }

        static FieldInfo GetRequiredField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field '{name}' was not found on {type.FullName}.");
        }

        sealed class TrackingMemoryOwner : IMemoryOwner<byte>
        {
            readonly byte[] _buffer;

            public TrackingMemoryOwner(int length)
            {
                _buffer = new byte[length];
            }

            public Memory<byte> Memory => _buffer;

            public int DisposeCount { get; private set; }

            public bool IsCleared => _buffer.All(value => value == 0);

            public void Dispose()
            {
                DisposeCount++;
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
