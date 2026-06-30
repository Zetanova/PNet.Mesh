using System;
using System.Collections.Generic;
using System.Globalization;

namespace PNet.Actor.E2ETests.Mesh;

public sealed class PNetMeshTestNodeSpec
{
    const string ComposePsk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4=";

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

    public int Port { get; init; } = 12401;

    public string BindAddress { get; init; } = "0.0.0.0";

    public int ConnectDelaySeconds { get; init; }

    public int RunDurationSeconds { get; init; } = 120;

    public bool PingAllConnectedNodes { get; init; }

    public IReadOnlyList<string> ConnectNodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PingNodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PNetMeshPeerEndpoint> Peers { get; init; } = Array.Empty<PNetMeshPeerEndpoint>();

    public IReadOnlyList<PNetMeshNodeIdentity> Nodes { get; init; } = Array.Empty<PNetMeshNodeIdentity>();

    public IReadOnlyDictionary<string, string> ToEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NodeName"] = Name,
            ["PublicKey"] = PublicKey,
            ["PrivateKey"] = PrivateKey,
            ["Psk"] = Psk,
            ["BindTo__0"] = $"{BindAddress}:{Port}",
            ["ConnectDelaySeconds"] = ConnectDelaySeconds.ToString(CultureInfo.InvariantCulture),
            ["PingAllConnectedNodes"] = PingAllConnectedNodes.ToString().ToLowerInvariant(),
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

    public static IReadOnlyList<PNetMeshTestNodeSpec> DirectPeerTopology()
    {
        var node00 = DirectPeerNode("node00", "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=", 12401, "node01", 12402);
        var node01 = DirectPeerNode("node01", "3VXQslNLrlZMjjo6T+RJ77WKnynH+LT1ZOBs74kISOk=", 12402, "node00", 12401);

        return new[] { node00, node01 };
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

    static PNetMeshTestNodeSpec DirectPeerNode(string name, string privateKey, int port, string peerName, int peerPort)
    {
        return new PNetMeshTestNodeSpec
        {
            Name = name,
            PublicKey = GetComposePublicKey(name),
            PrivateKey = privateKey,
            Psk = ComposePsk,
            Port = port,
            ConnectDelaySeconds = 1,
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

    static PNetMeshPeerEndpoint StaticPeer(string nodeName, params string[] endpoints)
    {
        return new PNetMeshPeerEndpoint
        {
            PublicKey = GetComposePublicKey(nodeName),
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
