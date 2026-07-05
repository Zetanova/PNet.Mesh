namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
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
