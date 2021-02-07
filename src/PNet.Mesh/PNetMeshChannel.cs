using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    public sealed class PNetMeshChannel : IDisposable
    {
        readonly ILogger _logger;

        Channel<ReadOnlyMemory<byte>> _inboundChannel;

        Channel<PNetMeshChannelCommands.Command> _controlChannel;
        Task _controlTask = Task.CompletedTask;

        ImmutableList<PNetMeshSession> _sessions = ImmutableList<PNetMeshSession>.Empty;
        PNetMeshSession _currentSession;

        //public byte[] RemotePublicKey { get; init; }

        public long Timestamp { get; private set; }

        public bool IsOpen => _currentSession?.Status == PNetMeshSessionStatus.Open;

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
                            _currentSession.WritePayload(cmd.Payload.Span);
                            cmd.MemoryOwner?.Dispose();
                            cmd.Result?.SetResult();
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
                            _currentSession.WriteRelay(cmd.Packet);
                            cmd.MemoryOwner?.Dispose();
                            cmd.Result?.SetResult();
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
            await using CancellationTokenRegistration reg = cancellationToken.Register(state => ((TaskCompletionSource<PNetMeshChannel>)state).TrySetCanceled(), cmd.Result);

            await _controlChannel.Writer.WriteAsync(cmd, cancellationToken);

            await cmd.Result.Task;
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
