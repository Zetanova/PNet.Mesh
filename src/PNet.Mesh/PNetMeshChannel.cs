using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    public sealed class PNetMeshChannel : IDisposable
    {
        readonly ILogger _logger;

        Channel<ReadOnlyMemory<byte>>? _inboundChannel;

        Channel<PNetMeshRoutePath>? _routePathChannel;

        Channel<PNetMeshChannelCommands.Command>? _controlChannel;
        Channel<PNetMeshChannelCommands.Relay>? _relayChannel;
        Task _controlTask = Task.CompletedTask;
        Task _relayTask;

        // multi-threading: public send/relay APIs, session callbacks, control/relay loops,
        // cancellation callbacks, and Dispose publish or observe channel/session state concurrently.
        ImmutableList<PNetMeshSession> _sessions = ImmutableList<PNetMeshSession>.Empty;
        PNetMeshSession? _currentSession;
        TaskCompletionSource _relayStateChanged = CreateRelayStateChanged();
        bool _disposed;

        //public byte[] RemotePublicKey { get; init; }

        public long Timestamp { get; private set; }

        public bool IsOpen => Volatile.Read(ref _currentSession)?.Status == PNetMeshSessionStatus.Open;

        internal bool CanRelayDirect => TryGetRelaySession(out _);

        internal bool HasRoutableSession => HasRelaySessionCandidate();

        Channel<ReadOnlyMemory<byte>> InboundChannel => _inboundChannel ?? throw new ObjectDisposedException(nameof(PNetMeshChannel));

        Channel<PNetMeshRoutePath> RoutePathChannel => _routePathChannel ?? throw new ObjectDisposedException(nameof(PNetMeshChannel));

        Channel<PNetMeshChannelCommands.Command> ControlChannel => _controlChannel ?? throw new ObjectDisposedException(nameof(PNetMeshChannel));

        Channel<PNetMeshChannelCommands.Relay> RelayChannel => _relayChannel ?? throw new ObjectDisposedException(nameof(PNetMeshChannel));

        public PNetMeshChannel(ILogger<PNetMeshChannel>? logger = null)
        {
            _logger = logger ?? NullLogger<PNetMeshChannel>.Instance;

            _inboundChannel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _routePathChannel = Channel.CreateBounded<PNetMeshRoutePath>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _controlChannel = Channel.CreateBounded<PNetMeshChannelCommands.Command>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _relayChannel = Channel.CreateBounded<PNetMeshChannelCommands.Relay>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _relayTask = Task.Run(ProcessRelays)
                .ContinueWith(t => _logger.LogError(t.Exception, "relay process error"), TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose()
        {
            Volatile.Write(ref _disposed, true);
            Volatile.Read(ref _currentSession)?.Dispose();
            Volatile.Write(ref _currentSession, null);

            _inboundChannel?.Writer.Complete();
            _inboundChannel = null;

            _routePathChannel?.Writer.Complete();
            _routePathChannel = null;

            var controlChannel = _controlChannel;
            controlChannel?.Writer.Complete();
            if (_controlTask.IsCompleted)
                DrainControlCommands(controlChannel);
            _controlChannel = null;

            _relayChannel?.Writer.Complete();
            _relayChannel = null;
            SignalRelayStateChanged();
        }

        internal void AddSession(PNetMeshSession session)
        {
            ImmutableList<PNetMeshSession> sessions;
            ImmutableList<PNetMeshSession> updatedSessions;
            do
            {
                sessions = Volatile.Read(ref _sessions);
                updatedSessions = sessions.Add(session);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _sessions, updatedSessions, sessions), sessions));

            Timestamp = Math.Max(session.Timestamp, Timestamp);

            session.AttachTo(InboundChannel.Writer, ControlChannel.Writer);
            session.StatusChanged += OnSessionStatusChanged;
            session.MessageReceived += OnSessionMessageReceived;
            OnSessionStatusChanged(session, EventArgs.Empty);
            SignalRelayStateChanged();

            //todo cleanup _sessions

            _logger.LogDebug("session[{sessionIndex}] to {remoteEndPoint} added.",
                session.SenderIndex, session.RemoteEndPoint);
        }

        private void OnSessionStatusChanged(object? sender, EventArgs e)
        {
            if (sender is not PNetMeshSession session)
                return;

            switch (session.Status)
            {
                case PNetMeshSessionStatus.Open:
                    Volatile.Write(ref _currentSession, session); //undone close other
                    if (_controlTask.IsCompleted)
                        _controlTask = Task.Run(ProcessControl)
                            .ContinueWith(t => _logger.LogError(t.Exception, "control process error"), TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case PNetMeshSessionStatus.Closed:
                    session.StatusChanged -= OnSessionStatusChanged;
                    session.MessageReceived -= OnSessionMessageReceived;
                    break;
            }

            SignalRelayStateChanged();
        }

        private void OnSessionMessageReceived(object? sender, EventArgs e)
        {
            var session = sender as PNetMeshSession;
            if (session?.Status == PNetMeshSessionStatus.Open)
                Volatile.Write(ref _currentSession, session);
        }

        async Task ProcessControl()
        {
            var reader = ControlChannel.Reader;

            do
            {
                while (reader.TryRead(out var command))
                {
                    switch (command)
                    {
                        case PNetMeshChannelCommands.Send cmd:
                            try
                            {
                                cmd.CancellationToken.ThrowIfCancellationRequested();
                                var session = Volatile.Read(ref _currentSession) ?? throw new InvalidOperationException("No current session is available.");
                                session.WritePayload(cmd.Payload.Span, cmd.Result, cmd.UnreliablePayloadDelivery);
                            }
                            catch (OperationCanceledException ex)
                            {
                                cmd.Result?.TrySetCanceled(ex.CancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "send command error");
                                cmd.Result?.TrySetException(ex);
                            }
                            finally
                            {
                                ClearAndDispose(cmd.MemoryOwner);
                            }
                            break;
                        case PNetMeshChannelCommands.RawFrame cmd:
                            try
                            {
                                cmd.CancellationToken.ThrowIfCancellationRequested();
                                var session = Volatile.Read(ref _currentSession) ?? throw new InvalidOperationException("No current session is available.");
                                session.WriteRawFrame(cmd.Payload.Span, cmd.Result);
                            }
                            catch (OperationCanceledException ex)
                            {
                                cmd.Result?.TrySetCanceled(ex.CancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "raw frame command error");
                                cmd.Result?.TrySetException(ex);
                            }
                            finally
                            {
                                ClearAndDispose(cmd.MemoryOwner);
                            }
                            break;
                        case PNetMeshChannelCommands.Invoke cmd:
                            try
                            {
                                cmd.Handler.Invoke();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "invoke command error");
                            }
                            break;
                        case PNetMeshChannelCommands.Relay cmd:
                            await EnqueueRelayAsync(cmd);
                            break;
                        default:
                            _logger.LogWarning("unknown command '{commandType}'", command?.GetType());
                            break;
                    }
                }
            }
            while (await reader.WaitToReadAsync());
        }

        async Task ProcessRelays()
        {
            var reader = RelayChannel.Reader;
            var pending = new Queue<PNetMeshChannelCommands.Relay>();

            do
            {
                var stateChanged = GetRelayStateChangedTask();
                while (reader.TryRead(out var command))
                    pending.Enqueue(command);

                ProcessPendingRelays(pending);
                if (pending.Count > 0)
                    await stateChanged;
            }
            while (pending.Count > 0 || await reader.WaitToReadAsync());
        }

        async ValueTask EnqueueRelayAsync(PNetMeshChannelCommands.Relay cmd)
        {
            try
            {
                cmd.CancellationToken.ThrowIfCancellationRequested();
                await RelayChannel.Writer.WriteAsync(cmd, cmd.CancellationToken);
                SignalRelayStateChanged();
            }
            catch (OperationCanceledException ex)
            {
                cmd.Result?.TrySetCanceled(ex.CancellationToken);
                cmd.CancellationRegistration.Dispose();
                ClearAndDispose(cmd.MemoryOwner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "relay enqueue error");
                cmd.Result?.TrySetException(ex);
                cmd.CancellationRegistration.Dispose();
                ClearAndDispose(cmd.MemoryOwner);
            }
        }

        void ProcessPendingRelays(Queue<PNetMeshChannelCommands.Relay> pending)
        {
            var count = pending.Count;
            for (var i = 0; i < count; i++)
            {
                var cmd = pending.Dequeue();
                if (TryCompleteCanceledRelay(cmd))
                    continue;

                if (Volatile.Read(ref _disposed))
                {
                    cmd.Result?.TrySetException(new ObjectDisposedException(nameof(PNetMeshChannel)));
                    DisposeRelayCommand(cmd);
                    continue;
                }

                if (TryGetRelaySession(out var relaySession))
                {
                    ProcessRelay(cmd, relaySession);
                    continue;
                }

                if (!HasRelaySessionCandidate())
                {
                    cmd.Result?.TrySetException(new InvalidOperationException("No routable session is available."));
                    DisposeRelayCommand(cmd);
                    continue;
                }

                pending.Enqueue(cmd);
            }
        }

        void ProcessRelay(PNetMeshChannelCommands.Relay cmd, PNetMeshSession relaySession)
        {
            try
            {
                cmd.CancellationToken.ThrowIfCancellationRequested();
                relaySession.WriteRelay(cmd.Packet, cmd.Result);
            }
            catch (OperationCanceledException ex)
            {
                cmd.Result?.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "relay command error");
                cmd.Result?.TrySetException(ex);
            }
            finally
            {
                DisposeRelayCommand(cmd);
            }
        }

        bool TryCompleteCanceledRelay(PNetMeshChannelCommands.Relay cmd)
        {
            if (!cmd.CancellationToken.IsCancellationRequested)
                return false;

            cmd.Result?.TrySetCanceled(cmd.CancellationToken);
            DisposeRelayCommand(cmd);
            return true;
        }

        static void DisposeRelayCommand(PNetMeshChannelCommands.Relay cmd)
        {
            cmd.CancellationRegistration.Dispose();
            ClearAndDispose(cmd.MemoryOwner);
        }

        static void DrainControlCommands(Channel<PNetMeshChannelCommands.Command>? channel)
        {
            if (channel == null)
                return;

            while (channel.Reader.TryRead(out var command))
            {
                switch (command)
                {
                    case PNetMeshChannelCommands.Send send:
                        ClearAndDispose(send.MemoryOwner);
                        break;
                    case PNetMeshChannelCommands.RawFrame rawFrame:
                        ClearAndDispose(rawFrame.MemoryOwner);
                        break;
                    case PNetMeshChannelCommands.Relay relay:
                        DisposeRelayCommand(relay);
                        break;
                }
            }
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            return InboundChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        public bool TryRead(out ReadOnlyMemory<byte> payload)
        {
            return InboundChannel.Reader.TryRead(out payload);
        }

        public ValueTask<bool> WaitToReadRoutePathAsync(CancellationToken cancellationToken = default)
        {
            return RoutePathChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        public bool TryReadRoutePath([NotNullWhen(true)] out PNetMeshRoutePath? routePath)
        {
            return RoutePathChannel.Reader.TryRead(out routePath);
        }

        internal bool TryWriteRoutePath(PNetMeshRoutePath routePath)
        {
            return RoutePathChannel.Writer.TryWrite(routePath);
        }

        public bool TryWrite(ReadOnlyMemory<byte> payload, IMemoryOwner<byte>? memoryOwner = default)
        {
            return ControlChannel.Writer.TryWrite(new PNetMeshChannelCommands.Send
            {
                Payload = payload,
                MemoryOwner = memoryOwner,
                Result = null
            });
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            var cmd = new PNetMeshChannelCommands.Send
            {
                Payload = payload,
                Result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                CancellationToken = cancellationToken
            };

            await ControlChannel.Writer.WriteAsync(cmd, cancellationToken);
            await cmd.Result.Task;
        }

        public ValueTask EnqueueWriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            return EnqueueWriteAsync(payload, unreliablePayloadDelivery: false, cancellationToken);
        }

        public ValueTask EnqueueUnreliableWriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            return EnqueueWriteAsync(payload, unreliablePayloadDelivery: true, cancellationToken);
        }

        async ValueTask EnqueueWriteAsync(ReadOnlyMemory<byte> payload, bool unreliablePayloadDelivery, CancellationToken cancellationToken)
        {
            if (payload.IsEmpty)
            {
                await EnqueueWriteCommandAsync(
                    ReadOnlyMemory<byte>.Empty,
                    memoryOwner: null,
                    unreliablePayloadDelivery,
                    cancellationToken);
                return;
            }

            var memoryOwner = MemoryPool<byte>.Shared.Rent(payload.Length);
            var commandPayload = memoryOwner.Memory.Slice(0, payload.Length);
            payload.CopyTo(commandPayload);

            await EnqueueWriteAsync(commandPayload, memoryOwner, unreliablePayloadDelivery, cancellationToken);
        }

        /// <summary>
        /// Enqueues an owned reliable payload without copying it.
        /// </summary>
        /// <remarks>
        /// The <paramref name="payload"/> memory must be backed by <paramref name="memoryOwner"/>.
        /// Ownership transfers to the channel when this method is called; callers must not read,
        /// write, or dispose the owner after calling it. The channel clears the whole owner memory
        /// and disposes it after the payload is copied into a session packet or if enqueue fails.
        /// </remarks>
        public ValueTask EnqueueWriteAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte> memoryOwner,
            CancellationToken cancellationToken = default)
        {
            return EnqueueWriteAsync(payload, memoryOwner, unreliablePayloadDelivery: false, cancellationToken);
        }

        /// <summary>
        /// Enqueues an owned unreliable payload without copying it.
        /// </summary>
        /// <remarks>
        /// The <paramref name="payload"/> memory must be backed by <paramref name="memoryOwner"/>.
        /// Ownership transfers to the channel when this method is called; callers must not read,
        /// write, or dispose the owner after calling it. The channel clears the whole owner memory
        /// and disposes it after the payload is copied into a session packet or if enqueue fails.
        /// </remarks>
        public ValueTask EnqueueUnreliableWriteAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte> memoryOwner,
            CancellationToken cancellationToken = default)
        {
            return EnqueueWriteAsync(payload, memoryOwner, unreliablePayloadDelivery: true, cancellationToken);
        }

        public ValueTask EnqueueUnreliableIpPacketAsync(
            ReadOnlyMemory<byte> packet,
            IMemoryOwner<byte> memoryOwner,
            CancellationToken cancellationToken = default)
        {
            if (memoryOwner == null)
                throw new ArgumentNullException(nameof(memoryOwner));

            var packetLength = GetIpPacketLength(packet.Span, nameof(packet));
            return EnqueueRawFrameCommandAsync(packet.Slice(0, packetLength), memoryOwner, cancellationToken);
        }

        async ValueTask EnqueueWriteAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte> memoryOwner,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken)
        {
            if (memoryOwner == null)
                throw new ArgumentNullException(nameof(memoryOwner));

            await EnqueueWriteCommandAsync(payload, memoryOwner, unreliablePayloadDelivery, cancellationToken);
        }

        async ValueTask EnqueueWriteCommandAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte>? memoryOwner,
            bool unreliablePayloadDelivery,
            CancellationToken cancellationToken)
        {
            var cmd = new PNetMeshChannelCommands.Send
            {
                Payload = payload,
                MemoryOwner = memoryOwner,
                Result = null,
                UnreliablePayloadDelivery = unreliablePayloadDelivery,
                CancellationToken = cancellationToken
            };

            try
            {
                await ControlChannel.Writer.WriteAsync(cmd, cancellationToken);
                memoryOwner = null;
            }
            finally
            {
                ClearAndDispose(memoryOwner);
            }
        }

        async ValueTask EnqueueRawFrameCommandAsync(
            ReadOnlyMemory<byte> payload,
            IMemoryOwner<byte> memoryOwner,
            CancellationToken cancellationToken)
        {
            IMemoryOwner<byte>? pendingOwner = memoryOwner;
            var cmd = new PNetMeshChannelCommands.RawFrame
            {
                Payload = payload,
                MemoryOwner = pendingOwner,
                Result = null,
                CancellationToken = cancellationToken
            };

            try
            {
                await ControlChannel.Writer.WriteAsync(cmd, cancellationToken);
                pendingOwner = null;
            }
            finally
            {
                ClearAndDispose(pendingOwner);
            }
        }

        internal bool TryRelay(PNetMeshRelayPacket packet, IMemoryOwner<byte>? memoryOwner = default)
        {
            return ControlChannel.Writer.TryWrite(new PNetMeshChannelCommands.Relay
            {
                Packet = packet,
                MemoryOwner = memoryOwner,
                Result = null
            });
        }

        internal async Task RelayAsync(PNetMeshRelayPacket packet, IMemoryOwner<byte>? memoryOwner = default, CancellationToken cancellationToken = default)
        {
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = new PNetMeshChannelCommands.Relay
            {
                Packet = packet,
                MemoryOwner = memoryOwner,
                Result = result,
                CancellationToken = cancellationToken
            };
            if (cancellationToken.CanBeCanceled)
            {
                cmd.CancellationRegistration = cancellationToken.Register(
                    () => SignalRelayStateChanged());
            }

            var queued = false;
            try
            {
                await ControlChannel.Writer.WriteAsync(cmd, cancellationToken);
                queued = true;
                await result.Task;
            }
            finally
            {
                if (!queued)
                    DisposeRelayCommand(cmd);
            }
        }

        Task GetRelayStateChangedTask()
        {
            return Volatile.Read(ref _relayStateChanged).Task;
        }

        void SignalRelayStateChanged()
        {
            var signal = Interlocked.Exchange(ref _relayStateChanged, CreateRelayStateChanged());
            signal.TrySetResult();
        }

        bool TryGetRelaySession([NotNullWhen(true)] out PNetMeshSession? session)
        {
            session = Volatile.Read(ref _currentSession);
            if (session is not null && IsRelaySession(session))
                return true;

            foreach (var candidate in Volatile.Read(ref _sessions))
            {
                if (IsRelaySession(candidate))
                {
                    session = candidate;
                    return true;
                }
            }

            session = null;
            return false;
        }

        bool HasRelaySessionCandidate()
        {
            if (IsRelaySessionCandidate(Volatile.Read(ref _currentSession)))
                return true;

            foreach (var candidate in Volatile.Read(ref _sessions))
            {
                if (IsRelaySessionCandidate(candidate))
                    return true;
            }

            return false;
        }

        static bool IsRelaySession(PNetMeshSession? session)
        {
            return session?.Status == PNetMeshSessionStatus.Open && session.RemoteEndPoint is not null;
        }

        static bool IsRelaySessionCandidate(PNetMeshSession? session)
        {
            return session?.RemoteEndPoint is not null
                   && (session.Status == PNetMeshSessionStatus.Open
                       || session.Status == PNetMeshSessionStatus.Opening);
        }

        static int GetIpPacketLength(ReadOnlySpan<byte> payload, string paramName)
        {
            if (!PNetMeshPayloadFraming.TryRead(payload, out var frame, out _)
                || (frame.Kind != PNetMeshPayloadFrameKind.IPv4 && frame.Kind != PNetMeshPayloadFrameKind.IPv6))
            {
                throw new ArgumentException("Payload must be a valid IPv4 or IPv6 packet.", paramName);
            }

            return frame.TotalLength;
        }

        static TaskCompletionSource CreateRelayStateChanged()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        static void ClearAndDispose(IMemoryOwner<byte>? memoryOwner)
        {
            if (memoryOwner == null)
                return;

            memoryOwner.Memory.Span.Clear();
            memoryOwner.Dispose();
        }
    }

    public sealed class PNetMeshRoutePath
    {
        public required byte[] DestinationAddress { get; init; }

        public ImmutableArray<byte[]> Route { get; init; }

        public ushort RemainingHopCount { get; init; }

        public EndPoint? RemoteEndPoint { get; init; }

        public override string ToString()
        {
            var route = Route.IsDefault ? ImmutableArray<byte[]>.Empty : Route;
            return string.Join(" -> ", route.Select(address => Convert.ToHexString(address ?? Array.Empty<byte>())));
        }

        internal static PNetMeshRoutePath FromRelayPacket(PNetMeshRelayPacket packet, EndPoint? remoteEndPoint)
        {
            return new PNetMeshRoutePath
            {
                DestinationAddress = packet.Address?.ToArray() ?? Array.Empty<byte>(),
                Route = CloneRoute(packet.Route),
                RemainingHopCount = packet.HopCount,
                RemoteEndPoint = remoteEndPoint
            };
        }

        static ImmutableArray<byte[]> CloneRoute(ImmutableArray<byte[]> route)
        {
            if (route.IsDefaultOrEmpty)
                return ImmutableArray<byte[]>.Empty;

            var builder = ImmutableArray.CreateBuilder<byte[]>(route.Length);
            foreach (var address in route)
                builder.Add(address?.ToArray() ?? Array.Empty<byte>());
            return builder.ToImmutable();
        }
    }

    internal static class PNetMeshChannelCommands
    {
        public abstract class Command
        {
        }

        public sealed class Send : Command
        {
            public ReadOnlyMemory<byte> Payload { get; init; }

            public IMemoryOwner<byte>? MemoryOwner { get; init; }

            public TaskCompletionSource? Result { get; init; }

            public bool UnreliablePayloadDelivery { get; init; }

            public CancellationToken CancellationToken { get; init; }
        }

        public sealed class RawFrame : Command
        {
            public ReadOnlyMemory<byte> Payload { get; init; }

            public IMemoryOwner<byte>? MemoryOwner { get; init; }

            public TaskCompletionSource? Result { get; init; }

            public CancellationToken CancellationToken { get; init; }
        }

        public sealed class Invoke : Command
        {
            public required Action Handler { get; init; }
        }

        public sealed class Relay : Command
        {
            public required PNetMeshRelayPacket Packet { get; init; }

            public IMemoryOwner<byte>? MemoryOwner { get; set; }

            public TaskCompletionSource? Result { get; init; }

            public CancellationToken CancellationToken { get; init; }

            public CancellationTokenRegistration CancellationRegistration { get; set; }
        }
    }
}
