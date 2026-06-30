using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Actor.E2ETests.Mesh;

public sealed class PNetMeshTestNodeHarness : IAsyncDisposable
{
    static readonly TimeSpan ImageBuildTimeout = TimeSpan.FromMinutes(4);
    static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan NodeStartupTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DiagnosticLogTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    readonly string _runId = Guid.NewGuid().ToString("N");
    readonly List<IContainer> _containers = new List<IContainer>();

    IFutureDockerImage _image;
    INetwork _network;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ImageBuildTimeout);

        var repositoryRoot = FindRepositoryRoot();
        _image = new ImageFromDockerfileBuilder()
            .WithName($"localhost/pnet-mesh-test-node:{_runId}")
            .WithContextDirectory(repositoryRoot)
            .WithDockerfileDirectory(repositoryRoot)
            .WithDockerfile("src/PNet.Mesh.TestNode/Dockerfile")
            .WithCleanUp(true)
            .Build();

        await _image.CreateAsync(timeout.Token);

        using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        networkTimeout.CancelAfter(NetworkTimeout);

        _network = new NetworkBuilder()
            .WithName($"pnet-mesh-e2e-{_runId}")
            .WithCleanUp(true)
            .Build();

        await _network.CreateAsync(networkTimeout.Token);
    }

    public async Task<IContainer> StartNodeAsync(PNetMeshTestNodeSpec node, CancellationToken cancellationToken)
    {
        if (_image is null || _network is null)
            throw new InvalidOperationException("Harness is not initialized.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NodeStartupTimeout);

        var container = new ContainerBuilder(_image)
            .WithName($"pnet-mesh-{node.Name}-{_runId}")
            .WithCleanUp(true)
            .WithNetwork(_network)
            .WithNetworkAliases(node.Name)
            .WithExposedPort($"{node.Port}/udp")
            .WithEnvironment(node.ToEnvironment())
            .Build();

        _containers.Add(container);

        try
        {
            await container.StartAsync(timeout.Token);
            await WaitForLogsAsync(container, new[] { $"Node[{node.Name}] started" }, timeout.Token);
            return container;
        }
        catch (Exception ex)
        {
            var logs = await TryGetLogsAsync(container, DiagnosticLogTimeout);
            throw new InvalidOperationException($"Test node '{node.Name}' failed to start. Container logs:{Environment.NewLine}{logs}", ex);
        }
    }

    public static async Task<string> GetLogsAsync(IContainer container, CancellationToken cancellationToken)
    {
        var logs = await container.GetLogsAsync(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(1), true, cancellationToken);
        return string.Concat(logs.Stdout, logs.Stderr);
    }

    public static async Task<string> WaitForLogsAsync(IContainer container, IReadOnlyCollection<string> expectedLogEntries, CancellationToken cancellationToken)
    {
        while (true)
        {
            var logs = await TryGetLogsAsync(container, cancellationToken);
            var missing = GetMissingLogEntries(logs, expectedLogEntries);

            if (missing.Count == 0)
                return logs;

            if (container.State is TestcontainersStates.Exited or TestcontainersStates.Dead)
                throw CreateMissingLogsException(container, missing, logs, null);

            if (logs.Contains("Application is shutting down...", StringComparison.Ordinal))
                throw CreateMissingLogsException(container, missing, logs, null);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                logs = await TryGetLogsAsync(container, DiagnosticLogTimeout);
                missing = GetMissingLogEntries(logs, expectedLogEntries);
                throw CreateMissingLogsException(container, missing, logs, ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cleanupErrors = new List<Exception>();

        foreach (var container in _containers)
        {
            await TryDisposeAsync(container, "container", cleanupErrors);
        }

        if (_network is not null)
            await TryDisposeAsync(_network, "network", cleanupErrors);

        if (_image is not null)
            await TryDeleteImageAsync(_image, cleanupErrors);

        if (cleanupErrors.Count == 1)
            throw cleanupErrors[0];

        if (cleanupErrors.Count > 1)
            throw new AggregateException("One or more Testcontainers resources failed to clean up.", cleanupErrors);
    }

    static async Task<string> TryGetLogsAsync(IContainer container, CancellationToken cancellationToken)
    {
        try
        {
            return await GetLogsAsync(container, cancellationToken);
        }
        catch (Exception logException)
        {
            return $"<log collection failed: {logException.Message}>";
        }
    }

    static async Task<string> TryGetLogsAsync(IContainer container, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await TryGetLogsAsync(container, cancellation.Token);
    }

    static List<string> GetMissingLogEntries(string logs, IReadOnlyCollection<string> expectedLogEntries)
    {
        var missing = new List<string>();

        foreach (var expectedLogEntry in expectedLogEntries)
        {
            if (!logs.Contains(expectedLogEntry, StringComparison.Ordinal))
                missing.Add(expectedLogEntry);
        }

        return missing;
    }

    static InvalidOperationException CreateMissingLogsException(IContainer container, IReadOnlyCollection<string> missing, string logs, Exception innerException)
    {
        var message = $"Container '{container.Name}' did not emit expected logs: {string.Join(", ", missing)}.{Environment.NewLine}Container logs:{Environment.NewLine}{logs}";
        return new InvalidOperationException(message, innerException);
    }

    static async Task TryDisposeAsync(IAsyncDisposable resource, string resourceName, List<Exception> cleanupErrors)
    {
        try
        {
            await resource.DisposeAsync().AsTask().WaitAsync(CleanupTimeout);
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(new InvalidOperationException($"Failed to clean up Testcontainers {resourceName}.", ex));
        }
    }

    static async Task TryDeleteImageAsync(IFutureDockerImage image, List<Exception> cleanupErrors)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupTimeout);
            await image.DeleteAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(new InvalidOperationException("Failed to delete generated Testcontainers image.", ex));
        }
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PNet.Mesh.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing PNet.Mesh.sln.");
    }
}
