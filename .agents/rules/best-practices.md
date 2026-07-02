# Project Best Practices

**Type:** .NET SDK solution + optional Linux TUN component/CLI + xUnit unit/e2e tests + Testcontainers-based e2e + BenchmarkDotNet benchmarks + protobuf schema.
**Runtime:** project targets `net10.0`; keep runtime, package, and container image changes coordinated through `.agents/docs/discovered-issues.md`.
**Docs:** follow `~/.agents/docs/projects/dotnet.md`, `docker.md`, and `protobuf-grpc.md`; dependency docs include `xunit`, `coverlet`, `testcontainers-dotnet`, `visual-studio-container-tools`, and `bouncycastle-cryptography`.
**SDK:** use installed .NET 10 SDK for tooling; add `global.json` only as part of the runtime migration decision.
**Restore:** `dotnet restore PNet.Mesh.sln`; `Noise.NET` is the restored Noise protocol package and `BouncyCastle.Cryptography` provides BLAKE2s helpers.
**Build/test:** run `dotnet build PNet.Mesh.sln -c Release --no-restore`; unit tests use `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none`; TUN tests use `dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none`; Testcontainers e2e uses `timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none`.
**TUN:** `src/PNet.Mesh.Tun` is optional Linux TUN support; it may require `/dev/net/tun`, `CAP_NET_ADMIN`, and `CAP_NET_RAW`, while core `PNet.Mesh` remains no-TUN/no-route-injection.
**Benchmarks:** restore/build in Release, then run BenchmarkDotNet with `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'`; macro JSON harnesses use `--macro in-memory` and `--macro udp-loopback`; privileged TUN uses `--tun-topology plan|preflight|create|teardown` and `--tun-benchmark pnet-mesh-tun`; current baseline/policy live in `.agents/docs/benchmarks/`.
**Formatting:** LF is canonical via `.gitattributes` + `.editorconfig`; verify scoped whitespace with `dotnet format whitespace PNet.Mesh.sln --include <paths> --no-restore --verify-no-changes --verbosity minimal`.
**NuGet:** use `scripts/packages.sh` for PackageReference maintenance; run vulnerable/outdated/deprecated package checks after restore works.
**Containers:** use Testcontainers for supported mesh e2e; use the named Testcontainers methods for mesh topology coverage.
**Protobuf:** schema source is `src/PNet.Mesh/Protos/MeshProtocol.proto`; prefer schema/descriptor validators over raw source-string assertions.
**Generated code:** treat `src/PNet.Mesh/Protos/MeshProtocol.cs` as generated from the proto; avoid hand edits unless the generator path is unavailable and documented.

Guide: `~/.agents/docs/projects/dotnet.md`, `~/.agents/docs/projects/docker.md`, `~/.agents/docs/projects/protobuf-grpc.md`
