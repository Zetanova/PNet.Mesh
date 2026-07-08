# Go Preset

Use this preset for Go host tools, sidecars, daemons, or reusable libraries.

## Checklist

- Keep `go.mod` and the `Containerfile` builder image aligned with the Avon Go
  baseline.
- Build static binaries with `CGO_ENABLED=0` unless a dependency requires cgo.
- Use `ARG TARGETARCH=amd64` and `GOARCH=${TARGETARCH}` in Containerfiles.
- Ship source for node-local or container-local builds; avoid prebuilt
  per-architecture artifacts unless the package already follows that exception.
- Add `go test ./...` coverage before publishing.

## Minimal Recipe

```yaml
name: my-go-tool
type: package
target: packages/my-go-tool
version:
  base: 0.1.0
apply:
  defaultMode: replace
  deleteRemoved: true
files:
  - from: my-go-tool
    to: my-go-tool
  - from: package.json
    to: package.json
```

## Notes

For reusable libraries consumed by other projects, private Git or vendored
snapshots are both acceptable. Choose the transport that keeps package builds
repeatable on the target nodes.
