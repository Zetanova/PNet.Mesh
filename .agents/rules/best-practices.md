# Project Best Practices

**Type:** .NET SDK solution + optional Linux TUN component/CLI + xUnit unit/e2e tests + Testcontainers-based e2e + BenchmarkDotNet benchmarks + protobuf schema.
**Runtime:** project targets `net10.0`; keep runtime, package, and container image changes coordinated through `.agents/docs/discovered-issues.md`.
**Docs:** follow `~/.agents/docs/projects/dotnet.md`, `docker.md`, and `protobuf-grpc.md`; available dep docs include `xunit`, `coverlet`, `testcontainers-dotnet`, and `visual-studio-container-tools`; remaining PNet.Mesh NuGet doc gaps are tracked in user issue #380.
**SDK:** use installed .NET 10 SDK for tooling; add `global.json` only as part of the runtime migration decision.
**Restore:** `dotnet restore PNet.Mesh.sln`; `Noise.NET` is the restored Noise protocol package and `BouncyCastle.Cryptography` provides BLAKE2s helpers.
**Build/test:** run `dotnet build PNet.Mesh.sln -c Release --no-restore`; unit tests use `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none`; TUN tests use `dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none`; Testcontainers e2e uses the bounded method batches documented in `README.md`, not one monolithic suite command.
**TUN:** `src/PNet.Mesh.Tun` is optional Linux TUN support; it may require `/dev/net/tun`, `CAP_NET_ADMIN`, and `CAP_NET_RAW`, while core `PNet.Mesh` remains no-TUN/no-route-injection.
**Benchmarks:** restore/build in Release, then run BenchmarkDotNet with `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'`; macro JSON harnesses use `--macro in-memory` and `--macro udp-loopback`; privileged TUN uses `--tun-topology plan|preflight|create|teardown`, `--tun-benchmark pnet-mesh-tun|wireguard-go`, saved-result `--tun-compare --pnet <json> --wireguard <json>`, and wrapper `scripts/bench-tun-comparison.sh`; current baseline/policy/TUN workflow live in `.agents/docs/benchmarks/`.
**Performance changes:** claimed performance improvements require before/after micro and macro benchmark evidence; any latency, throughput, allocation, or GC regression must be explicitly justified with a source-backed reason.
**Formatting:** LF is canonical via `.gitattributes` + `.editorconfig`; verify scoped whitespace with `dotnet format whitespace PNet.Mesh.sln --include <paths> --no-restore --verify-no-changes --verbosity minimal`.
**NuGet:** use `scripts/packages.sh` for PackageReference maintenance; run vulnerable/outdated/deprecated package checks after restore works.
**Containers:** use Testcontainers for supported mesh e2e; use the named Testcontainers methods for mesh topology coverage.
**Protobuf:** schema source is `src/PNet.Mesh/Protos/MeshProtocol.proto`; prefer schema/descriptor validators over raw source-string assertions.
**Generated code:** treat `src/PNet.Mesh/Protos/MeshProtocol.cs` as generated from the proto; avoid hand edits unless the generator path is unavailable and documented.

Guide: `~/.agents/docs/projects/dotnet.md`, `~/.agents/docs/projects/docker.md`, `~/.agents/docs/projects/protobuf-grpc.md`
