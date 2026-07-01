using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Immutable;
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

        Channel<ReadOnlyMemory<byte>> _inboundChannel;

        Channel<PNetMeshRoutePath> _routePathChannel;

        Channel<PNetMeshChannelCommands.Command> _controlChannel;
        Task _controlTask = Task.CompletedTask;

        ImmutableList<PNetMeshSession> _sessions = ImmutableList<PNetMeshSession>.Empty;
        PNetMeshSession _currentSession;

        //public byte[] RemotePublicKey { get; init; }

        public long Timestamp { get; private set; }

        public bool IsOpen => _currentSession?.Status == PNetMeshSessionStatus.Open;

        internal bool CanRelayDirect => TryGetRelaySession(out _);

        internal bool HasRoutableSession => TryGetRelaySession(out _);

        public PNetMeshChannel(ILogger<PNetMeshChannel> logger = null)
        {
            _logger = logger ?? NullLogger<PNetMeshChannel>.Instance;

            _inboundChannel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(100)
            {
                AllowSynchronousContinuations = true,
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
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
            _currentSession = null;

            _inboundChannel?.Writer.Complete();
            _inboundChannel = null;

            _routePathChannel?.Writer.Complete();
            _routePathChannel = null;

            _controlChannel?.Writer.Complete();
            _controlChannel = null;
        }

        internal void AddSession(PNetMeshSession session)
        {
            _sessions = _sessions.Add(session);
            Timestamp = Math.Max(session.Timestamp, Timestamp);

            session.AttachTo(_inboundChannel.Writer, _controlChannel.Writer);
            session.StatusChanged += OnSessionStatusChanged;
            OnSessionStatusChanged(session, EventArgs.Empty);

            //todo cleanup _sessions

            _logger.LogDebug("session[{sessionIndex}] to {remoteEndPoint} added.",
                session.SenderIndex, session.RemoteEndPoint);
        }

        private void OnSessionStatusChanged(object sender, EventArgs e)
        {
            var session = sender as PNetMeshSession;

            switch (session.Status)
            {
                case PNetMeshSessionStatus.Open:
                    _currentSession = session; //undone close other
                    if (_controlTask.IsCompleted)
                        _controlTask = Task.Run(ProcessControl)
                            .ContinueWith(t => _logger.LogError(t.Exception, "control process error"), TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case PNetMeshSessionStatus.Closed:
                    session.StatusChanged -= OnSessionStatusChanged;
                    break;
            }
        }

        async Task ProcessControl()
        {
            var reader = _controlChannel.Reader;

            do
            {
                while (reader.TryRead(out var command))
                {
                    switch (command)
                    {
                        case PNetMeshChannelCommands.Send cmd:
                            try
                            {
                                _currentSession.WritePayload(cmd.Payload.Span, cmd.Result);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "send command error");
                                cmd.Result?.TrySetException(ex);
                            }
                            finally
                            {
                                cmd.MemoryOwner?.Dispose();
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
                            //todo open session on demand
                            if (!TryGetRelaySession(out var relaySession))
                            {
                                cmd.MemoryOwner?.Dispose();
                                cmd.Result?.TrySetException(new InvalidOperationException("No routable session is available."));
                                break;
                            }

                            try
                            {
                                relaySession.WriteRelay(cmd.Packet, cmd.Result);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "relay command error");
                                cmd.Result?.TrySetException(ex);
                            }
                            finally
                            {
                                cmd.MemoryOwner?.Dispose();
                            }
                            break;
                        default:
                            _logger.LogWarning("unknown command '{commandType}'", command?.GetType());
                            break;
                    }
                }
            }
            while (await reader.WaitToReadAsync());
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            return _inboundChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        public bool TryRead(out ReadOnlyMemory<byte> payload)
        {
            return _inboundChannel.Reader.TryRead(out payload);
        }

        public ValueTask<bool> WaitToReadRoutePathAsync(CancellationToken cancellationToken = default)
        {
            return _routePathChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        public bool TryReadRoutePath(out PNetMeshRoutePath routePath)
        {
            return _routePathChannel.Reader.TryRead(out routePath);
        }

        internal bool TryWriteRoutePath(PNetMeshRoutePath routePath)
        {
            return _routePathChannel.Writer.TryWrite(routePath);
        }

        public bool TryWrite(ReadOnlyMemory<byte> payload, IMemoryOwner<byte> memoryOwner = default)
        {
            return _controlChannel.Writer.TryWrite(new PNetMeshChannelCommands.Send
            {
                Payload = payload,
                MemoryOwner = memoryOwner,
                Result = null
            });
        }

        internal bool TryRelay(PNetMeshRelayPacket packet, IMemoryOwner<byte> memoryOwner = default)
        {
            return _controlChannel.Writer.TryWrite(new PNetMeshChannelCommands.Relay
            {
                Packet = packet,
                MemoryOwner = memoryOwner,
                Result = null
            });
        }

        internal async Task RelayAsync(PNetMeshRelayPacket packet, IMemoryOwner<byte> memoryOwner = default, CancellationToken cancellationToken = default)
        {
            var cmd = new PNetMeshChannelCommands.Relay
            {
                Packet = packet,
                MemoryOwner = memoryOwner,
                Result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            //todo if (cancellationToken.IsCancellationRequested)
            await using CancellationTokenRegistration reg = cancellationToken.Register(state => ((TaskCompletionSource)state).TrySetCanceled(), cmd.Result);

            await _controlChannel.Writer.WriteAsync(cmd, cancellationToken);

            await cmd.Result.Task;
        }

        bool TryGetRelaySession(out PNetMeshSession session)
        {
            session = _currentSession;
            if (IsRelaySession(session))
                return true;

            foreach (var candidate in _sessions)
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

        static bool IsRelaySession(PNetMeshSession session)
        {
            return session?.Status == PNetMeshSessionStatus.Open && session.RemoteEndPoint is not null;
        }
    }

    public sealed class PNetMeshRoutePath
    {
        public byte[] DestinationAddress { get; init; }

        public ImmutableArray<byte[]> Route { get; init; }

        public ushort RemainingHopCount { get; init; }

        public EndPoint RemoteEndPoint { get; init; }

        public override string ToString()
        {
            var route = Route.IsDefault ? ImmutableArray<byte[]>.Empty : Route;
            return string.Join(" -> ", route.Select(address => Convert.ToHexString(address ?? Array.Empty<byte>())));
        }

        internal static PNetMeshRoutePath FromRelayPacket(PNetMeshRelayPacket packet, EndPoint remoteEndPoint)
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

            public IMemoryOwner<byte> MemoryOwner { get; init; }

            public TaskCompletionSource Result { get; init; }
        }

        public sealed class Invoke : Command
        {
            public Action Handler { get; init; }
        }

        public sealed class Relay : Command
        {
            public PNetMeshRelayPacket Packet { get; init; }

            public IMemoryOwner<byte> MemoryOwner { get; set; }

            public TaskCompletionSource Result { get; init; }
        }
    }
}
