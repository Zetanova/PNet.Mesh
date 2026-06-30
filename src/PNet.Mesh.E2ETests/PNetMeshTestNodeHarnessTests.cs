using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace PNet.Actor.E2ETests.Mesh;

public sealed class PNetMeshTestNodeHarnessTests
{
    readonly ITestOutputHelper _output;

    public PNetMeshTestNodeHarnessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task starts_single_test_node_container_and_waits_for_readiness()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await harness.InitializeAsync(timeout.Token);

        var node = PNetMeshTestNodeSpec.StandaloneNode00();
        var container = await harness.StartNodeAsync(node, timeout.Token);
        var logs = await PNetMeshTestNodeHarness.GetLogsAsync(container, timeout.Token);

        _output.WriteLine(logs);
        Assert.Contains($"Node[{node.Name}] started", logs);
    }

    [Fact]
    public async Task failed_startup_exception_includes_node_logs()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await harness.InitializeAsync(timeout.Token);

        var node = PNetMeshTestNodeSpec.InvalidBindNode();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.StartNodeAsync(node, timeout.Token));

        Assert.Contains($"Test node '{node.Name}' failed to start.", ex.Message);
        Assert.Contains("Container logs:", ex.Message);
        Assert.Contains($"Node[{node.Name}] starting", ex.Message);
    }

    [Fact]
    public async Task direct_peers_exchange_payloads_in_both_directions()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await harness.InitializeAsync(timeout.Token);

        var containers = new Dictionary<string, IContainer>(StringComparer.Ordinal);
        var nodes = PNetMeshTestNodeSpec.DirectPeerTopology();
        var expectedLogsByNode = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["node00"] = new[]
            {
                "Node[node00] started",
                "ping from node01 to node00",
                "pong from node01 to node00",
                "node00 got 1 pongs"
            },
            ["node01"] = new[]
            {
                "Node[node01] started",
                "ping from node00 to node01",
                "pong from node00 to node01",
                "node01 got 1 pongs"
            }
        };

        foreach (var node in nodes)
        {
            containers[node.Name] = await harness.StartNodeAsync(node, timeout.Token);
        }

        var logsByNode = await WaitForTopologyLogsAsync(containers, expectedLogsByNode, timeout.Token);

        foreach (var entry in logsByNode.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"===== {entry.Key} =====");
            _output.WriteLine(entry.Value);
        }
    }

    [Fact]
    public async Task bootstrap_peer_discovery_exchanges_payloads_between_learned_peers()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await harness.InitializeAsync(timeout.Token);

        var containers = new Dictionary<string, IContainer>(StringComparer.Ordinal);
        var nodes = PNetMeshTestNodeSpec.BootstrapDiscoveryTopology();
        var expectedLogsByNode = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["node00"] = new[]
            {
                "Node[node00] started"
            },
            ["node01"] = new[]
            {
                "Node[node01] started",
                "ping from node10 to node01",
                "pong from node10 to node01",
                "node01 got 1 pongs"
            },
            ["node10"] = new[]
            {
                "Node[node10] started",
                "ping from node01 to node10",
                "pong from node01 to node10",
                "node10 got 1 pongs"
            }
        };

        foreach (var node in nodes)
        {
            containers[node.Name] = await harness.StartNodeAsync(node, timeout.Token);
        }

        var logsByNode = await WaitForTopologyLogsAsync(containers, expectedLogsByNode, timeout.Token);

        foreach (var entry in logsByNode.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"===== {entry.Key} =====");
            _output.WriteLine(entry.Value);
        }
    }

    [Fact]
    public async Task multi_hop_route_crosses_separated_container_segments()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));

        await harness.InitializeAsync(timeout.Token);

        var containers = new Dictionary<string, IContainer>(StringComparer.Ordinal);
        var nodes = PNetMeshTestNodeSpec.MultiHopRouteTopology();
        var expectedLogsByNode = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["node00"] = new[]
            {
                "Node[node00] started"
            },
            ["node01"] = new[]
            {
                "Node[node01] started",
                "ping from node10 to node01",
                "pong from node10 to node01",
                "node01 got 1 pongs"
            },
            ["node10"] = new[]
            {
                "Node[node10] started",
                "ping from node01 to node10",
                "pong from node01 to node10",
                "node10 got 1 pongs"
            },
            ["node20"] = new[]
            {
                "Node[node20] started"
            }
        };

        foreach (var node in nodes)
        {
            containers[node.Name] = await harness.StartNodeAsync(node, timeout.Token);
        }

        var logsByNode = await WaitForTopologyLogsAsync(containers, expectedLogsByNode, timeout.Token);

        foreach (var entry in logsByNode.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"===== {entry.Key} =====");
            _output.WriteLine(entry.Value);
        }
    }

    [Fact]
    public async Task six_node_topology_matches_compose_smoke_route_with_docker_dns_aliases()
    {
        await using var harness = new PNetMeshTestNodeHarness();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));

        await harness.InitializeAsync(timeout.Token);

        var containers = new Dictionary<string, IContainer>(StringComparer.Ordinal);
        var nodes = PNetMeshTestNodeSpec.ComposeSmokeTopologyOnSingleDockerNetwork();
        var expectedLogsByNode = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["node00"] = new[]
            {
                "Node[node00] started",
                "ping from node01 to node00",
                "ping from node10 to node00",
                "ping from node11 to node00",
                "ping from node20 to node00",
                "node00 got 0 pongs"
            },
            ["node01"] = new[] { "Node[node01] started", "node01 got " },
            ["node10"] = new[] { "Node[node10] started", "node10 got " },
            ["node11"] = new[] { "Node[node11] started", "node11 got " },
            ["node20"] = new[]
            {
                "Node[node20] started",
                "pong from node00 to node20",
                "ping from node21 to node20",
                "node20 got 1 pongs"
            },
            ["node21"] = new[] { "Node[node21] started", "pong from node20 to node21", "node21 got 1 pongs" }
        };

        foreach (var node in nodes)
        {
            containers[node.Name] = await harness.StartNodeAsync(node, timeout.Token);
        }

        var logsByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            logsByNode[node.Name] = await PNetMeshTestNodeHarness.WaitForLogsAsync(
                containers[node.Name],
                expectedLogsByNode[node.Name],
                timeout.Token);
        }

        foreach (var entry in logsByNode.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"===== {entry.Key} =====");
            _output.WriteLine(entry.Value);
        }

        foreach (var node in nodes)
        {
            foreach (var expectedLog in expectedLogsByNode[node.Name])
            {
                Assert.Contains(expectedLog, logsByNode[node.Name]);
            }
        }
    }

    static async Task<Dictionary<string, string>> WaitForTopologyLogsAsync(
        IReadOnlyDictionary<string, IContainer> containers,
        IReadOnlyDictionary<string, string[]> expectedLogsByNode,
        CancellationToken cancellationToken)
    {
        var logsByNode = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var entry in expectedLogsByNode)
            {
                logsByNode[entry.Key] = await PNetMeshTestNodeHarness.WaitForLogsAsync(
                    containers[entry.Key],
                    entry.Value,
                    cancellationToken);
            }

            return logsByNode;
        }
        catch (Exception ex)
        {
            foreach (var entry in containers)
            {
                logsByNode[entry.Key] = await GetLogsForFailureAsync(entry.Value);
            }

            var message = string.Join(
                Environment.NewLine,
                logsByNode
                    .OrderBy(n => n.Key, StringComparer.Ordinal)
                    .Select(n => $"===== {n.Key} ====={Environment.NewLine}{n.Value}"));

            throw new InvalidOperationException($"Topology did not emit expected logs. All container logs:{Environment.NewLine}{message}", ex);
        }
    }

    static async Task<string> GetLogsForFailureAsync(IContainer container)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await PNetMeshTestNodeHarness.GetLogsAsync(container, timeout.Token);
    }
}
