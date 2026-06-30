using System;
using System.Threading;
using System.Threading.Tasks;
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
}
