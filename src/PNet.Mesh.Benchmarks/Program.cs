using BenchmarkDotNet.Running;
using PNet.Mesh.Benchmarks;

#if DEBUG
Console.Error.WriteLine("PNet.Mesh benchmarks must run in Release configuration. Use: dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'");
return 1;
#else
if (args.Length > 0 && args[0] == "--macro")
    return MacroBenchmarkRunner.Run(args[1..], Console.Out, Console.Error);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
#endif
