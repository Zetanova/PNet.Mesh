using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
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
    readonly byte[] _payload = BenchmarkProtocolHarness.CreatePayload(BenchmarkProtocolHarness.MaxPayloadSize);
    readonly byte[] _encryptedPacket = new byte[BenchmarkProtocolHarness.BufferSize];
    readonly byte[] _plaintext = new byte[BenchmarkProtocolHarness.BufferSize];

    byte[] _pnetFrame = Array.Empty<byte>();
    byte[] _ipv4Packet = Array.Empty<byte>();
    byte[] _ipv6Packet = Array.Empty<byte>();

    EstablishedTransportPair? _transports;
    PNetMeshSecureFrameSession? _secureFrameSender;
    PNetMeshSecureFrameSession? _secureFrameReceiver;
    SessionPair? _sessionPair;

    readonly PNetMeshFrameDispatcher _rawFrameDispatcher = new PNetMeshFrameDispatcher(
        NoopFrameHandler.Instance,
        NoopFrameHandler.Instance,
        NoopFrameHandler.Instance);

    [Params(64, 128, 512, 1280, 1420)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
        _secureFrameSender = new PNetMeshSecureFrameSession(_transports.Initiator);
        _secureFrameReceiver = new PNetMeshSecureFrameSession(_transports.Responder);
        _pnetFrame = PNetMeshPayloadFraming.CreatePNet(_payload.AsSpan(0, PayloadSize));
        _ipv4Packet = PNetMeshIpPacket.CreateIPv4(
            IPAddress.Parse("10.10.0.1"),
            IPAddress.Parse("10.10.0.2"),
            _payload.AsSpan(0, PayloadSize));
        _ipv6Packet = PNetMeshIpPacket.CreateIPv6(
            IPAddress.Parse("fd00::1"),
            IPAddress.Parse("fd00::2"),
            _payload.AsSpan(0, PayloadSize));
        _sessionPair = BenchmarkProtocolHarness.CreateOpenSessionPair();
        _sessionPair.Sender.UnreliablePayloadDelivery = true;
        DrainSessionState(_sessionPair);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _secureFrameSender = null;
        _secureFrameReceiver = null;
        _transports?.Dispose();
        _transports = null;
        _sessionPair?.Dispose();
        _sessionPair = null;
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

    [Benchmark(Description = "Transport decrypt PNet cleartext only")]
    [BenchmarkCategory("transport")]
    public int WriteThenReadPNetCleartextOnly()
    {
        return WriteThenReadCleartext(_pnetFrame);
    }

    [Benchmark(Description = "Transport decrypt IPv4 cleartext only")]
    [BenchmarkCategory("transport")]
    public int WriteThenReadIPv4CleartextOnly()
    {
        return WriteThenReadCleartext(_ipv4Packet);
    }

    [Benchmark(Description = "Transport decrypt IPv6 cleartext only")]
    [BenchmarkCategory("transport")]
    public int WriteThenReadIPv6CleartextOnly()
    {
        return WriteThenReadCleartext(_ipv6Packet);
    }

    [Benchmark(Description = "PNet frame create")]
    [BenchmarkCategory("framing")]
    public byte[] CreatePNetFrame()
    {
        return PNetMeshPayloadFraming.CreatePNet(_payload.AsSpan(0, PayloadSize));
    }

    [Benchmark(Description = "PNet frame write")]
    [BenchmarkCategory("framing")]
    public int TryWritePNetFrame()
    {
        if (!PNetMeshPayloadFraming.TryWritePNet(_payload.AsSpan(0, PayloadSize), _plaintext, out var bytesWritten))
            throw new InvalidOperationException("PNet frame buffer was too small.");

        return bytesWritten;
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

    [Benchmark(Description = "Already-decrypted PNet first-byte classification")]
    [BenchmarkCategory("classification")]
    public int ClassifyAlreadyDecryptedPNetFirstByte()
    {
        return ClassifyCleartext(_pnetFrame, PNetMeshPayloadFrameKind.PNet);
    }

    [Benchmark(Description = "Already-decrypted IPv4 first-byte classification")]
    [BenchmarkCategory("classification")]
    public int ClassifyAlreadyDecryptedIPv4FirstByte()
    {
        return ClassifyCleartext(_ipv4Packet, PNetMeshPayloadFrameKind.IPv4);
    }

    [Benchmark(Description = "Already-decrypted IPv6 first-byte classification")]
    [BenchmarkCategory("classification")]
    public int ClassifyAlreadyDecryptedIPv6FirstByte()
    {
        return ClassifyCleartext(_ipv6Packet, PNetMeshPayloadFrameKind.IPv6);
    }

    [Benchmark(Description = "Decrypted PNet cleartext first-byte classification")]
    [BenchmarkCategory("classification")]
    public int DecryptThenClassifyPNetCleartext()
    {
        return DecryptThenClassifyCleartext(_pnetFrame, PNetMeshPayloadFrameKind.PNet);
    }

    [Benchmark(Description = "Decrypted IPv4 cleartext first-byte classification")]
    [BenchmarkCategory("classification")]
    public int DecryptThenClassifyIPv4Cleartext()
    {
        return DecryptThenClassifyCleartext(_ipv4Packet, PNetMeshPayloadFrameKind.IPv4);
    }

    [Benchmark(Description = "Decrypted IPv6 cleartext first-byte classification")]
    [BenchmarkCategory("classification")]
    public int DecryptThenClassifyIPv6Cleartext()
    {
        return DecryptThenClassifyCleartext(_ipv6Packet, PNetMeshPayloadFrameKind.IPv6);
    }

    [Benchmark(Description = "Secure frame write/read dispatch PNet raw frame")]
    [BenchmarkCategory("raw-boundary")]
    public int SecureFrameWriteReadDispatchPNetRawFrame()
    {
        return SecureFrameWriteReadDispatchRawFrame(_pnetFrame);
    }

    [Benchmark(Description = "Secure frame write/read dispatch IPv4 raw frame")]
    [BenchmarkCategory("raw-boundary")]
    public int SecureFrameWriteReadDispatchIPv4RawFrame()
    {
        return SecureFrameWriteReadDispatchRawFrame(_ipv4Packet);
    }

    [Benchmark(Description = "Secure frame write/read dispatch IPv6 raw frame")]
    [BenchmarkCategory("raw-boundary")]
    public int SecureFrameWriteReadDispatchIPv6RawFrame()
    {
        return SecureFrameWriteReadDispatchRawFrame(_ipv6Packet);
    }

    [Benchmark(Description = "IPv4 header and total-length validation")]
    [BenchmarkCategory("parsing")]
    public int TryReadIPv4Header()
    {
        return ReadIpHeader(_ipv4Packet, PNetMeshIpPacketVersion.IPv4);
    }

    [Benchmark(Description = "IPv6 header and payload-length validation")]
    [BenchmarkCategory("parsing")]
    public int TryReadIPv6Header()
    {
        return ReadIpHeader(_ipv6Packet, PNetMeshIpPacketVersion.IPv6);
    }

    [Benchmark(Description = "IP materialized compatibility read")]
    [BenchmarkCategory("parsing")]
    public int TryReadIpPacketMaterializedCompatibility()
    {
        if (!PNetMeshIpPacket.TryRead(_ipv4Packet, out var ipv4Packet))
            throw new InvalidOperationException("IPv4 packet parsing failed.");
        if (!PNetMeshIpPacket.TryRead(_ipv6Packet, out var ipv6Packet))
            throw new InvalidOperationException("IPv6 packet parsing failed.");

        return ipv4Packet.PayloadLength
            + ipv6Packet.PayloadLength
            + (int)ipv4Packet.SourceAddress.AddressFamily
            + (int)ipv6Packet.DestinationAddress.AddressFamily;
    }

    [Benchmark(Description = "WireGuard full handshake setup")]
    [BenchmarkCategory("handshake")]
    public int FullHandshakeSetup()
    {
        using var transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
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
        using var pair = BenchmarkProtocolHarness.CreateOpenSessionPair();
        pair.Sender.WritePayload(_payload.AsSpan(0, PayloadSize));
        pair.Sender.WritePacket();

        var packet = BenchmarkProtocolHarness.ReadPacket(pair.SenderOutbound);
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

    [Benchmark(Description = "Session steady-state write/read payload")]
    [BenchmarkCategory("session")]
    public int SessionWriteReadPayloadPacketSteadyState()
    {
        var pair = _sessionPair ?? throw new InvalidOperationException("Benchmark setup did not create a session pair.");
        return WriteReadSteadyStateSessionPayloadPacket(pair, _payload.AsSpan(0, PayloadSize));
    }

    bool RejectTamperedPacket()
    {
        using var transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
        var packet = WriteTransportPacket(transports.Initiator);
        packet[^1] ^= 0x80;
        return !transports.Responder.TryReadMessage(packet, _plaintext, out _, out _);
    }

    bool RejectReplayPacket()
    {
        using var transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
        var packet = WriteTransportPacket(transports.Initiator);
        if (!transports.Responder.TryReadMessage(packet, _plaintext, out _, out _))
            throw new InvalidOperationException("Responder rejected first packet before replay.");

        return !transports.Responder.TryReadMessage(packet, _plaintext, out _, out _);
    }

    bool RejectUnknownReceiverPacket()
    {
        using var transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
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

    int WriteThenReadCleartext(ReadOnlySpan<byte> cleartext)
    {
        var transports = _transports ?? throw new InvalidOperationException("Benchmark setup did not create transports.");

        transports.Initiator.WriteMessage(
            cleartext,
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

    int DecryptThenClassifyCleartext(ReadOnlySpan<byte> cleartext, PNetMeshPayloadFrameKind expectedKind)
    {
        var plaintextBytes = WriteThenReadCleartext(cleartext);
        return plaintextBytes + ClassifyCleartext(_plaintext.AsSpan(0, plaintextBytes), expectedKind);
    }

    int SecureFrameWriteReadDispatchRawFrame(ReadOnlySpan<byte> cleartext)
    {
        var sender = _secureFrameSender ?? throw new InvalidOperationException("Benchmark setup did not create secure-frame sender.");
        var receiver = _secureFrameReceiver ?? throw new InvalidOperationException("Benchmark setup did not create secure-frame receiver.");

        if (!sender.TryWriteFrame(cleartext, _encryptedPacket, out var packetBytes, out _))
            throw new InvalidOperationException("Secure frame buffer was too small.");

        if (!receiver.TryReadFrame(
                _encryptedPacket.AsSpan(0, packetBytes),
                _plaintext,
                out var plaintext))
        {
            throw new InvalidOperationException("Secure frame did not decrypt.");
        }

        if (!_rawFrameDispatcher.TryDispatch(
                _plaintext.AsSpan(0, plaintext.BytesWritten),
                plaintext.Counter,
                out var payloadReceived,
                out var error))
        {
            throw new InvalidOperationException($"Raw frame dispatch failed: {error}");
        }

        return plaintext.BytesWritten + (payloadReceived ? 1 : 0);
    }

    static int ClassifyCleartext(ReadOnlySpan<byte> cleartext, PNetMeshPayloadFrameKind expectedKind)
    {
        if (!PNetMeshPayloadFraming.TryClassify(cleartext, out var kind, out var error) || kind != expectedKind)
            throw new InvalidOperationException($"Cleartext classification failed: {error}");

        return (int)kind;
    }

    static int ReadIpHeader(ReadOnlySpan<byte> packet, PNetMeshIpPacketVersion expectedVersion)
    {
        if (!PNetMeshIpPacket.TryReadHeader(packet, out var header) || header.Version != expectedVersion)
            throw new InvalidOperationException("IP header parsing failed.");

        return (int)header.Version + header.PayloadOffset + header.PayloadLength;
    }

    static int WriteReadSteadyStateSessionPayloadPacket(SessionPair pair, ReadOnlySpan<byte> payload)
    {
        DrainSessionState(pair);
        pair.Sender.WritePayload(payload);

        var packet = BenchmarkProtocolHarness.ReadPacket(pair.SenderOutbound);
        try
        {
            if (!pair.Receiver.TryReadMessage(packet.MemoryBuffer.Span))
                throw new InvalidOperationException("Receiver rejected session packet.");
        }
        finally
        {
            packet.MemoryOwner?.Dispose();
        }

        if (!pair.ReceiverInbound.Reader.TryRead(out var received))
            throw new InvalidOperationException("Receiver did not emit payload.");

        var receivedLength = received.Length;
        DrainSessionState(pair);
        return receivedLength;
    }

    static void DrainSessionState(SessionPair pair)
    {
        DrainOutboundMessages(pair.SenderOutbound);
        DrainOutboundMessages(pair.ReceiverOutbound);
        DrainInboundMessages(pair.SenderInbound);
        DrainInboundMessages(pair.ReceiverInbound);
        DrainControlCommands(pair.SenderControl);
        DrainControlCommands(pair.ReceiverControl);
    }

    static void DrainOutboundMessages(Channel<PNetMeshOutboundMessages.Message> channel)
    {
        while (channel.Reader.TryRead(out var message))
        {
            if (message is PNetMeshOutboundMessages.Packet packet)
                packet.MemoryOwner?.Dispose();
        }
    }

    static void DrainInboundMessages(Channel<ReadOnlyMemory<byte>> channel)
    {
        while (channel.Reader.TryRead(out _))
        {
        }
    }

    static void DrainControlCommands(Channel<PNetMeshChannelCommands.Command> channel)
    {
        while (channel.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case PNetMeshChannelCommands.Send send:
                    send.MemoryOwner?.Dispose();
                    break;
                case PNetMeshChannelCommands.Relay relay:
                    relay.CancellationRegistration.Dispose();
                    relay.MemoryOwner?.Dispose();
                    break;
            }
        }
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

    sealed class NoopFrameHandler : IPNetMeshFrameHandler
    {
        public static readonly NoopFrameHandler Instance = new NoopFrameHandler();

        NoopFrameHandler()
        {
        }

        public bool TryHandleFrame(ReadOnlySpan<byte> frame, ulong counter, out bool payloadReceived)
        {
            payloadReceived = false;
            return true;
        }
    }

}
