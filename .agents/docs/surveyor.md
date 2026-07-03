---
last-refined: 2026-07-01
---

# Surveyor Reference

## Naming Conventions

- Use `PNetMesh*` types for mesh runtime code.
- Keep test classes aligned to the feature under test, with `*Tests` suffixes.
- Prefer explicit method names that describe the scenario and expected outcome.

## Code Patterns

- Preserve the existing channel-and-session control loop structure.
- Keep endpoint and routing decisions in `PNetMeshServer`.
- Keep transport/session state transitions inside `PNetMeshSession`.
- Prefer returning `bool` from helper methods when callers need to gate follow-up work.

## Project Structure

- `src/PNet.Mesh/` holds runtime code.
- `src/PNet.Mesh.UnitTests/` holds unit tests.
- `src/PNet.Mesh.E2ETests/` holds Testcontainers-backed integration coverage.
- `src/PNet.Mesh/Protos/` is the protobuf schema and generated surface.

## Framework Patterns

- Target `net10.0` and keep runtime, package, and container changes coordinated.
- Use Testcontainers for supported mesh e2e coverage.
- Prefer schema/descriptor validation over raw source-string assertions for protobuf behavior.

## Tool Configs

- `dotnet restore PNet.Mesh.sln`
- `dotnet build PNet.Mesh.sln -c Release --no-restore`
- `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none`
- Use the bounded Testcontainers e2e method batches documented in `README.md`.
- `dotnet format whitespace PNet.Mesh.sln --include <paths> --no-restore --verify-no-changes --verbosity minimal`
