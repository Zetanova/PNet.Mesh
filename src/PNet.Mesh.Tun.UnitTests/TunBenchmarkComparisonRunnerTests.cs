using PNet.Mesh.Benchmarks;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh.Tun
{
    public sealed class TunBenchmarkComparisonRunnerTests
    {
        [Fact]
        public void compare_saved_results_exports_normalized_schema_and_preserves_raw_data()
        {
            using var directory = new TemporaryDirectory();
            var pnetPath = Path.Combine(directory.Path, "pnet.json");
            var wireGuardPath = Path.Combine(directory.Path, "wireguard.json");
            File.WriteAllText(pnetPath, CreateSavedReportJson(
                "pnet-mesh-tun",
                "pnet-topology",
                DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
                "pnet-commit",
                "--tun-benchmark pnet-mesh-tun --ping-count 1",
                ".NET 10.0.0",
                "PNet.Mesh.Tun.Cli.dll",
                "1.2.3",
                0.21,
                0.32,
                0,
                1.5,
                1000,
                2000,
                1000,
                2000,
                3,
                10,
                5,
                3000,
                4000,
                4,
                20,
                15,
                true));
            File.WriteAllText(wireGuardPath, CreateSavedReportJson(
                "wireguard-go",
                "wireguard-topology",
                DateTimeOffset.Parse("2026-07-02T10:01:00Z"),
                "wireguard-commit",
                "--tun-benchmark wireguard-go --ping-count 1",
                "Linux",
                "/usr/bin/wireguard-go",
                "0.0.20230223-1",
                0.41,
                0.52,
                0,
                0,
                900,
                1900,
                5000,
                6000,
                5,
                30,
                20,
                7000,
                8000,
                6,
                40,
                25,
                false));
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = BenchmarkCli.Run(
                new[] { "--tun-compare", "--pnet", pnetPath, "--wireguard", wireGuardPath },
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            Assert.Equal("pnet-mesh-tun-benchmark-comparison", root.GetProperty("kind").GetString());
            Assert.Equal("pass", root.GetProperty("status").GetString());

            var sources = root.GetProperty("sources");
            Assert.Equal(pnetPath, sources.GetProperty("pnet").GetProperty("sourceFile").GetString());
            Assert.Equal(wireGuardPath, sources.GetProperty("wireguard").GetProperty("sourceFile").GetString());
            Assert.Equal("pnet-mesh-tun", sources.GetProperty("pnet").GetProperty("scenario").GetString());
            Assert.Equal("wireguard-go", sources.GetProperty("wireguard").GetProperty("scenario").GetString());

            var traffic = root.GetProperty("metrics").GetProperty("traffic");
            Assert.Equal(0.21, traffic.GetProperty("ipv4PingAverageLatencyMilliseconds").GetProperty("pnet").GetDouble());
            Assert.Equal(0.41, traffic.GetProperty("ipv4PingAverageLatencyMilliseconds").GetProperty("wireguard").GetDouble());
            Assert.Equal(0.32, traffic.GetProperty("ipv6PingAverageLatencyMilliseconds").GetProperty("pnet").GetDouble());
            Assert.Equal(0.52, traffic.GetProperty("ipv6PingAverageLatencyMilliseconds").GetProperty("wireguard").GetDouble());
            Assert.Equal(1.5, traffic.GetProperty("ipv6PingPacketLossPercent").GetProperty("pnet").GetDouble());
            Assert.Equal(1000, traffic.GetProperty("ipv4IperfBitsPerSecond").GetProperty("pnet").GetDouble());
            Assert.Equal(1900, traffic.GetProperty("ipv6IperfBitsPerSecond").GetProperty("wireguard").GetDouble());

            var process = root.GetProperty("metrics").GetProperty("process");
            Assert.Equal(30, process.GetProperty("userCpuTicks").GetProperty("pnet").GetInt64());
            Assert.Equal(70, process.GetProperty("userCpuTicks").GetProperty("wireguard").GetInt64());
            Assert.Equal(50, process.GetProperty("totalCpuTicks").GetProperty("pnet").GetInt64());
            Assert.Equal(115, process.GetProperty("totalCpuTicks").GetProperty("wireguard").GetInt64());
            Assert.Equal(4000, process.GetProperty("residentSetBytes").GetProperty("pnet").GetInt64());
            Assert.Equal(12000, process.GetProperty("residentSetBytes").GetProperty("wireguard").GetInt64());
            Assert.Equal(6000, process.GetProperty("residentSetHighWatermarkBytes").GetProperty("pnet").GetInt64());
            Assert.Equal(14000, process.GetProperty("residentSetHighWatermarkBytes").GetProperty("wireguard").GetInt64());
            Assert.Equal(7, process.GetProperty("threads").GetProperty("pnet").GetInt32());
            Assert.Equal(11, process.GetProperty("threads").GetProperty("wireguard").GetInt32());

            var settings = root.GetProperty("metrics").GetProperty("settings");
            Assert.Equal(1280, settings.GetProperty("mtu").GetProperty("pnet").GetInt32());
            Assert.Equal(3, settings.GetProperty("iperfDurationSeconds").GetProperty("wireguard").GetDouble());

            var traceability = root.GetProperty("metrics").GetProperty("traceability");
            Assert.Equal("pnet-topology", traceability.GetProperty("topologyId").GetProperty("pnet").GetString());
            Assert.Equal("wireguard-commit", traceability.GetProperty("gitCommit").GetProperty("wireguard").GetString());
            Assert.Equal(DateTimeOffset.Parse("2026-07-02T10:00:00Z"), traceability.GetProperty("runTimestamp").GetProperty("pnet").GetDateTimeOffset());
            Assert.Equal("--tun-benchmark pnet-mesh-tun --ping-count 1", traceability.GetProperty("commandLine").GetProperty("pnet").GetString());

            var implementation = root.GetProperty("metrics").GetProperty("implementation");
            Assert.Equal("1.2.3", implementation.GetProperty("version").GetProperty("pnet").GetString());
            Assert.Equal("/usr/bin/wireguard-go", implementation.GetProperty("executablePath").GetProperty("wireguard").GetString());

            var managedRuntime = root.GetProperty("metrics").GetProperty("managedRuntime");
            Assert.True(managedRuntime.GetProperty("pnet").GetProperty("applicable").GetBoolean());
            Assert.True(managedRuntime.GetProperty("pnet").GetProperty("available").GetBoolean());
            Assert.Equal(123456, managedRuntime.GetProperty("pnet").GetProperty("allocationBytes").GetInt64());
            Assert.Equal(65536, managedRuntime.GetProperty("pnet").GetProperty("managedHeapBytes").GetInt64());
            Assert.Equal(3, managedRuntime.GetProperty("pnet").GetProperty("gen0Collections").GetInt32());
            Assert.False(managedRuntime.GetProperty("wireguard").GetProperty("applicable").GetBoolean());
            Assert.False(managedRuntime.GetProperty("wireguard").GetProperty("available").GetBoolean());
            Assert.Contains(".NET", managedRuntime.GetProperty("wireguard").GetProperty("notApplicableReason").GetString(), StringComparison.Ordinal);

            var rawPnet = root.GetProperty("raw").GetProperty("pnet");
            Assert.Equal("pnet-mesh-tun ipv4 ping raw", rawPnet.GetProperty("traffic")[0].GetProperty("stdout").GetString());
            Assert.Equal("pnet-mesh-tun command stdout", rawPnet.GetProperty("commands")[0].GetProperty("stdout").GetString());
            Assert.Equal("wireguard-go ipv6 iperf raw", root.GetProperty("raw").GetProperty("wireguard").GetProperty("traffic")[3].GetProperty("stdout").GetString());
        }

        [Fact]
        public void compare_saved_results_rejects_wrong_scenario_input()
        {
            using var directory = new TemporaryDirectory();
            var pnetPath = Path.Combine(directory.Path, "pnet.json");
            var wrongWireGuardPath = Path.Combine(directory.Path, "wrong-wireguard.json");
            var pnetJson = CreateSavedReportJson(
                "pnet-mesh-tun",
                "pnet-topology",
                DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
                "pnet-commit",
                "--tun-benchmark pnet-mesh-tun --ping-count 1",
                ".NET 10.0.0",
                "PNet.Mesh.Tun.Cli.dll",
                "1.2.3",
                0.21,
                0.32,
                0,
                0,
                1000,
                2000,
                1000,
                2000,
                3,
                10,
                5,
                3000,
                4000,
                4,
                20,
                15,
                true);
            File.WriteAllText(pnetPath, pnetJson);
            File.WriteAllText(wrongWireGuardPath, pnetJson);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = BenchmarkCli.Run(
                new[] { "--tun-compare", "--pnet", pnetPath, "--wireguard", wrongWireGuardPath },
                output,
                error);

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("expected 'wireguard-go'", error.ToString(), StringComparison.Ordinal);
        }

        static string CreateSavedReportJson(
            string scenario,
            string topologyName,
            DateTimeOffset createdAt,
            string gitCommit,
            string commandLine,
            string framework,
            string executablePath,
            string version,
            double ipv4PingLatency,
            double ipv6PingLatency,
            double ipv4PacketLoss,
            double ipv6PacketLoss,
            double ipv4Bandwidth,
            double ipv6Bandwidth,
            long leftRss,
            long leftHighWatermark,
            int leftThreads,
            long leftUserTicks,
            long leftSystemTicks,
            long rightRss,
            long rightHighWatermark,
            int rightThreads,
            long rightUserTicks,
            long rightSystemTicks,
            bool managedCountersAvailable)
        {
            var isPNet = string.Equals(scenario, "pnet-mesh-tun", StringComparison.Ordinal);
            var managedCounterUnavailableReason = managedCountersAvailable
                ? string.Empty
                : isPNet
                    ? "dotnet-counters is not installed in the TUN CLI image."
                    : "wireguard-go is not a .NET process; managed .NET allocation counters do not apply.";
            var report = new TunPNetBenchmarkReport(
                "pnet-mesh-tun-benchmark",
                scenario,
                "pass",
                createdAt,
                new TunTopologyEnvironment(
                    framework,
                    "Linux test",
                    "X64",
                    16,
                    true,
                    "27.0.0"),
                TunBenchmarkTopologyRunner.CreateSpec(topologyName, "localhost/pnet-mesh-tun:test"),
                new TunBenchmarkImplementationInfo(
                    scenario,
                    version,
                    executablePath,
                    isPNet ? "PNet.Mesh.Benchmarks assembly" : "dpkg-query wireguard-go",
                    null),
                new TunPNetBenchmarkSettings(
                    1,
                    2,
                    3,
                    5201,
                    1280),
                new[]
                {
                    CreatePingTraffic("ipv4", ipv4PingLatency, ipv4PacketLoss, $"{scenario} ipv4 ping raw"),
                    CreatePingTraffic("ipv6", ipv6PingLatency, ipv6PacketLoss, $"{scenario} ipv6 ping raw"),
                    CreateIperfTraffic("ipv4", ipv4Bandwidth, $"{scenario} ipv4 iperf raw"),
                    CreateIperfTraffic("ipv6", ipv6Bandwidth, $"{scenario} ipv6 iperf raw")
                },
                new[]
                {
                    CreateProcess("left", leftRss, leftHighWatermark, leftThreads, leftUserTicks, leftSystemTicks),
                    CreateProcess("right", rightRss, rightHighWatermark, rightThreads, rightUserTicks, rightSystemTicks)
                },
                managedCountersAvailable,
                managedCounterUnavailableReason,
                new TunBenchmarkManagedRuntimeMetrics(
                    isPNet,
                    managedCountersAvailable,
                    managedCountersAvailable ? 123456L : null,
                    managedCountersAvailable ? 65536L : null,
                    managedCountersAvailable ? 3 : null,
                    managedCountersAvailable ? 2 : null,
                    managedCountersAvailable ? 1 : null,
                    managedCountersAvailable ? null : isPNet ? "Managed counters were unavailable." : null,
                    isPNet ? null : "wireguard-go is not a .NET process; managed .NET allocation counters do not apply."),
                gitCommit,
                commandLine,
                Array.Empty<TunTopologyReport>(),
                new[]
                {
                    new TunTopologyCommandRecord(
                        "docker",
                        new[] { "exec", scenario },
                        0,
                        $"{scenario} command stdout",
                        $"{scenario} command stderr",
                        false)
                },
                $"{scenario} saved report");

            return JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        static TunBenchmarkTrafficResult CreatePingTraffic(string protocol, double latency, double packetLoss, string stdout)
        {
            return new TunBenchmarkTrafficResult(
                "ping",
                protocol,
                "left",
                "right",
                protocol == "ipv4" ? "10.80.0.2" : "fd80::2",
                0,
                1,
                packetLoss == 0 ? 1 : 0,
                packetLoss,
                latency,
                latency,
                latency,
                null,
                null,
                null,
                stdout,
                $"{protocol} ping stderr");
        }

        static TunBenchmarkTrafficResult CreateIperfTraffic(string protocol, double bandwidth, string stdout)
        {
            return new TunBenchmarkTrafficResult(
                "iperf3",
                protocol,
                "left",
                "right",
                protocol == "ipv4" ? "10.80.0.2" : "fd80::2",
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                3,
                375,
                bandwidth,
                stdout,
                $"{protocol} iperf stderr");
        }

        static TunBenchmarkProcessMetrics CreateProcess(
            string node,
            long rss,
            long highWatermark,
            int threads,
            long userTicks,
            long systemTicks)
        {
            return new TunBenchmarkProcessMetrics(
                node,
                $"test-{node}",
                true,
                42,
                rss,
                highWatermark,
                threads,
                userTicks,
                systemTicks,
                null);
        }

        sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pnet-tun-compare-{Guid.NewGuid():N}");

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
