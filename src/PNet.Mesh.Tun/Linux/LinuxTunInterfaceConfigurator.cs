using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PNet.Mesh.Tun.Linux
{
    public static class LinuxTunInterfaceConfigurator
    {
        static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(15);

        public static async Task ConfigureAsync(
            LinuxTunInterfaceConfiguration configuration,
            TimeSpan? commandTimeout = null,
            CancellationToken cancellationToken = default)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(configuration.InterfaceName))
                throw new ArgumentException("Interface name is required.", nameof(configuration));
            if (configuration.Mtu <= 0)
                throw new ArgumentOutOfRangeException(nameof(configuration.Mtu));

            var timeout = commandTimeout ?? DefaultCommandTimeout;

            foreach (var address in configuration.Addresses)
            {
                var arguments = address.Address.AddressFamily == AddressFamily.InterNetworkV6
                    ? new[] { "addr", "replace", address.ToString(), "dev", configuration.InterfaceName, "nodad" }
                    : new[] { "addr", "replace", address.ToString(), "dev", configuration.InterfaceName };

                await RunIpAsync(arguments, timeout, cancellationToken);
            }

            await RunIpAsync(new[] { "link", "set", "dev", configuration.InterfaceName, "mtu", configuration.Mtu.ToString() }, timeout, cancellationToken);
            await RunIpAsync(new[] { "link", "set", "dev", configuration.InterfaceName, "up" }, timeout, cancellationToken);

            foreach (var route in configuration.Routes)
            {
                var arguments = route.Address.AddressFamily == AddressFamily.InterNetworkV6
                    ? new[] { "-6", "route", "replace", route.ToString(), "dev", configuration.InterfaceName }
                    : new[] { "route", "replace", route.ToString(), "dev", configuration.InterfaceName };

                await RunIpAsync(arguments, timeout, cancellationToken);
            }
        }

        static async Task RunIpAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            var startInfo = new ProcessStartInfo("ip")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new IOException("Failed to start ip command.");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutTask = ReadToEndAsync(process.StandardOutput, stdout, timeoutSource.Token);
            var stderrTask = ReadToEndAsync(process.StandardError, stderr, timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"ip {string.Join(" ", arguments)} timed out after {timeout}.", ex);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ip {string.Join(" ", arguments)} failed with exit code {process.ExitCode}: {stderr}");
            }
        }

        static async Task ReadToEndAsync(StreamReader reader, StringBuilder output, CancellationToken cancellationToken)
        {
            var buffer = new char[1024];
            while (true)
            {
                var read = await reader.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;

                output.Append(buffer, 0, read);
            }
        }

        static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
