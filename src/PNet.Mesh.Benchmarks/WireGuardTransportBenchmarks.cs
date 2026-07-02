using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;

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

    [Params(64, 128, 512, 1280, 1420)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _transports = BenchmarkProtocolHarness.CreateEstablishedTransports();
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

}
