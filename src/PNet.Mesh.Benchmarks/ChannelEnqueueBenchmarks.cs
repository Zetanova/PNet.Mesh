using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using System.Buffers;
using System.Reflection;
using System.Threading.Channels;

namespace PNet.Mesh.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class ChannelEnqueueBenchmarks
{
    static readonly FieldInfo DispatcherField = typeof(PNetMeshChannel)
        .GetField("_sessionDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PNetMeshChannel._sessionDispatcher was not found.");
    static readonly FieldInfo PendingChannelField = typeof(PNetMeshSessionDispatcher)
        .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PNetMeshSessionDispatcher._pending was not found.");

    readonly byte[] _payload = BenchmarkProtocolHarness.CreatePayload(BenchmarkProtocolHarness.MaxPayloadSize);
    PNetMeshChannel _channel = null!;
    Channel<PNetMeshSessionDispatchItem> _pendingChannel = null!;

    [Params(128, 1280)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _channel = new PNetMeshChannel();
        var dispatcher = (PNetMeshSessionDispatcher)DispatcherField.GetValue(_channel)!;
        _pendingChannel = (Channel<PNetMeshSessionDispatchItem>)PendingChannelField.GetValue(dispatcher)!;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DrainQueuedCommands();
        _channel.Dispose();
    }

    [Benchmark(Description = "Channel enqueue copied payload")]
    [BenchmarkCategory("channel")]
    public async ValueTask EnqueueCopiedPayload()
    {
        await _channel.EnqueueUnreliableWriteAsync(_payload.AsMemory(0, PayloadSize));
        DisposeQueuedSend();
    }

    [Benchmark(Description = "Channel enqueue owned payload")]
    [BenchmarkCategory("channel")]
    public async ValueTask EnqueueOwnedPayload()
    {
        var owner = MemoryPool<byte>.Shared.Rent(PayloadSize);
        try
        {
            await _channel.EnqueueUnreliableWriteAsync(owner.Memory.Slice(0, PayloadSize), owner);
            owner = null;
            DisposeQueuedSend();
        }
        finally
        {
            ClearAndDispose(owner);
        }
    }

    void DisposeQueuedSend()
    {
        if (!_pendingChannel.Reader.TryRead(out var item))
            throw new InvalidOperationException("Channel enqueue did not queue a session dispatch item.");

        ClearAndDispose(item.MemoryOwner);
    }

    void DrainQueuedCommands()
    {
        while (_pendingChannel.Reader.TryRead(out var item))
            ClearAndDispose(item.MemoryOwner);
    }

    static void ClearAndDispose(IMemoryOwner<byte>? owner)
    {
        if (owner is null)
            return;

        owner.Memory.Span.Clear();
        owner.Dispose();
    }

    sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            ArtifactsPath = "artifacts/benchmarks";
            AddColumn(StatisticColumn.OperationsPerSecond);
        }
    }
}
