using BenchmarkDotNet.Running;

#if DEBUG
Console.Error.WriteLine("PNet.Mesh benchmarks must run in Release configuration. Use: dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'");
return 1;
#else
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
#endif
