using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    const string Kind = "pnet-mesh-tun-benchmark";
    const string PNetMeshTunScenario = "pnet-mesh-tun";
    const string WireGuardGoScenario = "wireguard-go";
    const string WireGuardGoPNetIcmpEchoScenario = "wireguard-go-pnet-icmp-echo";
    const string TunIcmpEchoDirectScenario = "tun-icmp-echo-direct";
    const string TunIcmpEchoBridgeQueueScenario = "tun-icmp-echo-bridge-queue";
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

            var tunOnly = IsTunOnlyIcmpEchoScenario(options.Scenario);

            if (!StartBenchmarkProcesses(commandRunner, options, left, right, leftKey, rightKey, psk, commands))
            {
                status = "fail";
                message = $"{GetScenarioDisplayName(options.Scenario)} process could not be started in both topology containers.";
            }
            else if (!WaitForBenchmarkProcess(commandRunner, options, left, commands)
                     || (!tunOnly && !WaitForBenchmarkProcess(commandRunner, options, right, commands)))
            {
                status = "fail";
                message = tunOnly
                    ? $"{GetScenarioDisplayName(options.Scenario)} process did not stay running in the topology container."
                    : $"{GetScenarioDisplayName(options.Scenario)} process did not stay running in both topology containers.";
            }
            else
            {
                Thread.Sleep(options.Warmup);
                if (tunOnly)
                {
                    if (!RunReadinessPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands, out message))
                    {
                        status = "fail";
                    }
                    else
                    {
                        traffic.Add(RunPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                        CapturePacketTrace(commandRunner, options, left, commands);
                        DumpTunOnlyIcmpEchoMetrics(commandRunner, options, left, commands);
                        CaptureProcessLog(commandRunner, options, left, commands);
                        processes.Add(ReadProcessMetrics(commandRunner, options, left, commands));
                    }
                }
                else if (options.Scenario == WireGuardGoPNetIcmpEchoScenario)
                {
                    if (!RunReadinessPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands, out message))
                    {
                        status = "fail";
                    }
                    else
                    {
                        traffic.Add(RunPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                        CapturePacketTrace(commandRunner, options, left, commands);
                        CapturePacketTrace(commandRunner, options, right, commands);
                        CaptureProcessLog(commandRunner, options, left, commands);
                        CaptureProcessLog(commandRunner, options, right, commands);
                        processes.Add(ReadProcessMetrics(commandRunner, options, right, commands));
                    }
                }
                else
                {
                    if (!RunReadinessPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands, out message)
                        || !RunReadinessPing(commandRunner, options, left, right, "ipv6", "fd80::2", true, commands, out message))
                    {
                        status = "fail";
                    }
                    else
                    {
                        traffic.Add(RunPing(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                        traffic.Add(RunPing(commandRunner, options, left, right, "ipv6", "fd80::2", true, commands));
                        traffic.Add(RunIperf(commandRunner, options, left, right, "ipv4", "10.80.0.2", false, commands));
                        traffic.Add(RunIperf(commandRunner, options, left, right, "ipv6", "fd80::2", true, commands));
                        CapturePacketTrace(commandRunner, options, left, commands);
                        CapturePacketTrace(commandRunner, options, right, commands);
                        CaptureProcessLog(commandRunner, options, left, commands);
                        CaptureProcessLog(commandRunner, options, right, commands);
                        processes.Add(ReadProcessMetrics(commandRunner, options, left, commands));
                    }
                }

                if (status != "fail")
                {
                    status = traffic.All(IsSuccessfulTrafficResult) && processes.All(process => process.Available)
                        ? "pass"
                        : "fail";
                    message = status == "pass"
                        ? $"{GetScenarioDisplayName(options.Scenario)} benchmark traffic completed."
                        : $"One or more {GetScenarioDisplayName(options.Scenario)} benchmark probes failed; see traffic and process records.";
                }
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
            WireGuardGoPNetIcmpEchoScenario => StartWireGuardGoPNetIcmpEchoProcesses(commandRunner, options, left, right, rightKey, psk, commands),
            TunIcmpEchoDirectScenario => StartTunIcmpEchoProcess(commandRunner, options, left, "direct", commands),
            TunIcmpEchoBridgeQueueScenario => StartTunIcmpEchoProcess(commandRunner, options, left, "bridge-queue", commands),
            _ => throw new InvalidOperationException($"Unsupported TUN benchmark scenario '{options.Scenario}'.")
        };
    }

    static bool IsTunOnlyIcmpEchoScenario(string scenario)
    {
        return string.Equals(scenario, TunIcmpEchoDirectScenario, StringComparison.Ordinal)
               || string.Equals(scenario, TunIcmpEchoBridgeQueueScenario, StringComparison.Ordinal);
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

        if (!CopySecretFiles(commandRunner, options, node, Convert.ToBase64String(nodeKey.PublicKey), Convert.ToBase64String(nodeKey.PrivateKey), Convert.ToBase64String(psk), commands))
            return false;

        var shellCommand = "rm -f /tmp/pnet-tun.log; "
                           + CreatePacketTraceEnvironment(node)
                           + CreatePNetUdpProbeEnvironment(options)
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
            "dotnet PNet.Mesh.Tun.Cli.dll run --interface <interface> --mtu <mtu> --address <address> --route <route> --bind <endpoint> --public-key-file <container-secret> --private-key-file <container-secret> --psk-file <container-secret> --peer <redacted> --allowed-ip <prefix> > /tmp/pnet-tun.log 2>&1"
        };

        commands.Add(RunCommand(commandRunner, "docker", dockerArguments, options.CommandTimeout, reportedArguments));
        return true;
    }

    static bool StartPNetIcmpEchoProcess(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        TunTopologyNode peer,
        string peerPublicKey,
        List<TunTopologyCommandRecord> commands)
    {
        const string secretDirectory = "/tmp/pnet-tun-secrets";
        const string publicKeyPath = $"{secretDirectory}/public.key";
        const string privateKeyPath = $"{secretDirectory}/private.key";
        const string pskPath = $"{secretDirectory}/psk";

        var arguments = new List<string>
        {
            "dotnet",
            "PNet.Mesh.Tun.Cli.dll",
            "icmp-echo",
            "--bind",
            $"0.0.0.0:{node.PNetUdpPort.ToString(CultureInfo.InvariantCulture)}",
            "--public-key-file",
            publicKeyPath,
            "--private-key-file",
            privateKeyPath,
            "--psk-file",
            pskPath,
            "--peer",
            $"{peer.Role}:{peerPublicKey}@{peer.HostName}:{peer.WireGuardUdpPort.ToString(CultureInfo.InvariantCulture)}"
        };

        var shellCommand = "rm -f /tmp/pnet-icmp-echo.log; "
                           + string.Join(" ", arguments.Select(ShellQuote))
                           + " > /tmp/pnet-icmp-echo.log 2>&1";

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
            "dotnet PNet.Mesh.Tun.Cli.dll icmp-echo --bind <endpoint> --public-key-file <container-secret> --private-key-file <container-secret> --psk-file <container-secret> --peer <redacted> > /tmp/pnet-icmp-echo.log 2>&1"
        };

        commands.Add(RunCommand(commandRunner, "docker", dockerArguments, options.CommandTimeout, reportedArguments));
        return true;
    }

    static string CreatePacketTraceEnvironment(TunTopologyNode node)
    {
        return "PNET_MESH_PACKET_TRACE_ROLE="
               + ShellQuote(node.Role)
               + " PNET_MESH_PACKET_TRACE_FILE=/tmp/pnet-packet-trace.csv PNET_MESH_PACKET_TRACE_CAPACITY=131072 ";
    }

    static string CreatePNetUdpProbeEnvironment(TunPNetBenchmarkOptions options)
    {
        var variables = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.PNetUdpReceiveMode))
            variables.Add("PNET_MESH_UDP_RECEIVE_MODE=" + ShellQuote(options.PNetUdpReceiveMode));

        if (options.PNetUdpSocketBufferBytes is { } socketBufferBytes)
            variables.Add("PNET_MESH_UDP_SOCKET_BUFFER_BYTES=" + socketBufferBytes.ToString(CultureInfo.InvariantCulture));

        return variables.Count == 0 ? string.Empty : string.Join(" ", variables) + " ";
    }

    static bool StartTunIcmpEchoProcess(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode node,
        string mode,
        List<TunTopologyCommandRecord> commands)
    {
        var arguments = new List<string>
        {
            "dotnet",
            "PNet.Mesh.Tun.Cli.dll",
            "tun-icmp-echo",
            "--interface",
            node.InterfaceName,
            "--mtu",
            options.Mtu.ToString(CultureInfo.InvariantCulture),
            "--address",
            node.Ipv4Address,
            "--route",
            node.PeerRoutes.First(route => !route.Contains(':', StringComparison.Ordinal)),
            "--tun-echo-mode",
            mode
        };

        var shellCommand = "rm -f /tmp/pnet-tun-icmp-echo.log; "
                           + string.Join(" ", arguments.Select(ShellQuote))
                           + " > /tmp/pnet-tun-icmp-echo.log 2>&1";

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
            $"dotnet PNet.Mesh.Tun.Cli.dll tun-icmp-echo --interface <interface> --mtu <mtu> --address <address> --route <route> --tun-echo-mode {mode} > /tmp/pnet-tun-icmp-echo.log 2>&1"
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

    static bool StartWireGuardGoPNetIcmpEchoProcesses(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        TunTopologyNode left,
        TunTopologyNode right,
        KeyPair pnetKey,
        byte[] psk,
        List<TunTopologyCommandRecord> commands)
    {
        var pskText = Convert.ToBase64String(psk);
        var pnetPublicKey = Convert.ToBase64String(pnetKey.PublicKey);
        return PrepareWireGuardGoSecretFiles(commandRunner, options, left, pskText, commands, out var wireGuardPublicKey)
               && CopySecretFiles(commandRunner, options, right, pnetPublicKey, Convert.ToBase64String(pnetKey.PrivateKey), pskText, commands)
               && StartWireGuardGoProcess(commandRunner, options, left, right, pnetPublicKey, commands, right.PNetUdpPort)
               && StartPNetIcmpEchoProcess(commandRunner, options, right, left, wireGuardPublicKey, commands);
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
        List<TunTopologyCommandRecord> commands,
        int? peerEndpointPort = null)
    {
        const string secretDirectory = "/tmp/pnet-tun-secrets";
        const string privateKeyPath = $"{secretDirectory}/private.key";
        const string pskPath = $"{secretDirectory}/psk";
        var peerEndpoint = $"{peer.HostName}:{(peerEndpointPort ?? peer.WireGuardUdpPort).ToString(CultureInfo.InvariantCulture)}";
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

    static void DumpTunOnlyIcmpEchoMetrics(
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
            $"pid=$(pgrep -f {ShellQuote(processPattern)} | head -n1); test -n \"$pid\" || exit 1; kill -HUP \"$pid\""
        }, options.CommandTimeout);
        commands.Add(command);
        Thread.Sleep(TimeSpan.FromMilliseconds(250));
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

}
