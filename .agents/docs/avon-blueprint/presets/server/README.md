# Server Preset

Use this preset for a tenant service, API, worker, or socket-activated daemon.

## Checklist

- Define `tenant.yaml` with one service per deployable process.
- Prefer socket activation for HTTP or RPC services that can idle.
- Put reusable runtime code under `functions/`, `server/`, or `assets/`.
- Add `avon-package.yaml` with `target: tenants/<name>` for tenant packages.
- Use `apply.defaultMode: replace` only when the tenant directory is owned by
  the package.
- Add tests for parser/generator output before publishing.

## Minimal Recipe

```yaml
name: my-service
type: package
target: tenants/my-service
version:
  base: 0.1.0
apply:
  defaultMode: replace
  deleteRemoved: true
files:
  - from: tenant.yaml
    to: tenant.yaml
  - from: functions
    to: functions
```

## Notes

Use project-root packages only for shared docs or scaffolding. Tenant runtime
payloads should target `tenants/<name>` so updates can remove stale generated
files safely.
