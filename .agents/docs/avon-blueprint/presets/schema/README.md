# Schema Package Preset

Use this preset for protobuf bundles shared by Avon services, generated
clients, or external consumers.

## Checklist

- Install schema packages under `.schemas/`.
- Preserve canonical protobuf import paths in the rendered package output.
- Configure protobuf generators with `.schemas` as an import root.
- Keep package source staging paths local to the package project.
- Put consumer-facing package instructions under
  `.schemas/docs/<schema-package>/README.md`.
- Document the domain-specific behavior, implementer responsibilities, and API
  calls a consumer needs to build against the schema.
- Use `overlay` with release and SHA256 metadata for generated schema folders
  that share import roots with other packages.
- Use `replace-if-newer` for authoritative generated bundles or single files
  where a newer upstream package should replace the whole path.

## Layout

Rendered schema packages should keep the protobuf import tree intact:

```text
.schemas/
  <owner>/...
  <dependency>/...
  google/...
  docs/<schema-package>/README.md
```

Imports and generators should refer to canonical paths directly:

```proto
import "google/protobuf/timestamp.proto";
import "pnet/events/v1/event.proto";
import "<owner>/<api>/v1/resource.proto";
```

Do not render schemas below `vendor/` or another local staging prefix when that
prefix would leak into generated imports. Local source paths may still use
staging folders such as `proto/`, `third_party/`, or `generated/`; the package
recipe should map them into the canonical tree below `.schemas/`.

## Minimal Recipe

Use a named recipe such as `schema.package.yaml` when a repo publishes schemas
alongside another Avon package:

```yaml
name: acme-schema
type: package
target: .schemas
registry: registry.avon.local/avon-packages
output: dist/avon
version:
  base: 0.1.0
source:
  root: .
apply:
  defaultMode: conflict
  deleteRemoved: false
  files:
    - path: acme
      mode: overlay
      release: 2026.06.14
      hash: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
    - path: pnet
      mode: overlay
      release: 2026.06.14
      hash: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
    - path: google
      mode: overlay
      release: 2026.06.14
      hash: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
    - path: docs/acme-schema/README.md
      mode: replace-if-newer
      release: 2026.06.14
      hash: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
files:
  - from: proto/acme
    to: acme
  - from: third_party/pnet
    to: pnet
  - from: third_party/googleapis/google
    to: google
  - from: docs/schema-package.md
    to: docs/acme-schema/README.md
instructions:
  text: >-
    Register acme as a consuming API surface and reference
    .schemas/docs/acme-schema/README.md from the consumer agent docs.
  file: docs/acme-schema/README.md
  required: true
```

`instructions.file` is relative to the package target, so the recipe above
checks `.schemas/docs/acme-schema/README.md` after apply. The instruction text
should mention the full installed path because that is what humans and agents
need to add to downstream project docs.

## Domain API Contract

Schema packages should ship a consumer README that explains domain-specific
behavior, not only protobuf paths. Treat
`.schemas/docs/<schema-package>/README.md` as the domain contract a downstream
service or generated client can implement.

Cover these topics:

| Topic | Include |
|-------|---------|
| Domain model | Resources, ownership boundaries, identifiers, and lifecycle states. |
| Implementer behavior | Required handlers, side effects, idempotency, ordering, and retry rules. |
| API surface | RPC/service names, HTTP mappings, event subjects, request/response messages, and errors. |
| Consumer setup | Generator include paths, generated package names, auth headers, base URLs, and transport assumptions. |
| Compatibility | Stable fields, experimental fields, deprecated fields, versioning, and migration notes. |

Prefer executable examples over prose-only descriptions:

````markdown
## Implementing the API

- Implement `acme.billing.v1.InvoiceService/CreateInvoice`.
- Treat `request_id` as the idempotency key for retries.
- Return `ALREADY_EXISTS` when the key maps to an existing invoice.

## Calling the API

```ts
import { InvoiceServiceClient } from "./gen/acme/billing/v1/invoice_connect";

const client = new InvoiceServiceClient({ baseUrl: "https://billing.example" });
await client.createInvoice({ customerId, requestId, lines });
```
````

When the schema describes events or commands, include the behavior that gives
messages meaning: who publishes, who consumes, delivery guarantees, duplicate
handling, and which state transition each message represents. Keep
consumer-specific registration notes in project-owned docs and link back to the
schema README.

## Consumer Notes

The installed README should tell consumers which APIs are stable, which schema
folders are package-owned, and which generator include path to use. Keep
consumer-specific registration notes in project-owned docs such as
`.agents/docs/`, and reference the schema package README from there instead of
editing files under `.schemas/`.

Run the named recipe before publishing:

```bash
avon-package package verify schema
avon-package package render --recipe schema --dry-run --verbose
```
