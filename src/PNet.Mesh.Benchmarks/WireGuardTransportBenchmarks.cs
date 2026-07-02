using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Noise;
using System.Globalization;

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

    EstablishedTransportPair? _transports;

    [Params(64, 128, 512, 1280, 1420)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        for (var i = 0; i < _payload.Length; i++)
            _payload[i] = (byte)(i % 251);

        _transports = CreateEstablishedTransports();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _transports?.Dispose();
        _transports = null;
    }

    [Benchmark(Description = "WireGuard transport write/read")]
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
    public byte[] CreatePNetFrame()
    {
        return PNetMeshPayloadFraming.CreatePNet(_payload.AsSpan(0, PayloadSize));
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
}
