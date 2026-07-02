using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PNet.Mesh.Benchmarks;

internal static class MacroBenchmarkRunner
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!MacroBenchmarkOptions.TryParse(args, error, out var options))
            return 2;

        if (options.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        if (options.Scenario == MacroBenchmarkOptions.TestNodeSmokeScenario)
        {
            WriteManualSmoke(output);
            return 0;
        }

        var summaries = new List<MacroBenchmarkSummary>();
        foreach (var scenarioName in options.ResolveScenarioNames())
        {
            using var scenario = CreateScenario(scenarioName, options.PayloadSize, options.ReceiveTimeout);
            summaries.Add(RunScenario(scenario, options));
        }

        var report = new MacroBenchmarkReport(
            "pnet-mesh-macro-benchmark",
            DateTimeOffset.UtcNow,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            summaries);
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return 0;
    }

    static IMacroBenchmarkScenario CreateScenario(string scenarioName, int payloadSize, TimeSpan receiveTimeout)
    {
        return scenarioName switch
        {
            MacroBenchmarkOptions.InMemoryScenario => new InMemorySessionScenario(payloadSize),
            MacroBenchmarkOptions.UdpLoopbackScenario => new UdpLoopbackScenario(payloadSize, receiveTimeout),
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioName), scenarioName, "Unknown macro scenario.")
        };
    }

    static MacroBenchmarkSummary RunScenario(IMacroBenchmarkScenario scenario, MacroBenchmarkOptions options)
    {
        var warmupUntil = GetDeadline(options.Warmup);
        while (Stopwatch.GetTimestamp() < warmupUntil)
            scenario.Execute();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();
        var startGen0 = GC.CollectionCount(0);
        var startGen1 = GC.CollectionCount(1);
        var startGen2 = GC.CollectionCount(2);
        var start = Stopwatch.GetTimestamp();
        var deadline = GetDeadline(options.Duration);
        var latency = new LatencyRecorder();
        long iterations = 0;
        long packets = 0;
        long payloadBytes = 0;
        long wireBytes = 0;

        do
        {
            var operationStart = Stopwatch.GetTimestamp();
            var result = scenario.Execute();
            var operationStop = Stopwatch.GetTimestamp();
            latency.Add(operationStop - operationStart);
            iterations++;
            packets += result.Packets;
            payloadBytes += result.PayloadBytes;
            wireBytes += result.WireBytes;
        }
        while (Stopwatch.GetTimestamp() < deadline);

        var stop = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(start, stop);
        var elapsedSeconds = Math.Max(elapsed.TotalSeconds, double.Epsilon);
        var percentiles = latency.GetPercentiles();

        return new MacroBenchmarkSummary(
            scenario.Name,
            options.PayloadSize,
            options.Warmup.TotalSeconds,
            elapsed.TotalSeconds,
            iterations,
            packets,
            payloadBytes,
            wireBytes,
            packets / elapsedSeconds,
            payloadBytes / elapsedSeconds,
            wireBytes / elapsedSeconds,
            percentiles.SampleCount,
            percentiles.P50Microseconds,
            percentiles.P95Microseconds,
            percentiles.P99Microseconds,
            GC.GetAllocatedBytesForCurrentThread() - startAllocated,
            GC.CollectionCount(0) - startGen0,
            GC.CollectionCount(1) - startGen1,
            GC.CollectionCount(2) - startGen2);
    }

    static long GetDeadline(TimeSpan duration)
    {
        return Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  --macro all|in-memory|udp-loopback [--payload <bytes>] [--warmup <duration>] [--duration <duration>]");
        output.WriteLine("  --macro testnode-smoke");
        output.WriteLine();
        output.WriteLine("Defaults: --payload 128 --warmup 00:00:05 --duration 00:00:30.");
        output.WriteLine("Durations accept TimeSpan values, seconds with 's', or milliseconds with 'ms'.");
    }

    static void WriteManualSmoke(TextWriter output)
    {
        var report = new ManualSmokeReport(
            "pnet-mesh-testnode-perf-smoke",
            false,
            false,
            "Manual, non-deterministic container smoke is intentionally not run by the benchmark executable.",
            new[]
            {
                "dotnet build PNet.Mesh.sln -c Release --no-restore",
                "timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none"
            });
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
    }

    interface IMacroBenchmarkScenario : IDisposable
    {
        string Name { get; }

        MacroOperationResult Execute();
    }

    sealed class InMemorySessionScenario : IMacroBenchmarkScenario
    {
        readonly byte[] _payload;
        readonly SessionPair _pair;

        public InMemorySessionScenario(int payloadSize)
        {
            _payload = BenchmarkProtocolHarness.CreatePayload(payloadSize);
            _pair = BenchmarkProtocolHarness.CreateOpenSessionPair();
        }

        public string Name => MacroBenchmarkOptions.InMemoryScenario;

        public MacroOperationResult Execute()
        {
            var first = WriteReadPayload(_pair.Sender, _pair.SenderOutbound, _pair.Receiver, _pair.ReceiverInbound, _payload);
            var second = WriteReadPayload(_pair.Receiver, _pair.ReceiverOutbound, _pair.Sender, _pair.SenderInbound, _payload);
            return new MacroOperationResult(first.Packets + second.Packets, _payload.Length * 2, first.WireBytes + second.WireBytes);
        }

        public void Dispose()
        {
            _pair.Dispose();
        }

        static SessionWriteResult WriteReadPayload(
            PNetMeshSession sender,
            System.Threading.Channels.Channel<PNetMeshOutboundMessages.Message> senderOutbound,
            PNetMeshSession receiver,
            System.Threading.Channels.Channel<ReadOnlyMemory<byte>> receiverInbound,
            ReadOnlySpan<byte> payload)
        {
            sender.WritePayload(payload);
            var wirePackets = 0;
            var wireBytes = 0;

            for (var i = 0; i < 8; i++)
            {
                if (!senderOutbound.Reader.TryRead(out var message))
                {
                    sender.WritePacket();
                    message = senderOutbound.Reader.TryRead(out var flushed)
                        ? flushed
                        : throw new InvalidOperationException("Sender did not emit a packet.");
                }

                var packet = message as PNetMeshOutboundMessages.Packet
                    ?? throw new InvalidOperationException("Expected packet message.");
                wirePackets++;
                wireBytes += packet.MemoryBuffer.Length;

                try
                {
                    if (!receiver.TryReadMessage(packet.MemoryBuffer.Span))
                        throw new InvalidOperationException("Receiver rejected session packet.");
                }
                finally
                {
                    packet.MemoryOwner?.Dispose();
                }

                if (!receiverInbound.Reader.TryRead(out var received))
                    continue;

                if (!received.Span.SequenceEqual(payload))
                    throw new InvalidOperationException("Receiver emitted unexpected payload.");

                return new SessionWriteResult(wirePackets, wireBytes);
            }

            throw new InvalidOperationException("Receiver did not emit payload.");
        }

        readonly record struct SessionWriteResult(int Packets, int WireBytes);
    }

    sealed class UdpLoopbackScenario : IMacroBenchmarkScenario
    {
        readonly byte[] _payload;
        readonly byte[] _frame;
        readonly byte[] _sendBuffer = new byte[BenchmarkProtocolHarness.BufferSize];
        readonly byte[] _replyBuffer = new byte[BenchmarkProtocolHarness.BufferSize];
        readonly byte[] _plaintextBuffer = new byte[BenchmarkProtocolHarness.BufferSize];
        readonly EstablishedTransportPair _transports;
        readonly UdpClient _left;
        readonly UdpClient _right;
        readonly IPEndPoint _leftEndpoint;
        readonly IPEndPoint _rightEndpoint;

        public UdpLoopbackScenario(int payloadSize, TimeSpan receiveTimeout)
        {
            _payload = BenchmarkProtocolHarness.CreatePayload(payloadSize);
            _frame = PNetMeshPayloadFraming.CreatePNet(_payload);
            _transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
            _left = CreateClient(receiveTimeout);
            _right = CreateClient(receiveTimeout);
            _leftEndpoint = (IPEndPoint)_left.Client.LocalEndPoint!;
            _rightEndpoint = (IPEndPoint)_right.Client.LocalEndPoint!;
        }

        public string Name => MacroBenchmarkOptions.UdpLoopbackScenario;

        public MacroOperationResult Execute()
        {
            _transports.Initiator.WriteMessage(_frame, _sendBuffer, out var requestBytes, out _);
            _left.Send(_sendBuffer, requestBytes, _rightEndpoint);

            var remote = new IPEndPoint(IPAddress.Any, 0);
            var request = _right.Receive(ref remote);
            if (!_transports.Responder.TryReadMessage(request, _plaintextBuffer, out var plaintextBytes, out _))
                throw new InvalidOperationException("Responder rejected UDP loopback packet.");
            ValidateFrame(_plaintextBuffer.AsSpan(0, plaintextBytes));

            _transports.Responder.WriteMessage(_plaintextBuffer.AsSpan(0, plaintextBytes), _replyBuffer, out var replyBytes, out _);
            _right.Send(_replyBuffer, replyBytes, _leftEndpoint);

            remote = new IPEndPoint(IPAddress.Any, 0);
            var reply = _left.Receive(ref remote);
            if (!_transports.Initiator.TryReadMessage(reply, _plaintextBuffer, out plaintextBytes, out _))
                throw new InvalidOperationException("Initiator rejected UDP loopback reply.");
            ValidateFrame(_plaintextBuffer.AsSpan(0, plaintextBytes));

            return new MacroOperationResult(2, _payload.Length * 2, requestBytes + replyBytes);
        }

        public void Dispose()
        {
            _left.Dispose();
            _right.Dispose();
            _transports.Dispose();
        }

        static UdpClient CreateClient(TimeSpan receiveTimeout)
        {
            var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var timeoutMs = Math.Max(1, (int)Math.Ceiling(receiveTimeout.TotalMilliseconds));
            client.Client.ReceiveTimeout = timeoutMs;
            client.Client.SendTimeout = timeoutMs;
            return client;
        }

        void ValidateFrame(ReadOnlySpan<byte> frameBytes)
        {
            if (!PNetMeshPayloadFraming.TryRead(frameBytes, out var frame, out var error))
                throw new InvalidOperationException($"UDP loopback plaintext did not contain a valid frame: {error}.");

            if (frame.Kind != PNetMeshPayloadFrameKind.PNet || !frame.Payload.SequenceEqual(_payload))
                throw new InvalidOperationException("UDP loopback plaintext payload did not round trip.");
        }
    }

    sealed class LatencyRecorder
    {
        const int MaxSamples = 1_000_000;
        readonly List<long> _samples = new();
        long _seen;

        public void Add(long ticks)
        {
            _seen++;
            if (_samples.Count < MaxSamples)
            {
                _samples.Add(ticks);
                return;
            }

            var slot = (int)(_seen % MaxSamples);
            _samples[slot] = ticks;
        }

        public LatencyPercentiles GetPercentiles()
        {
            _samples.Sort();
            return new LatencyPercentiles(
                _samples.Count,
                ToMicroseconds(Percentile(0.50)),
                ToMicroseconds(Percentile(0.95)),
                ToMicroseconds(Percentile(0.99)));
        }

        long Percentile(double percentile)
        {
            if (_samples.Count == 0)
                return 0;

            var index = (int)Math.Ceiling(percentile * _samples.Count) - 1;
            index = Math.Clamp(index, 0, _samples.Count - 1);
            return _samples[index];
        }

        static double ToMicroseconds(long stopwatchTicks)
        {
            return stopwatchTicks * 1_000_000.0 / Stopwatch.Frequency;
        }
    }
}

internal sealed class MacroBenchmarkOptions
{
    public const string AllScenario = "all";
    public const string InMemoryScenario = "in-memory";
    public const string UdpLoopbackScenario = "udp-loopback";
    public const string TestNodeSmokeScenario = "testnode-smoke";

    public string Scenario { get; private init; } = AllScenario;

    public int PayloadSize { get; private init; } = 128;

    public TimeSpan Warmup { get; private init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Duration { get; private init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ReceiveTimeout { get; private init; } = TimeSpan.FromSeconds(2);

    public bool ShowHelp { get; private init; }

    public static bool TryParse(string[] args, TextWriter error, out MacroBenchmarkOptions options)
    {
        options = new MacroBenchmarkOptions();
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            options = new MacroBenchmarkOptions { ShowHelp = true };
            return true;
        }

        var scenario = args[0];
        if (!IsKnownScenario(scenario))
        {
            error.WriteLine($"Unknown macro scenario '{scenario}'.");
            return false;
        }

        var payloadSize = 128;
        var warmup = TimeSpan.FromSeconds(5);
        var duration = TimeSpan.FromSeconds(30);
        var receiveTimeout = TimeSpan.FromSeconds(2);

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--payload":
                    if (!TryReadInt(args, ref i, out payloadSize))
                    {
                        error.WriteLine("--payload requires a positive integer value.");
                        return false;
                    }
                    break;
                case "--warmup":
                    if (!TryReadDuration(args, ref i, out warmup))
                    {
                        error.WriteLine("--warmup requires a duration.");
                        return false;
                    }
                    break;
                case "--duration":
                    if (!TryReadDuration(args, ref i, out duration))
                    {
                        error.WriteLine("--duration requires a duration.");
                        return false;
                    }
                    break;
                case "--receive-timeout":
                    if (!TryReadDuration(args, ref i, out receiveTimeout))
                    {
                        error.WriteLine("--receive-timeout requires a duration.");
                        return false;
                    }
                    break;
                default:
                    error.WriteLine($"Unknown macro option '{args[i]}'.");
                    return false;
            }
        }

        if (payloadSize <= 0 || payloadSize > BenchmarkProtocolHarness.MaxPayloadSize)
        {
            error.WriteLine($"--payload must be between 1 and {BenchmarkProtocolHarness.MaxPayloadSize}.");
            return false;
        }

        if (warmup < TimeSpan.Zero || duration <= TimeSpan.Zero || receiveTimeout <= TimeSpan.Zero)
        {
            error.WriteLine("Durations must be positive; --warmup may be zero.");
            return false;
        }

        options = new MacroBenchmarkOptions
        {
            Scenario = scenario,
            PayloadSize = payloadSize,
            Warmup = warmup,
            Duration = duration,
            ReceiveTimeout = receiveTimeout
        };
        return true;
    }

    public IReadOnlyList<string> ResolveScenarioNames()
    {
        return Scenario == AllScenario
            ? new[] { InMemoryScenario, UdpLoopbackScenario }
            : new[] { Scenario };
    }

    static bool IsKnownScenario(string scenario)
    {
        return scenario is AllScenario or InMemoryScenario or UdpLoopbackScenario or TestNodeSmokeScenario;
    }

    static bool TryReadInt(string[] args, ref int index, out int value)
    {
        value = 0;
        if (++index >= args.Length)
            return false;
        return int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    static bool TryReadDuration(string[] args, ref int index, out TimeSpan value)
    {
        value = default;
        if (++index >= args.Length)
            return false;

        var text = args[index];
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
}

internal readonly record struct MacroOperationResult(int Packets, int PayloadBytes, int WireBytes);

internal readonly record struct LatencyPercentiles(
    int SampleCount,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds);

internal sealed record MacroBenchmarkReport(
    string Kind,
    DateTimeOffset CreatedAt,
    string Framework,
    string Os,
    string ProcessArchitecture,
    int ProcessorCount,
    IReadOnlyList<MacroBenchmarkSummary> Summaries);

internal sealed record MacroBenchmarkSummary(
    string Scenario,
    int PayloadSize,
    double WarmupSeconds,
    double MeasurementSeconds,
    long Iterations,
    long Packets,
    long PayloadBytes,
    long WireBytes,
    double PacketsPerSecond,
    double PayloadBytesPerSecond,
    double WireBytesPerSecond,
    int LatencySampleCount,
    double P50LatencyMicroseconds,
    double P95LatencyMicroseconds,
    double P99LatencyMicroseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal sealed record ManualSmokeReport(
    string Scenario,
    bool RunsByDefault,
    bool Deterministic,
    string Message,
    IReadOnlyList<string> Commands);
