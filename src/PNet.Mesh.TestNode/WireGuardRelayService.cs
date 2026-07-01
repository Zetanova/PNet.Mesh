using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PNet.Mesh;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.TestNode
{
    sealed class WireGuardRelayService : BackgroundService
    {
        readonly IOptions<NodeOptions> _options;
        readonly IHostApplicationLifetime _lifetime;
        readonly ILogger _logger;

        UdpClient _udp;
        PNetMeshWireGuardRelayRegistry _registry;
        string _name;
        string _targetEndpointText;
        IPEndPoint _targetEndpoint;
        IPEndPoint _clientEndpoint;
        TimeSpan _runDuration;

        public WireGuardRelayService(
            IOptions<NodeOptions> options,
            IHostApplicationLifetime lifetime,
            ILogger<WireGuardRelayService> logger)
        {
            _options = options;
            _lifetime = lifetime;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var options = _options.Value;
            var peer = options.Peers.Single();
            _name = options.NodeName;
            _runDuration = TimeSpan.FromSeconds(Math.Max(1, options.RunDurationSeconds));
            _targetEndpointText = peer.Endpoints.Single();
            _targetEndpoint = await ResolveEndpointAsync(_targetEndpointText, cancellationToken);

            var bindTo = options.BindTo is { Length: > 0 }
                ? options.BindTo[0]
                : "0.0.0.0:12460";
            _udp = new UdpClient(IPEndPoint.Parse(bindTo));

            var protocol = new PNetMeshProtocol(
                Convert.FromBase64String(options.PrivateKey),
                Convert.FromBase64String(options.PublicKey),
                Convert.FromBase64String(options.Psk),
                PNetMeshTransportMode.WireGuard);
            _registry = new PNetMeshWireGuardRelayRegistry(protocol);
            _registry.RegisterOrRenew(
                Convert.FromBase64String(peer.PublicKey),
                Encoding.ASCII.GetBytes(_targetEndpointText),
                DateTimeOffset.UtcNow.Add(_runDuration),
                DateTimeOffset.UtcNow);

            _logger.LogInformation("WireGuardRelay[{nodeName}] starting...", _name);
            await base.StartAsync(cancellationToken);
            _logger.LogInformation("WireGuardRelay[{nodeName}] started", _name);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("WireGuardRelay[{nodeName}] stopping...", _name);

            _udp?.Dispose();

            await base.StopAsync(cancellationToken);

            _logger.LogInformation("WireGuardRelay[{nodeName}] stopped", _name);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(_runDuration);

            try
            {
                await RunRelayAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _logger.LogWarning("WireGuardRelay[{nodeName}] timed out waiting for relayed exchange", _name);
            }
            finally
            {
                _lifetime.StopApplication();
            }
        }

        async Task RunRelayAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var datagram = await _udp.ReceiveAsync(cancellationToken);
                if (IsTargetEndpoint(datagram.RemoteEndPoint))
                {
                    await RelayFromTargetAsync(datagram, cancellationToken);
                    continue;
                }

                await RelayFromClientAsync(datagram, cancellationToken);
            }
        }

        async Task RelayFromClientAsync(UdpReceiveResult datagram, CancellationToken cancellationToken)
        {
            if (_clientEndpoint is null)
            {
                if (!PNetMeshPacketFraming.TryReadMessageType(datagram.Buffer, out var messageType)
                    || messageType != PNetMeshMessageType.HandshakeInitiation
                    || !_registry.TryRoute(datagram.Buffer, datagram.RemoteEndPoint, DateTimeOffset.UtcNow, out _, out _))
                {
                    _logger.LogWarning("WireGuardRelay[{nodeName}] dropped unroutable client packet", _name);
                    return;
                }

                _clientEndpoint = datagram.RemoteEndPoint;
                await _udp.SendAsync(datagram.Buffer, _targetEndpoint, cancellationToken);
                _logger.LogInformation("WireGuardRelay[{nodeName}] routed handshake initiation to {targetEndpoint}", _name, _targetEndpointText);
                return;
            }

            if (!SameEndpoint(datagram.RemoteEndPoint, _clientEndpoint))
            {
                _logger.LogWarning("WireGuardRelay[{nodeName}] ignored packet from unexpected client endpoint", _name);
                return;
            }

            if (!PNetMeshPacketFraming.TryReadMessageType(datagram.Buffer, out var packetDataType)
                || packetDataType != PNetMeshMessageType.PacketData)
            {
                _logger.LogWarning("WireGuardRelay[{nodeName}] dropped unroutable client packet", _name);
                return;
            }

            await _udp.SendAsync(datagram.Buffer, _targetEndpoint, cancellationToken);
            _logger.LogInformation("WireGuardRelay[{nodeName}] relayed packet data to {targetEndpoint}", _name, _targetEndpointText);
        }

        async Task RelayFromTargetAsync(UdpReceiveResult datagram, CancellationToken cancellationToken)
        {
            if (_clientEndpoint is null)
            {
                _logger.LogWarning("WireGuardRelay[{nodeName}] dropped target packet before client mapping", _name);
                return;
            }

            if (!PNetMeshPacketFraming.TryReadMessageType(datagram.Buffer, out var messageType))
            {
                _logger.LogWarning("WireGuardRelay[{nodeName}] dropped malformed target packet", _name);
                return;
            }

            await _udp.SendAsync(datagram.Buffer, _clientEndpoint, cancellationToken);

            if (messageType == PNetMeshMessageType.HandshakeResponse)
            {
                _logger.LogInformation("WireGuardRelay[{nodeName}] relayed handshake response to client", _name);
                return;
            }

            if (messageType == PNetMeshMessageType.PacketData)
            {
                _logger.LogInformation("WireGuardRelay[{nodeName}] relayed packet data to client", _name);
                _clientEndpoint = null;
                _logger.LogInformation("WireGuardRelay[{nodeName}] released relay mapping", _name);
                _lifetime.StopApplication();
            }
        }

        bool IsTargetEndpoint(IPEndPoint endpoint)
        {
            return SameEndpoint(endpoint, _targetEndpoint);
        }

        static bool SameEndpoint(IPEndPoint left, IPEndPoint right)
        {
            return left is not null
                   && right is not null
                   && left.Port == right.Port
                   && left.Address.Equals(right.Address);
        }

        static async Task<IPEndPoint> ResolveEndpointAsync(string endpoint, CancellationToken cancellationToken)
        {
            var separator = endpoint.LastIndexOf(':');
            if (separator <= 0 || separator == endpoint.Length - 1)
                throw new FormatException($"Invalid endpoint '{endpoint}'.");

            var host = endpoint[..separator];
            var port = int.Parse(endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
            return new IPEndPoint(addresses[0], port);
        }
    }
}
