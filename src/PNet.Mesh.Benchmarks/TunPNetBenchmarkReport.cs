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
        IReadOnlyList<TunBenchmarkProcessMetrics> processes,
        string managedCounterUnavailableReason)
    {
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
                options.IperfBandwidth,
                options.IperfDatagramBytes),
            traffic,
            processes,
            false,
            managedCounterUnavailableReason,
            CreateManagedRuntimeMetrics(options.Scenario, available: false, managedCounterUnavailableReason),
            ReadCurrentGitCommit(),
            options.CommandLine,
            topologyReports,
            commands,
            message);
    }

    static TunBenchmarkManagedRuntimeMetrics CreateManagedRuntimeMetrics(
        string scenario,
        bool available,
        string unavailableReason)
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
                null),
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
                null),
            var value when IsTunOnlyIcmpEchoScenario(value) => new TunBenchmarkManagedRuntimeMetrics(
                true,
                false,
                null,
                null,
                null,
                null,
                null,
                unavailableReason,
                null),
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
    string? IperfBandwidth,
    int? IperfDatagramBytes);

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

internal sealed record TunBenchmarkManagedRuntimeMetrics(
    bool Applicable,
    bool Available,
    long? AllocationBytes,
    long? ManagedHeapBytes,
    int? Gen0Collections,
    int? Gen1Collections,
    int? Gen2Collections,
    string? UnavailableReason,
    string? NotApplicableReason);
