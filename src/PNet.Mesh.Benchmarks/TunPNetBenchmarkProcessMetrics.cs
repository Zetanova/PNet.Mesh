namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    const string ManagedRuntimeMetricsPrefix = "pnet_gc_metrics ";

    internal static bool TryParseManagedRuntimeSnapshot(
        string line,
        out TunBenchmarkManagedRuntimeSnapshot snapshot)
    {
        snapshot = default!;
        if (!line.StartsWith(ManagedRuntimeMetricsPrefix, StringComparison.Ordinal))
            return false;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in line[ManagedRuntimeMetricsPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
                continue;

            values[token[..separator]] = token[(separator + 1)..];
        }

        if (!TryReadRequiredInt64(values, "allocated_bytes", out var allocationBytes)
            || !TryReadRequiredInt64(values, "managed_heap_bytes", out var managedHeapBytes)
            || !TryReadRequiredInt64(values, "fragmented_bytes", out var fragmentedBytes)
            || !TryReadRequiredInt64(values, "memory_load_bytes", out var memoryLoadBytes)
            || !TryReadRequiredInt64(values, "total_available_memory_bytes", out var totalAvailableMemoryBytes)
            || !TryReadRequiredInt64(values, "high_memory_load_threshold_bytes", out var highMemoryLoadThresholdBytes)
            || !TryReadRequiredInt32(values, "gen0_collections", out var gen0Collections)
            || !TryReadRequiredInt32(values, "gen1_collections", out var gen1Collections)
            || !TryReadRequiredInt32(values, "gen2_collections", out var gen2Collections))
        {
            return false;
        }

        snapshot = new TunBenchmarkManagedRuntimeSnapshot(
            allocationBytes,
            managedHeapBytes,
            fragmentedBytes,
            memoryLoadBytes,
            totalAvailableMemoryBytes,
            highMemoryLoadThresholdBytes,
            gen0Collections,
            gen1Collections,
            gen2Collections);
        return true;
    }

    static bool TryReadRequiredInt64(
        IReadOnlyDictionary<string, string> values,
        string key,
        out long value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
               && long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    static bool TryReadRequiredInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
               && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    static IReadOnlyDictionary<string, TunBenchmarkManagedRuntimeSnapshot> CaptureManagedRuntimeSnapshots(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        IReadOnlyList<TunTopologyNode> nodes,
        List<TunTopologyCommandRecord> commands)
    {
        var snapshots = new Dictionary<string, TunBenchmarkManagedRuntimeSnapshot>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (CaptureManagedRuntimeSnapshot(commandRunner, options, node, commands) is { } snapshot)
                snapshots[node.Role] = snapshot;
        }

        return snapshots;
    }

    static TunBenchmarkManagedRuntimeSnapshot? CaptureManagedRuntimeSnapshot(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var processPattern = CreateProcessPattern(options, node);
        var command = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"metrics=/tmp/pnet-gc-metrics.log; rm -f \"$metrics\"; pid=$(pgrep -f {ShellQuote(processPattern)} | head -n1); test -n \"$pid\" || exit 1; kill -HUP \"$pid\"; for i in 1 2 3 4 5 6 7 8 9 10; do if [ -s \"$metrics\" ]; then cat \"$metrics\"; exit 0; fi; sleep 0.1; done; exit 1"
        }, options.CommandTimeout);
        commands.Add(command);

        return command.ExitCode == 0 && TryParseManagedRuntimeSnapshot(command.Stdout, out var snapshot)
            ? snapshot
            : null;
    }

    static TunBenchmarkManagedRuntimeMetrics CreateManagedRuntimeMetrics(
        TunPNetBenchmarkOptions options,
        IReadOnlyDictionary<string, TunBenchmarkManagedRuntimeSnapshot> before,
        IReadOnlyDictionary<string, TunBenchmarkManagedRuntimeSnapshot> after,
        int expectedNodeCount,
        string unavailableReason)
    {
        if (options.Scenario != PNetMeshTunScenario)
        {
            return CreateManagedRuntimeMetrics(
                options.Scenario,
                available: false,
                unavailableReason,
                options.ManagedHeapGrowthLimitBytes);
        }

        var nodeMetrics = new List<TunBenchmarkManagedRuntimeNodeMetrics>();
        foreach (var entry in before.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!after.TryGetValue(entry.Key, out var afterSnapshot))
                continue;

            var managedHeapDeltaBytes = afterSnapshot.ManagedHeapBytes - entry.Value.ManagedHeapBytes;
            nodeMetrics.Add(new TunBenchmarkManagedRuntimeNodeMetrics(
                entry.Key,
                entry.Value,
                afterSnapshot,
                afterSnapshot.AllocationBytes - entry.Value.AllocationBytes,
                managedHeapDeltaBytes,
                options.ManagedHeapGrowthLimitBytes,
                managedHeapDeltaBytes <= options.ManagedHeapGrowthLimitBytes));
        }

        if (nodeMetrics.Count != expectedNodeCount)
        {
            return new TunBenchmarkManagedRuntimeMetrics(
                true,
                false,
                null,
                null,
                null,
                null,
                null,
                FirstNonEmpty(unavailableReason, "PNet.Mesh.Tun.Cli did not emit complete managed runtime snapshots."),
                null,
                nodeMetrics,
                null,
                null,
                options.ManagedHeapGrowthLimitBytes,
                null);
        }

        return new TunBenchmarkManagedRuntimeMetrics(
            true,
            true,
            nodeMetrics.Sum(node => node.After.AllocationBytes),
            nodeMetrics.Sum(node => node.After.ManagedHeapBytes),
            nodeMetrics.Sum(node => node.After.Gen0Collections),
            nodeMetrics.Sum(node => node.After.Gen1Collections),
            nodeMetrics.Sum(node => node.After.Gen2Collections),
            null,
            null,
            nodeMetrics,
            nodeMetrics.Sum(node => node.AllocationDeltaBytes),
            nodeMetrics.Sum(node => node.ManagedHeapDeltaBytes),
            options.ManagedHeapGrowthLimitBytes,
            nodeMetrics.All(node => node.ManagedHeapWithinLimit));
    }

    static TunBenchmarkProcessMetrics ReadProcessMetrics(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var processPattern = CreateProcessPattern(options, node);
        var command = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"pid=$(pgrep -f {ShellQuote(processPattern)} | head -n1); test -n \"$pid\" || exit 1; printf 'pid=%s\\n' \"$pid\"; grep -E '^(VmRSS|VmHWM|Threads):' \"/proc/$pid/status\"; awk '{{print \"utime_ticks=\"$14; print \"stime_ticks=\"$15}}' \"/proc/$pid/stat\""
        }, options.CommandTimeout);
        commands.Add(command);

        int? pid = null;
        long? rssBytes = null;
        long? highWatermarkBytes = null;
        int? threads = null;
        long? userTicks = null;
        long? systemTicks = null;

        if (command.ExitCode == 0)
        {
            foreach (var line in command.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("pid=", StringComparison.Ordinal))
                    pid = TryReadInt(line["pid=".Length..]);
                else if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    rssBytes = TryReadKilobytes(line);
                else if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
                    highWatermarkBytes = TryReadKilobytes(line);
                else if (line.StartsWith("Threads:", StringComparison.Ordinal))
                    threads = TryReadTrailingInt(line);
                else if (line.StartsWith("utime_ticks=", StringComparison.Ordinal))
                    userTicks = TryReadInt64(line["utime_ticks=".Length..]);
                else if (line.StartsWith("stime_ticks=", StringComparison.Ordinal))
                    systemTicks = TryReadInt64(line["stime_ticks=".Length..]);
            }
        }

        return new TunBenchmarkProcessMetrics(
            node.Role,
            node.ContainerName,
            command.ExitCode == 0,
            pid,
            rssBytes,
            highWatermarkBytes,
            threads,
            userTicks,
            systemTicks,
            command.ExitCode == 0 ? null : TrimOutput(command.Stderr));
    }

    static string CreateProcessPattern(TunPNetBenchmarkOptions options, TunTopologyNode node)
    {
        return options.Scenario switch
        {
            PNetMeshTunScenario => "^dotnet PNet.Mesh.Tun.Cli.dll",
            WireGuardGoScenario => $"(^|/)wireguard-go .*{node.InterfaceName}($| )",
            WireGuardGoPNetIcmpEchoScenario when node.Role == "left" => $"(^|/)wireguard-go .*{node.InterfaceName}($| )",
            WireGuardGoPNetIcmpEchoScenario => "^dotnet PNet.Mesh.Tun.Cli.dll icmp-echo",
            var scenario when IsTunOnlyIcmpEchoScenario(scenario) => "^dotnet PNet.Mesh.Tun.Cli.dll tun-icmp-echo",
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static void CaptureProcessLog(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var logPath = options.Scenario switch
        {
            WireGuardGoScenario => "/tmp/wireguard-go.log",
            WireGuardGoPNetIcmpEchoScenario when node.Role == "left" => "/tmp/wireguard-go.log",
            WireGuardGoPNetIcmpEchoScenario => "/tmp/pnet-icmp-echo.log",
            var scenario when IsTunOnlyIcmpEchoScenario(scenario) => "/tmp/pnet-tun-icmp-echo.log",
            _ => "/tmp/pnet-tun.log"
        };
        commands.Add(RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"tail -n 120 {logPath} 2>/dev/null || true"
        }, options.CommandTimeout));
    }

    static void CaptureWireGuardGoState(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        commands.Add(RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"printf 'node={node.Role}\\n'; wg show {ShellQuote(node.InterfaceName)}; ip addr show dev {ShellQuote(node.InterfaceName)}; ip route; ip -6 route; ip -s link show dev {ShellQuote(node.InterfaceName)}; ss -lunp"
        }, options.CommandTimeout));
    }

    static void CapturePacketTrace(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        if (string.IsNullOrWhiteSpace(options.PacketTraceOutputDirectory))
            return;
        if (options.Scenario != PNetMeshTunScenario)
            return;

        Directory.CreateDirectory(options.PacketTraceOutputDirectory);
        var processPattern = CreateProcessPattern(options, node);
        var flush = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"pid=$(pgrep -f {ShellQuote(processPattern)} | head -n1); test -n \"$pid\" || exit 1; kill -HUP \"$pid\"; for i in 1 2 3 4 5 6 7 8 9 10; do test -s /tmp/pnet-packet-trace.csv && exit 0; sleep 0.1; done; test -s /tmp/pnet-packet-trace.csv"
        }, options.CommandTimeout);
        commands.Add(flush);
        if (flush.ExitCode != 0)
            return;

        var destination = Path.Combine(
            options.PacketTraceOutputDirectory,
            $"{options.Name}-{options.Scenario}-{node.Role}-packet-trace.csv");
        commands.Add(RunCommand(commandRunner, "docker", new[]
        {
            "cp",
            $"{node.ContainerName}:/tmp/pnet-packet-trace.csv",
            destination
        }, options.CommandTimeout));
    }
}

internal sealed record TunBenchmarkProcessMetrics(
    string Node,
    string ContainerName,
    bool Available,
    int? Pid,
    long? ResidentSetBytes,
    long? ResidentSetHighWatermarkBytes,
    int? Threads,
    long? UserCpuTicks,
    long? SystemCpuTicks,
    string? Error);
