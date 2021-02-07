using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PNet.Actor.Mesh;
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

        readonly ILogger _logger;

        IServiceScope _scope;

        PNetMeshServer _server;

        string _name;

        Dictionary<string, PNetMeshPeer> _peers;

        public NodeService(IServiceProvider services, ILogger<NodeService> logger)
        {
            _services = services;

            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _scope = _services.CreateScope();

            var options = _scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<NodeOptions>>().Value;

            _name = options.NodeName;

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

            _peers = new Dictionary<string, PNetMeshPeer>(options.Nodes.Length);
            foreach (var n in options.Nodes)
            {
                if (n.PublicKey != options.PublicKey)
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
            var channels = new List<(string Name, PNetMeshPeer Peer, PNetMeshChannel Channel)>();

            foreach (var entry in _peers)
            {
                var channel = await _server.ConnectToAsync(entry.Value, stoppingToken);

                channels.Add((entry.Key, entry.Value, channel));
            }

            var pongs = new bool[channels.Count];

            for (int i = 0; i < channels.Count; i++)
            {
                _ = Task.Run(async () =>
                {
                    var entry = channels[i];

                    var channel = entry.Channel;

                    ReadOnlyMemory<byte> payload;
                    bool r;

                    r = channel.TryWrite(Encoding.UTF8.GetBytes("ping"));
                    Debug.Assert(r);

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
                                    pongs[i] = true;
                                    r = channel.TryWrite(Encoding.UTF8.GetBytes("ping"));
                                    Debug.Assert(r);
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    while (await channel.WaitToReadAsync(stoppingToken));
                });
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            //Debug.Assert(pongs.All(n => n));

            var pongCount = pongs.Count(n => n);

            _logger.LogInformation("{nodeName} got {pongCount} pongs", _name, pongCount);
        }
    }
}
