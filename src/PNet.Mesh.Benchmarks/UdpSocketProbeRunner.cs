using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks.Sources;

namespace PNet.Mesh.Benchmarks;

internal static class UdpSocketProbeRunner
{
    const string Kind = "udp-socket-probe";
    const int DefaultIterations = 1000;
    const int DefaultWarmup = 100;
    const int DefaultPayloadBytes = 84;
    const int WireGuardSocketBufferSize = 7 << 20;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var options = UdpSocketProbeOptions.Parse(args);
            var report = RunAsync(options).GetAwaiter().GetResult();
            output.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex);
            WriteUsage(error);
            return 1;
        }
    }

    static async Task<UdpSocketProbeReport> RunAsync(UdpSocketProbeOptions options)
    {
        if (string.Equals(options.Role, "server", StringComparison.Ordinal))
            return await RunServerAsync(options).ConfigureAwait(false);
        if (string.Equals(options.Role, "client", StringComparison.Ordinal))
            return await RunClientAsync(options).ConfigureAwait(false);

        var runs = new List<UdpSocketProbeRun>();
        foreach (var mode in ExpandModes(options.Mode))
        {
            var socketBufferBytes = GetSocketBufferBytes(mode, options.SocketBufferBytes);
            var baseMode = GetBaseMode(mode);

            runs.Add(await RunModeAsync(
                baseMode,
                mode,
                options,
                socketBufferBytes).ConfigureAwait(false));
        }

        return CreateReport(options, runs);
    }

    static IReadOnlyList<string> ExpandModes(string mode)
    {
        if (!string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            return [mode];

        return
        [
            "saea-message",
            "saea-from",
            "task-message",
            "task-from",
            "blocking-message",
            "blocking-from",
            "saea-message-buffered",
            "blocking-message-buffered"
        ];
    }

    static async Task<UdpSocketProbeReport> RunServerAsync(UdpSocketProbeOptions options)
    {
        var modes = ExpandModes(options.Mode);
        if (modes.Count != 1)
            throw new ArgumentException("Server role requires exactly one --mode value.");

        var displayMode = modes[0];
        var socketBufferBytes = GetSocketBufferBytes(displayMode, options.SocketBufferBytes);
        var baseMode = GetBaseMode(displayMode);
        using var socket = CreateSocket(
            options.AddressFamily,
            socketBufferBytes,
            options.Timeout,
            CreateBindEndPoint(options, serverDefault: true));
        var totalPackets = checked(options.Warmup + options.Iterations);
        using var cts = new CancellationTokenSource(options.Timeout);
        await StartEchoServer(
            baseMode,
            socket,
            totalPackets,
            options.PayloadBytes,
            cts.Token).ConfigureAwait(false);

        return CreateReport(options, []);
    }

    static async Task<UdpSocketProbeReport> RunClientAsync(UdpSocketProbeOptions options)
    {
        var modes = ExpandModes(options.Mode);
        if (modes.Count != 1)
            throw new ArgumentException("Client role requires exactly one --mode value.");
        if (string.IsNullOrWhiteSpace(options.Target))
            throw new ArgumentException("Client role requires --target <ip:port>.");

        var displayMode = modes[0];
        var socketBufferBytes = GetSocketBufferBytes(displayMode, options.SocketBufferBytes);
        var baseMode = GetBaseMode(displayMode);
        using var socket = CreateSocket(
            options.AddressFamily,
            socketBufferBytes,
            options.Timeout,
            CreateBindEndPoint(options, serverDefault: false));
        using var cts = new CancellationTokenSource(options.Timeout);
        var serverEndpoint = ParseIpEndPoint(options.Target, options.AddressFamily, "--target");
        var samples = baseMode switch
        {
            "saea-message" => await RunSaeaClientAsync(socket, serverEndpoint, options, useMessageReceive: true, cts.Token).ConfigureAwait(false),
            "saea-from" => await RunSaeaClientAsync(socket, serverEndpoint, options, useMessageReceive: false, cts.Token).ConfigureAwait(false),
            "task-message" => await RunTaskClientAsync(socket, serverEndpoint, options, useMessageReceive: true, cts.Token).ConfigureAwait(false),
            "task-from" => await RunTaskClientAsync(socket, serverEndpoint, options, useMessageReceive: false, cts.Token).ConfigureAwait(false),
            "blocking-message" => RunBlockingClient(socket, serverEndpoint, options, useMessageReceive: true),
            "blocking-from" => RunBlockingClient(socket, serverEndpoint, options, useMessageReceive: false),
            _ => throw new ArgumentOutOfRangeException(nameof(baseMode), baseMode, "Unknown UDP socket probe mode.")
        };

        var run = UdpSocketProbeRun.Create(
            displayMode,
            baseMode,
            socketBufferBytes,
            socket.ReceiveBufferSize,
            socket.SendBufferSize,
            0,
            0,
            samples);
        return CreateReport(options, [run]);
    }

    static async Task<UdpSocketProbeRun> RunModeAsync(
        string baseMode,
        string displayMode,
        UdpSocketProbeOptions options,
        int? socketBufferBytes)
    {
        using var server = CreateSocket(options.AddressFamily, socketBufferBytes, options.Timeout);
        using var client = CreateSocket(options.AddressFamily, socketBufferBytes, options.Timeout);
        var serverEndpoint = (IPEndPoint)server.LocalEndPoint!;
        var totalPackets = checked(options.Warmup + options.Iterations);
        using var cts = new CancellationTokenSource(options.Timeout);
        var serverTask = StartEchoServer(baseMode, server, totalPackets, options.PayloadBytes, cts.Token);

        var samples = baseMode switch
        {
            "saea-message" => await RunSaeaClientAsync(client, serverEndpoint, options, useMessageReceive: true, cts.Token).ConfigureAwait(false),
            "saea-from" => await RunSaeaClientAsync(client, serverEndpoint, options, useMessageReceive: false, cts.Token).ConfigureAwait(false),
            "task-message" => await RunTaskClientAsync(client, serverEndpoint, options, useMessageReceive: true, cts.Token).ConfigureAwait(false),
            "task-from" => await RunTaskClientAsync(client, serverEndpoint, options, useMessageReceive: false, cts.Token).ConfigureAwait(false),
            "blocking-message" => RunBlockingClient(client, serverEndpoint, options, useMessageReceive: true),
            "blocking-from" => RunBlockingClient(client, serverEndpoint, options, useMessageReceive: false),
            _ => throw new ArgumentOutOfRangeException(nameof(baseMode), baseMode, "Unknown UDP socket probe mode.")
        };

        await serverTask.ConfigureAwait(false);

        return UdpSocketProbeRun.Create(
            displayMode,
            baseMode,
            socketBufferBytes,
            client.ReceiveBufferSize,
            client.SendBufferSize,
            server.ReceiveBufferSize,
            server.SendBufferSize,
            samples);
    }

    static string GetBaseMode(string mode)
    {
        return mode.EndsWith("-buffered", StringComparison.Ordinal)
            ? mode[..^"-buffered".Length]
            : mode;
    }

    static int? GetSocketBufferBytes(string mode, int? socketBufferBytes)
    {
        return mode.EndsWith("-buffered", StringComparison.Ordinal)
            ? WireGuardSocketBufferSize
            : socketBufferBytes;
    }

    static UdpSocketProbeReport CreateReport(
        UdpSocketProbeOptions options,
        IReadOnlyList<UdpSocketProbeRun> runs)
    {
        return new UdpSocketProbeReport(
            Kind,
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            Environment.OSVersion.ToString(),
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            options,
            runs);
    }

    static Socket CreateSocket(
        AddressFamily addressFamily,
        int? socketBufferBytes,
        TimeSpan timeout,
        EndPoint? bindEndPoint = null)
    {
        var socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (addressFamily == AddressFamily.InterNetworkV6)
            socket.DualMode = false;

        if (socketBufferBytes is { } bufferBytes)
        {
            socket.ReceiveBufferSize = bufferBytes;
            socket.SendBufferSize = bufferBytes;
        }

        socket.ReceiveTimeout = checked((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
        socket.SendTimeout = checked((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
        socket.Bind(bindEndPoint ?? CreateLoopbackEndPoint(addressFamily, 0));
        return socket;
    }

    static EndPoint CreateAnyEndPoint(AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);
    }

    static IPEndPoint CreateLoopbackEndPoint(AddressFamily addressFamily, int port)
    {
        return addressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Loopback, port)
            : new IPEndPoint(IPAddress.Loopback, port);
    }

    static IPEndPoint CreateBindEndPoint(UdpSocketProbeOptions options, bool serverDefault)
    {
        if (!string.IsNullOrWhiteSpace(options.Bind))
            return ParseIpEndPoint(options.Bind, options.AddressFamily, "--bind");

        if (serverDefault)
        {
            return options.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 19090)
                : new IPEndPoint(IPAddress.Any, 19090);
        }

        return options.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);
    }

    static IPEndPoint ParseIpEndPoint(string value, AddressFamily addressFamily, string option)
    {
        if (!IPEndPoint.TryParse(value, out var endpoint))
            throw new FormatException($"{option} must be an IP endpoint in ip:port form.");
        if (endpoint.AddressFamily != addressFamily)
            throw new ArgumentException($"{option} address family does not match --address-family.");
        return endpoint;
    }

    static Task StartEchoServer(
        string mode,
        Socket socket,
        int packets,
        int payloadBytes,
        CancellationToken cancellationToken)
    {
        if (mode.StartsWith("blocking-", StringComparison.Ordinal))
        {
            return Task.Factory.StartNew(
                () => RunBlockingServer(socket, packets, payloadBytes, mode.EndsWith("message", StringComparison.Ordinal)),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        return mode switch
        {
            "saea-message" => RunSaeaServerAsync(socket, packets, payloadBytes, useMessageReceive: true, cancellationToken),
            "saea-from" => RunSaeaServerAsync(socket, packets, payloadBytes, useMessageReceive: false, cancellationToken),
            "task-message" => RunTaskServerAsync(socket, packets, payloadBytes, useMessageReceive: true, cancellationToken),
            "task-from" => RunTaskServerAsync(socket, packets, payloadBytes, useMessageReceive: false, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown UDP socket probe mode.")
        };
    }

    static async Task RunSaeaServerAsync(
        Socket socket,
        int packets,
        int payloadBytes,
        bool useMessageReceive,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[payloadBytes];
        using var receive = new AwaitableSocketAsyncEventArgs(socket.AddressFamily, buffer);
        for (var i = 0; i < packets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = useMessageReceive
                ? await receive.ReceiveMessageFromAsync(socket).ConfigureAwait(false)
                : await receive.ReceiveFromAsync(socket).ConfigureAwait(false);
            ThrowIfSocketError(args);
            await socket.SendToAsync(
                buffer.AsMemory(0, args.BytesTransferred),
                SocketFlags.None,
                args.RemoteEndPoint!,
                cancellationToken).ConfigureAwait(false);
        }
    }

    static async Task<long[]> RunSaeaClientAsync(
        Socket socket,
        EndPoint serverEndpoint,
        UdpSocketProbeOptions options,
        bool useMessageReceive,
        CancellationToken cancellationToken)
    {
        var sendBuffer = CreatePayload(options.PayloadBytes);
        var receiveBuffer = new byte[options.PayloadBytes];
        var samples = new long[options.Iterations];
        using var receive = new AwaitableSocketAsyncEventArgs(socket.AddressFamily, receiveBuffer);

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            WriteSequence(sendBuffer, i);
            var start = Stopwatch.GetTimestamp();
            await socket.SendToAsync(
                sendBuffer,
                SocketFlags.None,
                serverEndpoint,
                cancellationToken).ConfigureAwait(false);
            var args = useMessageReceive
                ? await receive.ReceiveMessageFromAsync(socket).ConfigureAwait(false)
                : await receive.ReceiveFromAsync(socket).ConfigureAwait(false);
            ThrowIfSocketError(args);
            if (i >= options.Warmup)
                samples[i - options.Warmup] = Stopwatch.GetTimestamp() - start;
        }

        return samples;
    }

    static async Task RunTaskServerAsync(
        Socket socket,
        int packets,
        int payloadBytes,
        bool useMessageReceive,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[payloadBytes];
        for (var i = 0; i < packets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (useMessageReceive)
            {
                var result = await socket.ReceiveMessageFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    CreateAnyEndPoint(socket.AddressFamily),
                    cancellationToken).ConfigureAwait(false);
                await socket.SendToAsync(
                    buffer.AsMemory(0, result.ReceivedBytes),
                    SocketFlags.None,
                    result.RemoteEndPoint,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var result = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    CreateAnyEndPoint(socket.AddressFamily),
                    cancellationToken).ConfigureAwait(false);
                await socket.SendToAsync(
                    buffer.AsMemory(0, result.ReceivedBytes),
                    SocketFlags.None,
                    result.RemoteEndPoint,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    static async Task<long[]> RunTaskClientAsync(
        Socket socket,
        EndPoint serverEndpoint,
        UdpSocketProbeOptions options,
        bool useMessageReceive,
        CancellationToken cancellationToken)
    {
        var sendBuffer = CreatePayload(options.PayloadBytes);
        var receiveBuffer = new byte[options.PayloadBytes];
        var samples = new long[options.Iterations];

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            WriteSequence(sendBuffer, i);
            var start = Stopwatch.GetTimestamp();
            await socket.SendToAsync(
                sendBuffer,
                SocketFlags.None,
                serverEndpoint,
                cancellationToken).ConfigureAwait(false);
            if (useMessageReceive)
            {
                _ = await socket.ReceiveMessageFromAsync(
                    receiveBuffer.AsMemory(),
                    SocketFlags.None,
                    CreateAnyEndPoint(socket.AddressFamily),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = await socket.ReceiveFromAsync(
                    receiveBuffer.AsMemory(),
                    SocketFlags.None,
                    CreateAnyEndPoint(socket.AddressFamily),
                    cancellationToken).ConfigureAwait(false);
            }

            if (i >= options.Warmup)
                samples[i - options.Warmup] = Stopwatch.GetTimestamp() - start;
        }

        return samples;
    }

    static void RunBlockingServer(Socket socket, int packets, int payloadBytes, bool useMessageReceive)
    {
        var buffer = new byte[payloadBytes];
        for (var i = 0; i < packets; i++)
        {
            EndPoint remoteEndPoint = CreateAnyEndPoint(socket.AddressFamily);
            int received;
            if (useMessageReceive)
            {
                var flags = SocketFlags.None;
                received = socket.ReceiveMessageFrom(buffer, ref flags, ref remoteEndPoint, out _);
            }
            else
            {
                received = socket.ReceiveFrom(buffer, ref remoteEndPoint);
            }

            socket.SendTo(buffer, 0, received, SocketFlags.None, remoteEndPoint);
        }
    }

    static long[] RunBlockingClient(
        Socket socket,
        EndPoint serverEndpoint,
        UdpSocketProbeOptions options,
        bool useMessageReceive)
    {
        var sendBuffer = CreatePayload(options.PayloadBytes);
        var receiveBuffer = new byte[options.PayloadBytes];
        var samples = new long[options.Iterations];

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            WriteSequence(sendBuffer, i);
            var start = Stopwatch.GetTimestamp();
            socket.SendTo(sendBuffer, SocketFlags.None, serverEndpoint);
            EndPoint remoteEndPoint = CreateAnyEndPoint(socket.AddressFamily);
            if (useMessageReceive)
            {
                var flags = SocketFlags.None;
                _ = socket.ReceiveMessageFrom(receiveBuffer, ref flags, ref remoteEndPoint, out _);
            }
            else
            {
                _ = socket.ReceiveFrom(receiveBuffer, ref remoteEndPoint);
            }

            if (i >= options.Warmup)
                samples[i - options.Warmup] = Stopwatch.GetTimestamp() - start;
        }

        return samples;
    }

    static byte[] CreatePayload(int payloadBytes)
    {
        var payload = new byte[payloadBytes];
        for (var i = 8; i < payload.Length; i++)
            payload[i] = (byte)i;
        return payload;
    }

    static void WriteSequence(byte[] payload, int sequence)
    {
        if (payload.Length < 8)
            return;
        BitConverter.TryWriteBytes(payload.AsSpan(0, 8), sequence);
    }

    static void ThrowIfSocketError(SocketAsyncEventArgs args)
    {
        if (args.SocketError != SocketError.Success)
            throw new SocketException((int)args.SocketError);
    }

    static double TicksToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }

    static void WriteUsage(TextWriter output)
    {
        output.WriteLine("  --udp-socket-probe [--role loopback|server|client] [--mode all|saea-message|saea-from|task-message|task-from|blocking-message|blocking-from] [--iterations <count>] [--warmup <count>] [--payload <bytes>] [--socket-buffer <bytes>] [--address-family ipv4|ipv6] [--bind <ip:port>] [--target <ip:port>] [--timeout <duration>]");
    }

    sealed class AwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<SocketAsyncEventArgs>
    {
        readonly AddressFamily _addressFamily;
        ManualResetValueTaskSourceCore<SocketAsyncEventArgs> _core;

        public AwaitableSocketAsyncEventArgs(AddressFamily addressFamily, byte[] buffer)
        {
            _addressFamily = addressFamily;
            _core.RunContinuationsAsynchronously = true;
            SetBuffer(buffer);
            Completed += static (_, args) => ((AwaitableSocketAsyncEventArgs)args)._core.SetResult(args);
        }

        public ValueTask<SocketAsyncEventArgs> ReceiveMessageFromAsync(Socket socket)
        {
            RemoteEndPoint = CreateAnyEndPoint(_addressFamily);
            _core.Reset();
            if (!socket.ReceiveMessageFromAsync(this))
                _core.SetResult(this);
            return new ValueTask<SocketAsyncEventArgs>(this, _core.Version);
        }

        public ValueTask<SocketAsyncEventArgs> ReceiveFromAsync(Socket socket)
        {
            RemoteEndPoint = CreateAnyEndPoint(_addressFamily);
            _core.Reset();
            if (!socket.ReceiveFromAsync(this))
                _core.SetResult(this);
            return new ValueTask<SocketAsyncEventArgs>(this, _core.Version);
        }

        public SocketAsyncEventArgs GetResult(short token)
        {
            return _core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _core.GetStatus(token);
        }

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            _core.OnCompleted(continuation, state, token, flags);
        }
    }

    internal sealed record UdpSocketProbeOptions(
        int Iterations,
        int Warmup,
        int PayloadBytes,
        string Mode,
        string Role,
        string? Bind,
        string? Target,
        int? SocketBufferBytes,
        AddressFamily AddressFamily,
        TimeSpan Timeout)
    {
        public static UdpSocketProbeOptions Parse(string[] args)
        {
            var iterations = DefaultIterations;
            var warmup = DefaultWarmup;
            var payloadBytes = DefaultPayloadBytes;
            var mode = "all";
            var role = "loopback";
            string? bind = null;
            string? target = null;
            int? socketBufferBytes = null;
            var addressFamily = AddressFamily.InterNetwork;
            var timeout = TimeSpan.FromSeconds(10);

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--iterations":
                        iterations = int.Parse(ReadValue(args, ref i, "--iterations"));
                        break;
                    case "--warmup":
                        warmup = int.Parse(ReadValue(args, ref i, "--warmup"));
                        break;
                    case "--payload":
                        payloadBytes = int.Parse(ReadValue(args, ref i, "--payload"));
                        break;
                    case "--mode":
                        mode = ReadValue(args, ref i, "--mode");
                        break;
                    case "--role":
                        role = ParseRole(ReadValue(args, ref i, "--role"));
                        break;
                    case "--bind":
                        bind = ReadValue(args, ref i, "--bind");
                        break;
                    case "--target":
                        target = ReadValue(args, ref i, "--target");
                        break;
                    case "--socket-buffer":
                        socketBufferBytes = int.Parse(ReadValue(args, ref i, "--socket-buffer"));
                        break;
                    case "--address-family":
                        addressFamily = ParseAddressFamily(ReadValue(args, ref i, "--address-family"));
                        break;
                    case "--timeout":
                        timeout = ParseDuration(ReadValue(args, ref i, "--timeout"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown UDP socket probe option '{args[i]}'.");
                }
            }

            if (iterations <= 0)
                throw new ArgumentOutOfRangeException(nameof(iterations));
            if (warmup < 0)
                throw new ArgumentOutOfRangeException(nameof(warmup));
            if (payloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(payloadBytes));

            return new UdpSocketProbeOptions(
                iterations,
                warmup,
                payloadBytes,
                mode,
                role,
                bind,
                target,
                socketBufferBytes,
                addressFamily,
                timeout);
        }

        static string ParseRole(string value)
        {
            return value switch
            {
                "loopback" or "server" or "client" => value,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Role must be loopback, server, or client.")
            };
        }

        static AddressFamily ParseAddressFamily(string value)
        {
            return value switch
            {
                "ipv4" => AddressFamily.InterNetwork,
                "ipv6" => AddressFamily.InterNetworkV6,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Address family must be ipv4 or ipv6.")
            };
        }

        static TimeSpan ParseDuration(string value)
        {
            if (TimeSpan.TryParse(value, out var parsed))
                return parsed;
            if (value.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(value[..^2], out var milliseconds))
            {
                return TimeSpan.FromMilliseconds(milliseconds);
            }
            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(value[..^1], out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            throw new FormatException($"Invalid duration '{value}'.");
        }

        static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length)
                throw new ArgumentException($"{option} requires a value.");
            return args[index];
        }
    }

    internal sealed record UdpSocketProbeReport(
        string Kind,
        DateTimeOffset CreatedAt,
        string Framework,
        string OS,
        int ProcessorCount,
        long StopwatchFrequency,
        UdpSocketProbeOptions Options,
        IReadOnlyList<UdpSocketProbeRun> Runs);

    internal sealed record UdpSocketProbeRun(
        string Mode,
        string BaseMode,
        int? RequestedSocketBufferBytes,
        int ClientReceiveBufferBytes,
        int ClientSendBufferBytes,
        int ServerReceiveBufferBytes,
        int ServerSendBufferBytes,
        int Count,
        double MinMicroseconds,
        double AverageMicroseconds,
        double P50Microseconds,
        double P90Microseconds,
        double P95Microseconds,
        double P99Microseconds,
        double MaxMicroseconds,
        double StandardDeviationMicroseconds)
    {
        public static UdpSocketProbeRun Create(
            string mode,
            string baseMode,
            int? requestedSocketBufferBytes,
            int clientReceiveBufferBytes,
            int clientSendBufferBytes,
            int serverReceiveBufferBytes,
            int serverSendBufferBytes,
            long[] samples)
        {
            var sorted = samples.Order().ToArray();
            var values = sorted.Select(TicksToMicroseconds).ToArray();
            var average = values.Average();
            var sumSquares = 0.0;
            foreach (var value in values)
                sumSquares += Math.Pow(value - average, 2);

            return new UdpSocketProbeRun(
                mode,
                baseMode,
                requestedSocketBufferBytes,
                clientReceiveBufferBytes,
                clientSendBufferBytes,
                serverReceiveBufferBytes,
                serverSendBufferBytes,
                values.Length,
                values[0],
                average,
                Percentile(values, 0.50),
                Percentile(values, 0.90),
                Percentile(values, 0.95),
                Percentile(values, 0.99),
                values[^1],
                Math.Sqrt(sumSquares / values.Length));
        }

        static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 1)
                return sortedValues[0];
            var index = percentile * (sortedValues.Length - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            if (lower == upper)
                return sortedValues[lower];
            var weight = index - lower;
            return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
        }
    }
}
