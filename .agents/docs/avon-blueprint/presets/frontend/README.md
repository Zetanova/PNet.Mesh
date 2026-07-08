# Frontend Preset

Use this preset for static UI assets, built frontend bundles, or a frontend
served by an Avon HTTP service.

## Checklist

- Keep source build tooling in the project repo, not in the installed tenant
  payload, unless nodes build the frontend themselves.
- Publish built static assets under `assets/` or a service-owned directory.
- Declare the Caddy/FaaS route in `tenant.yaml`.
- Use immutable asset names or a package version bump for cache-sensitive
  releases.
- Prefer `create-if-missing` for starter config in project-root packages.

## Minimal Static Package

```yaml
name: my-frontend
type: package
target: tenants/my-frontend
version:
  base: 0.1.0
apply:
  defaultMode: replace
  deleteRemoved: true
files:
  - from: tenant.yaml
    to: tenant.yaml
  - from: dist
    to: assets/public
```

## Notes

If the frontend needs runtime API discovery, document the expected Avon
hostname and keep the route declaration in the tenant config rather than in
ad hoc deploy notes.
