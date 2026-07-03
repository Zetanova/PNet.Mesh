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
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static void CaptureProcessLog(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var logPath = options.Scenario == WireGuardGoScenario ? "/tmp/wireguard-go.log" : "/tmp/pnet-tun.log";
        commands.Add(RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            $"tail -n 120 {logPath} 2>/dev/null || true"
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
