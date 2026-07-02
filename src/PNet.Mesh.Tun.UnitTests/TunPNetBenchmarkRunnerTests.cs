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

        sealed class FakeCommandRunner : ITunTopologyCommandRunner
        {
            bool _networkCreated;

            public List<FakeCommandCall> Calls { get; } = new();

            public List<string> CopiedSensitiveContents { get; } = new();

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

                return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, string.Empty, string.Empty, false);
            }
        }

        sealed record FakeCommandCall(string FileName, IReadOnlyList<string> Arguments);
    }
}
