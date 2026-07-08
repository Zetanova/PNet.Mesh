# PNet Schema Package

PNet schema package for Avon projects.

## Import Root

Configure protobuf generators with `.schemas` as an import root.

## Stable Imports

```proto
import "pnet/type/blob.proto";
import "pnet/type/job.proto";
import "pnet/type/job_run.proto";
import "pnet/type/leader.proto";
import "pnet/type/route_policy.proto";
import "pnet/v1/blob_service.proto";
import "pnet/v1/blob_content_service.proto";
import "pnet/v1/cluster_leader_service.proto";
import "pnet/v1/job_service.proto";
import "pnet/v1/job_run_service.proto";
import "pnet/v1/route_policy_options.proto";
import "google/api/annotations.proto";
```

## Exported Paths

| Path | Purpose |
|------|---------|
| `pnet/type/` | PNet resource and shared type definitions. |
| `pnet/v1/` | PNet v1 service APIs. |
| `google/api/` | Google API annotations used by PNet schemas. |
| `docs/pnet-schema/` | Package-local consumer docs. |

## Proto Store Snapshots

Use [PNet Proto Store Concepts](proto-store.md) for the shared vocabulary around
`PNetProtoStore`, store entries, store snapshots, source status, stream
catch-up, and projections.

## Filter Language

Services that expose a `filter` field use the PNet AIP-160-style filter syntax.
See [PNet Filter Language](filter.md) for operators, literals, null handling,
wildcards, maps, and Blob metadata fields.

## Blob Clients

Use these exported schema surfaces together when generating blob clients:

| Surface | Purpose | Common resource names |
|---------|---------|-----------------------|
| `pnet.v1.BlobService` | Blob metadata lifecycle. | `tenants/{tenant}/blobs/{blob}`<br>`tenants/{tenant}/scopes/{scope}/blobs/{blob}` |
| `pnet.v1.BlobContentService` | Blob content chunk writes, reads, and blob info lookups. | Blob resource names for content transfer; `BlobInfo` names ending in `/info` for info lookup. |
| `pnet.type.Blob` | Materialized blob metadata returned by `BlobService`. | same blob resource names as `BlobService` |
| `pnet.type.BlobInfo` | Binary stream info for the head or a specific content version. | `tenants/{tenant}/blobs/{blob}/info`<br>`tenants/{tenant}/scopes/{scope}/blobs/{blob}/info`<br>`tenants/{tenant}/blobs/{blob}/versions/{version}/info`<br>`tenants/{tenant}/scopes/{scope}/blobs/{blob}/versions/{version}/info` |

### Metadata Lifecycle

`BlobService` covers read, list, create, update, delete, undelete, and expunge operations for blob metadata.
Generated clients should treat `pnet.type.Blob.name` as the canonical resource name and use the scoped or unscoped form that matches the resource being addressed.

Scoped metadata create request:

```http
POST /v1/tenants/acme/scopes/profile/blobs?blob_id=avatar-001
Content-Type: application/json

{
  "tags": ["profile"],
  "annotations": {
    "example.com/owner": "user-123"
  }
}
```

Unscoped metadata create request:

```http
POST /v1/tenants/acme/blobs?blob_id=manual-001
Content-Type: application/json

{
  "tags": ["manual"],
  "annotations": {
    "example.com/source": "import"
  }
}
```

Scoped metadata patch request:

```http
PATCH /v1/tenants/acme/scopes/profile/blobs/avatar-001?update_mask=annotations,tags
Content-Type: application/json

{
  "name": "tenants/acme/scopes/profile/blobs/avatar-001",
  "tags": ["profile", "avatar"],
  "annotations": {
    "example.com/owner": "user-123",
    "example.com/purpose": "profile-photo"
  }
}
```

Unscoped metadata patch requests use the same body shape with an unscoped name, for example
`PATCH /v1/tenants/acme/blobs/manual-001?update_mask=annotations`.

Temporary blob convention:

- Add the well-known tag `temporary` to `pnet.type.Blob.tags` to mark a blob as temporary.
- Raw `/content` uploads can send `tag: temporary`; the API persists it as a blob tag.
- Promote a blob to durable metadata by removing `temporary` with `BlobService.UpdateBlob` and `update_mask=tags`.

List partial-success convention:

- Set `returnPartialSuccess` only for broad list parents that can span multiple backend blob storage partitions, such as `tenants/{tenant}/scopes/-`.
- When partial success is returned, `ListBlobsResponse.unreachable` contains service-relative collection or resource names at the finest backend-known granularity.
- Narrow list parents that map to one required backend partition fail with a typed error instead of returning partial results.

### Content Lifecycle

`BlobContentService` covers chunked content writes and reads for an existing blob.

JSON-transcoded RPC routes:

- `PUT /v1/{name=tenants/*/scopes/*/blobs/*}:writeChunk`
- `PUT /v1/{name=tenants/*/blobs/*}:writeChunk`
- `GET /v1/{name=tenants/*/scopes/*/blobs/*}:read`
- `GET /v1/{name=tenants/*/blobs/*}:read`
- `GET /v1/{name=tenants/*/scopes/*/blobs/*/info}`
- `GET /v1/{name=tenants/*/blobs/*/info}`
- `GET /v1/{name=tenants/*/scopes/*/blobs/*/versions/*/info}`
- `GET /v1/{name=tenants/*/blobs/*/versions/*/info}`

Raw media routes exposed by the API layer:

- `PUT /v1/{name=tenants/*/scopes/*/blobs/*}/content`
- `PUT /v1/{name=tenants/*/blobs/*}/content`
- `GET /v1/{name=tenants/*/scopes/*/blobs/*}/content`
- `GET /v1/{name=tenants/*/blobs/*}/content`

Scoped JSON-transcoded chunk write:

```http
PUT /v1/tenants/acme/scopes/profile/blobs/avatar-001:writeChunk
Content-Type: application/json

{
  "version": "2026-06-14T15:00:00Z",
  "chunkIndex": 0,
  "headers": [
    {
      "name": "content-type",
      "values": ["image/png"]
    }
  ],
  "data": "iVBORw0KGgo="
}
```

Unscoped JSON-transcoded chunk write uses the same payload shape:

```http
PUT /v1/tenants/acme/blobs/manual-001:writeChunk
Content-Type: application/json

{
  "version": "2026-06-14T15:00:00Z",
  "chunkIndex": 0,
  "data": "SGVsbG8="
}
```

Scoped JSON-transcoded read with the currently required explicit version:

```http
GET /v1/tenants/acme/scopes/profile/blobs/avatar-001:read?version=2026-06-14T15%3A00%3A00Z&startChunkIndex=0&chunkCount=1
Accept: application/json
```

Unscoped JSON-transcoded reads use the unscoped resource name, for example
`GET /v1/tenants/acme/blobs/manual-001:read?version=2026-06-14T15%3A00%3A00Z`.

Scoped `BlobInfo` lookup for the current head:

```http
GET /v1/tenants/acme/scopes/profile/blobs/avatar-001/info?includeChunkSizeBytes=true
Accept: application/json
```

Scoped `BlobInfo` lookup for an explicit content version:

```http
GET /v1/tenants/acme/scopes/profile/blobs/avatar-001/versions/2026-06-14T15%3A00%3A00Z/info
Accept: application/json
```

Scoped raw media upload route:

```http
PUT /v1/tenants/acme/scopes/profile/blobs/avatar-001/content?version=2026-06-14T15%3A00%3A00Z
Content-Type: image/png
tag: temporary
```

Unscoped raw media upload route:

```http
PUT /v1/tenants/acme/blobs/manual-001/content?version=2026-06-14T15%3A00%3A00Z
Content-Type: text/plain
```

Raw uploads keep a specific `Content-Type` unchanged. If `Content-Type` is
missing, blank, or `application/octet-stream`, the server infers the response
`mediaType` from the `Content-Disposition` filename, blob resource name
extension, then initial bytes; unresolved uploads return `null` `mediaType` and
store no media type.

Promotion after a temporary upload:

```http
PATCH /v1/tenants/acme/scopes/profile/blobs/avatar-001?update_mask=tags
Content-Type: application/json

{
  "name": "tenants/acme/scopes/profile/blobs/avatar-001",
  "tags": ["profile", "avatar"]
}
```

Current runtime boundaries:

- `ListBlobsRequest.filter` is documented as the PNet filter surface, but the
  current `BlobService` runtime only applies parent and page-token filtering.
  Server-side metadata filter evaluation is pending.
- The blob content runtime currently requires an explicit version for reads; empty-version latest reads are declared in the schema contract but are not implemented yet.
- `GetBlobInfo` is part of the schema surface, but the current runtime implementation is still pending.
- Raw `/content` responses do not currently support redirects in the HTTP layer.

## Leader Clients

Use these exported schema surfaces together for cluster leader election:

| Surface | Purpose | Common resource names |
|---------|---------|-----------------------|
| `pnet.v1.ClusterLeaderService` | Leader observation and lifecycle. | `clusters/{cluster}/scopes/{scope}/leader` |
| `pnet.type.Leader` | Singleton leader state returned by `ClusterLeaderService`. | same leader resource name |
| `service_route_policy`, `method_route_policy` | Custom service and method route-policy options. | descriptor options |
| `pnet.type.PNetRoutePolicy` | Route-policy option payload. | descriptor options |

`GetLeader` returns only a current confirmed leader; candidates,
stale/resigned records, expired leases, and missing leaders return `NOT_FOUND`.
Over PNet proto transport this read is scatter-getter friendly: peers without a
current leader delay their miss response so a peer with the leader can win,
while an all-peer miss still resolves as `NOT_FOUND`.

`MonitorLeader` streams leader-state changes. `ClaimLeader` is unary and may
scatter-gather internally until the first winning claim or the RPC
deadline/timeout. Fan-out observers must use unique per-peer queue identities;
the shared default `q` queue can hide leader-control calls from peers.

`Leader` includes the current `peer_id`, `expire_time`, monotonic `revision`,
and lifecycle `state` so generated clients can map leader state to runtime lease
claims without transport-specific metadata. Claim, confirm, renew, and resign
requests carry `peer_id`; claim/confirm/renew may include `lease_duration`;
confirm/renew/resign may include `observed_revision` for optimistic conflict
detection.

The leader schema stays transport-agnostic. Generated clients should use the
regular PNet proto transport primitives, not NATS-specific APIs.

## Job Clients

Use these exported schema surfaces together when generating scheduled PNet Proto job clients:

| Surface | Purpose | Common resource names |
|---------|---------|-----------------------|
| `pnet.v1.JobService` | Job metadata lifecycle. | `tenants/{tenant}/jobs/{job}`<br>`tenants/{tenant}/scopes/{scope}/jobs/{job}` |
| `pnet.v1.JobRunService` | Job run listing, creation, and state updates. | `tenants/{tenant}/jobs/{job}/runs/{job_run}`<br>`tenants/{tenant}/scopes/{scope}/jobs/{job}/runs/{job_run}` |
| `pnet.type.Job` | Scheduled PNet Proto workload definition. | same job resource names as `JobService` |
| `pnet.type.JobRun` | Addressable execution record directly under a `Job`. | same run resource names as `JobRunService` |

`Job.target` and `JobRun.target` are PNet Proto subjects. `Job.payload` and
`JobRun.payload` are serialized `PNetProtoMessage` bytes, including headers and
inner payload when present. Empty payload bytes are valid and mean the runtime
emits an empty `PNetProtoMessage` workload to the target subject.

`JobRun` resources are direct children of `Job` resources; the parent job is
encoded in `JobRun.name` and there is no separate `job` field. NATS
scheduled-message compatibility requires adapter verification against the
selected NATS server and client versions during runtime implementation.

## Notes

`pnet.core` transport protos are not part of this schema package.
