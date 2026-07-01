using System;
using System.Collections.Generic;
using System.Globalization;

namespace PNet.Actor.E2ETests.Mesh;

public sealed class PNetMeshTestNodeSpec
{
    const string ComposePsk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4=";
    const string InvalidComposePsk = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    static readonly PNetMeshNodeIdentity[] ComposeNodes =
    {
        new PNetMeshNodeIdentity { Name = "node00", PublicKey = "zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=" },
        new PNetMeshNodeIdentity { Name = "node01", PublicKey = "ytmio4/zTDDXHt0A6jb8G7Gcr3ty7iMOEkduloie1Rk=" },
        new PNetMeshNodeIdentity { Name = "node10", PublicKey = "1BvJiBowJJExAa0+4VkquRkGJ06R5e+wYw1KCQu04iU=" },
        new PNetMeshNodeIdentity { Name = "node11", PublicKey = "F42i92MeeCp3hsKmO7XVLvJVhjK3EDZCgDMDGFM0YiI=" },
        new PNetMeshNodeIdentity { Name = "node20", PublicKey = "GIXvRdbvp/pN+3yY1OiuYGYQNSnY7Y35GUKkUmBHRCc=" },
        new PNetMeshNodeIdentity { Name = "node21", PublicKey = "s6hJSti/lhOL3iWG/cXJpV+uyRuKLQ1XVQXwNKRFZiQ=" }
    };

    public required string Name { get; init; }

    public required string PublicKey { get; init; }

    public required string PrivateKey { get; init; }

    public required string Psk { get; init; }

    public string Mode { get; init; } = "Mesh";

    public int Port { get; init; } = 12401;

    public string BindAddress { get; init; } = "0.0.0.0";

    public bool PublishUdpPort { get; init; }

    public int ConnectDelaySeconds { get; init; }

    public int RunDurationSeconds { get; init; } = 120;

    public bool PingAllConnectedNodes { get; init; }

    public IReadOnlyList<string> ConnectNodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PingNodes { get; init; } = Array.Empty<string>();

    public int PingPayloadBytes { get; init; } = 4;

    public IReadOnlyList<string> NetworkNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PNetMeshPeerEndpoint> Peers { get; init; } = Array.Empty<PNetMeshPeerEndpoint>();

    public IReadOnlyList<PNetMeshNodeIdentity> Nodes { get; init; } = Array.Empty<PNetMeshNodeIdentity>();

    public string ReadyLogEntry => Mode switch
    {
        var mode when string.Equals(mode, "WireGuardPeer", StringComparison.OrdinalIgnoreCase)
            => $"WireGuardPeer[{Name}] started",
        var mode when string.Equals(mode, "WireGuardRelay", StringComparison.OrdinalIgnoreCase)
            => $"WireGuardRelay[{Name}] started",
        _ => $"Node[{Name}] started"
    };

    public IReadOnlyDictionary<string, string> ToEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Mode"] = Mode,
            ["NodeName"] = Name,
            ["PublicKey"] = PublicKey,
            ["PrivateKey"] = PrivateKey,
            ["Psk"] = Psk,
            ["BindTo__0"] = $"{BindAddress}:{Port}",
            ["ConnectDelaySeconds"] = ConnectDelaySeconds.ToString(CultureInfo.InvariantCulture),
            ["PingAllConnectedNodes"] = PingAllConnectedNodes.ToString().ToLowerInvariant(),
            ["PingPayloadBytes"] = PingPayloadBytes.ToString(CultureInfo.InvariantCulture),
            ["RunDurationSeconds"] = RunDurationSeconds.ToString(CultureInfo.InvariantCulture)
        };

        AddIndexedValues(environment, "ConnectNodes", ConnectNodes);
        AddIndexedValues(environment, "PingNodes", PingNodes);

        for (var i = 0; i < Peers.Count; i++)
        {
            environment[$"Peers__{i}__PublicKey"] = Peers[i].PublicKey;
            AddIndexedValues(environment, $"Peers__{i}__Endpoints", Peers[i].Endpoints);
        }

        for (var i = 0; i < Nodes.Count; i++)
        {
            environment[$"Nodes__{i}__Name"] = Nodes[i].Name;
            environment[$"Nodes__{i}__PublicKey"] = Nodes[i].PublicKey;
        }

        return environment;
    }

    public static PNetMeshTestNodeSpec StandaloneNode00()
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "tc-node00",
            PublicKey = "zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=",
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4="
        };
    }

    public static PNetMeshTestNodeSpec WireGuardPeerContainerPeer(
        bool publishUdpPort = true,
        string networkName = null)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "wireguard-peer",
            Mode = "WireGuardPeer",
            PublicKey = GetComposePublicKey("node00"),
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = ComposePsk,
            Port = 12450,
            PublishUdpPort = publishUdpPort,
            RunDurationSeconds = 30,
            NetworkNames = networkName is null ? Array.Empty<string>() : new[] { networkName }
        };
    }

    public static PNetMeshTestNodeSpec WireGuardRelayContainerNode(PNetMeshTestNodeSpec peer, string networkName)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "wireguard-relay",
            Mode = "WireGuardRelay",
            PublicKey = GetComposePublicKey("node01"),
            PrivateKey = "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=",
            Psk = ComposePsk,
            Port = 12460,
            PublishUdpPort = true,
            RunDurationSeconds = 30,
            NetworkNames = new[] { networkName },
            Peers = new[]
            {
                PeerEndpoint(peer.PublicKey, $"{peer.Name}:{peer.Port}")
            }
        };
    }

    public static PNetMeshTestNodeSpec InvalidBindNode()
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "tc-invalid-bind",
            PublicKey = "zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=",
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4=",
            BindAddress = "not-an-ip-address"
        };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> ComposeSmokeTopologyOnSingleDockerNetwork()
    {
        // The compose smoke publishes node00 as a TCP host-gateway endpoint while the mesh server speaks UDP.
        // The Testcontainers port keeps the six-node route shape but uses one isolated Docker network and DNS aliases.
        var node00 = ComposeNode("node00", "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=", 12401, 9, new[] { "node01", "node10", "node11", "node20" }, Array.Empty<string>());
        var node01 = ComposeNode("node01", "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=", 12402, 3, new[] { "node00" }, new[] { "node00" }, StaticPeer("node00", "node00:12401"));
        var node10 = ComposeNode("node10", "NTxs6EdH52kw7bsDp0W1A5LD588wthPlrFU8K3RgP7Y=", 12410, 3, new[] { "node00" }, new[] { "node00" }, StaticPeer("node00", "node00:12401"));
        var node11 = ComposeNode("node11", "1hhY9rLTyqxDf9ZpzRxKigyGWhh+rqABV7CEcRKPAHM=", 12411, 3, new[] { "node00" }, new[] { "node00" }, StaticPeer("node00", "node00:12401"));
        var node20 = ComposeNode("node20", "D0cOwLrDQt4oM1kIEWI+vYrKmP1W57MdfOLmrXdE70k=", 12420, 6, new[] { "node00", "node21" }, new[] { "node00" }, StaticPeer("node00", "node00:12401"));
        var node21 = ComposeNode("node21", "B/B2Gd7/YHJ49yv04uQ+XZSRbKH7GvUecq5SQd3gOOA=", 12421, 3, new[] { "node20" }, new[] { "node20" }, StaticPeer("node20", "node20:12420"));

        return new[] { node00, node01, node10, node11, node20, node21 };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> DirectPeerTopology(int pingPayloadBytes = 4)
    {
        var node00 = DirectPeerNode("node00", "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=", 12401, "node01", 12402, pingPayloadBytes);
        var node01 = DirectPeerNode("node01", "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=", 12402, "node00", 12401, pingPayloadBytes);

        return new[] { node00, node01 };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> InvalidPskDirectPeerTopology()
    {
        var node00 = DirectPeerNode("node00", "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=", 12401, "node01", 12402);
        var node01 = DirectPeerNodeWithPsk(
            "node01",
            "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=",
            12402,
            "node00",
            12401,
            InvalidComposePsk);

        return new[] { node00, node01 };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> BootstrapDiscoveryTopology()
    {
        var node00 = new PNetMeshTestNodeSpec
        {
            Name = "node00",
            PublicKey = GetComposePublicKey("node00"),
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = ComposePsk,
            Port = 12401,
            ConnectDelaySeconds = 8,
            RunDurationSeconds = 28,
            ConnectNodes = new[] { "node10", "node01" },
            Peers = new[]
            {
                StaticPeer("node10", "node10:12410"),
                StaticPeer("node01", "node01:12402")
            },
            Nodes = new[]
            {
                new PNetMeshNodeIdentity { Name = "node00", PublicKey = GetComposePublicKey("node00") },
                new PNetMeshNodeIdentity { Name = "node10", PublicKey = GetComposePublicKey("node10") },
                new PNetMeshNodeIdentity { Name = "node01", PublicKey = GetComposePublicKey("node01") }
            }
        };

        var node10 = BootstrapDiscoveryEdgeNode("node10", "NTxs6EdH52kw7bsDp0W1A5LD588wthPlrFU8K3RgP7Y=", 12410, "node01", 12);
        var node01 = BootstrapDiscoveryEdgeNode("node01", "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=", 12402, "node10", 12);

        return new[] { node00, node10, node01 };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> MultiHopRouteTopology()
    {
        const string leftSegment = "left";
        const string middleSegment = "middle";
        const string rightSegment = "right";

        var node10 = MultiHopRouteNode(
            "node10",
            "NTxs6EdH52kw7bsDp0W1A5LD588wthPlrFU8K3RgP7Y=",
            12410,
            14,
            new[] { "node00", "node01" },
            new[] { "node01" },
            new[] { leftSegment },
            NodeIdentities("node00", "node01", "node10", "node20"),
            StaticPeer("node00", "node00:12401"));

        var node00 = MultiHopRouteNode(
            "node00",
            "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            12401,
            8,
            new[] { "node10", "node20" },
            Array.Empty<string>(),
            new[] { leftSegment, middleSegment },
            NodeIdentities("node10", "node20", "node00", "node01"),
            StaticPeer("node10", "node10:12410"),
            StaticPeer("node20", "node20:12420"));

        var node20 = MultiHopRouteNode(
            "node20",
            "D0cOwLrDQt4oM1kIEWI+vYrKmP1W57MdfOLmrXdE70k=",
            12420,
            8,
            new[] { "node00", "node01" },
            Array.Empty<string>(),
            new[] { middleSegment, rightSegment },
            NodeIdentities("node00", "node01", "node20", "node10"),
            StaticPeer("node00", "node00:12401"),
            StaticPeer("node01", "node01:12402"));

        var node01 = MultiHopRouteNode(
            "node01",
            "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=",
            12402,
            14,
            new[] { "node20", "node10" },
            new[] { "node10" },
            new[] { rightSegment },
            NodeIdentities("node20", "node10", "node01", "node00"),
            StaticPeer("node20", "node20:12420"));

        return new[] { node10, node00, node20, node01 };
    }

    public static IReadOnlyList<PNetMeshTestNodeSpec> RestartRecoveryTopology()
    {
        var node00 = new PNetMeshTestNodeSpec
        {
            Name = "node00",
            PublicKey = GetComposePublicKey("node00"),
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = ComposePsk,
            Port = 12401,
            ConnectDelaySeconds = 28,
            RunDurationSeconds = 60,
            ConnectNodes = new[] { "node01" },
            Peers = new[] { StaticPeer("node01", "node01:12402") },
            Nodes = NodeIdentities("node00", "node01")
        };

        var node10 = RestartRecoveryUnrelatedNode(
            "node10",
            "NTxs6EdH52kw7bsDp0W1A5LD588wthPlrFU8K3RgP7Y=",
            12410,
            "node11",
            12411);

        var node11 = RestartRecoveryUnrelatedNode(
            "node11",
            "1hhY9rLTyqxDf9ZpzRxKigyGWhh+rqABV7CEcRKPAHM=",
            12411,
            "node10",
            12410);

        var node01 = new PNetMeshTestNodeSpec
        {
            Name = "node01",
            PublicKey = GetComposePublicKey("node01"),
            PrivateKey = "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=",
            Psk = ComposePsk,
            Port = 12402,
            ConnectDelaySeconds = 8,
            RunDurationSeconds = 32,
            ConnectNodes = new[] { "node00" },
            PingNodes = new[] { "node00" },
            Peers = new[] { StaticPeer("node00", "node00:12401") },
            Nodes = NodeIdentities("node01", "node00")
        };

        return new[] { node00, node10, node11, node01 };
    }

    static PNetMeshTestNodeSpec ComposeNode(
        string name,
        string privateKey,
        int port,
        int connectDelaySeconds,
        IReadOnlyList<string> connectNodes,
        IReadOnlyList<string> pingNodes,
        params PNetMeshPeerEndpoint[] peers)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = connectDelaySeconds,
            RunDurationSeconds = 30,
            ConnectNodes = connectNodes,
            PingNodes = pingNodes,
            Peers = peers,
            Nodes = ComposeNodes
        };
    }

    static PNetMeshTestNodeSpec MultiHopRouteNode(
        string name,
        string privateKey,
        int port,
        int connectDelaySeconds,
        IReadOnlyList<string> connectNodes,
        IReadOnlyList<string> pingNodes,
        IReadOnlyList<string> networkNames,
        IReadOnlyList<PNetMeshNodeIdentity> nodes,
        params PNetMeshPeerEndpoint[] peers)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = connectDelaySeconds,
            RunDurationSeconds = 60,
            ConnectNodes = connectNodes,
            PingNodes = pingNodes,
            NetworkNames = networkNames,
            Peers = peers,
            Nodes = nodes
        };
    }

    static PNetMeshTestNodeSpec DirectPeerNode(
        string name,
        string privateKey,
        int port,
        string peerName,
        int peerPort,
        int pingPayloadBytes = 4)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = 4,
            RunDurationSeconds = 10,
            ConnectNodes = new[] { peerName },
            PingNodes = new[] { peerName },
            PingPayloadBytes = pingPayloadBytes,
            Peers = new[] { StaticPeer(peerName, $"{peerName}:{peerPort}") },
            Nodes = new[]
            {
                new PNetMeshNodeIdentity { Name = name, PublicKey = GetComposePublicKey(name) },
                new PNetMeshNodeIdentity { Name = peerName, PublicKey = GetComposePublicKey(peerName) }
            }
        };
    }

    static PNetMeshTestNodeSpec DirectPeerNodeWithPsk(
        string name,
        string privateKey,
        int port,
        string peerName,
        int peerPort,
        string psk)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = psk,
            Port = port,
            ConnectDelaySeconds = 4,
            RunDurationSeconds = 10,
            ConnectNodes = new[] { peerName },
            PingNodes = new[] { peerName },
            Peers = new[] { StaticPeer(peerName, $"{peerName}:{peerPort}") },
            Nodes = new[]
            {
                new PNetMeshNodeIdentity { Name = name, PublicKey = GetComposePublicKey(name) },
                new PNetMeshNodeIdentity { Name = peerName, PublicKey = GetComposePublicKey(peerName) }
            }
        };
    }

    static PNetMeshTestNodeSpec RestartRecoveryUnrelatedNode(
        string name,
        string privateKey,
        int port,
        string peerName,
        int peerPort)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = 8,
            RunDurationSeconds = 60,
            ConnectNodes = new[] { peerName },
            PingNodes = new[] { peerName },
            Peers = new[] { StaticPeer(peerName, $"{peerName}:{peerPort}") },
            Nodes = NodeIdentities(name, peerName)
        };
    }

    static PNetMeshTestNodeSpec BootstrapDiscoveryEdgeNode(
        string name,
        string privateKey,
        int port,
        string discoveredPeerName,
        int connectDelaySeconds)
    {
        // The edge nodes intentionally seed only node00; the edge-to-edge peer must be learned through that open channel.
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = connectDelaySeconds,
            RunDurationSeconds = 20,
            ConnectNodes = new[] { "node00", discoveredPeerName },
            PingNodes = new[] { discoveredPeerName },
            Peers = new[] { StaticPeer("node00", "node00:12401") },
            Nodes = new[]
            {
                new PNetMeshNodeIdentity { Name = "node00", PublicKey = GetComposePublicKey("node00") },
                new PNetMeshNodeIdentity { Name = name, PublicKey = GetComposePublicKey(name) },
                new PNetMeshNodeIdentity { Name = discoveredPeerName, PublicKey = GetComposePublicKey(discoveredPeerName) }
            }
        };
    }

    static PNetMeshNodeIdentity[] NodeIdentities(params string[] nodeNames)
    {
        var nodes = new PNetMeshNodeIdentity[nodeNames.Length];

        for (var i = 0; i < nodeNames.Length; i++)
        {
            nodes[i] = new PNetMeshNodeIdentity
            {
                Name = nodeNames[i],
                PublicKey = GetComposePublicKey(nodeNames[i])
            };
        }

        return nodes;
    }

    static PNetMeshPeerEndpoint StaticPeer(string nodeName, params string[] endpoints)
    {
        return new PNetMeshPeerEndpoint
        {
            PublicKey = GetComposePublicKey(nodeName),
            Endpoints = endpoints
        };
    }

    static PNetMeshPeerEndpoint PeerEndpoint(string publicKey, params string[] endpoints)
    {
        return new PNetMeshPeerEndpoint
        {
            PublicKey = publicKey,
            Endpoints = endpoints
        };
    }

    static string GetComposePublicKey(string nodeName)
    {
        foreach (var node in ComposeNodes)
        {
            if (string.Equals(node.Name, nodeName, StringComparison.Ordinal))
                return node.PublicKey;
        }

        throw new InvalidOperationException($"Unknown compose node '{nodeName}'.");
    }

    static void AddIndexedValues(Dictionary<string, string> environment, string prefix, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            environment[$"{prefix}__{i}"] = values[i];
        }
    }
}

public sealed class PNetMeshPeerEndpoint
{
    public required string PublicKey { get; init; }

    public required IReadOnlyList<string> Endpoints { get; init; }
}

public sealed class PNetMeshNodeIdentity
{
    public required string Name { get; init; }

    public required string PublicKey { get; init; }
}
