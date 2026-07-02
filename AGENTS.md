# PNet.Mesh

Project guidance for Codex agents working in this repository.

<!-- RULES IMPORT: generated from .agents/rules/*.md; edit source files there, then rerun /refine. -->

### best-practices.md

# Project Best Practices

**Type:** .NET SDK solution + xUnit unit/e2e tests + Testcontainers-based e2e + protobuf schema.
**Runtime:** project targets `net10.0`; keep runtime, package, and container image changes coordinated through `.agents/docs/discovered-issues.md`.
**Docs:** follow `~/.agents/docs/projects/dotnet.md`, `docker.md`, and `protobuf-grpc.md`; dependency docs include `xunit`, `coverlet`, `testcontainers-dotnet`, `visual-studio-container-tools`, and `bouncycastle-cryptography`.
**SDK:** use installed .NET 10 SDK for tooling; add `global.json` only as part of the runtime migration decision.
**Restore:** `dotnet restore PNet.Mesh.sln`; `Noise.NET` is the restored Noise protocol package and `BouncyCastle.Cryptography` provides BLAKE2s helpers.
**Build/test:** run `dotnet build PNet.Mesh.sln -c Release --no-restore`; unit tests use `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none`; TUN tests use `dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none`; Testcontainers e2e uses the bounded method batches documented in `README.md`, not one monolithic suite command.
**Formatting:** LF is canonical via `.gitattributes` + `.editorconfig`; verify scoped whitespace with `dotnet format whitespace PNet.Mesh.sln --include <paths> --no-restore --verify-no-changes --verbosity minimal`.
**NuGet:** use `scripts/packages.sh` for PackageReference maintenance; run vulnerable/outdated/deprecated package checks after restore works.
**Containers:** use Testcontainers for supported mesh e2e; use the named Testcontainers methods for mesh topology coverage.
**Protobuf:** schema source is `src/PNet.Mesh/Protos/MeshProtocol.proto`; prefer schema/descriptor validators over raw source-string assertions.
**Generated code:** treat `src/PNet.Mesh/Protos/MeshProtocol.cs` as generated from the proto; avoid hand edits unless the generator path is unavailable and documented.

Guide: `~/.agents/docs/projects/dotnet.md`, `~/.agents/docs/projects/docker.md`, `~/.agents/docs/projects/protobuf-grpc.md`

<!-- END RULES IMPORT -->
