using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Noise;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Threading.Channels;

namespace PNet.Mesh.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class WireGuardTransportBenchmarks
{
    const int MaxPayloadSize = 1420;
    const int BufferSize = 2048;

    readonly byte[] _payload = new byte[MaxPayloadSize];
    readonly byte[] _encryptedPacket = new byte[BufferSize];
    readonly byte[] _plaintext = new byte[BufferSize];

    byte[] _pnetFrame = Array.Empty<byte>();
    byte[] _ipv4Packet = Array.Empty<byte>();
    byte[] _ipv6Packet = Array.Empty<byte>();

    EstablishedTransportPair? _transports;

    [Params(64, 128, 512, 1280, 1420)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        for (var i = 0; i < _payload.Length; i++)
            _payload[i] = (byte)(i % 251);

        _transports = CreateEstablishedTransports();
        _pnetFrame = PNetMeshPayloadFraming.CreatePNet(_payload.AsSpan(0, PayloadSize));
        _ipv4Packet = PNetMeshIpPacket.CreateIPv4(
            IPAddress.Parse("10.10.0.1"),
            IPAddress.Parse("10.10.0.2"),
            _payload.AsSpan(0, PayloadSize));
        _ipv6Packet = PNetMeshIpPacket.CreateIPv6(
            IPAddress.Parse("fd00::1"),
            IPAddress.Parse("fd00::2"),
            _payload.AsSpan(0, PayloadSize));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _transports?.Dispose();
        _transports = null;
    }

    [Benchmark(Description = "WireGuard transport write/read")]
    [BenchmarkCategory("transport")]
    public int WriteThenReadTransportPacket()
    {
        var transports = _transports ?? throw new InvalidOperationException("Benchmark setup did not create transports.");

        transports.Initiator.WriteMessage(
            _payload.AsSpan(0, PayloadSize),
            _encryptedPacket,
            out var packetBytes,
            out _);

        if (!transports.Responder.TryReadMessage(
                _encryptedPacket.AsSpan(0, packetBytes),
                _plaintext,
                out var plaintextBytes,
                out _))
        {
            throw new InvalidOperationException("Transport packet did not decrypt.");
        }

        return plaintextBytes;
    }

    [Benchmark(Description = "PNet frame create")]
    [BenchmarkCategory("framing")]
    public byte[] CreatePNetFrame()
    {
        return PNetMeshPayloadFraming.CreatePNet(_payload.AsSpan(0, PayloadSize));
    }

    [Benchmark(Description = "PNet frame read")]
    [BenchmarkCategory("framing")]
    public int TryReadPNetFrame()
    {
        if (!PNetMeshPayloadFraming.TryRead(_pnetFrame, out var frame, out var error))
            throw new InvalidOperationException($"PNet frame parsing failed: {error}");

        return frame.PayloadLength;
    }

    [Benchmark(Description = "IPv4/IPv6 frame read")]
    [BenchmarkCategory("framing")]
    public int TryReadIpFrames()
    {
        if (!PNetMeshPayloadFraming.TryRead(_ipv4Packet, out var ipv4Frame, out var ipv4Error))
            throw new InvalidOperationException($"IPv4 frame parsing failed: {ipv4Error}");
        if (!PNetMeshPayloadFraming.TryRead(_ipv6Packet, out var ipv6Frame, out var ipv6Error))
            throw new InvalidOperationException($"IPv6 frame parsing failed: {ipv6Error}");

        return ipv4Frame.PayloadLength + ipv6Frame.PayloadLength;
    }

    [Benchmark(Description = "WireGuard full handshake setup")]
    [BenchmarkCategory("handshake")]
    public int FullHandshakeSetup()
    {
        using var transports = CreateEstablishedTransports();
        return (int)(transports.Initiator.SenderIndex + transports.Responder.SenderIndex);
    }

    [Benchmark(Description = "WireGuard rejection paths")]
    [BenchmarkCategory("rejection")]
    public int RejectInvalidTransportPackets()
    {
        var rejected = 0;
        rejected += RejectTamperedPacket() ? 1 : 0;
        rejected += RejectReplayPacket() ? 1 : 0;
        rejected += RejectUnknownReceiverPacket() ? 1 : 0;
        return rejected;
    }

    [Benchmark(Description = "Session write/read payload")]
    [BenchmarkCategory("session")]
    public int SessionWriteReadPayloadPacket()
    {
        using var pair = CreateOpenSessionPair();
        pair.Sender.WritePayload(_payload.AsSpan(0, PayloadSize));
        pair.Sender.WritePacket();

        var packet = ReadPacket(pair.SenderOutbound);
        try
        {
            if (!pair.Receiver.TryReadMessage(packet.MemoryBuffer.Span))
                throw new InvalidOperationException("Receiver rejected session packet.");

            if (!pair.ReceiverInbound.Reader.TryRead(out var received))
                throw new InvalidOperationException("Receiver did not emit payload.");

            return received.Length;
        }
        finally
        {
            packet.MemoryOwner?.Dispose();
        }
    }

    bool RejectTamperedPacket()
    {
        using var transports = CreateEstablishedTransports();
        var packet = WriteTransportPacket(transports.Initiator);
        packet[^1] ^= 0x80;
        return !transports.Responder.TryReadMessage(packet, _plaintext, out _, out _);
    }

    bool RejectReplayPacket()
    {
        using var transports = CreateEstablishedTransports();
        var packet = WriteTransportPacket(transports.Initiator);
        if (!transports.Responder.TryReadMessage(packet, _plaintext, out _, out _))
            throw new InvalidOperationException("Responder rejected first packet before replay.");

        return !transports.Responder.TryReadMessage(packet, _plaintext, out _, out _);
    }

    bool RejectUnknownReceiverPacket()
    {
        using var transports = CreateEstablishedTransports();
        var packet = WriteTransportPacket(transports.Initiator);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), uint.MaxValue);
        return !transports.Responder.TryReadMessage(packet, _plaintext, out _, out _);
    }

    byte[] WriteTransportPacket(PNetMeshTransport2 transport)
    {
        transport.WriteMessage(
            _payload.AsSpan(0, PayloadSize),
            _encryptedPacket,
            out var packetBytes,
            out _);

        return _encryptedPacket.AsSpan(0, packetBytes).ToArray();
    }

    static EstablishedTransportPair CreateEstablishedTransports()
    {
        var psk = new byte[32];
        for (var i = 0; i < psk.Length; i++)
            psk[i] = (byte)(0x80 + i);

        var initiatorStatic = KeyPair.Generate();
        var responderStatic = KeyPair.Generate();

        var initiatorProtocol = new PNetMeshProtocol(
            initiatorStatic.PrivateKey,
            initiatorStatic.PublicKey,
            psk);
        var responderProtocol = new PNetMeshProtocol(
            responderStatic.PrivateKey,
            responderStatic.PublicKey,
            psk);

        var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
        var responder = responderProtocol.CreateResponder(2);

        Span<byte> initiationBuffer = stackalloc byte[256];
        Span<byte> responseBuffer = stackalloc byte[128];

        initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
        if (!responder.TryReadInitiationMessage(initiationBuffer[..bytesWritten]))
            throw new InvalidOperationException("Responder rejected handshake initiation.");

        if (!responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport))
            throw new InvalidOperationException("Responder did not write handshake response.");

        if (!initiator.TryReadResponseMessage(responseBuffer[..bytesWritten], out var initiatorTransport))
            throw new InvalidOperationException("Initiator rejected handshake response.");

        return new EstablishedTransportPair(
            initiatorTransport,
            responderTransport,
            initiator,
            responder,
            initiatorStatic,
            responderStatic);
    }

    static SessionPair CreateOpenSessionPair()
    {
        var psk = new byte[32];
        for (var i = 0; i < psk.Length; i++)
            psk[i] = (byte)(0x40 + i);

        var senderStatic = KeyPair.Generate();
        var receiverStatic = KeyPair.Generate();
        var senderProtocol = new PNetMeshProtocol(senderStatic.PrivateKey, senderStatic.PublicKey, psk);
        var receiverProtocol = new PNetMeshProtocol(receiverStatic.PrivateKey, receiverStatic.PublicKey, psk);
        var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
        var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
        var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var receiverInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var senderControl = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
        var receiverControl = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
        var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 30001),
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 30002)
        };
        var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 30002),
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 30001)
        };
        sender.AttachTo(senderInbound.Writer, senderControl.Writer);
        receiver.AttachTo(receiverInbound.Writer, receiverControl.Writer);

        sender.WriteInitialize(1, receiverStatic.PublicKey);
        var initiation = ReadPacket(senderOutbound);
        try
        {
            if (!receiver.TryReadInitialize(2, initiation.MemoryBuffer.Span))
                throw new InvalidOperationException("Receiver rejected session initiation.");
        }
        finally
        {
            initiation.MemoryOwner?.Dispose();
        }

        receiver.WriteResponse();
        var response = ReadPacket(receiverOutbound);
        try
        {
            if (!sender.TryReadResponse(response.MemoryBuffer.Span))
                throw new InvalidOperationException("Sender rejected session response.");
        }
        finally
        {
            response.MemoryOwner?.Dispose();
        }

        return new SessionPair(
            sender,
            receiver,
            senderOutbound,
            receiverOutbound,
            senderInbound,
            receiverInbound,
            senderStatic,
            receiverStatic);
    }

    static PNetMeshOutboundMessages.Packet ReadPacket(Channel<PNetMeshOutboundMessages.Message> channel)
    {
        if (!channel.Reader.TryRead(out var message))
            throw new InvalidOperationException("Expected outbound packet.");

        return message as PNetMeshOutboundMessages.Packet
            ?? throw new InvalidOperationException("Expected packet message.");
    }

    sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            ArtifactsPath = "artifacts/benchmarks";
            AddColumn(StatisticColumn.OperationsPerSecond);
            AddColumn(new GcGenerationColumn("Gen1", stats => stats.Gen1Collections));
            AddColumn(new GcGenerationColumn("Gen2", stats => stats.Gen2Collections));
        }
    }

    sealed class GcGenerationColumn : IColumn
    {
        readonly Func<GcStats, double> _getValue;

        public GcGenerationColumn(string columnName, Func<GcStats, double> getValue)
        {
            ColumnName = columnName;
            _getValue = getValue;
        }

        public string Id => ColumnName;

        public string ColumnName { get; }

        public bool IsNumeric => true;

        public UnitType UnitType => UnitType.Dimensionless;

        public bool AlwaysShow => true;

        public ColumnCategory Category => ColumnCategory.Statistics;

        public string Legend => $"{ColumnName} : GC Generation {ColumnName[^1]} collects per 1000 operations";

        public int PriorityInCategory => 0;

        public bool IsAvailable(Summary summary)
        {
            return true;
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase)
        {
            return false;
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            return GetValue(summary, benchmarkCase, summary.Style);
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var report = summary[benchmarkCase];
            if (report is null)
                return "?";

            var value = _getValue(report.GcStats);
            return value == 0
                ? "-"
                : value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }

    sealed class EstablishedTransportPair : IDisposable
    {
        readonly IDisposable[] _disposables;

        public EstablishedTransportPair(
            PNetMeshTransport2 initiator,
            PNetMeshTransport2 responder,
            params IDisposable[] disposables)
        {
            Initiator = initiator;
            Responder = responder;
            _disposables = new IDisposable[] { initiator, responder }.Concat(disposables).ToArray();
        }

        public PNetMeshTransport2 Initiator { get; }

        public PNetMeshTransport2 Responder { get; }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
        }
    }

    sealed class SessionPair : IDisposable
    {
        readonly IDisposable[] _disposables;

        public SessionPair(
            PNetMeshSession sender,
            PNetMeshSession receiver,
            Channel<PNetMeshOutboundMessages.Message> senderOutbound,
            Channel<PNetMeshOutboundMessages.Message> receiverOutbound,
            Channel<ReadOnlyMemory<byte>> senderInbound,
            Channel<ReadOnlyMemory<byte>> receiverInbound,
            params IDisposable[] disposables)
        {
            Sender = sender;
            Receiver = receiver;
            SenderOutbound = senderOutbound;
            ReceiverOutbound = receiverOutbound;
            SenderInbound = senderInbound;
            ReceiverInbound = receiverInbound;
            _disposables = new IDisposable[] { sender, receiver }.Concat(disposables).ToArray();
        }

        public PNetMeshSession Sender { get; }

        public PNetMeshSession Receiver { get; }

        public Channel<PNetMeshOutboundMessages.Message> SenderOutbound { get; }

        public Channel<PNetMeshOutboundMessages.Message> ReceiverOutbound { get; }

        public Channel<ReadOnlyMemory<byte>> SenderInbound { get; }

        public Channel<ReadOnlyMemory<byte>> ReceiverInbound { get; }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
        }
    }
}
