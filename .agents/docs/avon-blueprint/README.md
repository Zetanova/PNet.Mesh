---
last-refined: 2026-06-11
status: stable
brief: "quick-start+project-presets+update-flow"
views:
  package-author: "quick-start+package-shape+apply-safety+maintenance"
---

# Avon Blueprint

Reusable package and library blueprint for downstream Avon projects.

## Quick Start

Install the blueprint in a downstream repo:

```bash
avon-package pull avon-blueprint
```

Then choose one preset under `.agents/docs/avon-blueprint/presets/` and adapt
only the project-owned names, paths, and runtime details.

## Project Presets

| Preset | Use When | Start Here |
|--------|----------|------------|
| Server | A service exposes HTTP, sockets, workers, or a tenant package. | [server](presets/server/README.md) |
| Frontend | A static or built frontend should ship behind Avon Caddy/FaaS. | [frontend](presets/frontend/README.md) |
| Go | A reusable Go binary or library should ship as source-built Avon package content. | [go](presets/go/README.md) |
| Rust | A reusable Rust binary or library should ship as vendored or source-built package content. | [rust](presets/rust/README.md) |
| Schema Package | Protobuf bundles should install with canonical import paths and consumer domain/API docs under `.schemas/`. | [schema](presets/schema/README.md) |

## Package Shape

Recommended downstream package layout:

```text
avon-package.yaml
package.json
tenant.yaml
functions/
assets/
.agents/docs/
```

Use `avon-package.yaml` as the source of truth for rendering and publishing
new package projects:

```bash
avon-package package verify
avon-package package render --dry-run --verbose
avon-package package publish
```

Use `package.json` for compatibility with existing `avon-package publish-all`
catalog scans.

## Apply Safety

Project-root packages must preserve local context. Prefer:

| Mode | Use For |
|------|---------|
| `create-if-missing` | Guides, starter files, default recipes, and local-owned config. |
| `conflict` | Files where any drift should stop the apply. |
| `merge-block` | Managed blocks inside files that also contain local edits. |
| `replace-if-newer` | Versioned generated bundles such as `.schemas` protobuf trees. |

Avoid `replace` and `deleteRemoved: true` for project-root blueprints unless the
target directory is wholly package-owned.

## Update Flow

Downstream projects keep the blueprint as a normal Avon package dependency:

```yaml
packages:
  - name: avon-blueprint
    version: "0.1.0"
```

Run package pulls during maintenance:

```bash
avon-package pull avon-blueprint
```

Because this blueprint uses `create-if-missing`, updates add new guide files and
presets without overwriting project choices. When a preset needs an intentional
managed refresh, place only that section inside `BEGIN/END AVON MANAGED <name>`
markers and switch that file to `merge-block` in a future recipe.

## Dependency Transport

Use the simplest transport that keeps updates repeatable:

| Dependency | First Choice | When To Vendor |
|------------|--------------|----------------|
| Avon packages | `packages.yaml` + `packages.lock` | When a package is not published yet. |
| Protobuf schemas | Avon schema package targeting `.schemas/` | When the schema package is not published yet. |
| Go modules | Private Git or module proxy | When offline node builds need the source snapshot. |
| Rust crates | `Cargo.lock` + registry or private Git | When the target registry is not reachable from nodes. |
| Static assets | Package files or archives | When upstream is not versioned or stable. |

Vendored snapshots are acceptable when they are versioned, documented, and easy
to refresh from upstream.

## Maintenance

Refresh `avon-blueprint` when any of these change:

- Avon package recipe fields, apply modes, or install instruction behavior.
- Project docs conventions under `.agents/docs/`.
- Supported Node, Go, Rust, Caddy, Podman, or Quadlet versions.
- New common project forms need reusable package guidance.
- Existing presets gain regression coverage or a safer default workflow.

Run the recipe verification after every edit:

```bash
cd avon-system/packages/avon-blueprint
avon-package package verify
avon-package package render --dry-run --verbose
```
