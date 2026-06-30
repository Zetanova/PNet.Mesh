# Project Best Practices

**Type:** .NET SDK solution + xUnit tests + Docker Compose test topology + protobuf schema.
**Runtime:** project targets `net10.0`; keep runtime, package, and container image changes coordinated through `.agents/docs/discovered-issues.md`.
**Docs:** follow `~/.agents/docs/projects/dotnet.md`, `docker.md`, and `protobuf-grpc.md`; dependency docs include `xunit`, `coverlet`, and `visual-studio-container-tools`.
**SDK:** use installed .NET 10 SDK for tooling; add `global.json` only as part of the runtime migration decision.
**Restore:** `dotnet restore PNet.Mesh.sln`; `Noise.NET` is the restored Noise protocol package.
**Build/test:** run `dotnet build PNet.Mesh.sln -c Release --no-restore`; unit tests use `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none`.
**Formatting:** LF is canonical via `.gitattributes` + `.editorconfig`; verify scoped whitespace with `dotnet format whitespace PNet.Mesh.sln --include <paths> --no-restore --verify-no-changes --verbosity minimal`.
**NuGet:** use `scripts/packages.sh` for PackageReference maintenance; run vulnerable/outdated/deprecated package checks after restore works.
**Containers:** use `docker compose`; keep Compose files versionless; .NET 10 base images back the test-node container; smoke with `scripts/e2e-mesh-topology.sh`.
**Protobuf:** schema source is `src/PNet.Mesh/Protos/MeshProtocol.proto`; prefer schema/descriptor validators over raw source-string assertions.
**Generated code:** treat `src/PNet.Mesh/Protos/MeshProtocol.cs` as generated from the proto; avoid hand edits unless the generator path is unavailable and documented.

Guide: `~/.agents/docs/projects/dotnet.md`, `~/.agents/docs/projects/docker.md`, `~/.agents/docs/projects/protobuf-grpc.md`
