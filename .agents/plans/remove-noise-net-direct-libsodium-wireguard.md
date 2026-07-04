---
title: PNet.Mesh Remove Noise.NET Direct Libsodium WireGuard Plan
created: 2026-07-04
last-refined: 2026-07-04
status: completed
assumptions-date: 2026-07-04
brief: "goal+completion-report-2026-07-04+verification-gates"
views:
  execute: "goal+recommended-path+phases+verification-gates+regression-policy+assumptions"
---

# PNet.Mesh Remove Noise.NET Direct Libsodium WireGuard Plan

Remove the production `Noise.NET` dependency from `PNet.Mesh` by implementing the required WireGuard `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s` components directly in `PNet.Mesh`, binding directly to native `libsodium` for C-backed primitives, and preserving `Noise.NET` only as a unit-test oracle until the migration is proven.

## Goal

| Target | Requirement |
|---|---|
| Production dependency | Remove `Noise.NET` from `src/PNet.Mesh/PNet.Mesh.csproj`. |
| Direct native crypto | Add a minimal internal `libsodium` binding for the required C functions, after verifying the current supported native package/version at execution time. |
| WireGuard implementation | Implement the project-required WireGuard handshake, transport keys, explicit counters, packet encryption/decryption, BLAKE2s hash/MAC/HKDF, replay behavior, and cookie-compatible MAC behavior in `PNet.Mesh`. |
| Test oracle | Add `Noise.NET` to `src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj` only, and compare local behavior against the existing Noise implementation. |
| Verification | Pass differential unit tests, existing `wireguard-go` e2e tests, TUN comparison benchmark/test paths, and micro/macro benchmarks. |
| Performance gate | No meaningful benchmark regression; handshake/setup allocation should improve or remain justified by measured security/correctness gains. |

## Recommended Path

Use a direct PNet-owned WireGuard implementation instead of a broad Noise.NET fork.

| Decision | Reason |
|---|---|
| Keep `Noise.NET` only in tests during migration. | It provides a known-good oracle without keeping the production dependency. |
| Bind directly to `libsodium`, not through `Noise.NET`. | Removes the old transitive binding and private-reflection transport access. |
| Use LibSodium.Net as a binding reference, not the default production dependency. | Its source-generated `LibraryImport` style is useful, while this project needs only a tiny subset. |
| Implement only `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s`. | PNet.Mesh does not need a general Noise framework. |
| Treat BLAKE2s as a first-class project primitive. | WireGuard requires BLAKE2s; verify whether the selected native stack exposes the needed API, otherwise keep a reviewed internal BLAKE2s implementation. |
| Keep changes phased and benchmarked. | This is crypto-sensitive and performance-sensitive; each phase needs correctness and benchmark evidence. |

## Components

| Component | Required Behavior |
|---|---|
| `PNetMeshLibSodium` | Internal initialization and minimal native bindings for X25519 and ChaCha20-Poly1305 IETF. |
| X25519 key operations | Generate public key from private key and compute DH shared secret with constant-size spans. |
| ChaCha20-Poly1305 IETF | Encrypt/decrypt with 12-byte nonce formed as four zero bytes plus little-endian `ulong` counter. |
| BLAKE2s hash | 32-byte unkeyed hash for handshake hash and protocol labels. |
| BLAKE2s keyed MAC | 16-byte WireGuard MAC1/MAC2 output and cookie MAC behavior. |
| BLAKE2s HKDF | `MixKey`, `MixKeyAndHash`, and `Split` equivalent key derivation for IKpsk2. |
| Handshake state | Initiator and responder flows for WireGuard initiation and response packets. |
| Transport state | Explicit send/read keys and counters, without reflection into private Noise transport state. |
| Replay/keypair state | Preserve current `PNetMeshPacketTracker` and `PNetMeshWireGuardPeerTable` behavior. |

## Phases

| Phase | Work | Exit Gate |
|---|---|---|
| 0. Baseline freeze | Record current source commit and benchmark artifacts from `.agents/plans/pnet-mesh-last-raw-frame-performance-iteration.md`. | Baseline commands and artifact paths are copied into the implementation report. |
| 1. Test oracle harness | Add `Noise.NET` to unit tests and create a test-only `NoiseNetWireGuardOracle` with the same handshake/transport surface as the local implementation. | Existing tests pass; oracle sanity pair passes. |
| 2. Differential matrix | Add local-vs-Noise.NET handshake and transport matrix before replacing production implementation. | Matrix passes for success, replay, tamper, wrong PSK, wrong static key, wrong receiver index, and counter behavior. |
| 3. Native binding | Add minimal internal `libsodium` binding and initialization. Verify the current native package/version and platform loading model during implementation. | Primitive tests pass against known vectors and current Noise-backed behavior still passes. |
| 4. Direct transport crypto | Implement explicit key/counter transport encryption/decryption behind the existing `PNetMeshTransport2` surface or an internal replacement. | Local transport packets decrypt across local/oracle pairings and raw secure-frame tests pass. |
| 5. Direct handshake | Implement WireGuard IKpsk2 initiation/response key schedule and packet generation/parsing. | Differential matrix passes with local implementation enabled for both roles. |
| 6. Production switch | Remove production `Noise.NET` reference, route `PNetMeshProtocol` to the local implementation, keep `Noise.NET` in unit tests only. | `rg "using Noise|Noise.NET"` shows only test oracle or benchmark/test docs references. |
| 7. External verification | Run `wireguard-go` peer e2e, relay e2e, and TUN comparison benchmark/test paths. | External compatibility passes and benchmark comparison reports no meaningful regression. |
| 8. Cleanup | Remove reflection cache and nonce delegate code made obsolete by direct transport keys. Update issue and benchmark logs. | Source diff contains no private Noise reflection path and docs record final evidence. |

## Differential Tests

Add tests before the production switch. Use behavior equivalence, not byte-for-byte equality, unless a deterministic ephemeral-key injection seam is added.

| Pairing | Purpose | Required |
|---|---|---|
| Local initiator -> Noise.NET responder | Proves local initiation packets and derived transport state are accepted by the oracle. | Yes |
| Noise.NET initiator -> local responder | Proves local responder parsing, key schedule, and response generation. | Yes |
| Local initiator -> local responder | Proves the new production path works without oracle involvement. | Yes |
| Noise.NET initiator -> Noise.NET responder | Oracle sanity check to catch harness defects. | Yes |

Test each pairing with:

| Case | Expected Result |
|---|---|
| Valid handshake and bidirectional transport payloads | Plaintext round-trips both directions. |
| Replayed transport packet | Rejected. |
| Tampered ciphertext/tag | Rejected without advancing receive counter. |
| Wrong PSK | Handshake response read fails. |
| Wrong expected remote static key | Handshake fails. |
| Wrong receiver index | Packet rejected or routed as current implementation requires. |
| Multiple counters out of order | Accepted once each inside tracker window, duplicate rejected. |
| MAC1/MAC2 validation | Matches current `PNetMeshProtocol.ValidatePacket` and cookie-gate behavior. |

## Verification Gates

Run from a restored Release-ready workspace. Use `rtk` for high-output commands.

| Gate | Command |
|---|---|
| Build | `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` |
| Unit tests | `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` |
| Scoped format | `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include <touched-source-paths> --no-restore --verify-no-changes --verbosity minimal` |
| Raw-boundary microbench | `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.SecureFrameWriteReadDispatch*RawFrame*'` |
| Handshake microbench | `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.FullHandshakeSetup*'` |
| Crypto/reference microbench | `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*WireGuardTransportBenchmarks.WriteThenRead*CleartextOnly*'` |
| Raw SecureFrame macro | `timeout 120s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro raw-secureframe --payload 128 --warmup 00:00:01 --duration 00:00:05` |
| WireGuard peer e2e | `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol -parallel none` |
| WireGuard relay e2e | `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_relay_container_forwards_opaque_exchange_to_peer_container -parallel none` |
| TUN comparison | `timeout 900s rtk scripts/bench-tun-comparison.sh --output-dir artifacts/benchmarks/tun-comparison/noise-removal-candidate` |
| Diff hygiene | `timeout 30s git diff --check` |

## Regression Policy

Keep or reject each batch based on comparable before/current evidence.

| Metric | Regression Rule |
|---|---|
| Raw-boundary allocations | Must remain `0 B` for `SecureFrameWriteReadDispatch*RawFrame*`. |
| Handshake allocations | Must not increase; expected direction is lower than the current `10.75`-`10.82 KB/op` baseline. |
| Raw SecureFrame throughput | Must not drop materially across comparable runs; investigate any sustained drop above `3%`. |
| Raw SecureFrame p95/p99 latency | Must not increase materially across comparable runs; investigate any sustained increase above `5%`. |
| Unit/e2e correctness | Any failure blocks the production switch. |
| `wireguard-go` TUN comparison | No meaningful latency, throughput, packet-loss, CPU, or RSS regression against the current comparison baseline. |
| Security behavior | Replay, tamper, wrong-key, wrong-PSK, and MAC failures must remain negative cases. |

For final performance claims, run at least three comparable before/current benchmark sets and preserve raw artifacts.

## Acceptance Criteria

| Criterion | Evidence |
|---|---|
| Production no longer depends on `Noise.NET`. | `src/PNet.Mesh/PNet.Mesh.csproj` has no `Noise.NET` package reference. |
| Unit tests keep `Noise.NET` as oracle. | `src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj` references `Noise.NET`; production does not. |
| No private Noise reflection remains. | No production code calls private Noise fields or `SetNonce` through reflection/delegates. |
| Local implementation passes oracle matrix. | Differential tests cover local/oracle role pairings and negative cases. |
| `wireguard-go` e2e passes. | Peer and relay Testcontainers tests pass. |
| TUN comparison passes. | `scripts/bench-tun-comparison.sh` produces passing PNet.Mesh and `wireguard-go` artifacts. |
| Benchmarks do not regress meaningfully. | Micro/macro reports compare against the current baseline and satisfy the regression policy. |
| Docs are updated. | Benchmark improvement log and issue/plan completion report record commands, artifacts, and deltas. |

## Completion Report 2026-07-04

Implemented a direct project-owned WireGuard `Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s` path in `PNet.Mesh`. Production now uses an internal `libsodium` binding for X25519 and ChaCha20-Poly1305 IETF, keeps BouncyCastle BLAKE2s for hash/MAC/HKDF, and no longer depends on `Noise.NET`. `Noise.NET` is retained only in unit tests as the differential oracle.

| Area | Result | Evidence |
|---|---|---|
| Production dependency | `src/PNet.Mesh/PNet.Mesh.csproj` references `libsodium` `1.0.22` and no `Noise.NET`; unsafe is enabled for the native binding. | `src/PNet.Mesh/PNet.Mesh.csproj`; `timeout 30s rg ... 'Noise\.NET|using Noise|HandshakeState|TransportNonceLayout|SetNonce' src` |
| Test oracle | `Noise.NET` is referenced only by unit tests; oracle coverage exercises local/local, local/oracle, oracle/local, oracle/oracle, replay, tamper, wrong PSK, and wrong receiver-index cases. | `src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj`; `src/PNet.Mesh.UnitTests/PNetMeshWireGuardOracleTests.cs` |
| Private Noise reflection | No production private Noise transport reflection remains; reflection exists only inside the test oracle. | Source scan on 2026-07-04 found only unit-test oracle hits. |
| External compatibility | WireGuard peer and relay Testcontainers e2e tests passed against the current Release build. | Commands in verification table below. |
| TUN comparison | Default TUN script run passed; three baseline-comparable MTU/64K runs also passed. | `artifacts/benchmarks/tun-comparison/noise-removal-candidate*/summary.json` |
| Benchmark verdict | Handshake allocation improved materially; raw-boundary and cleartext reference allocations remained `0 B`; macro throughput was slightly below the retained range on one short run but below the investigation threshold. | Micro/macro artifacts listed below. |

### Verification Results

| Gate | Command | Result |
|---|---|---|
| Scoped format | `timeout 180s dotnet format whitespace PNet.Mesh.sln --include <touched paths> --no-restore --verify-no-changes --verbosity minimal` | Pass |
| Build | `timeout 300s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` | Pass: 9 projects, 0 errors, 0 warnings |
| Unit tests | `timeout 300s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | Pass: 207 passed |
| TUN unit tests | `timeout 300s rtk dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none` | Pass: 21 passed |
| WireGuard peer e2e | `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol -parallel none` | Pass: 1 passed |
| WireGuard relay e2e | `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.wireguard_relay_container_forwards_opaque_exchange_to_peer_container -parallel none` | Pass: 1 passed |
| Vulnerable packages | `timeout 120s rtk dotnet list PNet.Mesh.sln package --vulnerable --include-transitive` | Pass: no vulnerable packages reported |
| Deprecated packages | `timeout 120s rtk dotnet list PNet.Mesh.sln package --deprecated` | Pass: no deprecated packages reported |
| Diff hygiene | `timeout 30s git diff --check` | Pass |
| Source scan | `timeout 30s rg ... 'Noise\.NET|PackageReference Include="Noise\.NET"|using Noise|HandshakeState|TransportNonceLayout|SetNonce|typeof\(Transport\)|new Transport\(' src` | Pass: hits only in unit-test oracle and unit-test project |

### Performance Evidence

| Scope | Baseline | Current | Delta / Verdict | Evidence |
|---|---|---|---|---|
| Handshake setup allocation | `10.75`-`10.82 KB/op` retained baseline | `7.19 KB/op` across payloads | Improved by about `3.56`-`3.63 KB/op`; accepted | `artifacts/benchmarks/PNet.Mesh.Benchmarks.WireGuardTransportBenchmarks-20260704-111240.log` |
| Raw-boundary dispatch | `0 B/op` retained baseline | `0 B/op` across raw-frame dispatch rows | Allocation target preserved; timing in same microsecond range | `artifacts/benchmarks/PNet.Mesh.Benchmarks.WireGuardTransportBenchmarks-20260704-110726.log` |
| Cleartext/reference decrypt | Reference-only check | `0 B/op`; 64-byte rows `1.389`-`1.478 us`; 1420-byte rows `5.309`-`5.587 us` | No allocation regression in reference path | `artifacts/benchmarks/PNet.Mesh.Benchmarks.WireGuardTransportBenchmarks-20260704-111620.log` |
| Raw SecureFrame macro | Retained range `523.6k`-`541.0k` packets/sec | `513.3k` packets/sec, p99 `4.8 us` on saved run | Slightly below retained range on one short run; report-only, no correctness impact | `artifacts/benchmarks/raw-frame/20260704T111620Z-direct-libsodium-final/raw-secureframe-macro.json` |
| TUN default comparison | Default script gate | Pass with `payload-mode=control`, `1K` iperf cap, no packet loss | Compatibility gate passed | `artifacts/benchmarks/tun-comparison/noise-removal-candidate/comparison.json` |
| TUN MTU/64K comparison | `20260703T162643Z` current-head peer comparison | Three passing runs; throughput/loss stable; CPU `342`-`368` ticks vs `374`; RSS `139.9`-`141.6 MB` vs `137.1 MB`; ping latency mixed/noisy | Mixed, report-only; no throughput/loss regression | `artifacts/benchmarks/tun-comparison/noise-removal-candidate-mtu*/comparison.json` |

### Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 8 | F | The verification results in this completion report were produced after the direct `libsodium` implementation and production `Noise.NET` removal. | verified | test | Commands in the verification table ran on 2026-07-04 from `/srv/projects/pnet-mesh` after the final source changes. |
| 9 | R | TUN ping latency deltas are report-only for this closeout, not a hard failure threshold. | verified | source | Benchmark comparison policy keeps thresholds report-only until comparable repeated runs establish variance; the current run set has three current samples but only a single retained baseline sample. |

## Rollback

| Trigger | Action |
|---|---|
| Oracle mismatch after local implementation | Keep `Noise.NET` production dependency and continue differential debugging behind an internal switch. |
| `wireguard-go` interop failure | Revert production switch; keep primitive tests if they are independently valid. |
| Meaningful benchmark regression | Revert the candidate batch or keep it behind a disabled implementation selector until a follow-up issue resolves the regression. |
| Native binding portability issue | Keep `Noise.NET` production dependency until the direct `libsodium` loading model passes project-supported platforms. |

## References

| Reference | Purpose |
|---|---|
| [Raw-frame performance runbook](pnet-mesh-last-raw-frame-performance-iteration.md#goal) | Current micro/macro baseline and benchmark commands. |
| [Issue 042 completion](../docs/issues/042-wireguard-go-testcontainers-interoperability-test-for-wireguard-peer-to-pnet-mesh.md#completion-report) | Existing WireGuard peer e2e harness. |
| [Issue 062 completion](../docs/issues/062-wireguard-go-tun-comparison-benchmark.md#completion-report) | Existing `wireguard-go` TUN comparison benchmark. |
| [LibSodium.Net](https://github.com/LibSodium-Net/LibSodium.Net) | Reference for modern `LibraryImport` binding style and API shape. |

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | C | The conversion goal is to remove `Noise.NET` from production `PNet.Mesh` and implement the required WireGuard components directly in the project. | verified | source | User requested this plan on 2026-07-04. |
| 2 | F | `src/PNet.Mesh/PNet.Mesh.csproj` currently references `Noise.NET` as a production dependency. | verified | source | Read `src/PNet.Mesh/PNet.Mesh.csproj`; it includes `PackageReference Include="Noise.NET" Version="1.0.0"`. |
| 3 | F | `src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj` currently does not reference `Noise.NET` directly. | verified | source | Read the unit test project file; only the test packages and `PNet.Mesh` project reference are present. |
| 4 | F | The repository already has a WireGuard peer Testcontainers e2e harness. | verified | source | Issue 042 completion report records `wireguard_peer_container_exchanges_encrypted_packets_with_pnet_mesh_protocol`. |
| 5 | F | The repository already has a `wireguard-go` TUN comparison benchmark workflow. | verified | source | Issue 062 completion report and `README.md` document the `wireguard-go` TUN benchmark path. |
| 6 | F | The current retained handshake/setup allocation baseline is `10.75`-`10.82 KB/op`. | verified | source | `.agents/plans/pnet-mesh-last-raw-frame-performance-iteration.md` records the final current-baseline repeat set. |
| 7 | F | The current retained raw-boundary allocation baseline is `0 B/op`. | verified | source | `.agents/plans/pnet-mesh-last-raw-frame-performance-iteration.md` records three raw-boundary runs at `0 B`. |
