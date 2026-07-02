using BenchmarkDotNet.Running;

namespace PNet.Mesh.Benchmarks;

internal static class BenchmarkCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] == "--macro")
            return MacroBenchmarkRunner.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "--tun-topology")
            return TunBenchmarkTopologyRunner.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "--tun-benchmark")
            return TunPNetBenchmarkRunner.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "--tun-compare")
            return TunBenchmarkComparisonRunner.Run(args[1..], output, error);

        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkCli).Assembly).Run(args);
        return 0;
    }
}
