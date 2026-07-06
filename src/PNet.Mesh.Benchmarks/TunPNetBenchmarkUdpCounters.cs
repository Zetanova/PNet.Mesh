using System.Globalization;

namespace PNet.Mesh.Benchmarks;

internal static partial class TunPNetBenchmarkRunner
{
    const int LeftPNetUdpPort = 12401;
    const int RightPNetUdpPort = 12402;
    const int LeftWireGuardUdpPort = 51820;
    const int RightWireGuardUdpPort = 51821;

    static IReadOnlyList<UdpCounterSampleTarget> CreateUdpCounterSampleTargets(
        string protocol,
        TunTopologyNode source,
        TunTopologyNode target)
    {
        return new[]
        {
            new UdpCounterSampleTarget(protocol, source),
            new UdpCounterSampleTarget(protocol, target)
        };
    }

    static IReadOnlyList<UdpCounterSampleResult> CaptureUdpCounterSamples(
        ITunTopologyCommandRunner commandRunner,
        TunPNetBenchmarkOptions options,
        IReadOnlyList<UdpCounterSampleTarget> targets,
        int sampleIndex,
        List<TunTopologyCommandRecord> commands)
    {
        var results = new List<UdpCounterSampleResult>(targets.Count);
        foreach (var target in targets)
        {
            var result = commandRunner.Run("docker", new[]
            {
                "exec",
                target.Node.ContainerName,
                "sh",
                "-c",
                CreateUdpCounterSampleScript(target.Protocol, target.Node.Role, sampleIndex)
            }, options.CommandTimeout);
            commands.Add(new TunTopologyCommandRecord(
                result.FileName,
                result.Arguments,
                result.ExitCode,
                TrimOutput(result.Stdout),
                TrimOutput(result.Stderr),
                result.TimedOut));
            results.Add(new UdpCounterSampleResult(target.Node.Role, result.Stdout));
        }

        return results;
    }

    static TunBenchmarkUdpCounterDelta CreateUdpCounterDelta(
        string protocol,
        TunTopologyNode source,
        TunTopologyNode target,
        string targetAddress,
        int iperfPort,
        IReadOnlyList<UdpCounterSampleResult> before,
        IReadOnlyList<UdpCounterSampleResult> after)
    {
        return new TunBenchmarkUdpCounterDelta(
            protocol,
            source.Role,
            target.Role,
            targetAddress,
            before
                .Concat(after)
                .GroupBy(result => result.Node, StringComparer.Ordinal)
                .Select(group => ParseUdpCounterMonitorResult(group.Key, string.Join('\n', group.Select(result => result.Output)), iperfPort))
                .ToArray());
    }

    internal static TunBenchmarkUdpNodeCounterDelta ParseUdpCounterMonitorResult(
        string node,
        string output,
        int iperfPort)
    {
        var samples = ParseUdpCounterSamples(output);
        if (samples.Count == 0)
        {
            var empty = new TunBenchmarkUdpGlobalCounters(null, null, null, null);
            return new TunBenchmarkUdpNodeCounterDelta(node, empty, empty, empty, Array.Empty<TunBenchmarkUdpSocketCounterDelta>());
        }

        var before = samples[0].Global;
        var after = samples[^1].Global;
        var sampleCount = samples.Count;
        var socketDeltas = samples
            .SelectMany((sample, index) => sample.Sockets.Select(socket => new SocketObservation(index, socket)))
            .GroupBy(observation => observation.Socket.Key, StringComparer.Ordinal)
            .Select(group => CreateSocketDelta(group, sampleCount, iperfPort))
            .Where(delta => delta.Role != "other" || delta.DropsDelta != 0)
            .OrderBy(delta => delta.Role, StringComparer.Ordinal)
            .ThenBy(delta => delta.Family, StringComparer.Ordinal)
            .ThenBy(delta => delta.LocalPort)
            .ThenBy(delta => delta.RemotePort)
            .ToArray();

        return new TunBenchmarkUdpNodeCounterDelta(
            node,
            before,
            after,
            Subtract(after, before),
            socketDeltas);
    }

    static string CreateUdpCounterSampleScript(string protocol, string role, int sampleIndex)
    {
        var label = $"pnet-udp-counters-{protocol}-{role}";
        return string.Join(
            ' ',
            ":",
            ShellQuote(label) + ";",
            "printf 'sample index=%s unix_ms=%s\\n'",
            sampleIndex.ToString(CultureInfo.InvariantCulture),
            "\"$(date +%s%3N 2>/dev/null || date +%s000)\";",
            "awk '/^Udp:/ { if (!seen) { for (i = 1; i <= NF; i++) h[i] = $i; seen = 1; next } for (i = 2; i <= NF; i++) printf \"snmp protocol=udp name=%s value=%s\\n\", h[i], $i }' /proc/net/snmp 2>/dev/null;",
            "if [ -r /proc/net/snmp6 ]; then awk '($1 == \"Udp6InErrors\" || $1 == \"Udp6RcvbufErrors\") { name = $1; sub(/^Udp6/, \"\", name); printf \"snmp protocol=udp6 name=%s value=%s\\n\", name, $2 }' /proc/net/snmp6; fi;",
            "awk 'NR > 1 { printf \"socket family=udp local=%s remote=%s state=%s queues=%s inode=%s drops=%s\\n\", $2, $3, $4, $5, $10, $NF }' /proc/net/udp 2>/dev/null;",
            "awk 'NR > 1 { printf \"socket family=udp6 local=%s remote=%s state=%s queues=%s inode=%s drops=%s\\n\", $2, $3, $4, $5, $10, $NF }' /proc/net/udp6 2>/dev/null;",
            "printf 'end_sample\\n'");
    }

    static List<UdpCounterSample> ParseUdpCounterSamples(string output)
    {
        var samples = new List<UdpCounterSample>();
        UdpCounterSampleBuilder? current = null;

        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("sample ", StringComparison.Ordinal))
            {
                if (current != null)
                    samples.Add(current.Build());

                current = new UdpCounterSampleBuilder();
                continue;
            }

            if (current == null)
                continue;

            if (rawLine.StartsWith("snmp ", StringComparison.Ordinal))
            {
                var values = ParseSpaceSeparatedKeyValues(rawLine);
                if (!values.TryGetValue("protocol", out var protocol)
                    || !values.TryGetValue("name", out var name)
                    || !values.TryGetValue("value", out var value)
                    || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var counter))
                {
                    continue;
                }

                current.SetGlobalCounter(protocol, name, counter);
            }
            else if (rawLine.StartsWith("socket ", StringComparison.Ordinal))
            {
                var values = ParseSpaceSeparatedKeyValues(rawLine);
                if (!values.TryGetValue("family", out var family)
                    || !values.TryGetValue("local", out var localAddress)
                    || !values.TryGetValue("remote", out var remoteAddress)
                    || !values.TryGetValue("drops", out var dropsValue)
                    || !long.TryParse(dropsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var drops))
                {
                    continue;
                }

                values.TryGetValue("inode", out var inode);
                current.Sockets.Add(new UdpCounterSocket(
                    family,
                    localAddress,
                    TryReadHexPort(localAddress),
                    remoteAddress,
                    TryReadHexPort(remoteAddress),
                    string.IsNullOrWhiteSpace(inode) ? null : inode,
                    drops));
            }
        }

        if (current != null)
            samples.Add(current.Build());

        return samples;
    }

    static TunBenchmarkUdpSocketCounterDelta CreateSocketDelta(
        IEnumerable<SocketObservation> observations,
        int sampleCount,
        int iperfPort)
    {
        var ordered = observations.OrderBy(observation => observation.SampleIndex).ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var socket = first.Socket;
        var maxDrops = ordered.Max(observation => observation.Socket.Drops);
        var firstDrops = first.Socket.Drops;
        return new TunBenchmarkUdpSocketCounterDelta(
            socket.Family,
            ClassifySocket(socket, iperfPort),
            socket.LocalAddressHex,
            socket.LocalPort,
            socket.RemoteAddressHex,
            socket.RemotePort,
            socket.Inode,
            firstDrops,
            last.Socket.Drops,
            maxDrops,
            Math.Max(0, maxDrops - firstDrops),
            ordered.Length,
            first.SampleIndex == 0,
            last.SampleIndex == sampleCount - 1);
    }

    static string ClassifySocket(UdpCounterSocket socket, int iperfPort)
    {
        if (socket.LocalPort == iperfPort || socket.RemotePort == iperfPort)
            return "iperf3";

        if (socket.LocalPort is LeftPNetUdpPort or RightPNetUdpPort
            || socket.RemotePort is LeftPNetUdpPort or RightPNetUdpPort)
        {
            return "pnet";
        }

        if (socket.LocalPort is LeftWireGuardUdpPort or RightWireGuardUdpPort
            || socket.RemotePort is LeftWireGuardUdpPort or RightWireGuardUdpPort)
        {
            return "wireguard";
        }

        return "other";
    }

    static TunBenchmarkUdpGlobalCounters Subtract(
        TunBenchmarkUdpGlobalCounters after,
        TunBenchmarkUdpGlobalCounters before)
    {
        return new TunBenchmarkUdpGlobalCounters(
            Subtract(after.UdpInErrors, before.UdpInErrors),
            Subtract(after.UdpRcvbufErrors, before.UdpRcvbufErrors),
            Subtract(after.Udp6InErrors, before.Udp6InErrors),
            Subtract(after.Udp6RcvbufErrors, before.Udp6RcvbufErrors));
    }

    static long? Subtract(long? after, long? before)
    {
        return after.HasValue && before.HasValue ? after - before : null;
    }

    static Dictionary<string, string> ParseSpaceSeparatedKeyValues(string line)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
                continue;

            values[token[..separator]] = token[(separator + 1)..];
        }

        return values;
    }

    static int? TryReadHexPort(string endpoint)
    {
        var separator = endpoint.LastIndexOf(':');
        if (separator < 0 || separator == endpoint.Length - 1)
            return null;

        return int.TryParse(endpoint[(separator + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)
            ? port
            : null;
    }

    sealed record UdpCounterSampleTarget(
        string Protocol,
        TunTopologyNode Node);

    sealed record UdpCounterSampleResult(
        string Node,
        string Output);

    sealed record UdpCounterSample(
        TunBenchmarkUdpGlobalCounters Global,
        IReadOnlyList<UdpCounterSocket> Sockets);

    sealed record UdpCounterSocket(
        string Family,
        string LocalAddressHex,
        int? LocalPort,
        string RemoteAddressHex,
        int? RemotePort,
        string? Inode,
        long Drops)
    {
        public string Key => string.Join('|', Family, LocalAddressHex, RemoteAddressHex, Inode);
    }

    readonly record struct SocketObservation(
        int SampleIndex,
        UdpCounterSocket Socket);

    sealed class UdpCounterSampleBuilder
    {
        long? _udpInErrors;
        long? _udpRcvbufErrors;
        long? _udp6InErrors;
        long? _udp6RcvbufErrors;

        public List<UdpCounterSocket> Sockets { get; } = new();

        public void SetGlobalCounter(string protocol, string name, long value)
        {
            if (protocol == "udp" && name == "InErrors")
                _udpInErrors = value;
            else if (protocol == "udp" && name == "RcvbufErrors")
                _udpRcvbufErrors = value;
            else if (protocol == "udp6" && name == "InErrors")
                _udp6InErrors = value;
            else if (protocol == "udp6" && name == "RcvbufErrors")
                _udp6RcvbufErrors = value;
        }

        public UdpCounterSample Build()
        {
            return new UdpCounterSample(
                new TunBenchmarkUdpGlobalCounters(_udpInErrors, _udpRcvbufErrors, _udp6InErrors, _udp6RcvbufErrors),
                Sockets.ToArray());
        }
    }
}
