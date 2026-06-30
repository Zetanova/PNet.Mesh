using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.TestNode
{
    public sealed class NodeOptions
    {
        public string NodeName { get; set; } = Environment.MachineName;

        public string PublicKey { get; set; }

        public string PrivateKey { get; set; }

        public string Psk { get; set; }

        public string[] BindTo { get; set; }

        public Peer[] Peers { get; set; } = Array.Empty<Peer>();

        public Node[] Nodes { get; set; } = Array.Empty<Node>();

        public string[] ConnectNodes { get; set; } = Array.Empty<string>();

        public bool PingAllConnectedNodes { get; set; } = true;

        public string[] PingNodes { get; set; } = Array.Empty<string>();

        public int ConnectDelaySeconds { get; set; } = 3;

        public int RunDurationSeconds { get; set; } = 60;

        public sealed class Peer
        {
            public string PublicKey { get; set; }

            public string[] Endpoints { get; set; }
        }

        public sealed class Node
        {
            public string Name { get; set; }

            public string PublicKey { get; set; }
        }
    }

    sealed class NodeService : BackgroundService
    {
        readonly IServiceProvider _services;

        readonly IHostApplicationLifetime _lifetime;

        readonly ILogger _logger;

        IServiceScope _scope;

        PNetMeshServer _server;

        string _name;

        Dictionary<string, PNetMeshPeer> _peers;

        HashSet<string> _pingNodes;

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

            await _server.ShutdownAsync(cancellationToken);

            _scope.Dispose();

            _logger.LogInformation($"Node[{_name}] stopped");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //tmp delay traffic
            await Task.Delay(_connectDelay, stoppingToken);

            var channels = new List<(string Name, PNetMeshPeer Peer, PNetMeshChannel Channel)>();

            foreach (var entry in _peers)
            {
                var channel = await _server.ConnectToAsync(entry.Value, stoppingToken);

                channels.Add((entry.Key, entry.Value, channel));
            }

            var pongs = new bool[channels.Count];

            for (int i = 0; i < channels.Count; i++)
            {
                _ = Task.Factory.StartNew(async (state) =>
                {
                    var index = (int)state;

                    var entry = channels[index];

                    var channel = entry.Channel;
                    var sendsPings = _pingNodes == null || _pingNodes.Contains(entry.Name);

                    ReadOnlyMemory<byte> payload;
                    bool r;

                    if (sendsPings)
                    {
                        r = channel.TryWrite(Encoding.UTF8.GetBytes("ping"));
                        Debug.Assert(r);
                    }

                    do
                    {
                        while (channel.TryRead(out payload))
                        {
                            var msg = Encoding.UTF8.GetString(payload.Span);
                            switch (msg)
                            {
                                case "ping":
                                    _logger.LogInformation("ping from {remoteName} to {nodeName}", entry.Name, _name);
                                    r = channel.TryWrite(Encoding.UTF8.GetBytes("pong"));
                                    Debug.Assert(r);
                                    break;
                                case "pong":
                                    _logger.LogInformation("pong from {remoteName} to {nodeName}", entry.Name, _name);
                                    pongs[index] = true;
                                    if (sendsPings)
                                    {
                                        r = channel.TryWrite(Encoding.UTF8.GetBytes("ping"));
                                        Debug.Assert(r);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    while (await channel.WaitToReadAsync(stoppingToken));
                }, i).ContinueWith(t => _logger.LogError(t.Exception, "node process error"), TaskContinuationOptions.OnlyOnFaulted);
            }

            await Task.Delay(_runDuration, stoppingToken);

            //Debug.Assert(pongs.All(n => n));

            var pongCount = pongs.Count(n => n);

            _logger.LogInformation("{nodeName} got {pongCount} pongs", _name, pongCount);

            //stop application
            _lifetime.StopApplication();
        }
    }
}
