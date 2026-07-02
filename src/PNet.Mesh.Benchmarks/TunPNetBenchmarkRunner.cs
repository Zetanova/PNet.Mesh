using Noise;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static class TunPNetBenchmarkRunner
{
    const string Kind = "pnet-mesh-tun-benchmark";
    const string PNetMeshTunScenario = "pnet-mesh-tun";
    const string WireGuardGoScenario = "wireguard-go";
    const string DefaultName = "pnet-tun-bench";
    const string DefaultImage = "localhost/pnet-mesh-tun:dev";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        return Run(args, output, error, SystemTunTopologyCommandRunner.Instance);
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error, ITunTopologyCommandRunner commandRunner)
    {
        if (!TunPNetBenchmarkOptions.TryParse(args, error, out var options))
            return 2;

        if (options.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        var report = RunBenchmark(options, commandRunner);
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "fail" ? 1 : 0;
    }

    internal static TunPNetBenchmarkReport RunBenchmark(TunPNetBenchmarkOptions options, ITunTopologyCommandRunner commandRunner)
    {
        var topologyOptions = CreateTopologyOptions("create", options);
        var createReport = TunBenchmarkTopologyRunner.CreateTopology(topologyOptions, commandRunner);
        var topologyReports = new List<TunTopologyReport> { createReport };
        var commands = createReport.Commands.ToList();
        var traffic = new List<TunBenchmarkTrafficResult>();
        var processes = new List<TunBenchmarkProcessMetrics>();
        var implementation = CreateInitialImplementationInfo(options);
        var managedCounterReason = CreateManagedCounterUnavailableReason(options.Scenario);
        var status = createReport.Status;
        var message = createReport.Status == "pass"
            ? $"{GetScenarioDisplayName(options.Scenario)} benchmark completed."
            : $"Topology create returned {createReport.Status}.";

        if (createReport.Status != "pass")
        {
            return CreateReport(options, implementation, createReport.Topology, status, message, topologyReports, commands, traffic, processes, managedCounterReason);
        }

        try
        {
            using var leftKey = KeyPair.Generate();
            using var rightKey = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var spec = createReport.Topology;
            var left = spec.Nodes.Single(node => node.Role == "left");
            var right = spec.Nodes.Single(node => node.Role == "right");
            implementation = ReadImplementationInfo(commandRunner, options, left, commands);

            if (!StartBenchmarkProcesses(commandRunner, options, left, right, leftKey, rightKey, psk, commands))
            {
                status = "fail";
                message = $"{GetScenarioDisplayName(options.Scenario)} process could not be started in both topology containers.";
            }
            else if (!WaitForBenchmarkProcess(commandRunner, options, left, commands) || !WaitForBenchmarkProcess(commandRunner, options, right, commands))
            {
                status = "fail";
                message = $"{GetScenarioDisplayName(options.Scenario)} process did not stay running in both topology containers.";
            }
            else
            {
                Thread.Sleep(options.Warmup);
                WarmupTunnel(commandRunner, options, left, "ipv4", "10.80.0.2", false, commands);
                WarmupTunnel(commandRunner, options, left, "ipv6", "fd80::2", true, commands);

                traffic.Add(RunPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                traffic.Add(RunPing(commandRunner, options, left, right, "ipv6", "fd80::2", true, commands));
                traffic.Add(RunIperf(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                traffic.Add(RunIperf(commandRunner, options, left, right, "ipv6", "fd80::2", true, commands));

                CaptureProcessLog(commandRunner, options, left, commands);
                CaptureProcessLog(commandRunner, options, right, commands);
                processes.Add(ReadProcessMetrics(commandRunner, options, left, commands));
                processes.Add(ReadProcessMetrics(commandRunner, options, right, commands));

                status = traffic.All(IsSuccessfulTrafficResult) && processes.All(process => process.Available)
                    ? "pass"
                    : "fail";
                message = status == "pass"
                    ? $"{GetScenarioDisplayName(options.Scenario)} benchmark traffic completed."
                    : $"One or more {GetScenarioDisplayName(options.Scenario)} benchmark probes failed; see traffic and process records.";
            }
        }
        finally
        {
            var teardownOptions = CreateTopologyOptions("teardown", options);
            var teardownReport = TunBenchmarkTopologyRunner.TeardownTopology(teardownOptions, commandRunner);
            topologyReports.Add(teardownReport);
            commands.AddRange(teardownReport.Commands);
            if (teardownReport.Status != "pass")
            {
                status = "fail";
                message = $"{message} Topology teardown returned {teardownReport.Status}.";
            }
        }

        return CreateReport(options, implementation, createReport.Topology, status, message, topologyReports, commands, traffic, processes, managedCounterReason);
    }

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
            null,
            null,
            null,
            null,
            seconds,
            bytes,
            bitsPerSecond,
            TrimOutput(command.Stdout),
            TrimOutput(command.Stderr));
    }

    static bool StartBenchmarkProcesses(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode left,
        TunTopologyNode right,
        KeyPair leftKey,
        KeyPair rightKey,
        byte[] psk,
        List<TunTopologyCommandRecord> commands)
    {
        return options.Scenario switch
        {
            PNetMeshTunScenario => StartPNetMeshTunProcess(commandRunner, options, left, right, leftKey, rightKey.PublicKey, psk, commands)
                                  && StartPNetMeshTunProcess(commandRunner, options, right, left, rightKey, leftKey.PublicKey, psk, commands),
            WireGuardGoScenario => StartWireGuardGoProcesses(commandRunner, options, left, right, psk, commands),
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static bool StartPNetMeshTunProcess(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        TunTopologyNode peer,
        KeyPair nodeKey,
        byte[] peerPublicKey,
        byte[] psk,
        List<TunTopologyCommandRecord> commands)
    {
        const string secretDirectory = "/tmp/pnet-tun-secrets";
        const string publicKeyPath = $"{secretDirectory}/public.key";
        const string privateKeyPath = $"{secretDirectory}/private.key";
        const string pskPath = $"{secretDirectory}/psk";

        var tunArguments = new List<string>
        {
            "dotnet",
            "PNet.Mesh.Tun.Cli.dll",
            "run",
            "--interface",
            node.InterfaceName,
            "--mtu",
            options.Mtu.ToString(CultureInfo.InvariantCulture),
            "--address",
            node.Ipv4Address,
            "--address",
            node.Ipv6Address
        };

        foreach (var route in node.PeerRoutes)
        {
            tunArguments.Add("--route");
            tunArguments.Add(route);
        }

        tunArguments.AddRange(new[]
        {
            "--bind",
            $"0.0.0.0:{node.PNetUdpPort}",
            "--public-key-file",
            publicKeyPath,
            "--private-key-file",
            privateKeyPath,
            "--psk-file",
            pskPath,
            "--peer",
            $"{peer.Role}:{Convert.ToBase64String(peerPublicKey)}@{peer.HostName}:{peer.PNetUdpPort}"
        });

        foreach (var route in node.PeerRoutes)
        {
            tunArguments.Add("--allowed-ip");
            tunArguments.Add($"{peer.Role}={route}");
        }
        tunArguments.Add("--verbose");

        if (!CopySecretFiles(commandRunner, options, node, Convert.ToBase64String(nodeKey.PublicKey), Convert.ToBase64String(nodeKey.PrivateKey), Convert.ToBase64String(psk), commands))
            return false;

        var shellCommand = "rm -f /tmp/pnet-tun.log; "
                           + string.Join(" ", tunArguments.Select(ShellQuote))
                           + " > /tmp/pnet-tun.log 2>&1";

        var dockerArguments = new[]
        {
            "exec",
            "-d",
            node.ContainerName,
            "sh",
            "-c",
            shellCommand
        };
        var reportedArguments = new[]
        {
            "exec",
            "-d",
            node.ContainerName,
            "sh",
            "-c",
            "dotnet PNet.Mesh.Tun.Cli.dll run --interface <interface> --mtu <mtu> --address <address> --route <route> --bind <endpoint> --public-key-file <container-secret> --private-key-file <container-secret> --psk-file <container-secret> --peer <redacted> --allowed-ip <prefix> --verbose > /tmp/pnet-tun.log 2>&1"
        };

        commands.Add(RunCommand(commandRunner, "docker", dockerArguments, options.CommandTimeout, reportedArguments));
        return true;
    }

    static bool StartWireGuardGoProcesses(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode left,
        TunTopologyNode right,
        byte[] psk,
        List<TunTopologyCommandRecord> commands)
    {
        var pskText = Convert.ToBase64String(psk);
        return PrepareWireGuardGoSecretFiles(commandRunner, options, left, pskText, commands, out var leftPublicKey)
               && PrepareWireGuardGoSecretFiles(commandRunner, options, right, pskText, commands, out var rightPublicKey)
               && StartWireGuardGoProcess(commandRunner, options, left, right, rightPublicKey, commands)
               && StartWireGuardGoProcess(commandRunner, options, right, left, leftPublicKey, commands);
    }

    static bool PrepareWireGuardGoSecretFiles(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        string psk,
        List<TunTopologyCommandRecord> commands,
        out string publicKey)
    {
        const string secretDirectory = "/tmp/pnet-tun-secrets";
        publicKey = string.Empty;
        string? tempDirectory = null;

        try
        {
            var prepare = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                node.ContainerName,
                "sh",
                "-c",
                "rm -rf /tmp/pnet-tun-secrets; install -d -m 700 /tmp/pnet-tun-secrets; umask 077; wg genkey > /tmp/pnet-tun-secrets/private.key; wg pubkey < /tmp/pnet-tun-secrets/private.key > /tmp/pnet-tun-secrets/public.key"
            }, options.CommandTimeout, new[]
            {
                "exec",
                node.ContainerName,
                "sh",
                "-c",
                "rm -rf /tmp/pnet-tun-secrets; install -d -m 700 /tmp/pnet-tun-secrets; umask 077; wg genkey > /tmp/pnet-tun-secrets/private.key; wg pubkey < /tmp/pnet-tun-secrets/private.key > /tmp/pnet-tun-secrets/public.key"
            });
            commands.Add(prepare);
            if (prepare.ExitCode != 0)
                return false;

            tempDirectory = Directory.CreateTempSubdirectory("pnet-tun-secrets-").FullName;
            var pskFile = WriteSecretFile(tempDirectory, "psk", psk);
            if (!CopySecretFile(commandRunner, options, node, pskFile, "psk", commands))
                return false;

            var readPublicKey = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                node.ContainerName,
                "cat",
                $"{secretDirectory}/public.key"
            }, options.CommandTimeout);
            commands.Add(readPublicKey);
            if (readPublicKey.ExitCode != 0 || string.IsNullOrWhiteSpace(readPublicKey.Stdout))
                return false;

            publicKey = readPublicKey.Stdout.Trim();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            commands.Add(new TunTopologyCommandRecord(
                "host",
                new[] { "write-wireguard-go-psk", node.Role },
                -1,
                string.Empty,
                TrimOutput(ex.Message),
                false));
            return false;
        }
        finally
        {
            if (tempDirectory != null)
                TryDeleteDirectory(tempDirectory);
        }
    }

    static bool StartWireGuardGoProcess(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        TunTopologyNode peer,
        string peerPublicKey,
        List<TunTopologyCommandRecord> commands)
    {
        const string secretDirectory = "/tmp/pnet-tun-secrets";
        const string privateKeyPath = $"{secretDirectory}/private.key";
        const string pskPath = $"{secretDirectory}/psk";
        var peerEndpoint = $"{peer.HostName}:{peer.WireGuardUdpPort}";
        var allowedIps = string.Join(",", node.PeerRoutes);
        var wgArguments = new[]
        {
            "wg",
            "set",
            node.InterfaceName,
            "private-key",
            privateKeyPath,
            "listen-port",
            node.WireGuardUdpPort.ToString(CultureInfo.InvariantCulture),
            "peer",
            peerPublicKey,
            "preshared-key",
            pskPath,
            "endpoint",
            peerEndpoint,
            "allowed-ips",
            allowedIps
        };

        var start = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            "-d",
            node.ContainerName,
            "sh",
            "-c",
            $"rm -f /tmp/wireguard-go.log; exec wireguard-go {ShellQuote(node.InterfaceName)} > /tmp/wireguard-go.log 2>&1"
        }, options.CommandTimeout, new[]
        {
            "exec",
            "-d",
            node.ContainerName,
            "sh",
            "-c",
            $"rm -f /tmp/wireguard-go.log; exec wireguard-go {node.InterfaceName} > /tmp/wireguard-go.log 2>&1"
        });
        commands.Add(start);
        if (start.ExitCode != 0)
            return false;

        if (!WaitForWireGuardGoInterface(commandRunner, options, node, commands))
            return false;

        var shellCommands = new List<string>
        {
            string.Join(" ", wgArguments.Select(ShellQuote)),
            $"ip link set dev {ShellQuote(node.InterfaceName)} mtu {options.Mtu.ToString(CultureInfo.InvariantCulture)}",
            $"ip addr replace {ShellQuote(node.Ipv4Address)} dev {ShellQuote(node.InterfaceName)}",
            $"ip -6 addr replace {ShellQuote(node.Ipv6Address)} dev {ShellQuote(node.InterfaceName)}",
            $"ip link set dev {ShellQuote(node.InterfaceName)} up"
        };

        foreach (var route in node.PeerRoutes)
        {
            var family = route.Contains(':', StringComparison.Ordinal) ? "ip -6 route" : "ip route";
            shellCommands.Add($"{family} replace {ShellQuote(route)} dev {ShellQuote(node.InterfaceName)}");
        }

        var configure = RunCommand(commandRunner, "docker", new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            "set -e; " + string.Join("; ", shellCommands)
        }, options.CommandTimeout, CreateReportedWireGuardGoConfigureArguments(node, peerEndpoint, allowedIps, options.Mtu));
        commands.Add(configure);
        return configure.ExitCode == 0;
    }

    static bool WaitForWireGuardGoInterface(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var deadline = DateTimeOffset.UtcNow + options.ProcessStartTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var command = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                node.ContainerName,
                "ip",
                "link",
                "show",
                "dev",
                node.InterfaceName
            }, options.CommandTimeout);
            commands.Add(command);
            if (command.ExitCode == 0)
                return true;

            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    static IReadOnlyList<string> CreateReportedWireGuardGoConfigureArguments(
        TunTopologyNode node,
        string peerEndpoint,
        string allowedIps,
        int mtu)
    {
        var reportedShellCommands = new List<string>
        {
            $"wg set {node.InterfaceName} private-key <container-secret> listen-port {node.WireGuardUdpPort.ToString(CultureInfo.InvariantCulture)} peer <peer-public-key> preshared-key <container-secret> endpoint {peerEndpoint} allowed-ips {allowedIps}",
            $"ip link set dev {node.InterfaceName} mtu {mtu.ToString(CultureInfo.InvariantCulture)}",
            $"ip addr replace {node.Ipv4Address} dev {node.InterfaceName}",
            $"ip -6 addr replace {node.Ipv6Address} dev {node.InterfaceName}",
            $"ip link set dev {node.InterfaceName} up"
        };

        foreach (var route in node.PeerRoutes)
        {
            var family = route.Contains(':', StringComparison.Ordinal) ? "ip -6 route" : "ip route";
            reportedShellCommands.Add($"{family} replace {route} dev {node.InterfaceName}");
        }

        return new[]
        {
            "exec",
            node.ContainerName,
            "sh",
            "-c",
            "set -e; " + string.Join("; ", reportedShellCommands)
        };
    }

    static bool CopySecretFiles(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        string publicKey,
        string privateKey,
        string psk,
        List<TunTopologyCommandRecord> commands)
    {
        string? tempDirectory = null;
        try
        {
            tempDirectory = Directory.CreateTempSubdirectory("pnet-tun-secrets-").FullName;
            var publicKeyFile = WriteSecretFile(tempDirectory, "public.key", publicKey);
            var privateKeyFile = WriteSecretFile(tempDirectory, "private.key", privateKey);
            var pskFile = WriteSecretFile(tempDirectory, "psk", psk);

            var prepare = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                node.ContainerName,
                "sh",
                "-c",
                "rm -rf /tmp/pnet-tun-secrets; install -d -m 700 /tmp/pnet-tun-secrets"
            }, options.CommandTimeout);
            commands.Add(prepare);
            if (prepare.ExitCode != 0)
                return false;

            return CopySecretFile(commandRunner, options, node, publicKeyFile, "public.key", commands)
                   && CopySecretFile(commandRunner, options, node, privateKeyFile, "private.key", commands)
                   && CopySecretFile(commandRunner, options, node, pskFile, "psk", commands);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            commands.Add(new TunTopologyCommandRecord(
                "host",
                new[] { "write-secret-files", node.Role },
                -1,
                string.Empty,
                TrimOutput(ex.Message),
                false));
            return false;
        }
        finally
        {
            if (tempDirectory != null)
                TryDeleteDirectory(tempDirectory);
        }
    }

    static string WriteSecretFile(string directory, string fileName, string value)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, value);
        TryRestrictFile(path);
        return path;
    }

    static bool CopySecretFile(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        string sourcePath,
        string destinationName,
        List<TunTopologyCommandRecord> commands)
    {
        var destination = $"{node.ContainerName}:/tmp/pnet-tun-secrets/{destinationName}";
        var command = RunCommand(commandRunner, "docker", new[]
        {
            "cp",
            sourcePath,
            destination
        }, options.CommandTimeout, new[]
        {
            "cp",
            "<host-secret-file>",
            destination
        });
        commands.Add(command);
        return command.ExitCode == 0;
    }

    static void TryRestrictFile(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    static bool WaitForBenchmarkProcess(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        List<TunTopologyCommandRecord> commands)
    {
        var processPattern = CreateProcessPattern(options, node);
        var deadline = DateTimeOffset.UtcNow + options.ProcessStartTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var command = RunCommand(commandRunner, "docker", new[]
            {
                "exec",
                node.ContainerName,
                "pgrep",
                "-f",
                processPattern
            }, options.CommandTimeout);
            commands.Add(command);
            if (command.ExitCode == 0)
                return true;

            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }

        return false;
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
        int timeoutSeconds)
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
            Math.Max(timeoutSeconds, (count * timeoutSeconds) + 1).ToString(CultureInfo.InvariantCulture),
            targetAddress
        });
        return arguments;
    }

    static void WarmupTunnel(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode source,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands)
    {
        var deadline = DateTimeOffset.UtcNow + options.ProcessStartTimeout + options.CommandTimeout;
        do
        {
            var arguments = CreatePingArguments(source, targetAddress, ipv6, count: 1, timeoutSeconds: 5);
            var command = RunCommand(commandRunner, "docker", arguments, options.CommandTimeout);
            commands.Add(command);
            if (IsSuccessfulPing(ParsePingResult(protocol, source.Role, string.Empty, targetAddress, command)))
                return;

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }
        while (DateTimeOffset.UtcNow < deadline);
    }

    static TunBenchmarkTrafficResult RunIperf(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode source,
        TunTopologyNode target,
        string protocol,
        string targetAddress,
        bool ipv6,
        List<TunTopologyCommandRecord> commands)
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
            "-u",
            "-b",
            "1K",
            "-l",
            "64",
            "-t",
            Math.Ceiling(options.IperfDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            "--json"
        });

        var command = RunCommand(commandRunner, "docker", arguments, options.CommandTimeout + options.IperfDuration);
        commands.Add(command);
        return ParseIperfResult(protocol, source.Role, target.Role, targetAddress, command);
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

    static bool IsSuccessfulTrafficResult(TunBenchmarkTrafficResult result)
    {
        return result.Tool switch
        {
            "ping" => IsSuccessfulPing(result),
            "iperf3" => result.ExitCode == 0 && result.BitsPerSecond > 0,
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
            PNetMeshTunScenario => "dotnet-counters is not installed in the TUN CLI image.",
            WireGuardGoScenario => "wireguard-go is not a .NET process; managed .NET allocation counters do not apply.",
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{scenario}'.")
        };
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
                options.Mtu),
            traffic,
            processes,
            false,
            managedCounterUnavailableReason,
            topologyReports,
            commands,
            message);
    }

    static TunBenchmarkTopologyRunner.TunTopologyOptions CreateTopologyOptions(string action, TunPNetBenchmarkOptions options)
    {
        var args = new[]
        {
            action,
            "--name",
            options.Name,
            "--image",
            options.Image,
            "--timeout",
            options.CommandTimeout.ToString()
        };

        if (!TunBenchmarkTopologyRunner.TunTopologyOptions.TryParse(args, TextWriter.Null, out var topologyOptions))
            throw new InvalidOperationException("Internal topology options are invalid.");

        return topologyOptions;
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-benchmark pnet-mesh-tun|wireguard-go [--name <name>] [--image <image>] [--ping-count <count>] [--warmup <duration>] [--iperf-duration <duration>] [--timeout <duration>]");
        output.WriteLine();
        output.WriteLine("Runs a manual privileged TUN traffic benchmark on the #060 topology and emits JSON.");
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

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunPNetBenchmarkOptions options)
        {
            options = new TunPNetBenchmarkOptions();
            if (args.Length == 0 || IsHelp(args[0]))
            {
                options = new TunPNetBenchmarkOptions { ShowHelp = true };
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
                PingCount = pingCount
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
    int Mtu);

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
