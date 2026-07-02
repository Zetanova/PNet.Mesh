using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static class TunBenchmarkTopologyRunner
{
    const string Kind = "pnet-mesh-tun-benchmark-topology";
    const string DefaultName = "pnet-tun-bench";
    const string DefaultImage = "localhost/pnet-mesh-tun:dev";
    const string RequiredDevice = "/dev/net/tun";
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

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
        if (!TunTopologyOptions.TryParse(args, error, out var options))
            return 2;

        if (options.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        var report = options.Action switch
        {
            TunTopologyAction.Plan => CreateReport(options, "pass", "Topology plan generated.", Array.Empty<TunTopologyCheck>(), Array.Empty<TunTopologyCommandRecord>()),
            TunTopologyAction.Preflight => RunPreflight(options, commandRunner),
            TunTopologyAction.Create => CreateTopology(options, commandRunner),
            TunTopologyAction.Teardown => TeardownTopology(options, commandRunner),
            _ => throw new ArgumentOutOfRangeException(nameof(args))
        };

        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "fail" ? 1 : 0;
    }

    internal static TunBenchmarkTopologySpec CreateSpec(string name, string image)
    {
        var left = new TunTopologyNode(
            "left",
            $"{name}-left",
            "left",
            "pnet0",
            "10.80.0.1/24",
            "fd80::1/64",
            new[] { "10.80.0.2/32", "fd80::2/128" },
            12401,
            51820);
        var right = new TunTopologyNode(
            "right",
            $"{name}-right",
            "right",
            "pnet0",
            "10.80.0.2/24",
            "fd80::2/64",
            new[] { "10.80.0.1/32", "fd80::1/128" },
            12402,
            51821);

        return new TunBenchmarkTopologySpec(
            name,
            image,
            1280,
            name,
            RequiredDevice,
            new[] { "NET_ADMIN", "NET_RAW" },
            new[] { left, right },
            new[]
            {
                new TunTopologyImplementationSlot(
                    "pnet-mesh-tun",
                    "Run PNet.Mesh.Tun.Cli inside each prepared container using the node address, peer route, MTU, and PNet UDP port.",
                    "dotnet /app/PNet.Mesh.Tun.Cli.dll run --interface <node.interfaceName> --mtu <mtu> --address <node.ipv4Address> --address <node.ipv6Address> --route <node.peerRoutes...> --bind 0.0.0.0:<node.pnetUdpPort> --public-key-file <container-secret> --private-key-file <container-secret> --psk-file <container-secret> --peer <peer-name:public-key@peer-host:peer-port> --allowed-ip <peer-name=peer-route>"),
                new TunTopologyImplementationSlot(
                    "wireguard-go",
                    "Use the same prepared containers, MTU, TUN addresses, peer routes, and traffic endpoints for the wireguard-go comparison.",
                    "wireguard-go <node.interfaceName> with equivalent wg configuration and ListenPort <node.wireGuardUdpPort>")
            },
            new TunTopologyTrafficProfile(
                "left",
                "right",
                5201,
                new[] { "10.80.0.2", "fd80::2" },
                "Benchmark traffic is added by #061; this topology only reserves the endpoints."));
    }

    internal static TunTopologyReport RunPreflight(TunTopologyOptions options, ITunTopologyCommandRunner commandRunner)
    {
        var checks = new List<TunTopologyCheck>();
        var commands = new List<TunTopologyCommandRecord>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            checks.Add(new TunTopologyCheck("linux", "skip", "TUN benchmark topology is Linux-only."));
            return CreateReport(options, "skip", "Linux is required for privileged TUN benchmarks.", checks, commands);
        }

        checks.Add(new TunTopologyCheck("linux", "pass", RuntimeInformation.OSDescription));

        if (!commandRunner.FileExists(RequiredDevice))
        {
            checks.Add(new TunTopologyCheck("tun-device", "skip", $"{RequiredDevice} is not available on this host/container."));
            return CreateReport(options, "skip", $"{RequiredDevice} is required for privileged TUN benchmarks.", checks, commands);
        }

        checks.Add(new TunTopologyCheck("tun-device", "pass", $"{RequiredDevice} exists."));

        var dockerVersion = Execute(commandRunner, "docker", new[] { "version", "--format", "{{.Server.Version}}" }, options.Timeout, commands);
        if (dockerVersion.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("docker", "skip", "Docker server is unavailable or not reachable."));
            return CreateReport(options, "skip", "Docker is required for the containerized TUN topology.", checks, commands, dockerVersion.Stdout.Trim());
        }

        var dockerServerVersion = dockerVersion.Stdout.Trim();
        checks.Add(new TunTopologyCheck("docker", "pass", dockerServerVersion));

        var imageInspect = Execute(commandRunner, "docker", new[] { "image", "inspect", options.Image, "--format", "{{.Id}}" }, options.Timeout, commands);
        if (imageInspect.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("tun-image", "skip", $"Image '{options.Image}' is missing; build src/PNet.Mesh.Tun.Cli/Dockerfile first."));
            return CreateReport(options, "skip", "The TUN CLI image is required before topology setup.", checks, commands, dockerServerVersion);
        }

        checks.Add(new TunTopologyCheck("tun-image", "pass", options.Image));

        var privilegedProbe = Execute(commandRunner, "docker", new[]
        {
            "run",
            "--rm",
            "--network",
            "none",
            "--device",
            RequiredDevice,
            "--cap-add",
            "NET_ADMIN",
            "--cap-add",
            "NET_RAW",
            "--entrypoint",
            "/bin/sh",
            options.Image,
            "-c",
            "test -c /dev/net/tun && command -v ip >/dev/null && command -v ping >/dev/null && command -v iperf3 >/dev/null && command -v nc >/dev/null"
        }, options.Timeout, commands);
        if (privilegedProbe.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("privileged-container", "fail", "Privileged container probe failed."));
            return CreateReport(options, "fail", "Docker is reachable but cannot run the required privileged TUN container.", checks, commands, dockerServerVersion);
        }

        checks.Add(new TunTopologyCheck("privileged-container", "pass", "Container can access TUN and required network tools."));
        return CreateReport(options, "pass", "Privileged TUN benchmark topology preflight passed.", checks, commands, dockerServerVersion);
    }

    internal static TunTopologyReport CreateTopology(TunTopologyOptions options, ITunTopologyCommandRunner commandRunner)
    {
        var preflight = RunPreflight(options, commandRunner);
        if (preflight.Status != "pass")
        {
            return preflight with
            {
                Action = "create",
                Message = $"Topology create skipped because preflight returned {preflight.Status}."
            };
        }

        var commands = preflight.Commands.ToList();
        var checks = preflight.Checks.ToList();
        var spec = CreateSpec(options.Name, options.Image);

        var networkInspect = Execute(commandRunner, "docker", new[] { "network", "inspect", options.Name, "--format", "{{ index .Labels \"pnet.mesh.benchmark.topology\" }}" }, options.Timeout, commands);
        if (networkInspect.ExitCode == 0)
        {
            checks.Add(new TunTopologyCheck("existing-network", "fail", $"Docker network '{options.Name}' already exists."));
            return CreateReport(options, "fail", "Run teardown before creating the topology again.", checks, commands, preflight.Environment.ContainerEngineVersion);
        }

        var networkCreate = Execute(commandRunner, "docker", new[]
        {
            "network",
            "create",
            "--label",
            $"pnet.mesh.benchmark.topology={options.Name}",
            options.Name
        }, options.Timeout, commands);
        if (networkCreate.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("network-create", "fail", $"Could not create Docker network '{options.Name}'."));
            return CreateReport(options, "fail", "Docker network creation failed.", checks, commands, preflight.Environment.ContainerEngineVersion);
        }

        checks.Add(new TunTopologyCheck("network-create", "pass", options.Name));

        foreach (var node in spec.Nodes)
        {
            var containerCreate = Execute(commandRunner, "docker", new[]
            {
                "run",
                "-d",
                "--name",
                node.ContainerName,
                "--hostname",
                node.HostName,
                "--network",
                spec.DockerNetwork,
                "--network-alias",
                node.HostName,
                "--device",
                spec.RequiredDevice,
                "--cap-add",
                "NET_ADMIN",
                "--cap-add",
                "NET_RAW",
                "--label",
                $"pnet.mesh.benchmark.topology={options.Name}",
                "--label",
                $"pnet.mesh.benchmark.node={node.Role}",
                "--entrypoint",
                "/bin/sleep",
                options.Image,
                "infinity"
            }, options.Timeout, commands);

            if (containerCreate.ExitCode == 0)
            {
                checks.Add(new TunTopologyCheck($"container-{node.Role}", "pass", node.ContainerName));
                continue;
            }

            checks.Add(new TunTopologyCheck($"container-{node.Role}", "fail", $"Could not create container '{node.ContainerName}'."));
            BestEffortTeardown(options, commandRunner, commands);
            return CreateReport(options, "fail", $"Container '{node.ContainerName}' creation failed; best-effort cleanup was attempted.", checks, commands, preflight.Environment.ContainerEngineVersion);
        }

        return CreateReport(options, "pass", "Topology containers and Docker network created.", checks, commands, preflight.Environment.ContainerEngineVersion);
    }

    internal static TunTopologyReport TeardownTopology(TunTopologyOptions options, ITunTopologyCommandRunner commandRunner)
    {
        var commands = new List<TunTopologyCommandRecord>();
        var checks = new List<TunTopologyCheck>();

        var ps = Execute(commandRunner, "docker", new[] { "ps", "-aq", "--filter", $"label=pnet.mesh.benchmark.topology={options.Name}" }, options.Timeout, commands);
        if (ps.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("container-query", "fail", "Could not list topology containers."));
            return CreateReport(options, "fail", "Docker container query failed.", checks, commands);
        }

        var containerIds = ps.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (containerIds.Length > 0)
        {
            var rmArgs = new List<string> { "rm", "-f" };
            rmArgs.AddRange(containerIds);
            var rm = Execute(commandRunner, "docker", rmArgs, options.Timeout, commands);
            if (rm.ExitCode != 0)
            {
                checks.Add(new TunTopologyCheck("container-teardown", "fail", "Could not remove topology containers."));
                return CreateReport(options, "fail", "Container teardown failed.", checks, commands);
            }
        }

        checks.Add(new TunTopologyCheck("container-teardown", "pass", $"{containerIds.Length} container(s) removed."));

        var networkInspect = Execute(commandRunner, "docker", new[] { "network", "inspect", options.Name, "--format", "{{ index .Labels \"pnet.mesh.benchmark.topology\" }}" }, options.Timeout, commands);
        if (networkInspect.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("network-teardown", "pass", "Topology network was not present."));
            return CreateReport(options, "pass", "Topology teardown completed.", checks, commands);
        }

        if (!string.Equals(networkInspect.Stdout.Trim(), options.Name, StringComparison.Ordinal))
        {
            checks.Add(new TunTopologyCheck("network-teardown", "fail", $"Network '{options.Name}' exists but is not owned by this topology."));
            return CreateReport(options, "fail", "Refusing to remove an unowned Docker network.", checks, commands);
        }

        var networkRemove = Execute(commandRunner, "docker", new[] { "network", "rm", options.Name }, options.Timeout, commands);
        if (networkRemove.ExitCode != 0)
        {
            checks.Add(new TunTopologyCheck("network-teardown", "fail", $"Could not remove Docker network '{options.Name}'."));
            return CreateReport(options, "fail", "Network teardown failed.", checks, commands);
        }

        checks.Add(new TunTopologyCheck("network-teardown", "pass", options.Name));
        return CreateReport(options, "pass", "Topology teardown completed.", checks, commands);
    }

    static void BestEffortTeardown(TunTopologyOptions options, ITunTopologyCommandRunner commandRunner, List<TunTopologyCommandRecord> commands)
    {
        Execute(commandRunner, "docker", new[] { "ps", "-aq", "--filter", $"label=pnet.mesh.benchmark.topology={options.Name}" }, options.Timeout, commands);
        Execute(commandRunner, "docker", new[] { "rm", "-f", $"{options.Name}-left", $"{options.Name}-right" }, options.Timeout, commands);
        Execute(commandRunner, "docker", new[] { "network", "rm", options.Name }, options.Timeout, commands);
    }

    static TunTopologyCommandRecord Execute(
        ITunTopologyCommandRunner commandRunner,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        List<TunTopologyCommandRecord> commands)
    {
        var result = commandRunner.Run(fileName, arguments, timeout);
        var record = new TunTopologyCommandRecord(
            result.FileName,
            result.Arguments,
            result.ExitCode,
            TrimOutput(result.Stdout),
            TrimOutput(result.Stderr),
            result.TimedOut);
        commands.Add(record);
        return record;
    }

    static string TrimOutput(string output)
    {
        const int maxLength = 4000;
        if (output.Length <= maxLength)
            return output;

        return output[..maxLength] + "\n[truncated]";
    }

    static TunTopologyReport CreateReport(
        TunTopologyOptions options,
        string status,
        string message,
        IReadOnlyList<TunTopologyCheck> checks,
        IReadOnlyList<TunTopologyCommandRecord> commands,
        string? dockerServerVersion = null)
    {
        return new TunTopologyReport(
            Kind,
            options.Action.ToString().ToLowerInvariant(),
            status,
            DateTimeOffset.UtcNow,
            new TunTopologyEnvironment(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                dockerServerVersion),
            CreateSpec(options.Name, options.Image),
            checks,
            commands,
            message);
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --tun-topology plan|preflight|create|teardown [--name <name>] [--image <image>] [--timeout <duration>]");
        output.WriteLine();
        output.WriteLine($"Defaults: --name {DefaultName}, --image {DefaultImage}, --timeout {DefaultTimeout}.");
        output.WriteLine("Durations accept TimeSpan values, seconds with 's', or milliseconds with 'ms'.");
        output.WriteLine("All actions emit JSON. preflight reports pass/skip/fail without mutating Docker state.");
    }

    internal sealed class TunTopologyOptions
    {
        public TunTopologyAction Action { get; private init; }

        public string Name { get; private init; } = DefaultName;

        public string Image { get; private init; } = DefaultImage;

        public TimeSpan Timeout { get; private init; } = DefaultTimeout;

        public bool ShowHelp { get; private init; }

        public static bool TryParse(string[] args, TextWriter error, out TunTopologyOptions options)
        {
            options = new TunTopologyOptions();
            if (args.Length == 0 || IsHelp(args[0]))
            {
                options = new TunTopologyOptions { ShowHelp = true };
                return true;
            }

            if (!TryParseAction(args[0], out var action))
            {
                error.WriteLine($"Unknown TUN topology action '{args[0]}'.");
                return false;
            }

            var name = DefaultName;
            var image = DefaultImage;
            var timeout = DefaultTimeout;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--name":
                        if (!TryReadValue(args, ref i, out name) || !IsSafeDockerName(name))
                        {
                            error.WriteLine("--name requires a Docker-safe value.");
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
                        if (!TryReadValue(args, ref i, out var timeoutText) || !TryReadDuration(timeoutText, out timeout) || timeout <= TimeSpan.Zero)
                        {
                            error.WriteLine("--timeout requires a positive duration.");
                            return false;
                        }
                        break;
                    case "--help":
                    case "-h":
                        options = new TunTopologyOptions { ShowHelp = true };
                        return true;
                    default:
                        error.WriteLine($"Unknown TUN topology option '{args[i]}'.");
                        return false;
                }
            }

            options = new TunTopologyOptions
            {
                Action = action,
                Name = name,
                Image = image,
                Timeout = timeout
            };
            return true;
        }

        static bool TryParseAction(string text, out TunTopologyAction action)
        {
            action = text switch
            {
                "plan" => TunTopologyAction.Plan,
                "preflight" => TunTopologyAction.Preflight,
                "create" => TunTopologyAction.Create,
                "teardown" => TunTopologyAction.Teardown,
                _ => default
            };
            return action != default;
        }

        static bool TryReadValue(string[] args, ref int index, out string value)
        {
            value = string.Empty;
            if (++index >= args.Length)
                return false;

            value = args[index];
            return !string.IsNullOrWhiteSpace(value);
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

        static bool IsSafeDockerName(string value)
        {
            return value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
        }

        static bool IsHelp(string value)
        {
            return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
        }
    }
}

internal enum TunTopologyAction
{
    None,
    Plan,
    Preflight,
    Create,
    Teardown
}

internal interface ITunTopologyCommandRunner
{
    bool FileExists(string path);

    TunTopologyCommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);
}

internal sealed class SystemTunTopologyCommandRunner : ITunTopologyCommandRunner
{
    public static readonly SystemTunTopologyCommandRunner Instance = new();

    SystemTunTopologyCommandRunner()
    {
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public TunTopologyCommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new TunTopologyCommandResult(fileName, arguments, -1, string.Empty, $"Failed to start {fileName}.", false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)Math.Ceiling(timeout.TotalMilliseconds)))
            {
                TryKill(process);
                return new TunTopologyCommandResult(fileName, arguments, -1, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), true);
            }

            return new TunTopologyCommandResult(fileName, arguments, process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new TunTopologyCommandResult(fileName, arguments, -1, string.Empty, ex.Message, false);
        }
    }

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed record TunTopologyCommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);

internal sealed record TunTopologyCommandRecord(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);

internal sealed record TunTopologyCheck(
    string Name,
    string Status,
    string Message);

internal sealed record TunTopologyEnvironment(
    string Framework,
    string Os,
    string ProcessArchitecture,
    int ProcessorCount,
    bool IsLinux,
    string? ContainerEngineVersion);

internal sealed record TunTopologyReport(
    string Kind,
    string Action,
    string Status,
    DateTimeOffset CreatedAt,
    TunTopologyEnvironment Environment,
    TunBenchmarkTopologySpec Topology,
    IReadOnlyList<TunTopologyCheck> Checks,
    IReadOnlyList<TunTopologyCommandRecord> Commands,
    string Message);

internal sealed record TunBenchmarkTopologySpec(
    string Name,
    string Image,
    int Mtu,
    string DockerNetwork,
    string RequiredDevice,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<TunTopologyNode> Nodes,
    IReadOnlyList<TunTopologyImplementationSlot> Implementations,
    TunTopologyTrafficProfile Traffic);

internal sealed record TunTopologyNode(
    string Role,
    string ContainerName,
    string HostName,
    string InterfaceName,
    string Ipv4Address,
    string Ipv6Address,
    IReadOnlyList<string> PeerRoutes,
    int PNetUdpPort,
    int WireGuardUdpPort);

internal sealed record TunTopologyImplementationSlot(
    string Name,
    string ProcessPlacement,
    string CommandTemplate);

internal sealed record TunTopologyTrafficProfile(
    string ClientNode,
    string ServerNode,
    int Iperf3Port,
    IReadOnlyList<string> ServerAddresses,
    string Note);
