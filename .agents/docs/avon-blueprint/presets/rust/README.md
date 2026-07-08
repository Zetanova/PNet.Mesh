# Rust Preset

Use this preset for Rust binaries, sidecars, or libraries that support an Avon
service.

## Checklist

- Commit `Cargo.lock` for deployable binaries.
- Document the Rust toolchain version used to build package artifacts.
- Prefer source builds in a container when node architecture matters.
- Vendor crates only when the target node cannot reach the registry or private
  Git source reliably.
- Add `cargo test` or focused package tests before publishing.

## Minimal Recipe

```yaml
name: my-rust-tool
type: package
target: packages/my-rust-tool
version:
  base: 0.1.0
apply:
  defaultMode: replace
  deleteRemoved: true
files:
  - from: Cargo.toml
    to: Cargo.toml
  - from: Cargo.lock
    to: Cargo.lock
  - from: src
    to: src
```

## Notes

For project-root starter files, switch to `target: .` and
`apply.defaultMode: create-if-missing` so local package choices survive
blueprint updates.
