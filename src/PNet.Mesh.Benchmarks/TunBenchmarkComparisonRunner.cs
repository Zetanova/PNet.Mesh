using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static class TunBenchmarkComparisonRunner
{
    const string Kind = "pnet-mesh-tun-benchmark-comparison";
    const string BenchmarkKind = "pnet-mesh-tun-benchmark";
    const string PNetMeshTunScenario = "pnet-mesh-tun";
    const string WireGuardGoScenario = "wireguard-go";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TunBenchmarkComparisonOptions.TryParse(args, error, out var options))
            return 2;

        if (options.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        if (!TryReadBenchmarkReport(options.PNetPath, PNetMeshTunScenario, error, out var pnet)
            || !TryReadBenchmarkReport(options.WireGuardPath, WireGuardGoScenario, error, out var wireGuard))
        {
            return 2;
        }

        var report = CreateComparison(pnet, wireGuard);
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "fail" ? 1 : 0;
    }

    internal static TunBenchmarkComparisonReport CreateComparison(
        TunBenchmarkSavedReport pnet,
        TunBenchmarkSavedReport wireGuard)
    {
        var status = pnet.Report.Status == "pass" && wireGuard.Report.Status == "pass" ? "pass" : "fail";
        var message = status == "pass"
            ? "TUN benchmark comparison generated from saved result files."
            : "TUN benchmark comparison includes one or more failing saved result files.";

        return new TunBenchmarkComparisonReport(
            Kind,
            status,
            DateTimeOffset.UtcNow,
            new TunBenchmarkComparisonSources(
                CreateSource(pnet),
                CreateSource(wireGuard)),
            new TunBenchmarkComparisonMetrics(
                CreateTrafficMetrics(pnet.Report, wireGuard.Report),
                CreateProcessMetrics(pnet.Report, wireGuard.Report),
                CreateSettingsMetrics(pnet.Report, wireGuard.Report),
                CreateTraceabilityMetrics(pnet.Report, wireGuard.Report),
                CreateEnvironmentMetrics(pnet.Report, wireGuard.Report),
                CreateImplementationMetrics(pnet.Report, wireGuard.Report),
                new TunBenchmarkManagedRuntimeComparison(
                    CreateManagedRuntimeMetrics(pnet.Report, PNetMeshTunScenario),
                    CreateManagedRuntimeMetrics(wireGuard.Report, WireGuardGoScenario))),
            new TunBenchmarkComparisonRaw(
                CreateRawRun(pnet.Report),
                CreateRawRun(wireGuard.Report)),
            message);
    }

    static bool TryReadBenchmarkReport(
        string path,
        string expectedScenario,
        TextWriter error,
        out TunBenchmarkSavedReport report)
    {
        report = default!;
        if (string.IsNullOrWhiteSpace(path))
        {
            error.WriteLine($"Saved {expectedScenario} benchmark path is required.");
            return false;
        }

        if (!File.Exists(path))
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' does not exist.");
            return false;
        }

        TunPNetBenchmarkReport? saved;
        try
        {
            using var stream = File.OpenRead(path);
            saved = JsonSerializer.Deserialize<TunPNetBenchmarkReport>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' is not valid benchmark JSON: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' could not be read: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' could not be read: {ex.Message}");
            return false;
        }

        if (saved == null)
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' did not contain a benchmark report.");
            return false;
        }

        if (!string.Equals(saved.Kind, BenchmarkKind, StringComparison.Ordinal))
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' has kind '{saved.Kind}', expected '{BenchmarkKind}'.");
            return false;
        }

        if (!string.Equals(saved.Scenario, expectedScenario, StringComparison.Ordinal))
        {
            error.WriteLine($"Saved {expectedScenario} benchmark file '{path}' has scenario '{saved.Scenario}', expected '{expectedScenario}'.");
            return false;
        }

        report = new TunBenchmarkSavedReport(path, saved);
        return true;
    }

    static TunBenchmarkComparisonSource CreateSource(TunBenchmarkSavedReport report)
    {
        return new TunBenchmarkComparisonSource(
            report.SourceFile,
            report.Report.Kind,
            report.Report.Scenario,
            report.Report.Status,
            report.Report.CreatedAt,
            report.Report.Message);
    }

    static TunBenchmarkTrafficMetrics CreateTrafficMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        return new TunBenchmarkTrafficMetrics(
            Pair(FindTraffic(pnet, "ping", "ipv4")?.AverageLatencyMilliseconds, FindTraffic(wireGuard, "ping", "ipv4")?.AverageLatencyMilliseconds),
            Pair(FindTraffic(pnet, "ping", "ipv6")?.AverageLatencyMilliseconds, FindTraffic(wireGuard, "ping", "ipv6")?.AverageLatencyMilliseconds),
            Pair(FindTraffic(pnet, "ping", "ipv4")?.PacketLossPercent, FindTraffic(wireGuard, "ping", "ipv4")?.PacketLossPercent),
            Pair(FindTraffic(pnet, "ping", "ipv6")?.PacketLossPercent, FindTraffic(wireGuard, "ping", "ipv6")?.PacketLossPercent),
            Pair(FindTraffic(pnet, "iperf3", "ipv4")?.BitsPerSecond, FindTraffic(wireGuard, "iperf3", "ipv4")?.BitsPerSecond),
            Pair(FindTraffic(pnet, "iperf3", "ipv6")?.BitsPerSecond, FindTraffic(wireGuard, "iperf3", "ipv6")?.BitsPerSecond));
    }

    static TunBenchmarkProcessComparisonMetrics CreateProcessMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        var pnetProcesses = AggregateProcessMetrics(pnet);
        var wireGuardProcesses = AggregateProcessMetrics(wireGuard);
        return new TunBenchmarkProcessComparisonMetrics(
            Pair(pnetProcesses.AvailableProcessCount, wireGuardProcesses.AvailableProcessCount),
            Pair(pnetProcesses.ResidentSetBytes, wireGuardProcesses.ResidentSetBytes),
            Pair(pnetProcesses.ResidentSetHighWatermarkBytes, wireGuardProcesses.ResidentSetHighWatermarkBytes),
            Pair(pnetProcesses.Threads, wireGuardProcesses.Threads),
            Pair(pnetProcesses.UserCpuTicks, wireGuardProcesses.UserCpuTicks),
            Pair(pnetProcesses.SystemCpuTicks, wireGuardProcesses.SystemCpuTicks),
            Pair(pnetProcesses.TotalCpuTicks, wireGuardProcesses.TotalCpuTicks));
    }

    static TunBenchmarkSettingsMetrics CreateSettingsMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        return new TunBenchmarkSettingsMetrics(
            Pair(pnet.Settings.PingCount, wireGuard.Settings.PingCount),
            Pair(pnet.Settings.WarmupSeconds, wireGuard.Settings.WarmupSeconds),
            Pair(pnet.Settings.IperfDurationSeconds, wireGuard.Settings.IperfDurationSeconds),
            Pair(pnet.Settings.IperfPort, wireGuard.Settings.IperfPort),
            Pair(pnet.Settings.Mtu, wireGuard.Settings.Mtu),
            Pair(pnet.Settings.PayloadMode, wireGuard.Settings.PayloadMode),
            Pair(pnet.Settings.IperfBandwidth, wireGuard.Settings.IperfBandwidth),
            Pair(pnet.Settings.IperfDatagramBytes, wireGuard.Settings.IperfDatagramBytes));
    }

    static TunBenchmarkTraceabilityMetrics CreateTraceabilityMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        return new TunBenchmarkTraceabilityMetrics(
            Pair(pnet.Topology.Name, wireGuard.Topology.Name),
            Pair(pnet.Topology.DockerNetwork, wireGuard.Topology.DockerNetwork),
            Pair(pnet.CreatedAt, wireGuard.CreatedAt),
            Pair(pnet.GitCommit, wireGuard.GitCommit),
            Pair(pnet.CommandLine, wireGuard.CommandLine));
    }

    static TunBenchmarkEnvironmentMetrics CreateEnvironmentMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        return new TunBenchmarkEnvironmentMetrics(
            Pair(pnet.Environment.Framework, wireGuard.Environment.Framework),
            Pair(pnet.Environment.Os, wireGuard.Environment.Os),
            Pair(pnet.Environment.ProcessArchitecture, wireGuard.Environment.ProcessArchitecture),
            Pair(pnet.Environment.ProcessorCount, wireGuard.Environment.ProcessorCount),
            Pair(pnet.Environment.IsLinux, wireGuard.Environment.IsLinux),
            Pair(pnet.Environment.ContainerEngineVersion, wireGuard.Environment.ContainerEngineVersion));
    }

    static TunBenchmarkImplementationMetrics CreateImplementationMetrics(
        TunPNetBenchmarkReport pnet,
        TunPNetBenchmarkReport wireGuard)
    {
        return new TunBenchmarkImplementationMetrics(
            Pair(pnet.Implementation.Name, wireGuard.Implementation.Name),
            Pair(pnet.Implementation.Version, wireGuard.Implementation.Version),
            Pair(pnet.Implementation.ExecutablePath, wireGuard.Implementation.ExecutablePath),
            Pair(pnet.Implementation.VersionSource, wireGuard.Implementation.VersionSource),
            Pair(pnet.Implementation.VersionUnavailableReason, wireGuard.Implementation.VersionUnavailableReason));
    }

    static TunBenchmarkManagedRuntimeMetrics CreateManagedRuntimeMetrics(
        TunPNetBenchmarkReport report,
        string scenario)
    {
        if (report.ManagedRuntime is { } managedRuntime)
            return managedRuntime;

        if (string.Equals(scenario, WireGuardGoScenario, StringComparison.Ordinal))
        {
            return new TunBenchmarkManagedRuntimeMetrics(
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                FirstNonEmpty(report.ManagedCounterUnavailableReason, "wireguard-go is not a .NET process; managed .NET counters do not apply."));
        }

        return new TunBenchmarkManagedRuntimeMetrics(
            true,
            report.ManagedCountersAvailable,
            null,
            null,
            null,
            null,
            null,
            report.ManagedCountersAvailable ? null : report.ManagedCounterUnavailableReason,
            null);
    }

    static TunBenchmarkComparisonRawRun CreateRawRun(TunPNetBenchmarkReport report)
    {
        return new TunBenchmarkComparisonRawRun(
            report.Traffic,
            report.Processes,
            report.TopologyReports,
            report.Commands);
    }

    static TunBenchmarkTrafficResult? FindTraffic(
        TunPNetBenchmarkReport report,
        string tool,
        string protocol)
    {
        return report.Traffic.FirstOrDefault(result =>
            string.Equals(result.Tool, tool, StringComparison.Ordinal)
            && string.Equals(result.Protocol, protocol, StringComparison.Ordinal));
    }

    static TunBenchmarkProcessAggregate AggregateProcessMetrics(TunPNetBenchmarkReport report)
    {
        var available = report.Processes.Where(process => process.Available).ToArray();
        return new TunBenchmarkProcessAggregate(
            available.Length,
            SumNullable(available.Select(process => process.ResidentSetBytes)),
            SumNullable(available.Select(process => process.ResidentSetHighWatermarkBytes)),
            SumNullable(available.Select(process => process.Threads)),
            SumNullable(available.Select(process => process.UserCpuTicks)),
            SumNullable(available.Select(process => process.SystemCpuTicks)));
    }

    static long? SumNullable(IEnumerable<long?> values)
    {
        long total = 0;
        var hasValue = false;
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    static int? SumNullable(IEnumerable<int?> values)
    {
        var total = 0;
        var hasValue = false;
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    static TunBenchmarkComparisonValue Pair(object? pnet, object? wireGuard)
    {
        return new TunBenchmarkComparisonValue(pnet, wireGuard);
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-compare --pnet <pnet-json> --wireguard <wireguard-json>");
        output.WriteLine();
        output.WriteLine("Reads saved --tun-benchmark JSON reports and emits one normalized comparison JSON report.");
    }

    internal sealed class TunBenchmarkComparisonOptions
    {
        public string PNetPath { get; private init; } = string.Empty;

        public string WireGuardPath { get; private init; } = string.Empty;

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunBenchmarkComparisonOptions options)
        {
            options = new TunBenchmarkComparisonOptions();
            if (args.Length == 0 || IsHelp(args[0]))
            {
                options = new TunBenchmarkComparisonOptions { ShowHelp = true };
                return true;
            }

            string? pnetPath = null;
            string? wireGuardPath = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--pnet":
                        if (!TryReadValue(args, ref i, out pnetPath))
                        {
                            error.WriteLine("--pnet requires a value.");
                            return false;
                        }
                        break;
                    case "--wireguard":
                        if (!TryReadValue(args, ref i, out wireGuardPath))
                        {
                            error.WriteLine("--wireguard requires a value.");
                            return false;
                        }
                        break;
                    case "--help":
                    case "-h":
                        options = new TunBenchmarkComparisonOptions { ShowHelp = true };
                        return true;
                    default:
                        error.WriteLine($"Unknown TUN comparison option '{args[i]}'.");
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(pnetPath))
            {
                error.WriteLine("--pnet requires a saved pnet-mesh-tun benchmark JSON file.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(wireGuardPath))
            {
                error.WriteLine("--wireguard requires a saved wireguard-go benchmark JSON file.");
                return false;
            }

            options = new TunBenchmarkComparisonOptions
            {
                PNetPath = pnetPath,
                WireGuardPath = wireGuardPath
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

        static bool IsHelp(string value)
        {
            return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
        }
    }
}

internal sealed record TunBenchmarkSavedReport(
    string SourceFile,
    TunPNetBenchmarkReport Report);

internal sealed record TunBenchmarkComparisonReport(
    string Kind,
    string Status,
    DateTimeOffset CreatedAt,
    TunBenchmarkComparisonSources Sources,
    TunBenchmarkComparisonMetrics Metrics,
    TunBenchmarkComparisonRaw Raw,
    string Message);

internal sealed record TunBenchmarkComparisonSources(
    [property: JsonPropertyName("pnet")] TunBenchmarkComparisonSource PNet,
    [property: JsonPropertyName("wireguard")] TunBenchmarkComparisonSource WireGuard);

internal sealed record TunBenchmarkComparisonSource(
    string SourceFile,
    string Kind,
    string Scenario,
    string Status,
    DateTimeOffset CreatedAt,
    string Message);

internal sealed record TunBenchmarkComparisonMetrics(
    TunBenchmarkTrafficMetrics Traffic,
    TunBenchmarkProcessComparisonMetrics Process,
    TunBenchmarkSettingsMetrics Settings,
    TunBenchmarkTraceabilityMetrics Traceability,
    TunBenchmarkEnvironmentMetrics Environment,
    TunBenchmarkImplementationMetrics Implementation,
    TunBenchmarkManagedRuntimeComparison ManagedRuntime);

internal sealed record TunBenchmarkTrafficMetrics(
    TunBenchmarkComparisonValue Ipv4PingAverageLatencyMilliseconds,
    TunBenchmarkComparisonValue Ipv6PingAverageLatencyMilliseconds,
    TunBenchmarkComparisonValue Ipv4PingPacketLossPercent,
    TunBenchmarkComparisonValue Ipv6PingPacketLossPercent,
    TunBenchmarkComparisonValue Ipv4IperfBitsPerSecond,
    TunBenchmarkComparisonValue Ipv6IperfBitsPerSecond);

internal sealed record TunBenchmarkProcessComparisonMetrics(
    TunBenchmarkComparisonValue AvailableProcessCount,
    TunBenchmarkComparisonValue ResidentSetBytes,
    TunBenchmarkComparisonValue ResidentSetHighWatermarkBytes,
    TunBenchmarkComparisonValue Threads,
    TunBenchmarkComparisonValue UserCpuTicks,
    TunBenchmarkComparisonValue SystemCpuTicks,
    TunBenchmarkComparisonValue TotalCpuTicks);

internal sealed record TunBenchmarkSettingsMetrics(
    TunBenchmarkComparisonValue PingCount,
    TunBenchmarkComparisonValue WarmupSeconds,
    TunBenchmarkComparisonValue IperfDurationSeconds,
    TunBenchmarkComparisonValue IperfPort,
    TunBenchmarkComparisonValue Mtu,
    TunBenchmarkComparisonValue PayloadMode,
    TunBenchmarkComparisonValue IperfBandwidth,
    TunBenchmarkComparisonValue IperfDatagramBytes);

internal sealed record TunBenchmarkTraceabilityMetrics(
    TunBenchmarkComparisonValue TopologyId,
    TunBenchmarkComparisonValue DockerNetwork,
    TunBenchmarkComparisonValue RunTimestamp,
    TunBenchmarkComparisonValue GitCommit,
    TunBenchmarkComparisonValue CommandLine);

internal sealed record TunBenchmarkEnvironmentMetrics(
    TunBenchmarkComparisonValue Framework,
    TunBenchmarkComparisonValue OS,
    TunBenchmarkComparisonValue ProcessArchitecture,
    TunBenchmarkComparisonValue ProcessorCount,
    TunBenchmarkComparisonValue IsLinux,
    TunBenchmarkComparisonValue ContainerEngineVersion);

internal sealed record TunBenchmarkImplementationMetrics(
    TunBenchmarkComparisonValue Name,
    TunBenchmarkComparisonValue Version,
    TunBenchmarkComparisonValue ExecutablePath,
    TunBenchmarkComparisonValue VersionSource,
    TunBenchmarkComparisonValue VersionUnavailableReason);

internal sealed record TunBenchmarkManagedRuntimeComparison(
    [property: JsonPropertyName("pnet")] TunBenchmarkManagedRuntimeMetrics PNet,
    [property: JsonPropertyName("wireguard")] TunBenchmarkManagedRuntimeMetrics WireGuard);

internal sealed record TunBenchmarkComparisonRaw(
    [property: JsonPropertyName("pnet")] TunBenchmarkComparisonRawRun PNet,
    [property: JsonPropertyName("wireguard")] TunBenchmarkComparisonRawRun WireGuard);

internal sealed record TunBenchmarkComparisonRawRun(
    IReadOnlyList<TunBenchmarkTrafficResult> Traffic,
    IReadOnlyList<TunBenchmarkProcessMetrics> Processes,
    IReadOnlyList<TunTopologyReport> TopologyReports,
    IReadOnlyList<TunTopologyCommandRecord> Commands);

internal sealed record TunBenchmarkComparisonValue(
    [property: JsonPropertyName("pnet")] object? PNet,
    [property: JsonPropertyName("wireguard")] object? WireGuard);

sealed record TunBenchmarkProcessAggregate(
    int AvailableProcessCount,
    long? ResidentSetBytes,
    long? ResidentSetHighWatermarkBytes,
    int? Threads,
    long? UserCpuTicks,
    long? SystemCpuTicks)
{
    public long? TotalCpuTicks => UserCpuTicks.HasValue || SystemCpuTicks.HasValue
        ? (UserCpuTicks ?? 0) + (SystemCpuTicks ?? 0)
        : null;
}
