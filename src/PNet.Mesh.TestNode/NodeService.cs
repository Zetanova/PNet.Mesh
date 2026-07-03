using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.TestNode
{
    public sealed class NodeOptions
    {
        public string NodeName { get; set; } = Environment.MachineName;

        public required string PublicKey { get; set; }

        public required string PrivateKey { get; set; }

        public required string Psk { get; set; }

        public required string[] BindTo { get; set; }

        public Peer[] Peers { get; set; } = Array.Empty<Peer>();

        public Node[] Nodes { get; set; } = Array.Empty<Node>();

        public string[] ConnectNodes { get; set; } = Array.Empty<string>();

        public bool PingAllConnectedNodes { get; set; } = true;

        public string[] PingNodes { get; set; } = Array.Empty<string>();

        public int PingPayloadBytes { get; set; } = 4;

        public int ConnectDelaySeconds { get; set; } = 3;

        public int RunDurationSeconds { get; set; } = 60;

        public sealed class Peer
        {
            public required string PublicKey { get; set; }

            public required string[] Endpoints { get; set; }
        }

        public sealed class Node
        {
            public required string Name { get; set; }

            public required string PublicKey { get; set; }
        }
    }

    sealed class NodeService : BackgroundService
    {
        static readonly byte[] PingPayloadPrefix = Encoding.UTF8.GetBytes("ping");
        static readonly byte[] PongPayload = Encoding.UTF8.GetBytes("pong");

        readonly IServiceProvider _services;

        readonly IHostApplicationLifetime _lifetime;

        readonly ILogger _logger;

        IServiceScope? _scope;

        PNetMeshServer? _server;

        string? _name;

        Dictionary<string, PNetMeshPeer>? _peers;

        HashSet<string>? _pingNodes;

        byte[]? _pingPayload;

        TimeSpan _connectDelay;

        TimeSpan _runDuration;

        public NodeService(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<NodeService> logger)
        {
            _services = services;
            _lifetime = lifetime;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _scope = _services.CreateScope();

            var options = _scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<NodeOptions>>().Value;

            _name = options.NodeName;
            _connectDelay = TimeSpan.FromSeconds(Math.Max(0, options.ConnectDelaySeconds));
            _runDuration = TimeSpan.FromSeconds(Math.Max(1, options.RunDurationSeconds));
            _pingNodes = options.PingAllConnectedNodes
                ? null
                : new HashSet<string>(options.PingNodes, StringComparer.OrdinalIgnoreCase);
            _pingPayload = CreatePingPayload(options.PingPayloadBytes);

            var settings = new PNetMeshServerSettings
            {
                PublicKey = Convert.FromBase64String(options.PublicKey),
                PrivateKey = Convert.FromBase64String(options.PrivateKey),
                Psk = Convert.FromBase64String(options.Psk),
                BindTo = options.BindTo,
                Peers = options.Peers.Select(n => new PNetMeshPeer
                {
                    PublicKey = Convert.FromBase64String(n.PublicKey),
                    EndPoints = n.Endpoints,
                }).ToArray()
            };

            var connectNodes = options.ConnectNodes.Length > 0
                ? new HashSet<string>(options.ConnectNodes, StringComparer.OrdinalIgnoreCase)
                : null;

            _peers = new Dictionary<string, PNetMeshPeer>(options.Nodes.Length);
            foreach (var n in options.Nodes)
            {
                if (n.PublicKey != options.PublicKey && (connectNodes == null || connectNodes.Contains(n.Name)))
                    _peers[n.Name] = new PNetMeshPeer
                    {
                        PublicKey = Convert.FromBase64String(n.PublicKey),
                        EndPoints = Array.Empty<string>()
                    };
            }

            _logger.LogInformation($"Node[{_name}] starting...");

            _server = ActivatorUtilities.CreateInstance<PNetMeshServer>(_scope.ServiceProvider, settings);

            _server.Start();

            await base.StartAsync(cancellationToken);

            _logger.LogInformation($"Node[{_name}] started");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Node[{_name}] stopping...");

            await base.StopAsync(cancellationToken);

            if (_server is not null)
                await _server.ShutdownAsync(cancellationToken);

            _scope?.Dispose();

            _logger.LogInformation($"Node[{_name}] stopped");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //tmp delay traffic
            await Task.Delay(_connectDelay, stoppingToken);

            var server = _server ?? throw new InvalidOperationException("Node server is not initialized.");
            var peers = _peers ?? throw new InvalidOperationException("Node peers are not initialized.");
            var pingPayload = _pingPayload ?? throw new InvalidOperationException("Ping payload is not initialized.");
            var pingNodes = _pingNodes;
            var name = _name ?? "unknown";

            var channels = new List<(string Name, PNetMeshPeer Peer, PNetMeshChannel Channel)>();

            foreach (var entry in peers)
            {
                var channel = await server.ConnectToAsync(entry.Value, stoppingToken);

                channels.Add((entry.Key, entry.Value, channel));
            }

            var pongs = new bool[channels.Count];

            for (int i = 0; i < channels.Count; i++)
            {
                _ = Task.Factory.StartNew(async (state) =>
                {
                    if (state is not int index)
                        return;

                    var entry = channels[index];

                    var channel = entry.Channel;
                    var sendsPings = pingNodes == null || pingNodes.Contains(entry.Name);

                    ReadOnlyMemory<byte> payload;
                    if (sendsPings)
                    {
                        await channel.EnqueueWriteAsync(pingPayload, stoppingToken);
                    }

                    do
                    {
                        while (channel.TryRead(out payload))
                        {
                            if (IsPingPayload(payload))
                            {
                                _logger.LogInformation("ping from {remoteName} to {nodeName}", entry.Name, name);
                                if (payload.Length != PingPayloadPrefix.Length)
                                    _logger.LogInformation("ping payload {payloadBytes} bytes from {remoteName} to {nodeName}", payload.Length, entry.Name, name);
                                await channel.EnqueueWriteAsync(PongPayload, stoppingToken);
                            }
                            else if (IsPongPayload(payload))
                            {
                                _logger.LogInformation("pong from {remoteName} to {nodeName}", entry.Name, name);
                                pongs[index] = true;
                                if (sendsPings)
                                {
                                    await channel.EnqueueWriteAsync(pingPayload, stoppingToken);
                                }
                            }
                        }
                    }
                    while (await channel.WaitToReadAsync(stoppingToken));
                }, i).ContinueWith(t => _logger.LogError(t.Exception, "node process error"), TaskContinuationOptions.OnlyOnFaulted);
            }

            await Task.Delay(_runDuration, stoppingToken);

            //Debug.Assert(pongs.All(n => n));

            var pongCount = pongs.Count(n => n);

            _logger.LogInformation("{nodeName} got {pongCount} pongs", name, pongCount);

            //stop application
            _lifetime.StopApplication();
        }

        static byte[] CreatePingPayload(int payloadBytes)
        {
            var size = Math.Max(PingPayloadPrefix.Length, payloadBytes);
            var payload = new byte[size];
            PingPayloadPrefix.CopyTo(payload, 0);
            if (size > PingPayloadPrefix.Length)
                payload.AsSpan(PingPayloadPrefix.Length).Fill((byte)'x');

            return payload;
        }

        static bool IsPingPayload(ReadOnlyMemory<byte> payload)
        {
            var span = payload.Span;
            return span.Length >= PingPayloadPrefix.Length
                   && span[..PingPayloadPrefix.Length].SequenceEqual(PingPayloadPrefix);
        }

        static bool IsPongPayload(ReadOnlyMemory<byte> payload)
        {
            return payload.Span.SequenceEqual(PongPayload);
        }
    }
}
