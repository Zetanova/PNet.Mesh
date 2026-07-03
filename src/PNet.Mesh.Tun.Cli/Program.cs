using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PNet.Mesh;
using PNet.Mesh.Tun;
using PNet.Mesh.Tun.Linux;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
                    Peers = peerRoutes.Select(route => route.Peer).ToArray()
                };

                using var server = new PNetMeshServer(settings, serviceProvider, loggerFactory.CreateLogger<PNetMeshServer>());
                server.Start();

                var bridge = new PNetMeshTunBridge(
                    server,
                    tunDevice,
                    peerRoutes,
                    loggerFactory.CreateLogger<PNetMeshTunBridge>());

                Console.WriteLine($"PNet.Mesh.Tun bridge running on {tunDevice.Name}; press Ctrl+C to stop.");
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

        static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  PNet.Mesh.Tun.Cli run --interface pnet0 --mtu 1280 --address 10.80.0.1/32 --route 10.80.0.2/32 --bind 0.0.0.0:12401 --public-key <base64> --private-key <base64> --psk <base64> --peer node02:<base64-public-key>@node02:12402 --allowed-ip node02=10.80.0.2/32");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --interface <name>          TUN interface name, default pnet0.");
            Console.Error.WriteLine("  --attach-existing           Attach to an existing interface name instead of requiring exclusive creation.");
            Console.Error.WriteLine("  --no-configure-interface    Skip ip addr/link/route commands.");
            Console.Error.WriteLine("  --mtu <bytes>               Interface MTU, default 1280.");
            Console.Error.WriteLine("  --address <prefix>          Local address prefix to add to the interface. Repeatable.");
            Console.Error.WriteLine("  --route <prefix>            Kernel route to add via the interface. Repeatable.");
            Console.Error.WriteLine("  --bind <ip:port>            UDP bind endpoint. Repeatable.");
            Console.Error.WriteLine("  --public-key <base64>       Local WireGuard public key.");
            Console.Error.WriteLine("  --public-key-file <path>    Read the local WireGuard public key from a file.");
            Console.Error.WriteLine("  --private-key <base64>      Local WireGuard private key.");
            Console.Error.WriteLine("  --private-key-file <path>   Read the local WireGuard private key from a file.");
            Console.Error.WriteLine("  --psk <base64>              Mesh pre-shared key.");
            Console.Error.WriteLine("  --psk-file <path>           Read the mesh pre-shared key from a file.");
            Console.Error.WriteLine("  --peer <name:key@endpoint>  Remote peer identity and endpoint. Repeatable.");
            Console.Error.WriteLine("  --allowed-ip <name=prefix>  Allowed source/destination prefix for a peer. Repeatable.");
            Console.Error.WriteLine("  --verbose                   Enable debug logging.");
        }

        sealed class TunCliOptions
        {
            readonly List<PeerSpec> _peers = new List<PeerSpec>();
            readonly Dictionary<string, List<IpPrefix>> _allowedIps = new Dictionary<string, List<IpPrefix>>(StringComparer.OrdinalIgnoreCase);

            public string InterfaceName { get; private set; } = "pnet0";

            public bool ExclusiveInterface { get; private set; } = true;

            public bool ConfigureInterface { get; private set; } = true;

            public bool Verbose { get; private set; }

            public int Mtu { get; private set; } = 1280;

            public List<IpPrefix> Addresses { get; } = new List<IpPrefix>();

            public List<IpPrefix> Routes { get; } = new List<IpPrefix>();

            public List<string> BindTo { get; } = new List<string>();

            public string? PublicKey { get; private set; }

            public string? PrivateKey { get; private set; }

            public string? Psk { get; private set; }

            public static bool TryParse(string[] args, [NotNullWhen(true)] out TunCliOptions? options, out string? error)
            {
                options = new TunCliOptions();
                error = null;

                if (args.Length == 0 || IsHelp(args[0]))
                    return false;

                var index = 0;
                if (string.Equals(args[index], "run", StringComparison.OrdinalIgnoreCase))
                    index++;

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

                foreach (var peer in _peers)
                {
                    if (!_allowedIps.TryGetValue(peer.Name, out var prefixes) || prefixes.Count == 0)
                    {
                        error = $"Peer '{peer.Name}' requires at least one --allowed-ip.";
                        return false;
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
