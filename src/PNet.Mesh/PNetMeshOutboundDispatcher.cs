using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    internal sealed class PNetMeshOutboundDispatcher
    {
        const int DefaultCapacity = 100;

        readonly Func<ImmutableArray<Socket>> _sockets;
        readonly ILogger _logger;
        readonly Channel<PNetMeshOutboundMessages.Message> _pending;
        readonly ChannelWriter<PNetMeshOutboundMessages.Message> _writer;

        public PNetMeshOutboundDispatcher(
            Func<ImmutableArray<Socket>> sockets,
            ILogger logger,
            int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _sockets = sockets ?? throw new ArgumentNullException(nameof(sockets));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pending = Channel.CreateBounded<PNetMeshOutboundMessages.Message>(new BoundedChannelOptions(capacity)
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _writer = new DispatcherWriter(this);
        }

        public ChannelReader<PNetMeshOutboundMessages.Message> Reader => _pending.Reader;

        public ChannelWriter<PNetMeshOutboundMessages.Message> Writer => _writer;

        public void Complete()
        {
            _pending.Writer.TryComplete();
        }

        bool TryDispatch(PNetMeshOutboundMessages.Message message)
        {
            if (message is PNetMeshOutboundMessages.Packet packet
                && packet.RemoteEndPoint is not null
                && TrySendDirect(packet))
            {
                return true;
            }

#if PNET_MESH_PACKET_TRACE
            if (message is PNetMeshOutboundMessages.Packet queuedPacket
                && queuedPacket.PacketTraceKey is { } queuedTraceKey)
            {
                PNetMeshPacketTrace.RecordKey(
                    PNetMeshPacketTraceStage.UdpSendFallbackQueued,
                    queuedTraceKey,
                    queuedPacket.MemoryBuffer.Length);
            }
#endif
            return _pending.Writer.TryWrite(message);
        }

        ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken)
        {
            return _pending.Writer.WaitToWriteAsync(cancellationToken);
        }

        bool TryComplete(Exception? error)
        {
            return _pending.Writer.TryComplete(error);
        }

        bool TrySendDirect(PNetMeshOutboundMessages.Packet packet)
        {
            var socket = SelectSocket(_sockets(), packet.LocalEndPoint);
            if (socket is null)
                return false;

            var memoryOwner = packet.MemoryOwner;
            try
            {
#if PNET_MESH_PACKET_TRACE
                var traceKey = packet.PacketTraceKey;
                if (traceKey is { } sendStartKey)
                {
                    PNetMeshPacketTrace.RecordKey(
                        PNetMeshPacketTraceStage.UdpSendStart,
                        sendStartKey,
                        packet.MemoryBuffer.Length);
                }
#endif
                var send = socket.SendToAsync(
                    packet.MemoryBuffer,
                    SocketFlags.None,
                    packet.RemoteEndPoint!,
                    CancellationToken.None);

                packet.MemoryOwner = null;
                if (send.IsCompletedSuccessfully)
                {
                    _ = send.Result;
#if PNET_MESH_PACKET_TRACE
                    if (traceKey is { } completedSyncKey)
                    {
                        PNetMeshPacketTrace.RecordKey(
                            PNetMeshPacketTraceStage.UdpSendCompletedSync,
                            completedSyncKey,
                            packet.MemoryBuffer.Length);
                    }
#endif
                    memoryOwner?.Dispose();
                }
                else
                {
#if PNET_MESH_PACKET_TRACE
                    _ = CompleteSendAsync(
                        send,
                        memoryOwner,
                        _logger,
                        traceKey,
                        packet.MemoryBuffer.Length);
#else
                    _ = CompleteSendAsync(send, memoryOwner, _logger);
#endif
                }

                return true;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "event=outbound_direct_send_failed");
                return false;
            }
        }

        internal static Socket? SelectSocket(ImmutableArray<Socket> sockets, EndPoint? localEndPoint)
        {
            if (localEndPoint is not null)
            {
                foreach (var socket in sockets)
                {
                    try
                    {
                        if (Equals(socket.LocalEndPoint, localEndPoint))
                            return socket;
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }

            return sockets.Length > 0 ? sockets[0] : null;
        }

        static async Task CompleteSendAsync(
            ValueTask<int> send,
            IMemoryOwner<byte>? memoryOwner,
            ILogger logger
#if PNET_MESH_PACKET_TRACE
            ,
            PNetMeshPacketTraceKey? traceKey,
            int byteCount
#endif
            )
        {
            try
            {
                _ = await send.ConfigureAwait(false);
#if PNET_MESH_PACKET_TRACE
                if (traceKey is { } completedAsyncKey)
                {
                    PNetMeshPacketTrace.RecordKey(
                        PNetMeshPacketTraceStage.UdpSendCompletedAsync,
                        completedAsyncKey,
                        byteCount);
                }
#endif
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                logger.LogDebug(ex, "event=outbound_direct_send_failed");
            }
            finally
            {
                memoryOwner?.Dispose();
            }
        }

        sealed class DispatcherWriter : ChannelWriter<PNetMeshOutboundMessages.Message>
        {
            readonly PNetMeshOutboundDispatcher _dispatcher;

            public DispatcherWriter(PNetMeshOutboundDispatcher dispatcher)
            {
                _dispatcher = dispatcher;
            }

            public override bool TryWrite(PNetMeshOutboundMessages.Message item)
            {
                return _dispatcher.TryDispatch(item);
            }

            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            {
                return _dispatcher.WaitToWriteAsync(cancellationToken);
            }

            public override bool TryComplete(Exception? error = null)
            {
                return _dispatcher.TryComplete(error);
            }
        }
    }
}
