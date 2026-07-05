using Microsoft.Extensions.Logging;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PNet.Mesh
{
    internal sealed class PNetMeshInboundDispatcher
    {
        readonly PNetMeshProtocol _protocol;
        readonly PNetMeshSessionTable _sessions;
        readonly PNetMeshEndpointUpdater _endpointUpdater;
        readonly ChannelWriter<PNetMeshControlCommands.Command> _fallbackWriter;
        readonly ILogger _logger;

        public PNetMeshInboundDispatcher(
            PNetMeshProtocol protocol,
            PNetMeshSessionTable sessions,
            PNetMeshEndpointUpdater endpointUpdater,
            ChannelWriter<PNetMeshControlCommands.Command> fallbackWriter,
            ILogger logger)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _endpointUpdater = endpointUpdater ?? throw new ArgumentNullException(nameof(endpointUpdater));
            _fallbackWriter = fallbackWriter ?? throw new ArgumentNullException(nameof(fallbackWriter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Dispatch(PNetMeshControlCommands.Receive command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            try
            {
                if (TryHandleDirect(command))
                    return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "event=inbound_direct_dispatch_failed");
                command.MemoryOwner?.Dispose();
                command.MemoryOwner = null;
                return;
            }

            QueueFallback(command);
        }

        public bool TryHandleDirect(PNetMeshControlCommands.Receive command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (!PNetMeshPacketFraming.TryReadMessageType(command.MemoryBuffer.Span, out var messageType)
                || messageType != PNetMeshMessageType.PacketData)
            {
                return false;
            }

            var receiverIndex = _protocol.GetReceiverIndex(command.MemoryBuffer.Span);
            if (!_sessions.TryGet(receiverIndex, out var session))
                return false;

            var status = session.Status;
            if (status is not (PNetMeshSessionStatus.Open or PNetMeshSessionStatus.Opening))
                return false;

            try
            {
                if (!session.SupportsDirectEndpointDiscovery)
                    _endpointUpdater.ApplyLegacyEndpointUpdate(session, command);

                if (session.TryReadMessage(command.MemoryBuffer.Span)
                    && session.SupportsDirectEndpointDiscovery)
                {
                    _endpointUpdater.ApplyAuthenticatedEndpointUpdate(session, command);
                }

                return true;
            }
            finally
            {
                command.MemoryOwner?.Dispose();
                command.MemoryOwner = null;
            }
        }

        void QueueFallback(PNetMeshControlCommands.Receive command)
        {
            if (_fallbackWriter.TryWrite(command))
                return;

            _ = WriteFallbackAsync(_fallbackWriter, command, _logger);
        }

        static async Task WriteFallbackAsync(
            ChannelWriter<PNetMeshControlCommands.Command> writer,
            PNetMeshControlCommands.Receive command,
            ILogger logger)
        {
            var queued = false;
            try
            {
                await writer.WriteAsync(command);
                queued = true;
            }
            catch (Exception ex) when (ex is ChannelClosedException or InvalidOperationException)
            {
                logger.LogDebug(ex, "dropping received packet because the control channel is closed");
            }
            finally
            {
                if (!queued)
                {
                    command.MemoryOwner?.Dispose();
                    command.MemoryOwner = null;
                }
            }
        }
    }
}
