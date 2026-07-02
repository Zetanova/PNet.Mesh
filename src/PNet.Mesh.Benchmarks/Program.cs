#if DEBUG
Console.Error.WriteLine("PNet.Mesh benchmarks must run in Release configuration. Use: dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'");
return 1;
#else
return PNet.Mesh.Benchmarks.BenchmarkCli.Run(args, Console.Out, Console.Error);
#endif
