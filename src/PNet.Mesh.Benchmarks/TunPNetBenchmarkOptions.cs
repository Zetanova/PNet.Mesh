using System.Globalization;

namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-benchmark pnet-mesh-tun|wireguard-go [--name <name>] [--image <image>] [--ping-count <count>] [--warmup <duration>] [--iperf-duration <duration>] [--mtu <bytes>] [--payload-mode control|mtu] [--timeout <duration>]");
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

        public string IperfBandwidth { get; private init; } = "1K";

        public int IperfDatagramBytes { get; private init; } = 64;

        public string CommandLine { get; private init; } = "--tun-benchmark";

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunPNetBenchmarkOptions options)
        {
            options = new TunPNetBenchmarkOptions();
            if (args.Length == 0 || IsHelp(args[0]))
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

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--name":
                        if (!TryReadValue(args, ref i, out name))
                        {
                            error.WriteLine("--name requires a value.");
                            return false;
                        }
                        break;
                    case "--image":
                        if (!TryReadValue(args, ref i, out image))
                        {
                            error.WriteLine("--image requires a value.");
                            return false;
                        }
                        break;
                    case "--timeout":
                        if (!TryReadDurationValue(args, ref i, out commandTimeout) || commandTimeout <= TimeSpan.Zero)
                        {
                            error.WriteLine("--timeout requires a positive duration.");
                            return false;
                        }
                        break;
                    case "--warmup":
                        if (!TryReadDurationValue(args, ref i, out warmup) || warmup < TimeSpan.Zero)
                        {
                            error.WriteLine("--warmup requires a non-negative duration.");
                            return false;
                        }
                        break;
                    case "--iperf-duration":
                        if (!TryReadDurationValue(args, ref i, out iperfDuration) || iperfDuration <= TimeSpan.Zero)
                        {
                            error.WriteLine("--iperf-duration requires a positive duration.");
                            return false;
                        }
                        break;
                    case "--ping-count":
                        if (!TryReadIntValue(args, ref i, out pingCount) || pingCount <= 0)
                        {
                            error.WriteLine("--ping-count requires a positive integer.");
                            return false;
                        }
                        break;
                    case "--mtu":
                        if (!TryReadIntValue(args, ref i, out mtu) || mtu <= 0)
                        {
                            error.WriteLine("--mtu requires a positive integer.");
                            return false;
                        }
                        break;
                    case "--payload-mode":
                        if (!TryReadValue(args, ref i, out payloadMode) || !IsSupportedPayloadMode(payloadMode))
                        {
                            error.WriteLine("--payload-mode requires one of: control, mtu.");
                            return false;
                        }
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
                IperfBandwidth = GetIperfBandwidth(payloadMode),
                IperfDatagramBytes = GetIperfDatagramBytes(payloadMode, mtu),
                CommandLine = CreateCommandLine(args)
            };
            return true;
        }

        static bool TryReadValue(string[] args, ref int index, out string value)
        {
            value = string.Empty;
            if (++index >= args.Length)
                return false;

            value = args[index];
            return !string.IsNullOrWhiteSpace(value);
        }

        static bool TryReadIntValue(string[] args, ref int index, out int value)
        {
            value = 0;
            return TryReadValue(args, ref index, out var text)
                   && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        static bool TryReadDurationValue(string[] args, ref int index, out TimeSpan value)
        {
            value = default;
            return TryReadValue(args, ref index, out var text) && TryReadDuration(text, out value);
        }

        static bool TryReadDuration(string text, out TimeSpan value)
        {
            value = default;
            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            {
                value = TimeSpan.FromMilliseconds(milliseconds);
                return true;
            }

            if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                value = TimeSpan.FromSeconds(seconds);
                return true;
            }

            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
        }

        static bool IsHelp(string value)
        {
            return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsSupportedScenario(string value)
        {
            return string.Equals(value, PNetMeshTunScenario, StringComparison.Ordinal)
                   || string.Equals(value, WireGuardGoScenario, StringComparison.Ordinal);
        }

        static bool IsSupportedPayloadMode(string value)
        {
            return string.Equals(value, "control", StringComparison.Ordinal)
                   || string.Equals(value, "mtu", StringComparison.Ordinal);
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
