using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PNet.Mesh;
using PNet.Mesh.Tun;
using PNet.Mesh.Tun.Linux;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh.Tun.Cli
{
    static class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (!TunCliOptions.TryParse(args, out var options, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Console.Error.WriteLine(error);

                PrintUsage();
                return 2;
            }

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole(settings =>
                {
                    settings.SingleLine = true;
                    settings.TimestampFormat = "HH:mm:ss ";
                });
                builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
            });
            using var serviceProvider = new ServiceCollection()
                .AddSingleton(loggerFactory)
                .AddLogging()
                .BuildServiceProvider();

            try
            {
                if (options.Mode == TunCliMode.IcmpEcho)
                {
                    await RunIcmpEchoResponderAsync(options, loggerFactory, shutdown.Token);
                    return 0;
                }

                if (options.Mode == TunCliMode.TunIcmpEcho)
                {
                    await RunTunIcmpEchoResponderAsync(options, shutdown.Token);
                    return 0;
                }

                await using var tunDevice = await LinuxTunDevice.CreateAsync(
                    options.InterfaceName,
                    options.Mtu,
                    exclusive: options.ExclusiveInterface,
                    cancellationToken: shutdown.Token);

                if (options.ConfigureInterface)
                {
                    await LinuxTunInterfaceConfigurator.ConfigureAsync(new LinuxTunInterfaceConfiguration
                    {
                        InterfaceName = tunDevice.Name,
                        Mtu = options.Mtu,
                        Addresses = options.Addresses,
                        Routes = options.Routes
                    }, cancellationToken: shutdown.Token);
                }

                var peerRoutes = options.CreatePeerRoutes();
                var settings = new PNetMeshServerSettings
                {
                    PublicKey = Convert.FromBase64String(RequiredValue(options.PublicKey, nameof(options.PublicKey))),
                    PrivateKey = Convert.FromBase64String(RequiredValue(options.PrivateKey, nameof(options.PrivateKey))),
                    Psk = Convert.FromBase64String(RequiredValue(options.Psk, nameof(options.Psk))),
                    BindTo = options.BindTo.ToArray(),
                    Peers = peerRoutes.Select(route => route.Peer).ToArray(),
                    UdpSocketBufferBytes = GetTunUdpSocketBufferBytes(options)
                };

                using var server = new PNetMeshServer(settings, serviceProvider, loggerFactory.CreateLogger<PNetMeshServer>());
                server.Start();

                var bridge = new PNetMeshTunBridge(
                    server,
                    tunDevice,
                    peerRoutes,
                    loggerFactory.CreateLogger<PNetMeshTunBridge>());

                using var gcMetricsSignal = RegisterGcMetricsSignal(Console.Out, "/tmp/pnet-gc-metrics.log");

                Console.WriteLine($"PNet.Mesh.Tun bridge running on {tunDevice.Name}; send SIGHUP to dump GC metrics; press Ctrl+C to stop.");
                await bridge.RunAsync(shutdown.Token);
                return 0;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static PosixSignalRegistration? RegisterGcMetricsSignal(TextWriter writer, string metricsPath)
        {
            if (OperatingSystem.IsWindows())
                return null;

            return PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
            {
                context.Cancel = true;
                var line = CreateGcMetricsLine();
                writer.WriteLine(line);
                writer.Flush();
                File.AppendAllText(metricsPath, line + Environment.NewLine);
            });
        }

        static string CreateGcMetricsLine()
        {
            var info = GC.GetGCMemoryInfo();
            return string.Create(
                CultureInfo.InvariantCulture,
                $"pnet_gc_metrics allocated_bytes={GC.GetTotalAllocatedBytes(precise: true)} managed_heap_bytes={info.HeapSizeBytes} fragmented_bytes={info.FragmentedBytes} memory_load_bytes={info.MemoryLoadBytes} total_available_memory_bytes={info.TotalAvailableMemoryBytes} high_memory_load_threshold_bytes={info.HighMemoryLoadThresholdBytes} gen0_collections={GC.CollectionCount(0)} gen1_collections={GC.CollectionCount(1)} gen2_collections={GC.CollectionCount(2)}");
        }

        static int? GetTunUdpSocketBufferBytes(TunCliOptions options)
        {
            if (options.UdpSocketBufferBytes is { } bufferBytes)
                return bufferBytes;

            return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PNET_MESH_UDP_SOCKET_BUFFER_BYTES"))
                ? PNetMeshServerSettings.WireGuardSocketBufferBytes
                : null;
        }

        static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  PNet.Mesh.Tun.Cli run --interface pnet0 --mtu 1280 --address 10.80.0.1/32 --route 10.80.0.2/32 --bind 0.0.0.0:12401 --public-key <base64> --private-key <base64> --psk <base64> --peer node02:<base64-public-key>@node02:12402 --allowed-ip node02=10.80.0.2/32");
            Console.Error.WriteLine("  PNet.Mesh.Tun.Cli icmp-echo --bind 0.0.0.0:12402 --public-key <base64> --private-key <base64> --psk <base64> --peer node01:<base64-public-key>@node01:51820");
            Console.Error.WriteLine("  PNet.Mesh.Tun.Cli tun-icmp-echo --interface pnet0 --mtu 1280 --address 10.80.0.1/24 --route 10.80.0.2/32 --tun-echo-mode direct|bridge-queue");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --interface <name>          TUN interface name, default pnet0.");
            Console.Error.WriteLine("  --attach-existing           Attach to an existing interface name instead of requiring exclusive creation.");
            Console.Error.WriteLine("  --no-configure-interface    Skip ip addr/link/route commands.");
            Console.Error.WriteLine("  --mtu <bytes>               Interface MTU, default 1280.");
            Console.Error.WriteLine("  --address <prefix>          Local address prefix to add to the interface. Repeatable.");
            Console.Error.WriteLine("  --route <prefix>            Kernel route to add via the interface. Repeatable.");
            Console.Error.WriteLine("  --bind <ip:port>            UDP bind endpoint. Repeatable.");
            Console.Error.WriteLine("  --udp-socket-buffer-bytes <bytes> UDP send/receive socket buffer size, default 7340032.");
            Console.Error.WriteLine("  --public-key <base64>       Local WireGuard public key.");
            Console.Error.WriteLine("  --public-key-file <path>    Read the local WireGuard public key from a file.");
            Console.Error.WriteLine("  --private-key <base64>      Local WireGuard private key.");
            Console.Error.WriteLine("  --private-key-file <path>   Read the local WireGuard private key from a file.");
            Console.Error.WriteLine("  --psk <base64>              Mesh pre-shared key.");
            Console.Error.WriteLine("  --psk-file <path>           Read the mesh pre-shared key from a file.");
            Console.Error.WriteLine("  --peer <name:key@endpoint>  Remote peer identity and endpoint. Repeatable.");
            Console.Error.WriteLine("  --allowed-ip <name=prefix>  Allowed source/destination prefix for a peer. Repeatable.");
            Console.Error.WriteLine("  --tun-echo-mode <mode>      TUN-only ICMP echo mode: direct or bridge-queue.");
            Console.Error.WriteLine("  --verbose                   Enable debug logging.");
        }

        static async Task RunIcmpEchoResponderAsync(
            TunCliOptions options,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            var bindTo = IPEndPoint.Parse(options.BindTo[0]);
            using var udp = new UdpClient(bindTo);
            var protocol = new PNetMeshProtocol(
                Convert.FromBase64String(RequiredValue(options.PrivateKey, nameof(options.PrivateKey))),
                Convert.FromBase64String(RequiredValue(options.PublicKey, nameof(options.PublicKey))),
                Convert.FromBase64String(RequiredValue(options.Psk, nameof(options.Psk))));

            var logger = loggerFactory.CreateLogger("PNet.Mesh.Tun.IcmpEcho");
            var encrypted = new byte[ushort.MaxValue];
            var plaintext = new byte[ushort.MaxValue];
            var reply = new byte[ushort.MaxValue];
            var senderIndex = 0x7000u;
            PNetMeshTransport2? transport = null;

            Console.WriteLine($"PNet.Mesh ICMP echo responder running on {bindTo}; press Ctrl+C to stop.");
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var datagram = await udp.ReceiveAsync(cancellationToken);
                    if (!protocol.ValidatePacket(datagram.Buffer)
                        || !PNetMeshPacketFraming.TryReadMessageType(datagram.Buffer, out var messageType))
                    {
                        continue;
                    }

                    if (messageType == PNetMeshMessageType.HandshakeInitiation)
                    {
                        using var responder = protocol.CreateResponder(++senderIndex);
                        if (!responder.TryReadInitiationMessage(datagram.Buffer)
                            || !responder.TryWriteResponseMessage(encrypted, out var bytesWritten, out var nextTransport))
                        {
                            logger.LogWarning("ICMP echo responder rejected handshake initiation.");
                            continue;
                        }

                        transport?.Dispose();
                        transport = nextTransport;
                        await udp.SendAsync(encrypted.AsMemory(0, bytesWritten), datagram.RemoteEndPoint, cancellationToken);
                        logger.LogInformation("ICMP echo responder handshake complete.");
                        continue;
                    }

                    if (messageType != PNetMeshMessageType.PacketData || transport is null)
                        continue;

                    if (!transport.TryReadPlaintext(datagram.Buffer, plaintext, out var received))
                        continue;

                    if (!PNetMeshIcmpEcho.TryCreateIPv4EchoReply(
                            plaintext.AsSpan(0, received.BytesWritten),
                            reply,
                            out var replyBytes))
                    {
                        continue;
                    }

                    transport.WriteMessage(reply.AsSpan(0, replyBytes), encrypted, out var encryptedBytes, out _);
                    await udp.SendAsync(encrypted.AsMemory(0, encryptedBytes), datagram.RemoteEndPoint, cancellationToken);
                }
            }
            finally
            {
                transport?.Dispose();
            }
        }

        static async Task RunTunIcmpEchoResponderAsync(
            TunCliOptions options,
            CancellationToken cancellationToken)
        {
            await using var tunDevice = await LinuxTunDevice.CreateAsync(
                options.InterfaceName,
                options.Mtu,
                exclusive: options.ExclusiveInterface,
                cancellationToken: cancellationToken);

            if (options.ConfigureInterface)
            {
                await LinuxTunInterfaceConfigurator.ConfigureAsync(new LinuxTunInterfaceConfiguration
                {
                    InterfaceName = tunDevice.Name,
                    Mtu = options.Mtu,
                    Addresses = options.Addresses,
                    Routes = options.Routes
                }, cancellationToken: cancellationToken);
            }

            var stats = new TunIcmpEchoStats(options.TunEchoMode);
            using var dumpSignal = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
            {
                context.Cancel = true;
                stats.WriteSummary(Console.Out);
            });

            Console.WriteLine($"PNet.Mesh TUN ICMP echo responder running on {tunDevice.Name} mode={FormatTunEchoMode(options.TunEchoMode)}; send SIGHUP to dump metrics.");
            try
            {
                if (options.TunEchoMode == TunIcmpEchoMode.BridgeQueue)
                    await RunQueuedTunIcmpEchoAsync(tunDevice, stats, cancellationToken);
                else
                    await RunDirectTunIcmpEchoAsync(tunDevice, stats, cancellationToken);
            }
            finally
            {
                stats.WriteSummary(Console.Out);
            }
        }

        static async Task RunDirectTunIcmpEchoAsync(
            ITunDevice tunDevice,
            TunIcmpEchoStats stats,
            CancellationToken cancellationToken)
        {
            var packet = new byte[ushort.MaxValue];
            var reply = new byte[ushort.MaxValue];

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await tunDevice.ReadPacketAsync(packet, cancellationToken);
                var started = Stopwatch.GetTimestamp();
                var transformed = Stopwatch.GetTimestamp();
                if (!PNetMeshIcmpEcho.TryCreateIPv4EchoReply(packet.AsSpan(0, bytesRead), reply, out var replyBytes))
                    continue;

                var transformFinished = Stopwatch.GetTimestamp();
                await tunDevice.WritePacketAsync(reply.AsMemory(0, replyBytes), cancellationToken);
                var writeFinished = Stopwatch.GetTimestamp();
                stats.Record(
                    transformFinished - transformed,
                    queueTicks: 0,
                    writeFinished - transformFinished,
                    writeFinished - started);
            }
        }

        static async Task RunQueuedTunIcmpEchoAsync(
            ITunDevice tunDevice,
            TunIcmpEchoStats stats,
            CancellationToken cancellationToken)
        {
            var queue = Channel.CreateBounded<QueuedTunPacket>(new BoundedChannelOptions(256)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            var writer = Task.Run(() => RunQueuedTunIcmpEchoWriterAsync(tunDevice, queue.Reader, stats, cancellationToken), cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    IMemoryOwner<byte>? owner = MemoryPool<byte>.Shared.Rent(ushort.MaxValue);
                    try
                    {
                        var bytesRead = await tunDevice.ReadPacketAsync(owner.Memory, cancellationToken);
                        await queue.Writer.WriteAsync(
                            new QueuedTunPacket(owner, bytesRead, Stopwatch.GetTimestamp()),
                            cancellationToken);
                        owner = null;
                    }
                    finally
                    {
                        owner?.Dispose();
                    }
                }
            }
            finally
            {
                queue.Writer.TryComplete();
                try
                {
                    await writer;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }

        static async Task RunQueuedTunIcmpEchoWriterAsync(
            ITunDevice tunDevice,
            ChannelReader<QueuedTunPacket> reader,
            TunIcmpEchoStats stats,
            CancellationToken cancellationToken)
        {
            var reply = new byte[ushort.MaxValue];
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                while (reader.TryRead(out var packet))
                {
                    using var owner = packet.MemoryOwner;
                    var dequeued = Stopwatch.GetTimestamp();
                    if (!PNetMeshIcmpEcho.TryCreateIPv4EchoReply(
                            owner.Memory.Span[..packet.Length],
                            reply,
                            out var replyBytes))
                    {
                        continue;
                    }

                    var transformed = Stopwatch.GetTimestamp();
                    await tunDevice.WritePacketAsync(reply.AsMemory(0, replyBytes), cancellationToken);
                    var written = Stopwatch.GetTimestamp();
                    stats.Record(
                        transformed - dequeued,
                        dequeued - packet.EnqueuedTimestamp,
                        written - transformed,
                        written - packet.EnqueuedTimestamp);
                }
            }
        }

        static string FormatTunEchoMode(TunIcmpEchoMode mode)
        {
            return mode switch
            {
                TunIcmpEchoMode.Direct => "direct",
                TunIcmpEchoMode.BridgeQueue => "bridge-queue",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        readonly struct QueuedTunPacket
        {
            public QueuedTunPacket(IMemoryOwner<byte> memoryOwner, int length, long enqueuedTimestamp)
            {
                MemoryOwner = memoryOwner;
                Length = length;
                EnqueuedTimestamp = enqueuedTimestamp;
            }

            public IMemoryOwner<byte> MemoryOwner { get; }

            public int Length { get; }

            public long EnqueuedTimestamp { get; }
        }

        sealed class TunIcmpEchoStats
        {
            readonly object _gate = new object();
            readonly TunIcmpEchoMode _mode;
            long _packets;
            long _transformTicks;
            long _queueTicks;
            long _writeTicks;
            long _postReadTicks;
            long _postReadMinTicks = long.MaxValue;
            long _postReadMaxTicks;

            public TunIcmpEchoStats(TunIcmpEchoMode mode)
            {
                _mode = mode;
            }

            public void Record(long transformTicks, long queueTicks, long writeTicks, long postReadTicks)
            {
                lock (_gate)
                {
                    _packets++;
                    _transformTicks += transformTicks;
                    _queueTicks += queueTicks;
                    _writeTicks += writeTicks;
                    _postReadTicks += postReadTicks;
                    _postReadMinTicks = Math.Min(_postReadMinTicks, postReadTicks);
                    _postReadMaxTicks = Math.Max(_postReadMaxTicks, postReadTicks);
                }
            }

            public void WriteSummary(TextWriter writer)
            {
                long packets;
                long transformTicks;
                long queueTicks;
                long writeTicks;
                long postReadTicks;
                long minTicks;
                long maxTicks;

                lock (_gate)
                {
                    packets = _packets;
                    transformTicks = _transformTicks;
                    queueTicks = _queueTicks;
                    writeTicks = _writeTicks;
                    postReadTicks = _postReadTicks;
                    minTicks = _postReadMinTicks == long.MaxValue ? 0 : _postReadMinTicks;
                    maxTicks = _postReadMaxTicks;
                }

                var divisor = Math.Max(1, packets);
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"tun_icmp_echo_metrics mode={FormatTunEchoMode(_mode)} packets={packets} transform_avg_us={ToMicroseconds(transformTicks) / divisor:F3} queue_avg_us={ToMicroseconds(queueTicks) / divisor:F3} write_avg_us={ToMicroseconds(writeTicks) / divisor:F3} post_read_avg_us={ToMicroseconds(postReadTicks) / divisor:F3} post_read_min_us={ToMicroseconds(minTicks):F3} post_read_max_us={ToMicroseconds(maxTicks):F3}"));
            }

            static double ToMicroseconds(long ticks)
            {
                return ticks * 1_000_000.0 / Stopwatch.Frequency;
            }
        }

        enum TunCliMode
        {
            Run,
            IcmpEcho,
            TunIcmpEcho
        }

        enum TunIcmpEchoMode
        {
            Direct,
            BridgeQueue
        }

        sealed class TunCliOptions
        {
            readonly List<PeerSpec> _peers = new List<PeerSpec>();
            readonly Dictionary<string, List<IpPrefix>> _allowedIps = new Dictionary<string, List<IpPrefix>>(StringComparer.OrdinalIgnoreCase);

            public TunCliMode Mode { get; private set; } = TunCliMode.Run;

            public string InterfaceName { get; private set; } = "pnet0";

            public bool ExclusiveInterface { get; private set; } = true;

            public bool ConfigureInterface { get; private set; } = true;

            public bool Verbose { get; private set; }

            public int Mtu { get; private set; } = 1280;

            public List<IpPrefix> Addresses { get; } = new List<IpPrefix>();

            public List<IpPrefix> Routes { get; } = new List<IpPrefix>();

            public List<string> BindTo { get; } = new List<string>();

            public int? UdpSocketBufferBytes { get; private set; }

            public string? PublicKey { get; private set; }

            public string? PrivateKey { get; private set; }

            public string? Psk { get; private set; }

            public TunIcmpEchoMode TunEchoMode { get; private set; } = TunIcmpEchoMode.Direct;

            public static bool TryParse(string[] args, [NotNullWhen(true)] out TunCliOptions? options, out string? error)
            {
                options = new TunCliOptions();
                error = null;

                if (args.Length == 0 || IsHelp(args[0]))
                    return false;

                var index = 0;
                if (string.Equals(args[index], "run", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = TunCliMode.Run;
                    index++;
                }
                else if (string.Equals(args[index], "icmp-echo", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = TunCliMode.IcmpEcho;
                    index++;
                }
                else if (string.Equals(args[index], "tun-icmp-echo", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = TunCliMode.TunIcmpEcho;
                    index++;
                }

                while (index < args.Length)
                {
                    var option = args[index++];
                    switch (option)
                    {
                        case "--interface":
                            options.InterfaceName = NextValue(args, ref index, option, ref error);
                            break;
                        case "--attach-existing":
                            options.ExclusiveInterface = false;
                            break;
                        case "--no-configure-interface":
                            options.ConfigureInterface = false;
                            break;
                        case "--verbose":
                            options.Verbose = true;
                            break;
                        case "--mtu":
                            if (!int.TryParse(NextValue(args, ref index, option, ref error), NumberStyles.None, CultureInfo.InvariantCulture, out var mtu) || mtu <= 0)
                            {
                                error = "Invalid --mtu value.";
                                return false;
                            }
                            options.Mtu = mtu;
                            break;
                        case "--address":
                            if (!TryAddPrefix(options.Addresses, NextValue(args, ref index, option, ref error), option, ref error))
                                return false;
                            break;
                        case "--route":
                            if (!TryAddPrefix(options.Routes, NextValue(args, ref index, option, ref error), option, ref error))
                                return false;
                            break;
                        case "--bind":
                            options.BindTo.Add(NextValue(args, ref index, option, ref error));
                            break;
                        case "--udp-socket-buffer-bytes":
                            if (!int.TryParse(NextValue(args, ref index, option, ref error), NumberStyles.None, CultureInfo.InvariantCulture, out var udpSocketBufferBytes) || udpSocketBufferBytes <= 0)
                            {
                                error = "Invalid --udp-socket-buffer-bytes value.";
                                return false;
                            }
                            options.UdpSocketBufferBytes = udpSocketBufferBytes;
                            break;
                        case "--public-key":
                            options.PublicKey = NextValue(args, ref index, option, ref error);
                            break;
                        case "--public-key-file":
                            if (!TryReadKeyFile(NextValue(args, ref index, option, ref error), option, out var publicKey, ref error))
                                return false;
                            options.PublicKey = publicKey;
                            break;
                        case "--private-key":
                            options.PrivateKey = NextValue(args, ref index, option, ref error);
                            break;
                        case "--private-key-file":
                            if (!TryReadKeyFile(NextValue(args, ref index, option, ref error), option, out var privateKey, ref error))
                                return false;
                            options.PrivateKey = privateKey;
                            break;
                        case "--psk":
                            options.Psk = NextValue(args, ref index, option, ref error);
                            break;
                        case "--psk-file":
                            if (!TryReadKeyFile(NextValue(args, ref index, option, ref error), option, out var psk, ref error))
                                return false;
                            options.Psk = psk;
                            break;
                        case "--peer":
                            if (!PeerSpec.TryParse(NextValue(args, ref index, option, ref error), out var peer, out error))
                                return false;
                            options._peers.Add(peer);
                            break;
                        case "--allowed-ip":
                            if (!options.TryAddAllowedIp(NextValue(args, ref index, option, ref error), ref error))
                                return false;
                            break;
                        case "--tun-echo-mode":
                            if (!TryReadTunEchoMode(NextValue(args, ref index, option, ref error), out var tunEchoMode))
                            {
                                error = "--tun-echo-mode requires one of: direct, bridge-queue.";
                                return false;
                            }
                            options.TunEchoMode = tunEchoMode;
                            break;
                        case "--help":
                        case "-h":
                            return false;
                        default:
                            error = $"Unknown option '{option}'.";
                            return false;
                    }

                    if (error != null)
                        return false;
                }

                return options.Validate(ref error);
            }

            public IReadOnlyList<PNetMeshTunPeerRoute> CreatePeerRoutes()
            {
                return _peers.Select(peer => new PNetMeshTunPeerRoute
                {
                    Name = peer.Name,
                    Peer = new PNetMeshPeer
                    {
                        PublicKey = Convert.FromBase64String(RequiredValue(peer.PublicKey, nameof(peer.PublicKey))),
                        EndPoints = new[] { peer.Endpoint }
                    },
                    AllowedIPs = _allowedIps[peer.Name].ToArray()
                }).ToArray();
            }

            bool Validate(ref string? error)
            {
                if (string.IsNullOrWhiteSpace(InterfaceName))
                {
                    error = "Interface name is required.";
                    return false;
                }
                if (Mode == TunCliMode.TunIcmpEcho)
                {
                    if (Addresses.Count == 0)
                    {
                        error = "At least one --address prefix is required.";
                        return false;
                    }
                    if (Routes.Count == 0)
                    {
                        error = "At least one --route prefix is required.";
                        return false;
                    }

                    return true;
                }
                if (BindTo.Count == 0)
                {
                    error = "At least one --bind endpoint is required.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(PublicKey) || string.IsNullOrWhiteSpace(PrivateKey) || string.IsNullOrWhiteSpace(Psk))
                {
                    error = "--public-key, --private-key, and --psk are required.";
                    return false;
                }
                if (_peers.Count == 0)
                {
                    error = "At least one --peer is required.";
                    return false;
                }

                if (Mode == TunCliMode.Run)
                {
                    foreach (var peer in _peers)
                    {
                        if (!_allowedIps.TryGetValue(peer.Name, out var prefixes) || prefixes.Count == 0)
                        {
                            error = $"Peer '{peer.Name}' requires at least one --allowed-ip.";
                            return false;
                        }
                    }
                }

                try
                {
                    Convert.FromBase64String(RequiredValue(PublicKey, nameof(PublicKey)));
                    Convert.FromBase64String(RequiredValue(PrivateKey, nameof(PrivateKey)));
                    Convert.FromBase64String(RequiredValue(Psk, nameof(Psk)));
                    foreach (var peer in _peers)
                        Convert.FromBase64String(RequiredValue(peer.PublicKey, nameof(peer.PublicKey)));
                }
                catch (FormatException ex)
                {
                    error = $"Invalid base64 key: {ex.Message}";
                    return false;
                }

                return true;
            }

            bool TryAddAllowedIp(string value, ref string? error)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    error ??= "Invalid --allowed-ip. Expected name=prefix.";
                    return false;
                }

                var separator = value.IndexOf('=');
                if (separator <= 0 || separator == value.Length - 1)
                {
                    error = "Invalid --allowed-ip. Expected name=prefix.";
                    return false;
                }

                var name = value[..separator];
                if (!IpPrefix.TryParse(value[(separator + 1)..], out var prefix))
                {
                    error = $"Invalid --allowed-ip prefix '{value[(separator + 1)..]}'.";
                    return false;
                }

                if (!_allowedIps.TryGetValue(name, out var prefixes))
                {
                    prefixes = new List<IpPrefix>();
                    _allowedIps[name] = prefixes;
                }

                prefixes.Add(prefix);
                return true;
            }

            static bool TryReadTunEchoMode(string value, out TunIcmpEchoMode mode)
            {
                if (string.Equals(value, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    mode = TunIcmpEchoMode.Direct;
                    return true;
                }
                if (string.Equals(value, "bridge-queue", StringComparison.OrdinalIgnoreCase))
                {
                    mode = TunIcmpEchoMode.BridgeQueue;
                    return true;
                }

                mode = default;
                return false;
            }

            static bool TryAddPrefix(List<IpPrefix> prefixes, string value, string option, ref string? error)
            {
                if (!IpPrefix.TryParse(value, out var prefix))
                {
                    error = $"Invalid {option} prefix '{value}'.";
                    return false;
                }

                prefixes.Add(prefix);
                return true;
            }

            static string NextValue(string[] args, ref int index, string option, ref string? error)
            {
                if (index >= args.Length)
                {
                    error = $"{option} requires a value.";
                    return string.Empty;
                }

                return args[index++];
            }

            static bool TryReadKeyFile(string path, string option, [NotNullWhen(true)] out string? value, ref string? error)
            {
                value = null;
                if (error != null)
                    return false;

                try
                {
                    value = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return true;

                    error = $"{option} file is empty.";
                    return false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    error = $"{option} could not be read: {ex.Message}";
                    return false;
                }
            }

            static bool IsHelp(string value)
            {
                return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
            }
        }

        sealed class PeerSpec
        {
            public required string Name { get; init; }

            public required string PublicKey { get; init; }

            public required string Endpoint { get; init; }

            public static bool TryParse(string value, [NotNullWhen(true)] out PeerSpec? peer, out string? error)
            {
                peer = null;
                error = null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "Invalid --peer. Expected name:base64-public-key@endpoint.";
                    return false;
                }

                var nameSeparator = value.IndexOf(':');
                var endpointSeparator = value.IndexOf('@');
                if (nameSeparator <= 0 || endpointSeparator <= nameSeparator + 1 || endpointSeparator == value.Length - 1)
                {
                    error = "Invalid --peer. Expected name:base64-public-key@endpoint.";
                    return false;
                }

                peer = new PeerSpec
                {
                    Name = value[..nameSeparator],
                    PublicKey = value[(nameSeparator + 1)..endpointSeparator],
                    Endpoint = value[(endpointSeparator + 1)..]
                };
                return true;
            }
        }

        static string RequiredValue(string? value, string name)
        {
            return value ?? throw new InvalidOperationException($"{name} is required.");
        }
    }
}
