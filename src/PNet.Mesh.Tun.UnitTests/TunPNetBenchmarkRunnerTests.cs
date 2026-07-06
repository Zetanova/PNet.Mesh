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
                      "bits_per_second": 329205.69,
                      "lost_percent": 0
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
            Assert.Equal(0, result.PacketLossPercent);
        }

        [Fact]
        public void parse_udp_counter_monitor_result_extracts_global_and_socket_drop_deltas()
        {
            var result = TunPNetBenchmarkRunner.ParseUdpCounterMonitorResult(
                "right",
                """
                sample index=0 unix_ms=1000
                snmp protocol=udp name=InErrors value=10
                snmp protocol=udp name=RcvbufErrors value=2
                snmp protocol=udp6 name=InErrors value=20
                snmp protocol=udp6 name=RcvbufErrors value=3
                socket family=udp local=00000000:3072 remote=00000000:0000 state=07 queues=00000000:00000000 inode=100 drops=4
                socket family=udp local=0200500A:1451 remote=00000000:0000 state=07 queues=00000000:00000000 inode=200 drops=5
                end_sample
                sample index=1 unix_ms=1050
                snmp protocol=udp name=InErrors value=17
                snmp protocol=udp name=RcvbufErrors value=9
                snmp protocol=udp6 name=InErrors value=20
                snmp protocol=udp6 name=RcvbufErrors value=3
                socket family=udp local=00000000:3072 remote=00000000:0000 state=07 queues=00000000:00000000 inode=100 drops=11
                socket family=udp local=0200500A:1451 remote=00000000:0000 state=07 queues=00000000:00000000 inode=200 drops=5
                end_sample
                """,
                5201);

            Assert.Equal("right", result.Node);
            Assert.Equal(7, result.GlobalDelta.UdpInErrors);
            Assert.Equal(7, result.GlobalDelta.UdpRcvbufErrors);
            Assert.Equal(0, result.GlobalDelta.Udp6InErrors);
            Assert.Equal(0, result.GlobalDelta.Udp6RcvbufErrors);

            var pnet = Assert.Single(result.Sockets, socket => socket.Role == "pnet");
            Assert.Equal(12402, pnet.LocalPort);
            Assert.Equal(7, pnet.DropsDelta);
            Assert.True(pnet.PresentInFirstSample);
            Assert.True(pnet.PresentInLastSample);

            var iperf = Assert.Single(result.Sockets, socket => socket.Role == "iperf3");
            Assert.Equal(5201, iperf.LocalPort);
            Assert.Equal(0, iperf.DropsDelta);
        }

        [Fact]
        public void parse_managed_runtime_snapshot_extracts_gc_metrics()
        {
            var parsed = TunPNetBenchmarkRunner.TryParseManagedRuntimeSnapshot(
                "pnet_gc_metrics allocated_bytes=123456 managed_heap_bytes=65536 fragmented_bytes=1024 memory_load_bytes=4096 total_available_memory_bytes=1073741824 high_memory_load_threshold_bytes=966367641 gen0_collections=3 gen1_collections=2 gen2_collections=1",
                out var snapshot);

            Assert.True(parsed);
            Assert.Equal(123456, snapshot.AllocationBytes);
            Assert.Equal(65536, snapshot.ManagedHeapBytes);
            Assert.Equal(1024, snapshot.FragmentedBytes);
            Assert.Equal(3, snapshot.Gen0Collections);
        }

        [Fact]
        public void run_benchmark_fails_when_iperf_reports_loss_or_short_fixed_transfer()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--iperf-bytes", "104857600", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var lossReport = TunPNetBenchmarkRunner.RunBenchmark(
                options,
                new FakeCommandRunner { IperfPacketLossPercent = 0.5 });
            var shortTransferReport = TunPNetBenchmarkRunner.RunBenchmark(
                options,
                new FakeCommandRunner { IperfBytesDelta = -1 });

            Assert.Equal("fail", lossReport.Status);
            Assert.Equal("fail", shortTransferReport.Status);
            Assert.Contains(lossReport.Traffic, traffic => traffic.Tool == "iperf3" && traffic.PacketLossPercent == 0.5);
            Assert.Contains(shortTransferReport.Traffic, traffic => traffic.Tool == "iperf3" && traffic.Bytes == 104857599);
        }

        [Fact]
        public void options_accept_supported_tun_benchmark_scenarios()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun" },
                TextWriter.Null,
                out var pnetOptions));
            Assert.Equal("pnet-mesh-tun", pnetOptions.Scenario);
            Assert.Null(pnetOptions.PNetUdpSocketBufferBytes);
            Assert.Equal("4M", pnetOptions.IperfWindow);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "wireguard-go" },
                TextWriter.Null,
                out var wireGuardOptions));
            Assert.Equal("wireguard-go", wireGuardOptions.Scenario);
            Assert.Null(wireGuardOptions.PNetUdpSocketBufferBytes);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "wireguard-go-pnet-icmp-echo" },
                TextWriter.Null,
                out var icmpEchoOptions));
            Assert.Equal("wireguard-go-pnet-icmp-echo", icmpEchoOptions.Scenario);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "tun-icmp-echo-direct" },
                TextWriter.Null,
                out var tunDirectOptions));
            Assert.Equal("tun-icmp-echo-direct", tunDirectOptions.Scenario);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "tun-icmp-echo-bridge-queue" },
                TextWriter.Null,
                out var tunBridgeQueueOptions));
            Assert.Equal("tun-icmp-echo-bridge-queue", tunBridgeQueueOptions.Scenario);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--mtu", "1420", "--payload-mode", "mtu" },
                TextWriter.Null,
                out var mtuOptions));
            Assert.Equal(1420, mtuOptions.Mtu);
            Assert.Equal("mtu", mtuOptions.PayloadMode);
            Assert.Equal("64K", mtuOptions.IperfBandwidth);
            Assert.Equal(1340, mtuOptions.IperfDatagramBytes);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--iperf-bytes", "104857600", "--iperf-bandwidth", "1G", "--managed-heap-growth-limit-bytes", "8388608" },
                TextWriter.Null,
                out var loadOptions));
            Assert.Equal(104857600, loadOptions.IperfBytes);
            Assert.Equal("1G", loadOptions.IperfBandwidth);
            Assert.Equal(8388608, loadOptions.ManagedHeapGrowthLimitBytes);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--iperf-window", "2M" },
                TextWriter.Null,
                out var iperfWindowOptions));
            Assert.Equal("2M", iperfWindowOptions.IperfWindow);

            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--pnet-udp-socket-buffer-bytes", "4194304" },
                TextWriter.Null,
                out var socketBufferOptions));
            Assert.Equal(4_194_304, socketBufferOptions.PNetUdpSocketBufferBytes);
        }

        [Theory]
        [InlineData("--mtu", "0", "--mtu requires a positive integer.")]
        [InlineData("--mtu", "not-a-number", "--mtu requires a positive integer.")]
        [InlineData("--payload-mode", "bulk", "--payload-mode requires one of: control, mtu.")]
        [InlineData("--iperf-bytes", "0", "--iperf-bytes requires a positive integer.")]
        [InlineData("--managed-heap-growth-limit-bytes", "-1", "--managed-heap-growth-limit-bytes requires a non-negative integer.")]
        [InlineData("--pnet-udp-socket-buffer-bytes", "0", "--pnet-udp-socket-buffer-bytes requires a positive integer.")]
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
            Assert.True(report.ManagedCountersAvailable);
            Assert.NotNull(report.ManagedRuntime);
            Assert.True(report.ManagedRuntime.Available);
            Assert.True(report.ManagedRuntime.ManagedHeapWithinLimit);
            Assert.Equal(2, report.ManagedRuntime.Nodes?.Count);
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
                Assert.DoesNotContain("PNET_MESH_UDP_SOCKET_BUFFER_BYTES=7340032", shellCommand);
                Assert.DoesNotContain("'--public-key'", shellCommand);
                Assert.DoesNotContain("'--private-key'", shellCommand);
                Assert.DoesNotContain("'--psk'", shellCommand);
                Assert.DoesNotContain("--verbose", shellCommand);
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
                Assert.DoesNotContain("--verbose", shellCommand);
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
        public void run_benchmark_preflights_one_ping_per_protocol_before_measured_traffic()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--ping-count", "2", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            var pingCalls = runner.Calls.Where(IsDockerPing).ToArray();
            Assert.Equal(4, pingCalls.Length);
            AssertPingCommand(pingCalls[0], ipv6: false, count: "1", wait: "3", deadline: "3", targetAddress: "10.80.0.2", timeout: TimeSpan.FromSeconds(3));
            AssertPingCommand(pingCalls[1], ipv6: true, count: "1", wait: "3", deadline: "3", targetAddress: "fd80::2", timeout: TimeSpan.FromSeconds(3));
            AssertPingCommand(pingCalls[2], ipv6: false, count: "2", wait: "5", deadline: "11", targetAddress: "10.80.0.2");
            AssertPingCommand(pingCalls[3], ipv6: true, count: "2", wait: "5", deadline: "11", targetAddress: "fd80::2");

            var firstReadinessIndex = runner.Calls.IndexOf(pingCalls[0]);
            var firstMeasuredPingIndex = runner.Calls.IndexOf(pingCalls[2]);
            var firstIperfIndex = runner.Calls.FindIndex(IsDockerIperfClient);
            Assert.True(firstMeasuredPingIndex > firstReadinessIndex);
            Assert.True(firstIperfIndex > firstMeasuredPingIndex);
            Assert.Equal(4, report.Traffic.Count);
        }

        [Fact]
        public void run_benchmark_uses_fixed_iperf_bytes_and_records_managed_heap_delta()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--iperf-bytes", "104857600", "--iperf-bandwidth", "1G", "--managed-heap-growth-limit-bytes", "8388608", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { ManagedHeapGrowthBytes = 4096 };

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            Assert.Equal(104857600, report.Settings.IperfBytes);
            Assert.Equal("1G", report.Settings.IperfBandwidth);
            Assert.Equal("4M", report.Settings.IperfWindow);
            Assert.Equal(8388608, report.Settings.ManagedHeapGrowthLimitBytes);
            Assert.All(runner.Calls.Where(IsDockerIperfClient), call =>
            {
                Assert.Equal("104857600", ReadOption(call.Arguments, "-n"));
                Assert.DoesNotContain("-t", call.Arguments);
                Assert.Equal("1G", ReadOption(call.Arguments, "-b"));
                Assert.Equal("4M", ReadOption(call.Arguments, "-w"));
            });

            var managedRuntime = Assert.IsType<TunBenchmarkManagedRuntimeMetrics>(report.ManagedRuntime);
            Assert.True(managedRuntime.Available);
            Assert.True(managedRuntime.ManagedHeapWithinLimit);
            Assert.Equal(8192, managedRuntime.ManagedHeapDeltaBytes);
            Assert.Equal(2, managedRuntime.Nodes?.Count);
            Assert.All(managedRuntime.Nodes!, node =>
            {
                Assert.Equal(4096, node.ManagedHeapDeltaBytes);
                Assert.True(node.ManagedHeapWithinLimit);
            });

            Assert.Equal(2, report.UdpCounters.Count);
            Assert.All(report.UdpCounters, counter =>
            {
                Assert.Equal(2, counter.Nodes.Count);
                Assert.All(counter.Nodes, node =>
                {
                    Assert.Contains(node.Sockets, socket => socket.Role == "pnet");
                    Assert.Contains(node.Sockets, socket => socket.Role == "iperf3");
                });
            });

            var ipv4Right = Assert.Single(report.UdpCounters, counter => counter.Protocol == "ipv4")
                .Nodes.Single(node => node.Node == "right");
            Assert.Equal(4, ipv4Right.GlobalDelta.UdpRcvbufErrors);
            Assert.Equal(4, ipv4Right.GlobalDelta.UdpInErrors);
            Assert.Equal(4, Assert.Single(ipv4Right.Sockets, socket => socket.Role == "pnet").DropsDelta);
            Assert.Equal(0, Assert.Single(ipv4Right.Sockets, socket => socket.Role == "iperf3").DropsDelta);
        }

        [Fact]
        public void run_bandwidth_series_uses_requested_caps_fixed_size_and_records_managed_heap_delta()
        {
            using var outputDirectory = new TemporaryDirectory();
            Assert.True(TunPNetBandwidthSeriesRunner.TunPNetBandwidthSeriesOptions.TryParse(
                new[] { "--warmup", "0ms", "--timeout", "1s", "--managed-heap-growth-limit-bytes", "8388608", "--output-dir", outputDirectory.Path },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { ManagedHeapGrowthBytes = 4096 };

            var report = TunPNetBandwidthSeriesRunner.RunSeries(options, runner);

            var expectedCaps = new[] { "10M", "25M", "50M", "100M", "250M", "500M", "1G", "2.5G" };
            Assert.True(
                report.Status == "pass",
                string.Join("; ", report.Runs.Select(run => $"{run.BandwidthCap}:{run.Status}:{run.Message}")));
            Assert.Equal(expectedCaps, report.Settings.BandwidthCaps);
            Assert.Equal("pnet-mesh-tun", report.Settings.Scenario);
            Assert.Equal(104857600, report.Settings.IperfBytes);
            Assert.Equal(8388608, report.Settings.ManagedHeapGrowthLimitBytes);
            Assert.Equal(expectedCaps, report.Runs.Select(run => run.BandwidthCap));
            Assert.All(report.Runs, run =>
            {
                Assert.Equal("pass", run.Status);
                Assert.Equal(104857600, run.IperfBytes);
                Assert.True(run.ManagedHeapWithinLimit);
                Assert.Equal(8192, run.ManagedHeapDeltaBytes);
                Assert.Equal(2, run.Traffic.Count);
                Assert.Equal(2, run.ManagedRuntimeNodes.Count);
                Assert.NotNull(run.ReportPath);
                Assert.True(File.Exists(run.ReportPath));
            });
            Assert.Contains(Path.Combine(outputDirectory.Path, "10m.json"), report.Runs.Select(run => run.ReportPath));
            Assert.Contains(Path.Combine(outputDirectory.Path, "2_5g.json"), report.Runs.Select(run => run.ReportPath));

            var iperfCalls = runner.Calls.Where(IsDockerIperfClient).ToArray();
            Assert.Equal(expectedCaps.Length * 2, iperfCalls.Length);
            foreach (var cap in expectedCaps)
            {
                var callsForCap = iperfCalls.Where(call => ReadOption(call.Arguments, "-b") == cap).ToArray();
                Assert.Equal(2, callsForCap.Length);
                Assert.All(callsForCap, call =>
                {
                    Assert.Equal("104857600", ReadOption(call.Arguments, "-n"));
                    Assert.DoesNotContain("-t", call.Arguments);
                    Assert.Equal("4M", ReadOption(call.Arguments, "-w"));
                });
            }
        }

        [Fact]
        public void run_bandwidth_series_accepts_wireguard_go_scenario()
        {
            Assert.True(TunPNetBandwidthSeriesRunner.TunPNetBandwidthSeriesOptions.TryParse(
                new[] { "--scenario", "wireguard-go", "--warmup", "0ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBandwidthSeriesRunner.RunSeries(options, runner);

            var expectedCaps = new[] { "10M", "25M", "50M", "100M", "250M", "500M", "1G", "2.5G" };
            Assert.Equal("wireguard-go", report.Settings.Scenario);
            Assert.Equal("wireguard-go-bandwidth-series", report.Settings.Name);
            Assert.Equal(expectedCaps, report.Runs.Select(run => run.BandwidthCap));
            Assert.All(report.Runs, run =>
            {
                Assert.Equal("pass", run.Status);
                Assert.Null(run.ManagedHeapWithinLimit);
                Assert.Empty(run.ManagedRuntimeNodes);
                Assert.Equal(2, run.Traffic.Count);
            });

            var startCommands = runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 6
                               && call.Arguments[0] == "exec"
                               && call.Arguments[1] == "-d"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("wireguard-go", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(expectedCaps.Length * 2, startCommands.Length);
            Assert.All(startCommands, call =>
                Assert.Contains("WG_PROCESS_FOREGROUND=1 exec wireguard-go", call.Arguments[^1], StringComparison.Ordinal));
        }

        [Fact]
        public void run_bandwidth_series_preserves_skip_status()
        {
            Assert.True(TunPNetBandwidthSeriesRunner.TunPNetBandwidthSeriesOptions.TryParse(
                new[] { "--warmup", "0ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { TunDeviceAvailable = false };

            var report = TunPNetBandwidthSeriesRunner.RunSeries(options, runner);

            Assert.Equal("skip", report.Status);
            var run = Assert.Single(report.Runs);
            Assert.Equal("10M", run.BandwidthCap);
            Assert.Equal("skip", run.Status);
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public void run_benchmark_fails_when_managed_heap_growth_exceeds_limit()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--managed-heap-growth-limit-bytes", "1024", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { ManagedHeapGrowthBytes = 2048 };

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("fail", report.Status);
            Assert.Contains("managed heap grew beyond", report.Message, StringComparison.Ordinal);
            Assert.NotNull(report.ManagedRuntime);
            Assert.False(report.ManagedRuntime.ManagedHeapWithinLimit);
        }

        [Fact]
        public void run_benchmark_fails_setup_when_readiness_ping_times_out()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "pnet-mesh-tun", "--warmup", "0ms", "--iperf-duration", "1ms", "--managed-heap-growth-limit-bytes", "8388608", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner { TimeoutReadinessPing = true };

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("fail", report.Status);
            Assert.Contains("benchmark setup failed", report.Message, StringComparison.Ordinal);
            Assert.Contains("readiness ping", report.Message, StringComparison.Ordinal);
            Assert.Contains("timed out after 3 seconds", report.Message, StringComparison.Ordinal);
            Assert.Empty(report.Traffic);
            Assert.NotNull(report.ManagedRuntime);
            Assert.Equal(8_388_608, report.ManagedRuntime.ManagedHeapGrowthLimitBytes);
            Assert.DoesNotContain(runner.Calls, IsDockerIperfClient);

            var pingCall = Assert.Single(runner.Calls, IsDockerPing);
            AssertPingCommand(pingCall, ipv6: false, count: "1", wait: "3", deadline: "3", targetAddress: "10.80.0.2", timeout: TimeSpan.FromSeconds(3));
            Assert.Contains(report.Commands, command => command.TimedOut && command.Arguments.SequenceEqual(pingCall.Arguments));
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
            Assert.Contains(runner.Calls, call =>
                call.FileName == "docker"
                && call.Arguments.Count >= 6
                && call.Arguments[0] == "exec"
                && call.Arguments[1] == "-d"
                && call.Arguments[^2] == "-c"
                && call.Arguments[^1].Contains("WG_PROCESS_FOREGROUND=1 exec wireguard-go", StringComparison.Ordinal));
            Assert.DoesNotContain(runner.Calls, call =>
                call.FileName == "docker"
                && call.Arguments.Count >= 5
                && call.Arguments[0] == "exec"
                && call.Arguments[^2] == "-c"
                && call.Arguments[^1].Contains("kill -QUIT", StringComparison.Ordinal)
                && call.Arguments[^1].Contains("/tmp/pnet-gc-metrics.log", StringComparison.Ordinal));
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
        public void run_wireguard_go_pnet_icmp_echo_benchmark_records_focused_ping_and_redacts_secrets()
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { "wireguard-go-pnet-icmp-echo", "--warmup", "0ms", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            Assert.Equal("wireguard-go-pnet-icmp-echo", report.Scenario);
            var traffic = Assert.Single(report.Traffic);
            Assert.Equal("ping", traffic.Tool);
            Assert.Equal("ipv4", traffic.Protocol);
            Assert.Equal("10.80.0.2", traffic.TargetAddress);
            Assert.Equal(0, traffic.PacketLossPercent);

            var actualConfigCommand = Assert.Single(runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 5
                               && call.Arguments[0] == "exec"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("wg", StringComparison.Ordinal)
                               && call.Arguments[^1].Contains("'endpoint' 'right:12402'", StringComparison.Ordinal))
                .Select(call => call.Arguments[^1]));
            Assert.Contains("'allowed-ips' '10.80.0.2/32,fd80::2/128'", actualConfigCommand, StringComparison.Ordinal);

            var actualEchoCommand = Assert.Single(runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 6
                               && call.Arguments[0] == "exec"
                               && call.Arguments[1] == "-d"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("PNet.Mesh.Tun.Cli.dll", StringComparison.Ordinal)
                               && call.Arguments[^1].Contains("'icmp-echo'", StringComparison.Ordinal))
                .Select(call => call.Arguments[^1]));
            Assert.Contains("'--public-key-file'", actualEchoCommand, StringComparison.Ordinal);
            Assert.Contains("'--private-key-file'", actualEchoCommand, StringComparison.Ordinal);
            Assert.Contains("'--psk-file'", actualEchoCommand, StringComparison.Ordinal);

            var reportedArguments = string.Join('\n', report.Commands.SelectMany(command => command.Arguments));
            Assert.Contains("endpoint right:12402", reportedArguments);
            Assert.Contains("dotnet PNet.Mesh.Tun.Cli.dll icmp-echo", reportedArguments);
            Assert.Contains("<container-secret>", reportedArguments);
            Assert.Contains("<redacted>", reportedArguments);
            Assert.DoesNotContain(actualEchoCommand, reportedArguments);
            foreach (var secret in runner.CopiedSensitiveContents)
                Assert.DoesNotContain(secret, reportedArguments);
        }

        [Theory]
        [InlineData("tun-icmp-echo-direct", "direct")]
        [InlineData("tun-icmp-echo-bridge-queue", "bridge-queue")]
        public void run_tun_icmp_echo_benchmark_records_focused_ping_and_metrics_dump(string scenario, string mode)
        {
            Assert.True(TunPNetBenchmarkRunner.TunPNetBenchmarkOptions.TryParse(
                new[] { scenario, "--warmup", "0ms", "--iperf-duration", "1ms", "--timeout", "1s" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner();

            var report = TunPNetBenchmarkRunner.RunBenchmark(options, runner);

            Assert.Equal("pass", report.Status);
            Assert.Equal(scenario, report.Scenario);
            var traffic = Assert.Single(report.Traffic);
            Assert.Equal("ping", traffic.Tool);
            Assert.Equal("ipv4", traffic.Protocol);
            Assert.Equal("10.80.0.2", traffic.TargetAddress);
            Assert.Equal(0, traffic.PacketLossPercent);
            Assert.Single(report.Processes);

            var actualCommand = Assert.Single(runner.Calls
                .Where(call => call.FileName == "docker"
                               && call.Arguments.Count >= 6
                               && call.Arguments[0] == "exec"
                               && call.Arguments[1] == "-d"
                               && call.Arguments[^2] == "-c"
                               && call.Arguments[^1].Contains("PNet.Mesh.Tun.Cli.dll", StringComparison.Ordinal)
                               && call.Arguments[^1].Contains("'tun-icmp-echo'", StringComparison.Ordinal))
                .Select(call => call.Arguments[^1]));
            Assert.Contains($"'--tun-echo-mode' '{mode}'", actualCommand, StringComparison.Ordinal);
            Assert.DoesNotContain("--public-key", actualCommand);
            Assert.DoesNotContain("--private-key", actualCommand);
            Assert.DoesNotContain("--psk", actualCommand);
            Assert.DoesNotContain("--peer", actualCommand);

            Assert.Contains(runner.Calls, call =>
                call.FileName == "docker"
                && call.Arguments.Count >= 5
                && call.Arguments[0] == "exec"
                && call.Arguments[^2] == "-c"
                && call.Arguments[^1].Contains("kill -HUP", StringComparison.Ordinal));

            var reportedArguments = string.Join('\n', report.Commands.SelectMany(command => command.Arguments));
            Assert.Contains($"--tun-echo-mode {mode}", reportedArguments, StringComparison.Ordinal);
            Assert.Contains("tail -n 120 /tmp/pnet-tun-icmp-echo.log", reportedArguments, StringComparison.Ordinal);
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

        static bool IsDockerPing(FakeCommandCall call)
        {
            return call.FileName == "docker"
                   && call.Arguments.Count >= 3
                   && call.Arguments[0] == "exec"
                   && call.Arguments[2] == "ping";
        }

        static bool IsDockerIperfClient(FakeCommandCall call)
        {
            return call.FileName == "docker"
                   && call.Arguments.Count >= 3
                   && call.Arguments[0] == "exec"
                   && call.Arguments[2] == "iperf3";
        }

        static void AssertPingCommand(
            FakeCommandCall call,
            bool ipv6,
            string count,
            string wait,
            string deadline,
            string targetAddress,
            TimeSpan? timeout = null)
        {
            Assert.True(IsDockerPing(call));
            Assert.Equal(ipv6, call.Arguments.Contains("-6"));
            Assert.Equal(count, ReadOption(call.Arguments, "-c"));
            Assert.Equal(wait, ReadOption(call.Arguments, "-W"));
            Assert.Equal(deadline, ReadOption(call.Arguments, "-w"));
            Assert.Equal(targetAddress, call.Arguments[^1]);
            if (timeout.HasValue)
                Assert.Equal(timeout.Value, call.Timeout);
        }

        static string? ReadOption(IReadOnlyList<string> arguments, string option)
        {
            var index = arguments.ToList().IndexOf(option);
            return index >= 0 && index + 1 < arguments.Count
                ? arguments[index + 1]
                : null;
        }

        sealed class FakeCommandRunner : ITunTopologyCommandRunner
        {
            string? _networkName;
            readonly Dictionary<string, int> _managedRuntimeSnapshotCounts = new(StringComparer.Ordinal);

            public List<FakeCommandCall> Calls { get; } = new();

            public List<string> CopiedSensitiveContents { get; } = new();

            public bool PartialFinalIpv4Ping { get; init; }

            public bool TimeoutReadinessPing { get; init; }

            public long ManagedHeapGrowthBytes { get; init; } = 4096;

            public double IperfPacketLossPercent { get; init; }

            public long IperfBytesDelta { get; init; }

            public bool TunDeviceAvailable { get; init; } = true;

            public bool FileExists(string path)
            {
                return TunDeviceAvailable && string.Equals(path, "/dev/net/tun", StringComparison.Ordinal);
            }

            public TunTopologyCommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
            {
                Calls.Add(new FakeCommandCall(fileName, arguments.ToArray(), timeout));
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

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "sh"
                    && arguments[^1].Contains("pnet-udp-counters", StringComparison.Ordinal))
                {
                    var node = arguments[1].Contains("right", StringComparison.Ordinal) ? "right" : "left";
                    var ipv6 = arguments[^1].Contains("pnet-udp-counters-ipv6-", StringComparison.Ordinal);
                    var sampleIndex = ReadUdpCounterSampleIndex(arguments[^1]);
                    return new TunTopologyCommandResult(
                        fileName,
                        arguments.ToArray(),
                        0,
                        CreateUdpCounterSampleOutput(node, ipv6, sampleIndex),
                        string.Empty,
                        false);
                }

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "inspect")
                {
                    var requestedNetwork = arguments.ElementAtOrDefault(2);
                    var exitCode = string.Equals(_networkName, requestedNetwork, StringComparison.Ordinal) ? 0 : 1;
                    var stdout = exitCode == 0 ? _networkName + "\n" : string.Empty;
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), exitCode, stdout, string.Empty, false);
                }

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "create")
                    _networkName = arguments[^1];

                if (arguments.Count >= 2 && arguments[0] == "network" && arguments[1] == "rm")
                    _networkName = null;

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "sh" && arguments[^1].Contains("/proc/$pid/status", StringComparison.Ordinal))
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "pid=42\nVmRSS:\t1024 kB\nVmHWM:\t2048 kB\nThreads:\t8\nutime_ticks=10\nstime_ticks=5\n", string.Empty, false);

                if (arguments.Count >= 4
                    && arguments[0] == "exec"
                    && arguments[2] == "sh"
                    && arguments[^1].Contains("kill -QUIT", StringComparison.Ordinal)
                    && arguments[^1].Contains("/tmp/pnet-gc-metrics.log", StringComparison.Ordinal))
                {
                    var node = arguments[1].Contains("right", StringComparison.Ordinal) ? "right" : "left";
                    _managedRuntimeSnapshotCounts.TryGetValue(node, out var count);
                    _managedRuntimeSnapshotCounts[node] = count + 1;
                    var before = count % 2 == 0;
                    var baseHeap = node == "left" ? 65_536L : 70_000L;
                    var heap = before ? baseHeap : baseHeap + ManagedHeapGrowthBytes;
                    var allocation = before ? 123_456L : 123_456L + 1_000_000L;
                    var gen0 = before ? 3 : 5;
                    var gen1 = before ? 2 : 4;
                    var gen2 = before ? 1 : 3;
                    return new TunTopologyCommandResult(
                        fileName,
                        arguments.ToArray(),
                        0,
                        $"pnet_gc_metrics allocated_bytes={allocation} managed_heap_bytes={heap} fragmented_bytes=1024 memory_load_bytes=4096 total_available_memory_bytes=1073741824 high_memory_load_threshold_bytes=966367641 gen0_collections={gen0} gen1_collections={gen1} gen2_collections={gen2}\n",
                        string.Empty,
                        false);
                }

                if (arguments.Count >= 4 && arguments[0] == "exec" && arguments[2] == "sh" && arguments[^1].Contains("ss ", StringComparison.Ordinal) && arguments[^1].Contains("-H -ltn", StringComparison.Ordinal))
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, "LISTEN 0 4096 10.80.0.2:5201 0.0.0.0:*\n", string.Empty, false);

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "ping")
                {
                    var count = ReadOption(arguments, "-c");
                    var isReadinessPing = count == "1"
                                          && ReadOption(arguments, "-W") == "3"
                                          && ReadOption(arguments, "-w") == "3";
                    if (TimeoutReadinessPing && isReadinessPing)
                    {
                        return new TunTopologyCommandResult(
                            fileName,
                            arguments.ToArray(),
                            -1,
                            string.Empty,
                            "readiness ping timed out.",
                            true);
                    }

                    var isFinalIpv4Ping = !arguments.Contains("-6")
                                          && count == "1"
                                          && !isReadinessPing;

                    var stdout = PartialFinalIpv4Ping && isFinalIpv4Ping
                        ? "2 packets transmitted, 1 received, 50% packet loss, time 1000ms\nrtt min/avg/max/mdev = 0.100/0.100/0.100/0.000 ms\n"
                        : $"{count ?? "1"} packets transmitted, {count ?? "1"} received, 0% packet loss, time 0ms\nrtt min/avg/max/mdev = 0.042/0.042/0.042/0.000 ms\n";
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, stdout, string.Empty, false);
                }

                if (arguments.Count >= 3 && arguments[0] == "exec" && arguments[2] == "iperf3")
                {
                    var requestedBytes = ReadOption(arguments, "-n");
                    var bytes = long.TryParse(requestedBytes, out var parsedBytes) ? parsedBytes + IperfBytesDelta : 128;
                    return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, $"{{\"end\":{{\"sum_received\":{{\"seconds\":1,\"bytes\":{bytes},\"bits_per_second\":1024,\"lost_percent\":{IperfPacketLossPercent}}}}}}}", string.Empty, false);
                }

                return new TunTopologyCommandResult(fileName, arguments.ToArray(), 0, string.Empty, string.Empty, false);
            }

            static int ReadUdpCounterSampleIndex(string script)
            {
                return script.Contains(" 1 \"$(date", StringComparison.Ordinal) ? 1 : 0;
            }

            static string CreateUdpCounterSampleOutput(string node, bool ipv6, int sampleIndex)
            {
                var pnetPort = node == "right" ? "3072" : "3071";
                var pnetDropDelta = node == "right" ? 4 : 0;
                var after = sampleIndex > 0;
                var family = ipv6 ? "udp6" : "udp";
                var localAddress = ipv6
                    ? $"00000000000000000000000000000000:{pnetPort}"
                    : $"00000000:{pnetPort}";
                var iperfAddress = ipv6
                    ? "00000000000000000000000000000000:1451"
                    : "0200500A:1451";
                var udpInErrors = after && !ipv6 ? 10 + pnetDropDelta : 10;
                var udpRcvbufErrors = after && !ipv6 ? 2 + pnetDropDelta : 2;
                var udp6InErrors = after && ipv6 ? 20 + pnetDropDelta : 20;
                var udp6RcvbufErrors = after && ipv6 ? 3 + pnetDropDelta : 3;
                var pnetDrops = after ? 4 + pnetDropDelta : 4;
                return $"""
                sample index={sampleIndex} unix_ms={1000 + (sampleIndex * 50)}
                snmp protocol=udp name=InErrors value={udpInErrors}
                snmp protocol=udp name=RcvbufErrors value={udpRcvbufErrors}
                snmp protocol=udp6 name=InErrors value={udp6InErrors}
                snmp protocol=udp6 name=RcvbufErrors value={udp6RcvbufErrors}
                socket family={family} local={localAddress} remote=00000000:0000 state=07 queues=00000000:00000000 inode=100 drops={pnetDrops}
                socket family={family} local={iperfAddress} remote=00000000:0000 state=07 queues=00000000:00000000 inode=200 drops=5
                end_sample
                """;
            }
        }

        sealed record FakeCommandCall(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

        sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pnet-tun-series-{Guid.NewGuid():N}");

            public TemporaryDirectory()
            {
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
