using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    static TunPNetBenchmarkReport CreateReport(
        TunPNetBenchmarkOptions options,
        TunBenchmarkImplementationInfo implementation,
        TunBenchmarkTopologySpec topology,
        string status,
        string message,
        IReadOnlyList<TunTopologyReport> topologyReports,
        IReadOnlyList<TunTopologyCommandRecord> commands,
        IReadOnlyList<TunBenchmarkTrafficResult> traffic,
        IReadOnlyList<TunBenchmarkUdpCounterDelta> udpCounters,
        IReadOnlyList<TunBenchmarkProcessMetrics> processes,
        string managedCounterUnavailableReason,
        TunBenchmarkManagedRuntimeMetrics? managedRuntime = null)
    {
        var isUdpIperf = string.Equals(options.IperfProtocol, "udp", StringComparison.Ordinal);
        return new TunPNetBenchmarkReport(
            Kind,
            options.Scenario,
            status,
            DateTimeOffset.UtcNow,
            new TunTopologyEnvironment(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                topologyReports.FirstOrDefault()?.Environment.ContainerEngineVersion),
            topology,
            implementation,
            new TunPNetBenchmarkSettings(
                options.PingCount,
                options.Warmup.TotalSeconds,
                options.IperfDuration.TotalSeconds,
                options.IperfPort,
                options.Mtu,
                options.PayloadMode,
                options.IperfProtocol,
                isUdpIperf ? options.IperfBandwidth : null,
                isUdpIperf ? options.IperfDatagramBytes : null,
                options.IperfBytes,
                options.ManagedHeapGrowthLimitBytes,
                options.PNetUdpReceiveMode,
                options.PNetUdpSocketBufferBytes,
                options.IperfWindow),
            traffic,
            udpCounters,
            processes,
            managedRuntime?.Available ?? false,
            managedCounterUnavailableReason,
            managedRuntime ?? CreateManagedRuntimeMetrics(options.Scenario, available: false, managedCounterUnavailableReason, options.ManagedHeapGrowthLimitBytes),
            ReadCurrentGitCommit(),
            options.CommandLine,
            topologyReports,
            commands,
            message);
    }

    static TunBenchmarkManagedRuntimeMetrics CreateManagedRuntimeMetrics(
        string scenario,
        bool available,
        string unavailableReason,
        long managedHeapGrowthLimitBytes)
    {
        return scenario switch
        {
            PNetMeshTunScenario => new TunBenchmarkManagedRuntimeMetrics(
                true,
                available,
                null,
                null,
                null,
                null,
                null,
                available ? null : unavailableReason,
                null,
                ManagedHeapGrowthLimitBytes: available ? null : managedHeapGrowthLimitBytes),
            WireGuardGoScenario => new TunBenchmarkManagedRuntimeMetrics(
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                unavailableReason),
            WireGuardGoPNetIcmpEchoScenario => new TunBenchmarkManagedRuntimeMetrics(
                true,
                false,
                null,
                null,
                null,
                null,
                null,
                unavailableReason,
                null,
                ManagedHeapGrowthLimitBytes: managedHeapGrowthLimitBytes),
            var value when IsTunOnlyIcmpEchoScenario(value) => new TunBenchmarkManagedRuntimeMetrics(
                true,
                false,
                null,
                null,
                null,
                null,
                null,
                unavailableReason,
                null,
                ManagedHeapGrowthLimitBytes: managedHeapGrowthLimitBytes),
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{scenario}'.")
        };
    }

    static string? ReadCurrentGitCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse --short=12 HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process == null)
                return null;

            if (!process.WaitForExit(1000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return null;
            }

            if (process.ExitCode != 0)
                return null;

            var commit = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(commit) ? null : commit;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

internal sealed record TunPNetBenchmarkReport(
    string Kind,
    string Scenario,
    string Status,
    DateTimeOffset CreatedAt,
    TunTopologyEnvironment Environment,
    TunBenchmarkTopologySpec Topology,
    TunBenchmarkImplementationInfo Implementation,
    TunPNetBenchmarkSettings Settings,
    IReadOnlyList<TunBenchmarkTrafficResult> Traffic,
    IReadOnlyList<TunBenchmarkUdpCounterDelta> UdpCounters,
    IReadOnlyList<TunBenchmarkProcessMetrics> Processes,
    bool ManagedCountersAvailable,
    string ManagedCounterUnavailableReason,
    TunBenchmarkManagedRuntimeMetrics? ManagedRuntime,
    string? GitCommit,
    string? CommandLine,
    IReadOnlyList<TunTopologyReport> TopologyReports,
    IReadOnlyList<TunTopologyCommandRecord> Commands,
    string Message);

internal sealed record TunBenchmarkImplementationInfo(
    string Name,
    string? Version,
    string? ExecutablePath,
    string? VersionSource,
    string? VersionUnavailableReason);

internal sealed record TunPNetBenchmarkSettings(
    int PingCount,
    double WarmupSeconds,
    double IperfDurationSeconds,
    int IperfPort,
    int Mtu,
    string? PayloadMode,
    string? IperfProtocol,
    string? IperfBandwidth,
    int? IperfDatagramBytes,
    long? IperfBytes = null,
    long? ManagedHeapGrowthLimitBytes = null,
    string? PNetUdpReceiveMode = null,
    int? PNetUdpSocketBufferBytes = null,
    string? IperfWindow = null);

internal sealed record TunBenchmarkTrafficResult(
    string Tool,
    string Protocol,
    string SourceNode,
    string TargetNode,
    string TargetAddress,
    int ExitCode,
    int? PacketsTransmitted,
    int? PacketsReceived,
    double? PacketLossPercent,
    double? MinLatencyMilliseconds,
    double? AverageLatencyMilliseconds,
    double? MaxLatencyMilliseconds,
    double? Seconds,
    long? Bytes,
    double? BitsPerSecond,
    string Stdout,
    string Stderr);

internal sealed record TunBenchmarkUdpCounterDelta(
    string Protocol,
    string SourceNode,
    string TargetNode,
    string TargetAddress,
    IReadOnlyList<TunBenchmarkUdpNodeCounterDelta> Nodes);

internal sealed record TunBenchmarkUdpNodeCounterDelta(
    string Node,
    TunBenchmarkUdpGlobalCounters BeforeGlobal,
    TunBenchmarkUdpGlobalCounters AfterGlobal,
    TunBenchmarkUdpGlobalCounters GlobalDelta,
    IReadOnlyList<TunBenchmarkUdpSocketCounterDelta> Sockets);

internal sealed record TunBenchmarkUdpGlobalCounters(
    long? UdpInErrors,
    long? UdpRcvbufErrors,
    long? Udp6InErrors,
    long? Udp6RcvbufErrors);

internal sealed record TunBenchmarkUdpSocketCounterDelta(
    string Family,
    string Role,
    string LocalAddressHex,
    int? LocalPort,
    string RemoteAddressHex,
    int? RemotePort,
    string? Inode,
    long FirstDrops,
    long LastDrops,
    long MaxDrops,
    long DropsDelta,
    int ObservedSamples,
    bool PresentInFirstSample,
    bool PresentInLastSample);

internal sealed record TunBenchmarkManagedRuntimeMetrics(
    bool Applicable,
    bool Available,
    long? AllocationBytes,
    long? ManagedHeapBytes,
    int? Gen0Collections,
    int? Gen1Collections,
    int? Gen2Collections,
    string? UnavailableReason,
    string? NotApplicableReason,
    IReadOnlyList<TunBenchmarkManagedRuntimeNodeMetrics>? Nodes = null,
    long? AllocationDeltaBytes = null,
    long? ManagedHeapDeltaBytes = null,
    long? ManagedHeapGrowthLimitBytes = null,
    bool? ManagedHeapWithinLimit = null);

internal sealed record TunBenchmarkManagedRuntimeNodeMetrics(
    string Node,
    TunBenchmarkManagedRuntimeSnapshot Before,
    TunBenchmarkManagedRuntimeSnapshot After,
    long AllocationDeltaBytes,
    long ManagedHeapDeltaBytes,
    long ManagedHeapGrowthLimitBytes,
    bool ManagedHeapWithinLimit);

internal sealed record TunBenchmarkManagedRuntimeSnapshot(
    long AllocationBytes,
    long ManagedHeapBytes,
    long FragmentedBytes,
    long MemoryLoadBytes,
    long TotalAvailableMemoryBytes,
    long HighMemoryLoadThresholdBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
