using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    internal sealed class PNetMeshSessionDispatcher
    {
        const int PendingCapacity = 256;

        readonly ILogger _logger;
        readonly Channel<PNetMeshSessionDispatchItem> _pending;
        TaskCompletionSource _sessionChanged = CreateSignal();
        PNetMeshSession? _currentSession;
        bool _disposed;

        public PNetMeshSessionDispatcher(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pending = Channel.CreateBounded<PNetMeshSessionDispatchItem>(new BoundedChannelOptions(PendingCapacity)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public void SetCurrentSession(PNetMeshSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Status == PNetMeshSessionStatus.Open)
            {
                Volatile.Write(ref _currentSession, session);
                SignalSessionChanged();
            }
        }

        public bool TryDispatchPayload(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte>? memoryOwner,
            TaskCompletionSource? result,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed))
                throw new ObjectDisposedException(nameof(PNetMeshChannel));

            var item = PNetMeshSessionDispatchItem.CreatePayload(payload, memoryOwner, result, unreliablePayloadDelivery, cancellationToken);
            try
            {
                if (TryDispatchDirect(item))
                    return true;

                return _pending.Writer.TryWrite(item);
            }
            catch
            {
                DisposeItem(item);
                throw;
            }
        }

        public async ValueTask DispatchPayloadAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte>? memoryOwner,
            TaskCompletionSource? result,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed))
                throw new ObjectDisposedException(nameof(PNetMeshChannel));

            var item = PNetMeshSessionDispatchItem.CreatePayload(payload, memoryOwner, result, unreliablePayloadDelivery, cancellationToken);
            await DispatchAsync(item, cancellationToken);
        }

        public bool TryDispatchRawFrame(
            ReadOnlyMemory<byte> packet,
            IMemoryOwner<byte> memoryOwner,
            PNetMeshSessionDispatchKind kind,
            CancellationToken cancellationToken = default)
        {
            if (memoryOwner == null)
                throw new ArgumentNullException(nameof(memoryOwner));
            if (Volatile.Read(ref _disposed))
                throw new ObjectDisposedException(nameof(PNetMeshChannel));

            var item = PNetMeshSessionDispatchItem.RawFrame(packet, memoryOwner, kind, cancellationToken);
            try
            {
                if (TryDispatchDirect(item))
                    return true;

                return _pending.Writer.TryWrite(item);
            }
            catch
            {
                DisposeItem(item);
                throw;
            }
        }

        public async ValueTask DispatchRawFrameAsync(
            ReadOnlyMemory<byte> packet,
            IMemoryOwner<byte> memoryOwner,
            PNetMeshSessionDispatchKind kind,
            CancellationToken cancellationToken)
        {
            if (memoryOwner == null)
                throw new ArgumentNullException(nameof(memoryOwner));
            if (Volatile.Read(ref _disposed))
                throw new ObjectDisposedException(nameof(PNetMeshChannel));

            var item = PNetMeshSessionDispatchItem.RawFrame(packet, memoryOwner, kind, cancellationToken);
            await DispatchAsync(item, cancellationToken);
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var reader = _pending.Reader;
            var pending = new Queue<PNetMeshSessionDispatchItem>();

            while (pending.Count > 0 || await reader.WaitToReadAsync(cancellationToken))
            {
                var sessionChanged = GetSessionChangedTask();
                while (reader.TryRead(out var item))
                    pending.Enqueue(item);

                ProcessPendingItems(pending);

                if (pending.Count > 0)
                    await sessionChanged.WaitAsync(cancellationToken);
            }
        }

        public void CompleteAndDisposePending()
        {
            Volatile.Write(ref _disposed, true);
            _pending.Writer.TryComplete();
            while (_pending.Reader.TryRead(out var item))
            {
                item.Result?.TrySetException(new ObjectDisposedException(nameof(PNetMeshChannel)));
                DisposeItem(item);
            }
            SignalSessionChanged();
        }

        async ValueTask DispatchAsync(PNetMeshSessionDispatchItem item, CancellationToken cancellationToken)
        {
            var queued = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryDispatchDirect(item))
                    return;

                await _pending.Writer.WriteAsync(item, cancellationToken);
                queued = true;
            }
            finally
            {
                if (!queued && item.MemoryOwner is not null)
                    DisposeItem(item);
            }
        }

        void ProcessPendingItems(Queue<PNetMeshSessionDispatchItem> pending)
        {
            var count = pending.Count;
            for (var i = 0; i < count; i++)
            {
                var item = pending.Dequeue();

                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Result?.TrySetCanceled(item.CancellationToken);
                    DisposeItem(item);
                    continue;
                }

                if (Volatile.Read(ref _disposed))
                {
                    item.Result?.TrySetException(new ObjectDisposedException(nameof(PNetMeshChannel)));
                    DisposeItem(item);
                    continue;
                }

                try
                {
                    if (TryDispatchDirect(item))
                        continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "session dispatch error");
                    item.Result?.TrySetException(ex);
                    DisposeItem(item);
                    continue;
                }

                pending.Enqueue(item);
            }
        }

        bool TryDispatchDirect(PNetMeshSessionDispatchItem item)
        {
            var session = Volatile.Read(ref _currentSession);
            if (session is null || session.Status != PNetMeshSessionStatus.Open)
                return false;

            var dispatched = item.Kind switch
            {
                PNetMeshSessionDispatchKind.PNetPayload =>
                    session.TryWritePayload(item.Payload.Span, item.Result, item.UnreliablePayloadDelivery),
                PNetMeshSessionDispatchKind.RawIPv4 or PNetMeshSessionDispatchKind.RawIPv6 =>
                    session.TryWriteRawFrame(item.Payload.Span, item.Payload.Length),
                _ => false
            };

            if (!dispatched)
                return false;

            if (item.Kind is PNetMeshSessionDispatchKind.RawIPv4 or PNetMeshSessionDispatchKind.RawIPv6)
                item.Result?.TrySetResult();
            DisposeItem(item);
            return true;
        }

        Task GetSessionChangedTask()
        {
            return Volatile.Read(ref _sessionChanged).Task;
        }

        void SignalSessionChanged()
        {
            var signal = Interlocked.Exchange(ref _sessionChanged, CreateSignal());
            signal.TrySetResult();
        }

        static TaskCompletionSource CreateSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        static void DisposeItem(PNetMeshSessionDispatchItem item)
        {
            ClearAndDispose(item.MemoryOwner);
            item.MemoryOwner = null;
        }

        static void ClearAndDispose(IMemoryOwner<byte>? memoryOwner)
        {
            if (memoryOwner == null)
                return;

            memoryOwner.Memory.Span.Clear();
            memoryOwner.Dispose();
        }
    }

    internal enum PNetMeshSessionDispatchKind
    {
        PNetPayload,
        RawIPv4,
        RawIPv6
    }

    internal sealed class PNetMeshSessionDispatchItem
    {
        PNetMeshSessionDispatchItem(
            PNetMeshSessionDispatchKind kind,
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte>? memoryOwner,
            TaskCompletionSource? result,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken)
        {
            Kind = kind;
            Payload = payload;
            MemoryOwner = memoryOwner;
            Result = result;
            UnreliablePayloadDelivery = unreliablePayloadDelivery;
            CancellationToken = cancellationToken;
        }

        public PNetMeshSessionDispatchKind Kind { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public IMemoryOwner<byte>? MemoryOwner { get; set; }

        public TaskCompletionSource? Result { get; }

        public bool UnreliablePayloadDelivery { get; }

        public CancellationToken CancellationToken { get; }

        public static PNetMeshSessionDispatchItem CreatePayload(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte>? memoryOwner,
            TaskCompletionSource? result,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken)
        {
            return new PNetMeshSessionDispatchItem(
                PNetMeshSessionDispatchKind.PNetPayload,
                payload,
                memoryOwner,
                result,
                unreliablePayloadDelivery,
                cancellationToken);
        }

        public static PNetMeshSessionDispatchItem RawFrame(
            ReadOnlyMemory<byte> packet,
            IMemoryOwner<byte> memoryOwner,
            PNetMeshSessionDispatchKind kind,
            CancellationToken cancellationToken)
        {
            if (kind is not (PNetMeshSessionDispatchKind.RawIPv4 or PNetMeshSessionDispatchKind.RawIPv6))
                throw new ArgumentOutOfRangeException(nameof(kind));

            return new PNetMeshSessionDispatchItem(
                kind,
                packet,
                memoryOwner,
                result: null,
                unreliablePayloadDelivery: true,
                cancellationToken);
        }
    }
}
