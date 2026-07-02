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
    static readonly FieldInfo ControlChannelField = typeof(PNetMeshChannel)
        .GetField("_controlChannel", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PNetMeshChannel._controlChannel was not found.");

    readonly byte[] _payload = BenchmarkProtocolHarness.CreatePayload(BenchmarkProtocolHarness.MaxPayloadSize);
    PNetMeshChannel _channel = null!;
    Channel<PNetMeshChannelCommands.Command> _controlChannel = null!;

    [Params(128, 1280)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _channel = new PNetMeshChannel();
        _controlChannel = (Channel<PNetMeshChannelCommands.Command>)ControlChannelField.GetValue(_channel)!;
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
        if (!_controlChannel.Reader.TryRead(out var command))
            throw new InvalidOperationException("Channel enqueue did not queue a send command.");

        var send = command as PNetMeshChannelCommands.Send
            ?? throw new InvalidOperationException($"Expected send command, got {command.GetType().Name}.");
        ClearAndDispose(send.MemoryOwner);
    }

    void DrainQueuedCommands()
    {
        while (_controlChannel.Reader.TryRead(out var command))
        {
            if (command is PNetMeshChannelCommands.Send send)
                ClearAndDispose(send.MemoryOwner);
        }
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
