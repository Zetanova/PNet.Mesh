using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    const string DefaultNetworkName = "default";
    static readonly string SharedImageName = $"localhost/pnet-mesh-test-node:{Guid.NewGuid():N}";
    static readonly SemaphoreSlim SharedImageLock = new SemaphoreSlim(1, 1);
    static Task<IFutureDockerImage> SharedImageTask;

    readonly string _runId = Guid.NewGuid().ToString("N");
    readonly List<IContainer> _containers = new List<IContainer>();
    readonly Dictionary<string, INetwork> _networks = new Dictionary<string, INetwork>(StringComparer.Ordinal);

    IFutureDockerImage _image;

    internal string TestNodeImageName => SharedImageName;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _image = await GetOrCreateSharedImageAsync(cancellationToken);
        await GetOrCreateNetworkAsync(DefaultNetworkName, cancellationToken);
    }

    public async Task<IContainer> StartNodeAsync(PNetMeshTestNodeSpec node, CancellationToken cancellationToken)
    {
        if (_image is null)
            throw new InvalidOperationException("Harness is not initialized.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NodeStartupTimeout);

        var builder = new ContainerBuilder(_image)
            .WithName($"pnet-mesh-{node.Name}-{_runId}")
            .WithCleanUp(true)
            .WithNetworkAliases(node.Name)
            .WithEnvironment(node.ToEnvironment())
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new HostConfig();
                parameters.HostConfig.Privileged = false;
                parameters.HostConfig.CapDrop = (parameters.HostConfig.CapDrop ?? Array.Empty<string>())
                    .Concat(new[] { "NET_ADMIN", "NET_RAW" })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

        builder = node.PublishUdpPort
            ? builder.WithPortBinding($"{node.Port}/udp", true)
            : builder.WithExposedPort($"{node.Port}/udp");

        var networkNames = node.NetworkNames.Count > 0
            ? node.NetworkNames
            : new[] { DefaultNetworkName };

        foreach (var networkName in networkNames)
        {
            var network = await GetOrCreateNetworkAsync(networkName, cancellationToken);
            builder = builder.WithNetwork(network);
        }

        var container = builder.Build();

        _containers.Add(container);

        try
        {
            await container.StartAsync(timeout.Token);
            await WaitForLogsAsync(container, new[] { node.ReadyLogEntry }, timeout.Token);
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
        return await GetLogsAsync(container, DateTime.UtcNow.AddMinutes(-10), cancellationToken);
    }

    public static async Task<string> GetLogsAsync(IContainer container, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var logs = await container.GetLogsAsync(sinceUtc, DateTime.UtcNow.AddMinutes(1), true, cancellationToken);
        return string.Concat(logs.Stdout, logs.Stderr);
    }

    public static async Task<string> WaitForLogsAsync(IContainer container, IReadOnlyCollection<string> expectedLogEntries, CancellationToken cancellationToken)
    {
        return await WaitForLogsAsync(container, DateTime.UtcNow.AddMinutes(-10), expectedLogEntries, cancellationToken);
    }

    public static async Task<string> WaitForLogsAsync(IContainer container, DateTime sinceUtc, IReadOnlyCollection<string> expectedLogEntries, CancellationToken cancellationToken)
    {
        while (true)
        {
            var logs = await TryGetLogsAsync(container, sinceUtc, cancellationToken);
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
                logs = await TryGetLogsAsync(container, sinceUtc, DiagnosticLogTimeout);
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

        foreach (var network in _networks.Values)
        {
            await TryDisposeAsync(network, "network", cleanupErrors);
        }

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

    static async Task<string> TryGetLogsAsync(IContainer container, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        try
        {
            return await GetLogsAsync(container, sinceUtc, cancellationToken);
        }
        catch (Exception logException)
        {
            return $"<log collection failed: {logException.Message}>";
        }
    }

    async Task<INetwork> GetOrCreateNetworkAsync(string networkName, CancellationToken cancellationToken)
    {
        if (_networks.TryGetValue(networkName, out var network))
            return network;

        using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        networkTimeout.CancelAfter(NetworkTimeout);

        network = new NetworkBuilder()
            .WithName($"pnet-mesh-e2e-{networkName}-{_runId}")
            .WithCleanUp(true)
            .Build();

        await network.CreateAsync(networkTimeout.Token);

        _networks.Add(networkName, network);
        return network;
    }

    static async Task<string> TryGetLogsAsync(IContainer container, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await TryGetLogsAsync(container, cancellation.Token);
    }

    static async Task<string> TryGetLogsAsync(IContainer container, DateTime sinceUtc, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await TryGetLogsAsync(container, sinceUtc, cancellation.Token);
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

    static async Task<IFutureDockerImage> GetOrCreateSharedImageAsync(CancellationToken cancellationToken)
    {
        Task<IFutureDockerImage> imageTask;
        await SharedImageLock.WaitAsync(cancellationToken);
        try
        {
            SharedImageTask ??= CreateSharedImageAsync(cancellationToken);
            imageTask = SharedImageTask;
        }
        finally
        {
            SharedImageLock.Release();
        }

        try
        {
            return await imageTask.WaitAsync(cancellationToken);
        }
        catch
        {
            await ClearFailedSharedImageTaskAsync(imageTask);
            throw;
        }
    }

    static async Task<IFutureDockerImage> CreateSharedImageAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ImageBuildTimeout);

        var repositoryRoot = FindRepositoryRoot();
        var image = new ImageFromDockerfileBuilder()
            .WithName(SharedImageName)
            .WithContextDirectory(repositoryRoot)
            .WithDockerfileDirectory(repositoryRoot)
            .WithDockerfile("src/PNet.Mesh.TestNode/Dockerfile")
            .WithCleanUp(true)
            .Build();

        await image.CreateAsync(timeout.Token);
        return image;
    }

    static async Task ClearFailedSharedImageTaskAsync(Task<IFutureDockerImage> imageTask)
    {
        if (!imageTask.IsCanceled && !imageTask.IsFaulted)
            return;

        await SharedImageLock.WaitAsync();
        try
        {
            if (ReferenceEquals(SharedImageTask, imageTask))
                SharedImageTask = null;
        }
        finally
        {
            SharedImageLock.Release();
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
