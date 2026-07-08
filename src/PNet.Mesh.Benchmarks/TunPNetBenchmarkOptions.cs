namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-benchmark pnet-mesh-tun|wireguard-go|wireguard-go-pnet-icmp-echo|tun-icmp-echo-direct|tun-icmp-echo-bridge-queue [--name <name>] [--image <image>] [--ping-count <count>] [--warmup <duration>] [--iperf-duration <duration>] [--iperf-bytes <bytes>] [--iperf-protocol tcp|udp] [--iperf-bandwidth <rate>] [--iperf-window <size>] [--mtu <bytes>] [--payload-mode control|mtu] [--managed-heap-growth-limit-bytes <bytes>] [--timeout <duration>] [--trace-output-dir <dir>] [--pnet-udp-receive-mode async|blocking] [--pnet-udp-socket-buffer-bytes <bytes>]");
        output.WriteLine();
        output.WriteLine("Runs a manual privileged TUN traffic benchmark on the #060 topology and emits JSON.");
    }

    internal sealed class TunPNetBenchmarkOptions
    {
        public string Scenario { get; private init; } = PNetMeshTunScenario;

        public string Name { get; private init; } = DefaultName;

        public string Image { get; private init; } = DefaultImage;

        public TimeSpan CommandTimeout { get; private init; } = TimeSpan.FromSeconds(30);

        public TimeSpan ProcessStartTimeout { get; private init; } = TimeSpan.FromSeconds(10);

        public TimeSpan Warmup { get; private init; } = TimeSpan.FromSeconds(2);

        public TimeSpan IperfDuration { get; private init; } = TimeSpan.FromSeconds(3);

        public int PingCount { get; private init; } = 1;

        public int IperfPort { get; private init; } = 5201;

        public int Mtu { get; private init; } = 1280;

        public string PayloadMode { get; private init; } = "control";

        public string? PacketTraceOutputDirectory { get; private init; }

        public string? PNetUdpReceiveMode { get; private init; }

        public int? PNetUdpSocketBufferBytes { get; private init; }

        public string IperfBandwidth { get; private init; } = "1K";

        public string IperfProtocol { get; private init; } = "udp";

        public string IperfWindow { get; private init; } = "4M";

        public int IperfDatagramBytes { get; private init; } = 64;

        public long? IperfBytes { get; private init; }

        public long ManagedHeapGrowthLimitBytes { get; private init; } = 16 * 1024 * 1024;

        public string CommandLine { get; private init; } = "--tun-benchmark";

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunPNetBenchmarkOptions options)
        {
            options = new TunPNetBenchmarkOptions();
            if (args.Length == 0 || TunBenchmarkCliParser.IsHelp(args[0]))
            {
                options = new TunPNetBenchmarkOptions { ShowHelp = true, CommandLine = CreateCommandLine(args) };
                return true;
            }

            if (!IsSupportedScenario(args[0]))
            {
                error.WriteLine($"Unknown TUN benchmark scenario '{args[0]}'.");
                return false;
            }

            var scenario = args[0];
            var name = DefaultName;
            var image = DefaultImage;
            var commandTimeout = TimeSpan.FromSeconds(30);
            var warmup = TimeSpan.FromSeconds(2);
            var iperfDuration = TimeSpan.FromSeconds(3);
            var pingCount = 1;
            var mtu = 1280;
            var payloadMode = "control";
            var iperfProtocol = "udp";
            string? iperfBandwidth = null;
            var iperfWindow = "4M";
            long? iperfBytes = null;
            var managedHeapGrowthLimitBytes = 16L * 1024 * 1024;
            string? packetTraceOutputDirectory = null;
            string? pnetUdpReceiveMode = null;
            int? pnetUdpSocketBufferBytes = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--name":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out name))
                        {
                            error.WriteLine("--name requires a value.");
                            return false;
                        }
                        break;
                    case "--image":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out image))
                        {
                            error.WriteLine("--image requires a value.");
                            return false;
                        }
                        break;
                    case "--timeout":
                        if (!TunBenchmarkCliParser.TryReadDurationValue(args, ref i, out commandTimeout) || commandTimeout <= TimeSpan.Zero)
                        {
                            error.WriteLine("--timeout requires a positive duration.");
                            return false;
                        }
                        break;
                    case "--warmup":
                        if (!TunBenchmarkCliParser.TryReadDurationValue(args, ref i, out warmup) || warmup < TimeSpan.Zero)
                        {
                            error.WriteLine("--warmup requires a non-negative duration.");
                            return false;
                        }
                        break;
                    case "--iperf-duration":
                        if (!TunBenchmarkCliParser.TryReadDurationValue(args, ref i, out iperfDuration) || iperfDuration <= TimeSpan.Zero)
                        {
                            error.WriteLine("--iperf-duration requires a positive duration.");
                            return false;
                        }
                        break;
                    case "--iperf-bytes":
                        if (!TunBenchmarkCliParser.TryReadInt64Value(args, ref i, out var parsedIperfBytes) || parsedIperfBytes <= 0)
                        {
                            error.WriteLine("--iperf-bytes requires a positive integer.");
                            return false;
                        }

                        iperfBytes = parsedIperfBytes;
                        break;
                    case "--iperf-bandwidth":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out iperfBandwidth))
                        {
                            error.WriteLine("--iperf-bandwidth requires a value.");
                            return false;
                        }
                        break;
                    case "--iperf-protocol":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out iperfProtocol) || !IsSupportedIperfProtocol(iperfProtocol))
                        {
                            error.WriteLine("--iperf-protocol requires one of: tcp, udp.");
                            return false;
                        }
                        break;
                    case "--iperf-window":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out iperfWindow))
                        {
                            error.WriteLine("--iperf-window requires a value.");
                            return false;
                        }
                        break;
                    case "--ping-count":
                        if (!TunBenchmarkCliParser.TryReadIntValue(args, ref i, out pingCount) || pingCount <= 0)
                        {
                            error.WriteLine("--ping-count requires a positive integer.");
                            return false;
                        }
                        break;
                    case "--mtu":
                        if (!TunBenchmarkCliParser.TryReadIntValue(args, ref i, out mtu) || mtu <= 0)
                        {
                            error.WriteLine("--mtu requires a positive integer.");
                            return false;
                        }
                        break;
                    case "--payload-mode":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out payloadMode) || !TunBenchmarkCliParser.IsSupportedPayloadMode(payloadMode))
                        {
                            error.WriteLine("--payload-mode requires one of: control, mtu.");
                            return false;
                        }
                        break;
                    case "--managed-heap-growth-limit-bytes":
                        if (!TunBenchmarkCliParser.TryReadInt64Value(args, ref i, out managedHeapGrowthLimitBytes) || managedHeapGrowthLimitBytes < 0)
                        {
                            error.WriteLine("--managed-heap-growth-limit-bytes requires a non-negative integer.");
                            return false;
                        }
                        break;
                    case "--trace-output-dir":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out var traceOutputDirectory))
                        {
                            error.WriteLine("--trace-output-dir requires a value.");
                            return false;
                        }

                        packetTraceOutputDirectory = traceOutputDirectory;
                        break;
                    case "--pnet-udp-receive-mode":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out pnetUdpReceiveMode) || !IsSupportedPNetUdpReceiveMode(pnetUdpReceiveMode))
                        {
                            error.WriteLine("--pnet-udp-receive-mode requires one of: async, blocking.");
                            return false;
                        }
                        break;
                    case "--pnet-udp-socket-buffer-bytes":
                        if (!TunBenchmarkCliParser.TryReadIntValue(args, ref i, out var socketBufferBytes) || socketBufferBytes <= 0)
                        {
                            error.WriteLine("--pnet-udp-socket-buffer-bytes requires a positive integer.");
                            return false;
                        }

                        pnetUdpSocketBufferBytes = socketBufferBytes;
                        break;
                    case "--help":
                    case "-h":
                        options = new TunPNetBenchmarkOptions { ShowHelp = true };
                        return true;
                    default:
                        error.WriteLine($"Unknown TUN benchmark option '{args[i]}'.");
                        return false;
                }
            }

            options = new TunPNetBenchmarkOptions
            {
                Scenario = scenario,
                Name = name,
                Image = image,
                CommandTimeout = commandTimeout,
                Warmup = warmup,
                IperfDuration = iperfDuration,
                PingCount = pingCount,
                Mtu = mtu,
                PayloadMode = payloadMode,
                PacketTraceOutputDirectory = packetTraceOutputDirectory,
                PNetUdpReceiveMode = pnetUdpReceiveMode,
                PNetUdpSocketBufferBytes = pnetUdpSocketBufferBytes,
                IperfProtocol = iperfProtocol,
                IperfBandwidth = string.IsNullOrWhiteSpace(iperfBandwidth) ? GetIperfBandwidth(payloadMode) : iperfBandwidth,
                IperfWindow = iperfWindow,
                IperfDatagramBytes = GetIperfDatagramBytes(payloadMode, mtu),
                IperfBytes = iperfBytes,
                ManagedHeapGrowthLimitBytes = managedHeapGrowthLimitBytes,
                CommandLine = CreateCommandLine(args)
            };
            return true;
        }

        static bool IsSupportedScenario(string value)
        {
            return string.Equals(value, PNetMeshTunScenario, StringComparison.Ordinal)
                   || string.Equals(value, WireGuardGoScenario, StringComparison.Ordinal)
                   || string.Equals(value, WireGuardGoPNetIcmpEchoScenario, StringComparison.Ordinal)
                   || string.Equals(value, TunIcmpEchoDirectScenario, StringComparison.Ordinal)
                   || string.Equals(value, TunIcmpEchoBridgeQueueScenario, StringComparison.Ordinal);
        }

        static bool IsSupportedIperfProtocol(string value)
        {
            return string.Equals(value, "tcp", StringComparison.Ordinal)
                   || string.Equals(value, "udp", StringComparison.Ordinal);
        }

        static bool IsSupportedPNetUdpReceiveMode(string value)
        {
            return string.Equals(value, "async", StringComparison.Ordinal)
                   || string.Equals(value, "blocking", StringComparison.Ordinal);
        }

        static string GetIperfBandwidth(string payloadMode)
        {
            return string.Equals(payloadMode, "mtu", StringComparison.Ordinal) ? "64K" : "1K";
        }

        static int GetIperfDatagramBytes(string payloadMode, int mtu)
        {
            return string.Equals(payloadMode, "mtu", StringComparison.Ordinal)
                ? Math.Max(64, mtu - 80)
                : 64;
        }

        static string CreateCommandLine(string[] args)
        {
            return "--tun-benchmark " + string.Join(" ", args.Select(QuoteCommandLineToken));
        }

        static string QuoteCommandLineToken(string value)
        {
            if (value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character != '"' && character != '\\'))
                return value;

            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
    }
}
