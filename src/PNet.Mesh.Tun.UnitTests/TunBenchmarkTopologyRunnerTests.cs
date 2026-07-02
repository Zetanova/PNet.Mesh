using PNet.Mesh.Benchmarks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh.Tun
{
    public sealed class TunBenchmarkTopologyRunnerTests
    {
        [Fact]
        public void topology_plan_reserves_dual_stack_slots_for_both_implementations()
        {
            var spec = TunBenchmarkTopologyRunner.CreateSpec("mesh-test", "localhost/pnet-mesh-tun:test");

            Assert.Equal("mesh-test", spec.DockerNetwork);
            Assert.Equal("localhost/pnet-mesh-tun:test", spec.Image);
            Assert.Equal(1280, spec.Mtu);
            Assert.Equal("/dev/net/tun", spec.RequiredDevice);
            Assert.Contains("NET_ADMIN", spec.RequiredCapabilities);
            Assert.Contains("NET_RAW", spec.RequiredCapabilities);

            var left = Assert.Single(spec.Nodes, node => node.Role == "left");
            Assert.Equal("mesh-test-left", left.ContainerName);
            Assert.Equal("pnet0", left.InterfaceName);
            Assert.Equal("10.80.0.1/24", left.Ipv4Address);
            Assert.Equal("fd80::1/64", left.Ipv6Address);
            Assert.Equal(new[] { "10.80.0.2/32", "fd80::2/128" }, left.PeerRoutes);
            Assert.Equal(12401, left.PNetUdpPort);
            Assert.Equal(51820, left.WireGuardUdpPort);

            var right = Assert.Single(spec.Nodes, node => node.Role == "right");
            Assert.Equal("10.80.0.2/24", right.Ipv4Address);
            Assert.Equal("fd80::2/64", right.Ipv6Address);
            Assert.Equal(new[] { "10.80.0.1/32", "fd80::1/128" }, right.PeerRoutes);
            Assert.Equal(12402, right.PNetUdpPort);
            Assert.Equal(51821, right.WireGuardUdpPort);

            Assert.Contains(spec.Implementations, slot => slot.Name == "pnet-mesh-tun");
            Assert.Contains(spec.Implementations, slot => slot.Name == "wireguard-go");
            Assert.Equal("left", spec.Traffic.ClientNode);
            Assert.Equal("right", spec.Traffic.ServerNode);
            Assert.Equal(5201, spec.Traffic.Iperf3Port);
        }

        [Fact]
        public void preflight_skips_without_running_docker_when_tun_device_is_missing()
        {
            Assert.True(TunBenchmarkTopologyRunner.TunTopologyOptions.TryParse(
                new[] { "preflight" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner(fileExists: false);
            var report = TunBenchmarkTopologyRunner.RunPreflight(options, runner);

            Assert.Equal("skip", report.Status);
            Assert.Empty(report.Commands);
            Assert.Contains(report.Checks, check => check.Status == "skip");
        }

        [Fact]
        public void preflight_reports_linux_skip_or_privileged_probe_pass()
        {
            Assert.True(TunBenchmarkTopologyRunner.TunTopologyOptions.TryParse(
                new[] { "preflight" },
                TextWriter.Null,
                out var options));

            var runner = new FakeCommandRunner(fileExists: true);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                runner.Enqueue(0, "27.0.0\n", string.Empty);
                runner.Enqueue(0, "[]\n", string.Empty);
                runner.Enqueue(0, string.Empty, string.Empty);

                var linuxReport = TunBenchmarkTopologyRunner.RunPreflight(options, runner);

                Assert.Equal("pass", linuxReport.Status);
                Assert.Equal("27.0.0", linuxReport.Environment.ContainerEngineVersion);
                Assert.Equal(3, linuxReport.Commands.Count);
                Assert.Contains(linuxReport.Checks, check => check.Name == "privileged-container" && check.Status == "pass");
                Assert.Equal(new[] { "version", "--format", "{{.Server.Version}}" }, runner.Calls[0].Arguments);
            }
            else
            {
                var nonLinuxReport = TunBenchmarkTopologyRunner.RunPreflight(options, runner);

                Assert.Equal("skip", nonLinuxReport.Status);
                Assert.Empty(nonLinuxReport.Commands);
                Assert.Empty(runner.Calls);
                Assert.Contains(nonLinuxReport.Checks, check => check.Name == "linux" && check.Status == "skip");
            }
        }

        sealed class FakeCommandRunner : ITunTopologyCommandRunner
        {
            readonly bool _fileExists;
            readonly Queue<TunTopologyCommandResult> _results = new();

            public FakeCommandRunner(bool fileExists)
            {
                _fileExists = fileExists;
            }

            public List<FakeCommandCall> Calls { get; } = new();

            public bool FileExists(string path)
            {
                return _fileExists && string.Equals(path, "/dev/net/tun", StringComparison.Ordinal);
            }

            public TunTopologyCommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
            {
                Calls.Add(new FakeCommandCall(fileName, arguments.ToArray()));
                var result = _results.Count == 0
                    ? new TunTopologyCommandResult(fileName, arguments, 0, string.Empty, string.Empty, false)
                    : _results.Dequeue();

                return result with
                {
                    FileName = fileName,
                    Arguments = arguments.ToArray()
                };
            }

            public void Enqueue(int exitCode, string stdout, string stderr)
            {
                _results.Enqueue(new TunTopologyCommandResult(string.Empty, Array.Empty<string>(), exitCode, stdout, stderr, false));
            }
        }

        sealed record FakeCommandCall(string FileName, IReadOnlyList<string> Arguments);
    }
}
