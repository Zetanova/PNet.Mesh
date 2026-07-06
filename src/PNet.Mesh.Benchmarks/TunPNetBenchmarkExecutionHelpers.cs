using System.Globalization;
using System.Text.Json;

namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    const int ReadinessPingTimeoutSeconds = 3;
    static readonly TimeSpan ReadinessPingTimeout = TimeSpan.FromSeconds(ReadinessPingTimeoutSeconds);

    internal static TunBenchmarkTrafficResult ParsePingResult(
        string protocol,
        string sourceNode,
        string targetNode,
        string targetAddress,
        TunTopologyCommandRecord command)
    {
        int? transmitted = null;
        int? received = null;
        double? packetLossPercent = null;
        double? minMs = null;
        double? avgMs = null;
        double? maxMs = null;

        foreach (var line in command.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("packets transmitted", StringComparison.Ordinal))
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                transmitted = TryReadLeadingInt(parts.ElementAtOrDefault(0));
                received = TryReadLeadingInt(parts.ElementAtOrDefault(1));
                packetLossPercent = TryReadPercent(parts.FirstOrDefault(part => part.Contains("packet loss", StringComparison.Ordinal)));
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0 || !line.Contains("min/avg/max", StringComparison.Ordinal))
                continue;

            var values = line[(separator + 1)..].Replace(" ms", string.Empty, StringComparison.Ordinal).Split('/', StringSplitOptions.TrimEntries);
            if (values.Length >= 3)
            {
                minMs = TryReadDouble(values[0]);
                avgMs = TryReadDouble(values[1]);
                maxMs = TryReadDouble(values[2]);
            }
        }

        return new TunBenchmarkTrafficResult(
            "ping",
            protocol,
            sourceNode,
            targetNode,
            targetAddress,
            command.ExitCode,
            transmitted,
            received,
            packetLossPercent,
            minMs,
            avgMs,
            maxMs,
            null,
            null,
            null,
            TrimOutput(command.Stdout),
            TrimOutput(command.Stderr));
    }

    internal static TunBenchmarkTrafficResult ParseIperfResult(
        string protocol,
        string sourceNode,
        string targetNode,
        string targetAddress,
        TunTopologyCommandRecord command)
    {
        double? seconds = null;
        long? bytes = null;
        double? bitsPerSecond = null;
        double? packetLossPercent = null;

        if (command.ExitCode == 0 && !string.IsNullOrWhiteSpace(command.Stdout))
        {
            try
            {
                using var document = JsonDocument.Parse(command.Stdout);
                if (TryGetProperty(document.RootElement, "end", out var end))
                {
                    if (TryGetProperty(end, "sum_received", out var sum) || TryGetProperty(end, "sum", out sum))
                    {
                        seconds = TryReadJsonDouble(sum, "seconds");
                        bytes = TryReadJsonInt64(sum, "bytes");
                        bitsPerSecond = TryReadJsonDouble(sum, "bits_per_second");
                        packetLossPercent = TryReadJsonDouble(sum, "lost_percent");
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return new TunBenchmarkTrafficResult(
            "iperf3",
            protocol,
            sourceNode,
            targetNode,
            targetAddress,
            command.ExitCode,
            null,
            null,
            packetLossPercent,
            null,
            null,
            null,
            seconds,
            bytes,
            bitsPerSecond,
            TrimOutput(command.Stdout),
            TrimOutput(command.Stderr));
    }

    static TunBenchmarkTrafficResult RunPing(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode source,
        TunTopologyNode target,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands)
    {
        var arguments = CreatePingArguments(source, targetAddress, ipv6, options.PingCount, timeoutSeconds: 5);
        var command = RunCommand(commandRunner, "docker", arguments, options.CommandTimeout);
        commands.Add(command);
        return ParsePingResult(protocol, source.Role, target.Role, targetAddress, command);
    }

    static List<string> CreatePingArguments(
        TunTopologyNode source,
        string targetAddress,
        bool ipv6,
        int count,
        int timeoutSeconds,
        int? deadlineSeconds = null)
    {
        var arguments = new List<string>
        {
            "exec",
            source.ContainerName,
            "ping"
        };
        if (ipv6)
            arguments.Add("-6");

        arguments.AddRange(new[]
        {
            "-c",
            count.ToString(CultureInfo.InvariantCulture),
            "-W",
            timeoutSeconds.ToString(CultureInfo.InvariantCulture),
            "-w",
            (deadlineSeconds ?? Math.Max(timeoutSeconds, (count * timeoutSeconds) + 1)).ToString(CultureInfo.InvariantCulture),
            targetAddress
        });
        return arguments;
    }

    static bool RunReadinessPing(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode source,
        TunTopologyNode target,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands,
        out string failureMessage)
    {
        var arguments = CreatePingArguments(
            source,
            targetAddress,
            ipv6,
            count: 1,
            timeoutSeconds: ReadinessPingTimeoutSeconds,
            deadlineSeconds: ReadinessPingTimeoutSeconds);
        var command = RunCommand(commandRunner, "docker", arguments, ReadinessPingTimeout);
        commands.Add(command);
        var result = ParsePingResult(protocol, source.Role, target.Role, targetAddress, command);
        if (IsSuccessfulPing(result))
        {
            failureMessage = string.Empty;
            return true;
        }

        var reason = command.TimedOut
            ? $"timed out after {ReadinessPingTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds"
            : $"failed with exit code {command.ExitCode.ToString(CultureInfo.InvariantCulture)}";
        failureMessage = $"{GetScenarioDisplayName(options.Scenario)} benchmark setup failed: {protocol} readiness ping to {targetAddress} {reason} before measured traffic.";
        return false;
    }

    static TunBenchmarkTrafficResult RunIperf(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode source,
        TunTopologyNode target,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands,
        List<TunBenchmarkUdpCounterDelta> udpCounters)
    {
        var serverPath = $"/tmp/pnet-iperf-{protocol}-server.json";
        var serverErrorPath = $"/tmp/pnet-iperf-{protocol}-server.err";
        var serverCommand = $"rm -f {serverPath} {serverErrorPath}; iperf3 {(ipv6 ? "-6 " : string.Empty)}-s -1 -B {targetAddress} -p {options.IperfPort} --json > {serverPath} 2>{serverErrorPath}";
        var serverStart = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            "-d",
            target.ContainerName,
            "sh",
            "-c",
            serverCommand
        }, options.CommandTimeout);
        commands.Add(serverStart);
        if (serverStart.ExitCode != 0)
            return CreateIperfFailure(protocol, source.Role, target.Role, targetAddress, "iperf3 server did not start.");

        if (!WaitForIperfServer(commandRunner, options, target, protocol, targetAddress, ipv6, commands))
            return CreateIperfFailure(protocol, source.Role, target.Role, targetAddress, "iperf3 server did not become ready.");

        var arguments = new List<string>
        {
            "exec",
            source.ContainerName,
            "iperf3"
        };
        if (ipv6)
            arguments.Add("-6");

        arguments.AddRange(new[]
        {
            "-c",
            targetAddress,
            "-p",
            options.IperfPort.ToString(CultureInfo.InvariantCulture),
            "-w",
            options.IperfWindow
        });
        if (string.Equals(options.IperfProtocol, "udp", StringComparison.Ordinal))
        {
            arguments.AddRange(new[]
            {
                "-u",
                "-b",
                options.IperfBandwidth,
                "-l",
                options.IperfDatagramBytes.ToString(CultureInfo.InvariantCulture)
            });
        }

        if (options.IperfBytes is { } iperfBytes)
        {
            arguments.Add("-n");
            arguments.Add(iperfBytes.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add("-t");
            arguments.Add(Math.Ceiling(options.IperfDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("--json");

        var udpCounterTargets = CreateUdpCounterSampleTargets(protocol, source, target);
        var beforeUdpCounters = CaptureUdpCounterSamples(commandRunner, options, udpCounterTargets, 0, commands);
        TunTopologyCommandResult result;
        try
        {
            result = commandRunner.Run("docker", arguments, options.IperfBytes.HasValue ? options.CommandTimeout : options.CommandTimeout + options.IperfDuration);
        }
        finally
        {
            var afterUdpCounters = CaptureUdpCounterSamples(commandRunner, options, udpCounterTargets, 1, commands);
            udpCounters.Add(CreateUdpCounterDelta(protocol, source, target, targetAddress, options.IperfPort, beforeUdpCounters, afterUdpCounters));
        }

        var command = new TunTopologyCommandRecord(
            result.FileName,
            result.Arguments,
            result.ExitCode,
            TrimOutput(result.Stdout),
            TrimOutput(result.Stderr),
            result.TimedOut);
        commands.Add(command);

        var rawCommand = new TunTopologyCommandRecord(
            result.FileName,
            result.Arguments,
            result.ExitCode,
            result.Stdout,
            result.Stderr,
            result.TimedOut);
        return ParseIperfResult(protocol, source.Role, target.Role, targetAddress, rawCommand);
    }

    static bool WaitForIperfServer(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode target,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands)
    {
        var deadline = DateTimeOffset.UtcNow + options.ProcessStartTimeout;
        var timeout = options.CommandTimeout < TimeSpan.FromSeconds(2) ? options.CommandTimeout : TimeSpan.FromSeconds(2);
        var port = options.IperfPort.ToString(CultureInfo.InvariantCulture);
        var listenAddress = ipv6 ? $"[{targetAddress}]:{port}" : $"{targetAddress}:{port}";
        var family = ipv6 ? "-6" : "-4";
        var readinessCommand = $"ss {family} -H -ltn sport = :{port} | awk '{{print $4}}' | grep -Fx -- {ShellQuote(listenAddress)}";
        while (DateTimeOffset.UtcNow < deadline)
        {
            var command = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                target.ContainerName,
                "sh",
                "-c",
                readinessCommand
            }, timeout);
            commands.Add(command);
            if (command.ExitCode == 0)
                return true;

            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }

        commands.Add(new TunTopologyCommandRecord(
            "docker",
            new[] { "exec", target.ContainerName, "sh", "-c", readinessCommand },
            -1,
            string.Empty,
            "iperf3 server readiness check timed out.",
            false));
        return false;
    }

    static bool IsSuccessfulTrafficResult(TunBenchmarkTrafficResult result, TunPNetBenchmarkOptions options)
    {
        return result.Tool switch
        {
            "ping" => IsSuccessfulPing(result),
            "iperf3" => result.ExitCode == 0
                        && result.BitsPerSecond > 0
                        && (!result.PacketLossPercent.HasValue || result.PacketLossPercent == 0)
                        && (!options.IperfBytes.HasValue
                            || string.Equals(options.IperfProtocol, "tcp", StringComparison.Ordinal)
                            || result.Bytes >= options.IperfBytes),
            _ => result.ExitCode == 0
        };
    }

    static bool IsSuccessfulPing(TunBenchmarkTrafficResult result)
    {
        return result.ExitCode == 0
               && result.PacketsTransmitted > 0
               && result.PacketsReceived == result.PacketsTransmitted
               && result.PacketLossPercent == 0;
    }

    static TunBenchmarkTrafficResult CreateIperfFailure(
        string protocol,
        string sourceNode,
        string targetNode,
        string targetAddress,
        string error)
    {
        return new TunBenchmarkTrafficResult(
            "iperf3",
            protocol,
            sourceNode,
            targetNode,
            targetAddress,
            -1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            string.Empty,
            error);
    }

    static TunTopologyCommandRecord RunCommand(
        ITunTopologyCommandRunner commandRunner,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<string>? reportedArguments = null)
    {
        var result = commandRunner.Run(fileName, arguments, timeout);
        return new TunTopologyCommandRecord(
            result.FileName,
            reportedArguments ?? result.Arguments,
            result.ExitCode,
            TrimOutput(result.Stdout),
            TrimOutput(result.Stderr),
            result.TimedOut);
    }

    static TunBenchmarkImplementationInfo CreateInitialImplementationInfo(TunPNetBenchmarkOptions options)
    {
        return options.Scenario switch
        {
            PNetMeshTunScenario => CreatePNetMeshTunImplementationInfo(),
            WireGuardGoScenario => new TunBenchmarkImplementationInfo(
                WireGuardGoScenario,
                null,
                null,
                null,
                "Topology create did not pass; wireguard-go version was not queried."),
            WireGuardGoPNetIcmpEchoScenario => new TunBenchmarkImplementationInfo(
                WireGuardGoPNetIcmpEchoScenario,
                typeof(TunPNetBenchmarkRunner).Assembly.GetName().Version?.ToString(),
                "wireguard-go + PNet.Mesh.Tun.Cli.dll icmp-echo",
                "PNet.Mesh.Benchmarks assembly",
                "Topology create did not pass; wireguard-go version was not queried."),
            var scenario when IsTunOnlyIcmpEchoScenario(scenario) => CreateTunOnlyIcmpEchoImplementationInfo(scenario),
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static TunBenchmarkImplementationInfo ReadImplementationInfo(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        return options.Scenario switch
        {
            PNetMeshTunScenario => CreatePNetMeshTunImplementationInfo(),
            WireGuardGoScenario => ReadWireGuardGoImplementationInfo(commandRunner, options, node, commands),
            WireGuardGoPNetIcmpEchoScenario => ReadWireGuardGoPNetIcmpEchoImplementationInfo(commandRunner, options, node, commands),
            var scenario when IsTunOnlyIcmpEchoScenario(scenario) => CreateTunOnlyIcmpEchoImplementationInfo(scenario),
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static TunBenchmarkImplementationInfo CreatePNetMeshTunImplementationInfo()
    {
        return new TunBenchmarkImplementationInfo(
            PNetMeshTunScenario,
            typeof(TunPNetBenchmarkRunner).Assembly.GetName().Version?.ToString(),
            "PNet.Mesh.Tun.Cli.dll",
            "PNet.Mesh.Benchmarks assembly",
            null);
    }

    static TunBenchmarkImplementationInfo CreateTunOnlyIcmpEchoImplementationInfo(string scenario)
    {
        return new TunBenchmarkImplementationInfo(
            scenario,
            typeof(TunPNetBenchmarkRunner).Assembly.GetName().Version?.ToString(),
            "PNet.Mesh.Tun.Cli.dll tun-icmp-echo",
            "PNet.Mesh.Benchmarks assembly",
            null);
    }

    static TunBenchmarkImplementationInfo ReadWireGuardGoImplementationInfo(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        const string versionCommand = "if path=$(command -v wireguard-go); then version=$(dpkg-query -W -f='${Version}' wireguard-go 2>/dev/null || true); if [ -n \"$version\" ]; then printf 'path=%s\\nversion=%s\\nsource=dpkg-query wireguard-go\\n' \"$path\" \"$version\"; else version=$(wireguard-go --version 2>&1 || true); if [ -n \"$version\" ]; then printf 'path=%s\\nversion=%s\\nsource=wireguard-go --version\\n' \"$path\" \"$version\"; else printf 'reason=wireguard-go version unavailable\\n'; exit 1; fi; fi; else printf 'reason=wireguard-go executable not found\\n'; exit 127; fi";
        var command = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            versionCommand
        }, options.CommandTimeout);
        commands.Add(command);

        var values = ReadKeyValueLines(command.Stdout);
        values.TryGetValue("path", out var path);
        values.TryGetValue("version", out var version);
        values.TryGetValue("source", out var source);
        values.TryGetValue("reason", out var reason);

        if (command.ExitCode != 0)
        {
            return new TunBenchmarkImplementationInfo(
                WireGuardGoScenario,
                null,
                path,
                source,
                FirstNonEmpty(reason, command.Stderr, command.Stdout, $"wireguard-go version command exited {command.ExitCode.ToString(CultureInfo.InvariantCulture)}."));
        }

        return new TunBenchmarkImplementationInfo(
            WireGuardGoScenario,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.IsNullOrWhiteSpace(path) ? null : path,
            string.IsNullOrWhiteSpace(source) ? null : source,
            string.IsNullOrWhiteSpace(version) ? FirstNonEmpty(reason, "wireguard-go version output did not include a version.") : null);
    }

    static TunBenchmarkImplementationInfo ReadWireGuardGoPNetIcmpEchoImplementationInfo(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var wireGuard = ReadWireGuardGoImplementationInfo(commandRunner, options, node, commands);
        var pnetVersion = typeof(TunPNetBenchmarkRunner).Assembly.GetName().Version?.ToString();
        var version = string.IsNullOrWhiteSpace(wireGuard.Version)
            ? pnetVersion
            : $"{wireGuard.Version}; pnet {pnetVersion}";

        return new TunBenchmarkImplementationInfo(
            WireGuardGoPNetIcmpEchoScenario,
            version,
            $"{wireGuard.ExecutablePath ?? "wireguard-go"} + PNet.Mesh.Tun.Cli.dll icmp-echo",
            wireGuard.VersionSource,
            wireGuard.VersionUnavailableReason);
    }

    static Dictionary<string, string> ReadKeyValueLines(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            values[line[..separator]] = line[(separator + 1)..];
        }

        return values;
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    static string CreateManagedCounterUnavailableReason(string scenario)
    {
        return scenario switch
        {
            PNetMeshTunScenario => "PNet.Mesh.Tun.Cli managed runtime snapshots were not captured.",
            WireGuardGoScenario => "wireguard-go is not a .NET process; managed .NET allocation counters do not apply.",
            WireGuardGoPNetIcmpEchoScenario => "PNet.Mesh.Tun.Cli managed runtime snapshots were not captured; wireguard-go is not a .NET process.",
            var value when IsTunOnlyIcmpEchoScenario(value) => "PNet.Mesh.Tun.Cli managed runtime snapshots were not captured.",
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{scenario}'.")
        };
    }

    static string GetScenarioDisplayName(string scenario)
    {
        return scenario switch
        {
            PNetMeshTunScenario => "PNet.Mesh.Tun",
            WireGuardGoScenario => "wireguard-go",
            WireGuardGoPNetIcmpEchoScenario => "wireguard-go + PNet ICMP echo",
            TunIcmpEchoDirectScenario => "TUN ICMP echo direct",
            TunIcmpEchoBridgeQueueScenario => "TUN ICMP echo bridge queue",
            _ => scenario
        };
    }

    static int? TryReadLeadingInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return TryReadInt(token);
    }

    static int? TryReadTrailingInt(string text)
    {
        var token = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return TryReadInt(token);
    }

    static int? TryReadInt(string? text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    static long? TryReadInt64(string? text)
    {
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    static double? TryReadDouble(string? text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    static double? TryReadPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var percent = text.Replace("% packet loss", string.Empty, StringComparison.Ordinal).Trim();
        return TryReadDouble(percent);
    }

    static long? TryReadKilobytes(string text)
    {
        var value = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return TryReadInt64(value) * 1024;
    }

    static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            return true;

        value = default;
        return false;
    }

    static double? TryReadJsonDouble(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var value) && value.TryGetDouble(out var result)
            ? result
            : null;
    }

    static long? TryReadJsonInt64(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    static string TrimOutput(string output)
    {
        const int maxLength = 4000;
        if (output.Length <= maxLength)
            return output;

        return output[..maxLength] + "\n[truncated]";
    }

    static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
