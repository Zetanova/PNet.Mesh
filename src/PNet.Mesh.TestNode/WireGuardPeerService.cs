using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PNet.Mesh;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.TestNode
{
    sealed class WireGuardPeerService : BackgroundService
    {
        static readonly byte[] ExpectedRequest = Encoding.UTF8.GetBytes("pnet-wireguard-e2e-request");
        static readonly byte[] Response = Encoding.UTF8.GetBytes("pnet-wireguard-e2e-response");

        readonly IOptions<NodeOptions> _options;
        readonly IHostApplicationLifetime _lifetime;
        readonly ILogger _logger;

        UdpClient? _udp;
        PNetMeshProtocol? _protocol;
        string? _name;
        TimeSpan _runDuration;

        public WireGuardPeerService(
            IOptions<NodeOptions> options,
            IHostApplicationLifetime lifetime,
            ILogger<WireGuardPeerService> logger)
        {
            _options = options;
            _lifetime = lifetime;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var options = _options.Value;

            _name = options.NodeName;
            _runDuration = TimeSpan.FromSeconds(Math.Max(1, options.RunDurationSeconds));

            var bindTo = options.BindTo is { Length: > 0 }
                ? options.BindTo[0]
                : "0.0.0.0:12450";
            _udp = new UdpClient(IPEndPoint.Parse(bindTo));
            _protocol = new PNetMeshProtocol(
                Convert.FromBase64String(options.PrivateKey),
                Convert.FromBase64String(options.PublicKey),
                Convert.FromBase64String(options.Psk));

            _logger.LogInformation("WireGuardPeer[{nodeName}] starting...", _name);
            await base.StartAsync(cancellationToken);
            _logger.LogInformation("WireGuardPeer[{nodeName}] started", _name);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("WireGuardPeer[{nodeName}] stopping...", _name);

            _udp?.Dispose();

            await base.StopAsync(cancellationToken);

            _logger.LogInformation("WireGuardPeer[{nodeName}] stopped", _name);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(_runDuration);

            try
            {
                await RunExchangeAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _logger.LogWarning("WireGuardPeer[{nodeName}] timed out waiting for encrypted exchange", _name);
            }
            finally
            {
                _lifetime.StopApplication();
            }
        }

        async Task RunExchangeAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4098];
            var plaintext = new byte[4098];
            var udp = _udp ?? throw new InvalidOperationException("UDP socket is not initialized.");
            var protocol = _protocol ?? throw new InvalidOperationException("WireGuard protocol is not initialized.");

            var initiation = await udp.ReceiveAsync(cancellationToken);
            using var responder = protocol.CreateResponder(0x2001);
            if (!responder.TryReadInitiationMessage(initiation.Buffer))
            {
                _logger.LogWarning("WireGuardPeer[{nodeName}] rejected handshake initiation", _name);
                return;
            }

            if (!responder.TryWriteResponseMessage(buffer, out var bytesWritten, out var transport))
            {
                _logger.LogWarning("WireGuardPeer[{nodeName}] failed to create handshake response", _name);
                return;
            }

            _logger.LogInformation("WireGuardPeer[{nodeName}] handshake complete", _name);
            await udp.SendAsync(buffer.AsMemory(0, bytesWritten), initiation.RemoteEndPoint, cancellationToken);

            using (transport)
            {
                var request = await udp.ReceiveAsync(cancellationToken);
                if (!transport.TryReadPlaintext(request.Buffer, plaintext, out var received)
                    || !StartsWithPayloadAndZeroPadding(plaintext, received.BytesWritten, ExpectedRequest))
                {
                    _logger.LogWarning("WireGuardPeer[{nodeName}] rejected encrypted request", _name);
                    return;
                }

                _logger.LogInformation("WireGuardPeer[{nodeName}] received plaintext {payload}", _name, Encoding.UTF8.GetString(ExpectedRequest));

                transport.WriteMessage(Response, buffer, out bytesWritten, out _);
                await udp.SendAsync(buffer.AsMemory(0, bytesWritten), request.RemoteEndPoint, cancellationToken);
                _logger.LogInformation("WireGuardPeer[{nodeName}] sent encrypted response {payload}", _name, Encoding.UTF8.GetString(Response));
            }
        }

        static bool StartsWithPayloadAndZeroPadding(byte[] plaintext, int bytesWritten, byte[] expectedPayload)
        {
            if (bytesWritten < expectedPayload.Length)
                return false;

            if (!plaintext.AsSpan(0, expectedPayload.Length).SequenceEqual(expectedPayload))
                return false;

            for (var i = expectedPayload.Length; i < bytesWritten; i++)
            {
                if (plaintext[i] != 0)
                    return false;
            }

            return true;
        }
    }
}
