using PNet.Mesh.Benchmarks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh.Tun
{
    public sealed class TunPNetBenchmarkRunnerTests
    {
        [Fact]
        public void parse_ping_result_extracts_packet_loss_and_latency()
        {
            var command = new TunTopologyCommandRecord(
                "docker",
                Array.Empty<string>(),
                0,
                """
                1 packets transmitted, 1 received, 0% packet loss, time 0ms
                rtt min/avg/max/mdev = 0.042/0.042/0.042/0.000 ms
                """,
                string.Empty,
                false);

            var result = TunPNetBenchmarkRunner.ParsePingResult("ipv4", "left", "right", "10.80.0.2", command);

            Assert.Equal("ping", result.Tool);
            Assert.Equal("ipv4", result.Protocol);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(1, result.PacketsTransmitted);
            Assert.Equal(1, result.PacketsReceived);
            Assert.Equal(0, result.PacketLossPercent);
            Assert.Equal(0.042, result.AverageLatencyMilliseconds);
        }

        [Fact]
        public void parse_iperf_result_extracts_received_bandwidth()
        {
            var command = new TunTopologyCommandRecord(
                "docker",
                Array.Empty<string>(),
                0,
                """
                {
                  "end": {
                    "sum_received": {
                      "seconds": 3.0001,
                      "bytes": 123456,
                      "bits_per_second": 329205.69
                    }
                  }
                }
                """,
                string.Empty,
                false);

            var result = TunPNetBenchmarkRunner.ParseIperfResult("ipv6", "left", "right", "fd80::2", command);

            Assert.Equal("iperf3", result.Tool);
            Assert.Equal("ipv6", result.Protocol);
            Assert.Equal(3.0001, result.Seconds);
            Assert.Equal(123456, result.Bytes);
            Assert.Equal(329205.69, result.BitsPerSecond);
        }

        [Fact]
        public void options_accept_supported_tun_benchmark_scenarios()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun" },
                TextWriter.Null,
                out var pnetOptions));
            Assert.Equal("pnet-mesh-tun", pnetOptions.Scenario);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "wireguard-go" },
                TextWriter.Null,
                out var wireGuardOptions));
            Assert.Equal("wireguard-go", wireGuardOptions.Scenario);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--mtu", "1420", "--payload-mode", "mtu" },
                TextWriter.Null,
                out var mtuOptions));
            Assert.Equal(1420, mtuOptions.Mtu);
            Assert.Equal("mtu", mtuOptions.PayloadMode);
            Assert.Equal("64K", mtuOptions.IperfBandwidth);
            Assert.Equal(1340, mtuOptions.IperfDatagramBytes);
        }

        [Theory]
        [InlineData("--mtu", "0", "--mtu requires a positive integer.")]
        [InlineData("--mtu", "not-a-number", "--mtu requires a positive integer.")]
        [InlineData("--payload-mode", "bulk", "--payload-mode requires one of: control, mtu.")]
        public void options_reject_invalid_tun_benchmark_payload_options(string option, string value, string expectedError)
        {
            var error = new StringWriter();

            var parsed = TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", option, value },
                error,
                out _);

            Assert.False(parsed);
            Assert.Contains(expectedError, error.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void run_benchmark_keeps_generated_keys_out_of_command_lines_and_report_commands()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            var actualShellCommands = runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 6
                               && call.Arguments[0] == "exec"
                               && call.Arguments[1] == "-d"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("PNet.Mesh.Tun.Cli.dll", StringComparison.Ordinal))
                .Select(call => call.Arguments[^1])
                .ToArray();
            Assert.Equal(2, actualShellCommands.Length);
            Assert.All(actualShellCommands, shellCommand =>
            {
                Assert.Contains("'--public-key-file'", shellCommand);
                Assert.Contains("'--private-key-file'", shellCommand);
                Assert.Contains("'--psk-file'", shellCommand);
                Assert.DoesNotContain("'--public-key'", shellCommand);
                Assert.DoesNotContain("'--private-key'", shellCommand);
                Assert.DoesNotContain("'--psk'", shellCommand);
            });

            var reportedShellCommands = report.Commands
                .Where(command => command.FileName == "docker"
                                  && command.Arguments.Count >= 6
                                  && command.Arguments[0] == "exec"
                                  && command.Arguments[1] == "-d"
                                  && command.Arguments[^2] == "-c"
                                  && command.Arguments[^1].Contains("PNet.Mesh.Tun.Cli.dll", StringComparison.Ordinal))
                .Select(command => command.Arguments[^1])
                .ToArray();
            Assert.Equal(2, reportedShellCommands.Length);
            Assert.All(reportedShellCommands, shellCommand =>
            {
                Assert.Contains("<container-secret>", shellCommand);
                Assert.Contains("<redacted>", shellCommand);
            });

            Assert.True(runner.CopiedSensitiveContents.Count >= 4);
            var actualArguments = string.Join('\n', runner.Calls.SelectMany(command => command.Arguments));
            var reportedArguments = string.Join('\n', report.Commands.SelectMany(command => command.Arguments));
            foreach (var secret in runner.CopiedSensitiveContents)
            {
                Assert.DoesNotContain(secret, actualArguments);
                Assert.DoesNotContain(secret, reportedArguments);
            }

            Assert.DoesNotContain(actualShellCommands[0], reportedArguments);
        }

        [Fact]
        public void run_wireguard_go_benchmark_records_version_config_and_redacts_secrets()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "wireguard-go", "--warmup", "0ms", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            Assert.Equal("wireguard-go", report.Scenario);
            Assert.Equal("wireguard-go", report.Implementation.Name);
            Assert.Equal("0.0.20230223-1", report.Implementation.Version);
            Assert.Equal("/usr/bin/wireguard-go", report.Implementation.ExecutablePath);
            Assert.Equal("dpkg-query wireguard-go", report.Implementation.VersionSource);
            Assert.Null(report.Implementation.VersionUnavailableReason);
            Assert.False(report.ManagedCountersAvailable);
            Assert.Contains("wireguard-go is not a .NET process", report.ManagedCounterUnavailableReason);

            var actualConfigCommands = runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 5
                               && call.Arguments[0] == "exec"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("wg", StringComparison.Ordinal)
                               && call.Arguments[^1].Contains("set", StringComparison.Ordinal))
                .Select(call => call.Arguments[^1])
                .ToArray();
            Assert.Equal(2, actualConfigCommands.Length);
            Assert.Contains(actualConfigCommands, shellCommand =>
                shellCommand.Contains("'listen-port' '51820'", StringComparison.Ordinal)
                && shellCommand.Contains("'endpoint' 'right:51821'", StringComparison.Ordinal)
                && shellCommand.Contains("'allowed-ips' '10.80.0.2/32,fd80::2/128'", StringComparison.Ordinal));
            Assert.Contains(actualConfigCommands, shellCommand =>
                shellCommand.Contains("'listen-port' '51821'", StringComparison.Ordinal)
                && shellCommand.Contains("'endpoint' 'left:51820'", StringComparison.Ordinal)
                && shellCommand.Contains("'allowed-ips' '10.80.0.1/32,fd80::1/128'", StringComparison.Ordinal));
            Assert.All(actualConfigCommands, shellCommand =>
            {
                Assert.StartsWith("set -e; ", shellCommand, StringComparison.Ordinal);
                var linkUpIndex = shellCommand.IndexOf("ip link set dev 'pnet0' up", StringComparison.Ordinal);
                Assert.True(linkUpIndex >= 0);
                var ipv4RouteIndex = shellCommand.IndexOf("ip route replace", StringComparison.Ordinal);
                var ipv6RouteIndex = shellCommand.IndexOf("ip -6 route replace", StringComparison.Ordinal);
                Assert.True(ipv4RouteIndex > linkUpIndex);
                Assert.True(ipv6RouteIndex > linkUpIndex);
            });

            var reportedArguments = string.Join('\n', report.Commands.SelectMany(command => command.Arguments));
            Assert.Contains("wireguard-go pnet0", reportedArguments);
            Assert.Contains("wg set pnet0 private-key <container-secret>", reportedArguments);
            Assert.Contains("listen-port 51820", reportedArguments);
            Assert.Contains("endpoint right:51821", reportedArguments);
            Assert.Contains("allowed-ips 10.80.0.2/32,fd80::2/128", reportedArguments);
            Assert.Contains("<peer-public-key>", reportedArguments);
            var reportedConfigCommands = report.Commands
                .Where(command => command.Arguments.Count >= 5
                                  && command.Arguments[0] == "exec"
                                  && command.Arguments[^2] == "-c"
                                  && command.Arguments[^1].Contains("wg set pnet0", StringComparison.Ordinal))
                .Select(command => command.Arguments[^1])
                .ToArray();
            Assert.Equal(2, reportedConfigCommands.Length);
            Assert.All(reportedConfigCommands, shellCommand =>
            {
                Assert.StartsWith("set -e; ", shellCommand, StringComparison.Ordinal);
                var linkUpIndex = shellCommand.IndexOf("ip link set dev pnet0 up", StringComparison.Ordinal);
                Assert.True(linkUpIndex >= 0);
                var ipv4RouteIndex = shellCommand.IndexOf("ip route replace", StringComparison.Ordinal);
                var ipv6RouteIndex = shellCommand.IndexOf("ip -6 route replace", StringComparison.Ordinal);
                Assert.True(ipv4RouteIndex > linkUpIndex);
                Assert.True(ipv6RouteIndex > linkUpIndex);
            });
            Assert.Contains(runner.Calls, call =>
                call.Arguments.SequenceEqual(new[] { "exec", "pnet-tun-bench-left", "pgrep", "-f", "(^|/)wireguard-go .*pnet0($| )" }));
            Assert.Contains(report.Commands, command =>
                command.Arguments.SequenceEqual(new[] { "exec", "pnet-tun-bench-left", "sh", "-c", "tail -n 120 /tmp/wireguard-go.log 2>/dev/null || true" }));
            Assert.DoesNotContain(report.Commands, command =>
                command.Arguments.SequenceEqual(new[] { "exec", "pnet-tun-bench-left", "sh", "-c", "tail -n 120 /tmp/pnet-tun.log 2>/dev/null || true" }));

            var actualArguments = string.Join('\n', runner.Calls.SelectMany(command => command.Arguments));
            foreach (var secret in runner.CopiedSensitiveContents)
            {
                Assert.DoesNotContain(secret, actualArguments);
                Assert.DoesNotContain(secret, reportedArguments);
            }
        }

        [Fact]
        public void run_benchmark_fails_when_final_ping_has_partial_packet_loss()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { PartialFinalIpv4Ping = true };

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("fail", report.Status);
            var ipv4Ping = Assert.Single(report.Traffic, result => result.Tool == "ping" && result.Protocol == "ipv4");
            Assert.Equal(2, ipv4Ping.PacketsTransmitted);
            Assert.Equal(1, ipv4Ping.PacketsReceived);
            Assert.Equal(50, ipv4Ping.PacketLossPercent);
        }

        sealed class FakeCommandRunner : ITunTopologyCommandRunner
        {
            bool _networkCreated;

            public List<FakeCommandCall> Calls { get; } = new();

            public List<string> CopiedSensitiveContents { get; } = new();

            public bool PartialFinalIpv4Ping { get; init; }

            public bool FileExists(string path)
            {
                return string.Equals(path, "/dev/net/tun", StringComparison.Ordinal);
            }

            public TunTopologyCommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
            {
                Calls.Add(new FakeCommandCall(fileName, arguments.ToArray()));
                if (fileName != "docker")
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, string.Empty, string.Empty, false);

                if (arguments.SequenceEqual(new[] { "version", "--format", "{{.Server.Version}}" }))
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "27.0.0\n", string.Empty, false);

                if (arguments.Count >= 2 && arguments[0] == "image" && arguments[1] == "inspect")
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "sha256:test\n", string.Empty, false);

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "sh"
                    && arguments[^1].Contains("dpkg-query -W", StringComparison.Ordinal)
                    && arguments[^1].Contains("wireguard-go", StringComparison.Ordinal))
                {
                    return new TunTopologyCommandResult(
                        fileName,
                        arguments.ToArray(),
                        0,
                        "path=/usr/bin/wireguard-go\nversion=0.0.20230223-1\nsource=dpkg-query wireguard-go\n",
                        string.Empty,
                        false);
                }

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "sh"
                    && arguments[^1].Contains("wg genkey", StringComparison.Ordinal))
                {
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, string.Empty, string.Empty, false);
                }

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "cat"
                    && arguments[1].Contains("left", StringComparison.Ordinal))
                {
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "BVgqSt6+IyxeVh2OacdS+42af3w58QF2ehTAZ31nnm8=\n", string.Empty, false);
                }

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "cat"
                    && arguments[1].Contains("right", StringComparison.Ordinal))
                {
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "cs3lkYSvSryeLBG/D6WRsNvhtv2UW1tQoO59I/MLv3Y=\n", string.Empty, false);
                }

                if (arguments.Count >= 3 && arguments[0] == "cp" && !arguments[2].EndsWith("/public.key", StringComparison.Ordinal))
                    CopiedSensitiveContents.Add(File.ReadAllText(arguments[1]));

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "inspect")
                {
                    var exitCode = _networkCreated ? 0 : 1;
                    var stdout = _networkCreated ? "pnet-tun-bench\n" : string.Empty;
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), exitCode, stdout, string.Empty, false);
                }

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "create")
                    _networkCreated = true;

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "rm")
                    _networkCreated = false;

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "sh" && arguments[^1].Contains("/proc/$pid/status", StringComparison.Ordinal))
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "pid=42\nVmRSS:\t1024 kB\nVmHWM:\t2048 kB\nThreads:\t8\nutime_ticks=10\nstime_ticks=5\n", string.Empty, false);

                if (arguments.Count >= 4 && arguments[0] == "exec" && arguments[2] == "sh" && arguments[^1].Contains("ss ", StringComparison.Ordinal) && arguments[^1].Contains("-H -ltn", StringComparison.Ordinal))
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "LISTEN 0 4096 10.80.0.2:5201 0.0.0.0:*\n", string.Empty, false);

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "ping")
                {
                    var countIndex = arguments.ToList().IndexOf("-c");
                    var isFinalIpv4Ping = !arguments.Contains("-6")
                                          && countIndex >= 0
                                          && countIndex + 1 < arguments.Count
                                          && arguments[countIndex + 1] == "1";

                    var stdout = PartialFinalIpv4Ping && isFinalIpv4Ping
                        ? "2 packets transmitted, 1 received, 50% packet loss, time 1000ms\nrtt min/avg/max/mdev = 0.100/0.100/0.100/0.000 ms\n"
                        : "1 packets transmitted, 1 received, 0% packet loss, time 0ms\nrtt min/avg/max/mdev = 0.042/0.042/0.042/0.000 ms\n";
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, stdout, string.Empty, false);
                }

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "iperf3")
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "{\"end\":{\"sum_received\":{\"seconds\":1,\"bytes\":128,\"bits_per_second\":1024}}}", string.Empty, false);

                return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, string.Empty, string.Empty, false);
            }
        }

        sealed record FakeCommandCall(string FileName, IReadOnlyList<string> Arguments);
    }
}
