using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static class TunPNetBandwidthSeriesRunner
{
    const string Kind = "pnet-mesh-tun-bandwidth-series";
    const string DefaultName = "pnet-tun-bandwidth-series";
    const string DefaultImage = "localhost/pnet-mesh-tun:dev";
    const string PNetMeshTunScenario = "pnet-mesh-tun";
    const string WireGuardGoScenario = "wireguard-go";
    const long DefaultIperfBytes = 100L * 1024 * 1024;
    const long DefaultManagedHeapGrowthLimitBytes = 16L * 1024 * 1024;

    static readonly string[] BandwidthCaps = { "10M", "25M", "50M", "100M", "250M", "500M", "1G", "2.5G" };

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TunPNetBandwidthSeriesOptions.TryParse(args, error, out var options))
            return 2;

        if (options.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        var report = RunSeries(options, SystemTunTopologyCommandRunner.Instance);
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "fail" ? 1 : 0;
    }

    internal static TunPNetBandwidthSeriesReport RunSeries(
        TunPNetBandwidthSeriesOptions options,
        ITunTopologyCommandRunner commandRunner)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            Directory.CreateDirectory(options.OutputDirectory);

        var runs = new List<TunPNetBandwidthSeriesRun>();
        foreach (var bandwidthCap in BandwidthCaps)
        {
            var benchmarkArgs = options.CreateBenchmarkArgs(bandwidthCap);
            if (!TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(benchmarkArgs, TextWriter.Null, out var benchmarkOptions))
            {
                runs.Add(new TunPNetBandwidthSeriesRun(
                    bandwidthCap,
                    "fail",
                    DateTimeOffset.UtcNow,
                    options.IperfBytes,
                    options.ManagedHeapGrowthLimitBytes,
                    null,
                    null,
                    null,
                    Array.Empty<TunPNetBandwidthSeriesTraffic>(),
                    Array.Empty<TunPNetBandwidthSeriesManagedRuntimeNode>(),
                    null,
                    "Generated benchmark arguments were invalid."));
                continue;
            }

            var report = TunPNetBenchmarkRunner.RunBenchmark(benchmarkOptions, commandRunner);
            var reportPath = WriteRunReport(options, bandwidthCap, report);
            runs.Add(CreateRunSummary(bandwidthCap, report, reportPath));
            if (report.Status == "skip")
                break;
        }

        var status = runs.Any(run => run.Status == "fail")
            ? "fail"
            : runs.Any(run => run.Status == "skip")
                ? "skip"
                : "pass";
        var message = status switch
        {
            "pass" => $"{GetScenarioDisplayName(options.Scenario)} bandwidth cap series completed.",
            "skip" => $"{GetScenarioDisplayName(options.Scenario)} bandwidth cap series skipped by one or more runs.",
            _ => $"{GetScenarioDisplayName(options.Scenario)} bandwidth cap series completed with one or more failing runs."
        };

        return new TunPNetBandwidthSeriesReport(
            Kind,
            status,
            DateTimeOffset.UtcNow,
            new TunPNetBandwidthSeriesSettings(
                options.Scenario,
                options.Name,
                options.Image,
                BandwidthCaps,
                options.IperfBytes,
                options.ManagedHeapGrowthLimitBytes,
                options.Mtu,
                options.PayloadMode,
                options.PingCount,
                options.Warmup.TotalSeconds,
                options.CommandTimeout.TotalSeconds,
                options.OutputDirectory),
            runs,
            message);
    }

    static TunPNetBandwidthSeriesRun CreateRunSummary(
        string bandwidthCap,
        TunPNetBenchmarkReport report,
        string? reportPath)
    {
        return new TunPNetBandwidthSeriesRun(
            bandwidthCap,
            report.Status,
            report.CreatedAt,
            report.Settings.IperfBytes,
            report.Settings.ManagedHeapGrowthLimitBytes,
            report.ManagedRuntime?.AllocationDeltaBytes,
            report.ManagedRuntime?.ManagedHeapDeltaBytes,
            report.ManagedRuntime?.ManagedHeapWithinLimit,
            report.Traffic
                .Where(result => result.Tool == "iperf3")
                .Select(result => new TunPNetBandwidthSeriesTraffic(
                    result.Protocol,
                    result.Bytes,
                    result.BitsPerSecond,
                    result.Seconds,
                    result.PacketLossPercent))
                .ToArray(),
            report.ManagedRuntime?.Nodes?
                .Select(node => new TunPNetBandwidthSeriesManagedRuntimeNode(
                    node.Node,
                    node.AllocationDeltaBytes,
                    node.ManagedHeapDeltaBytes,
                    node.ManagedHeapGrowthLimitBytes,
                    node.ManagedHeapWithinLimit))
                .ToArray() ?? Array.Empty<TunPNetBandwidthSeriesManagedRuntimeNode>(),
            reportPath,
            report.Message);
    }

    static string? WriteRunReport(
        TunPNetBandwidthSeriesOptions options,
        string bandwidthCap,
        TunPNetBenchmarkReport report)
    {
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            return null;

        var path = Path.Combine(options.OutputDirectory, $"{SanitizeBandwidthCap(bandwidthCap)}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    static string SanitizeBandwidthCap(string bandwidthCap)
    {
        return bandwidthCap.Replace(".", "_", StringComparison.Ordinal).ToLowerInvariant();
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-bandwidth-series [--scenario pnet-mesh-tun|wireguard-go] [--name <name>] [--image <image>] [--ping-count <count>] [--warmup <duration>] [--timeout <duration>] [--mtu <bytes>] [--payload-mode control|mtu] [--managed-heap-growth-limit-bytes <bytes>] [--output-dir <dir>]");
        output.WriteLine();
        output.WriteLine("Runs a TUN-to-TUN iperf3 series at 10M, 25M, 50M, 100M, 250M, 500M, 1G, and 2.5G with 100 MB per iperf3 transfer.");
    }

    internal sealed class TunPNetBandwidthSeriesOptions
    {
        public string Scenario { get; private init; } = PNetMeshTunScenario;

        public string Name { get; private init; } = DefaultName;

        public string Image { get; private init; } = DefaultImage;

        public int PingCount { get; private init; } = 1;

        public TimeSpan Warmup { get; private init; } = TimeSpan.FromSeconds(2);

        public TimeSpan CommandTimeout { get; private init; } = TimeSpan.FromSeconds(240);

        public int Mtu { get; private init; } = 1280;

        public string PayloadMode { get; private init; } = "mtu";

        public long IperfBytes { get; private init; } = DefaultIperfBytes;

        public long ManagedHeapGrowthLimitBytes { get; private init; } = DefaultManagedHeapGrowthLimitBytes;

        public string? OutputDirectory { get; private init; }

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunPNetBandwidthSeriesOptions options)
        {
            options = new TunPNetBandwidthSeriesOptions();
            if (args.Length > 0 && TunBenchmarkCliParser.IsHelp(args[0]))
            {
                options = new TunPNetBandwidthSeriesOptions { ShowHelp = true };
                return true;
            }

            var scenario = PNetMeshTunScenario;
            string? name = null;
            var image = DefaultImage;
            var pingCount = 1;
            var warmup = TimeSpan.FromSeconds(2);
            var commandTimeout = TimeSpan.FromSeconds(240);
            var mtu = 1280;
            var payloadMode = "mtu";
            var managedHeapGrowthLimitBytes = DefaultManagedHeapGrowthLimitBytes;
            string? outputDirectory = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--scenario":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out scenario) || !IsSupportedScenario(scenario))
                        {
                            error.WriteLine("--scenario requires one of: pnet-mesh-tun, wireguard-go.");
                            return false;
                        }
                        break;
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
                    case "--ping-count":
                        if (!TunBenchmarkCliParser.TryReadIntValue(args, ref i, out pingCount) || pingCount <= 0)
                        {
                            error.WriteLine("--ping-count requires a positive integer.");
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
                    case "--timeout":
                        if (!TunBenchmarkCliParser.TryReadDurationValue(args, ref i, out commandTimeout) || commandTimeout <= TimeSpan.Zero)
                        {
                            error.WriteLine("--timeout requires a positive duration.");
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
                    case "--output-dir":
                        if (!TunBenchmarkCliParser.TryReadValue(args, ref i, out outputDirectory))
                        {
                            error.WriteLine("--output-dir requires a value.");
                            return false;
                        }
                        break;
                    case "--help":
                    case "-h":
                        options = new TunPNetBandwidthSeriesOptions { ShowHelp = true };
                        return true;
                    default:
                        error.WriteLine($"Unknown TUN bandwidth series option '{args[i]}'.");
                        return false;
                }
            }

            options = new TunPNetBandwidthSeriesOptions
            {
                Scenario = scenario,
                Name = name ?? GetDefaultName(scenario),
                Image = image,
                PingCount = pingCount,
                Warmup = warmup,
                CommandTimeout = commandTimeout,
                Mtu = mtu,
                PayloadMode = payloadMode,
                ManagedHeapGrowthLimitBytes = managedHeapGrowthLimitBytes,
                OutputDirectory = outputDirectory
            };
            return true;
        }

        public string[] CreateBenchmarkArgs(string bandwidthCap)
        {
            return new[]
            {
                Scenario,
                "--name",
                $"{Name}-{SanitizeBandwidthCap(bandwidthCap)}",
                "--image",
                Image,
                "--ping-count",
                PingCount.ToString(CultureInfo.InvariantCulture),
                "--warmup",
                TunBenchmarkCliParser.FormatDuration(Warmup),
                "--timeout",
                TunBenchmarkCliParser.FormatDuration(CommandTimeout),
                "--mtu",
                Mtu.ToString(CultureInfo.InvariantCulture),
                "--payload-mode",
                PayloadMode,
                "--iperf-bytes",
                IperfBytes.ToString(CultureInfo.InvariantCulture),
                "--iperf-bandwidth",
                bandwidthCap,
                "--managed-heap-growth-limit-bytes",
                ManagedHeapGrowthLimitBytes.ToString(CultureInfo.InvariantCulture)
            };
        }

        static bool IsSupportedScenario(string value)
        {
            return string.Equals(value, PNetMeshTunScenario, StringComparison.Ordinal)
                   || string.Equals(value, WireGuardGoScenario, StringComparison.Ordinal);
        }

        static string GetDefaultName(string scenario)
        {
            return scenario switch
            {
                WireGuardGoScenario => "wireguard-go-bandwidth-series",
                _ => DefaultName
            };
        }

    }

    static string GetScenarioDisplayName(string scenario)
    {
        return scenario switch
        {
            PNetMeshTunScenario => "PNet.Mesh.Tun",
            WireGuardGoScenario => "wireguard-go",
            _ => scenario
        };
    }
}

internal sealed record TunPNetBandwidthSeriesReport(
    string Kind,
    string Status,
    DateTimeOffset CreatedAt,
    TunPNetBandwidthSeriesSettings Settings,
    IReadOnlyList<TunPNetBandwidthSeriesRun> Runs,
    string Message);

internal sealed record TunPNetBandwidthSeriesSettings(
    string Scenario,
    string Name,
    string Image,
    IReadOnlyList<string> BandwidthCaps,
    long IperfBytes,
    long ManagedHeapGrowthLimitBytes,
    int Mtu,
    string PayloadMode,
    int PingCount,
    double WarmupSeconds,
    double CommandTimeoutSeconds,
    string? OutputDirectory);

internal sealed record TunPNetBandwidthSeriesRun(
    string BandwidthCap,
    string Status,
    DateTimeOffset CreatedAt,
    long? IperfBytes,
    long? ManagedHeapGrowthLimitBytes,
    long? AllocationDeltaBytes,
    long? ManagedHeapDeltaBytes,
    bool? ManagedHeapWithinLimit,
    IReadOnlyList<TunPNetBandwidthSeriesTraffic> Traffic,
    IReadOnlyList<TunPNetBandwidthSeriesManagedRuntimeNode> ManagedRuntimeNodes,
    string? ReportPath,
    string Message);

internal sealed record TunPNetBandwidthSeriesTraffic(
    string Protocol,
    long? Bytes,
    double? BitsPerSecond,
    double? Seconds,
    double? PacketLossPercent);

internal sealed record TunPNetBandwidthSeriesManagedRuntimeNode(
    string Node,
    long AllocationDeltaBytes,
    long ManagedHeapDeltaBytes,
    long ManagedHeapGrowthLimitBytes,
    bool ManagedHeapWithinLimit);
