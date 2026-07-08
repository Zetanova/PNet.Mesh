# PNet Proto Store Concepts

PNet proto stores keep the latest protobuf-backed state for keyed items and expose
snapshots for peer copy before stream catch-up.

## Concept Map

| Name | Meaning | Boundary |
|------|---------|----------|
| `PNetProtoStore` | Latest-state store abstraction for keyed protobuf entries. | Store API; not a projection or stream consumer. |
| `PNetProtoStoreEntry` | One keyed item with revision, timestamp, headers, and protobuf payload. | Logical store item; not necessarily an AIP resource. |
| `PNetProtoStoreEntity` | Persistence row name for durable latest-state entries. | Backend storage row, currently used by EF read models. |
| `PNetProtoStoreSnapshot` | Point-in-time read transaction over entries and source status. | Snapshot copy boundary for peers. |
| `PNetProtoStoreSnapshotCursor` | Stable ordered scan cursor for large snapshots. | Resumes entry copy without observing newer writes. |
| `PNetProtoStatus` | Store source-status document. | Captures durable source progress at snapshot time. |
| `PNetProtoStatus.Source` | Per-source progress: name, sequence, update time, generation. | Catch-up start point for one source. |
| `PNetProtoConsumerCursor` | Stream consumer resume position. | Stream/event catch-up, not latest-state snapshot scan. |
| `PNetProtoStreamInfo` | Stream metadata such as first/last sequence. | Publisher/consumer stream vocabulary. |
| `PNetProtoProjection` | App/query read model derived from store entries. | Derived data, not core store state. |

Use `Resource` only when the item is an actual AIP/resource-shaped concept.
Store entries may also be child items, split payloads, tombstones, cached
protobuf state, or backend-specific state. Use `Entry`, `Entity`, `Snapshot`,
`Status`, and `Projection` for those generic store concepts.

## Snapshot Sync

Peer store snapshot sync copies latest state first, then catches up through
source streams:

1. Open a `PNetProtoStoreSnapshot`.
2. Read `PNetProtoStatus` from the same point in time as the entries.
3. Scan `PNetProtoStoreEntry` rows in stable order with
   `PNetProtoStoreSnapshotCursor`.
4. Persist the copied entries and captured `PNetProtoStatus` on the target peer.
5. Start catch-up from each `PNetProtoStatus.Source` through stream consumers
   using `PNetProtoConsumerCursor` and `PNetProtoStreamInfo`.

The copied snapshot is a bootstrap image. It is not an event journal replay and
it is not a query projection export.

## Source Status

`PNetProtoStatus.Source` carries source progress independently from projection
checkpoints:

| Field | Meaning |
|-------|---------|
| `Name` | Stable source or stream name. |
| `Sequence` | Last durable source sequence included in the store. |
| `UpdateTime` | Time associated with that source progress. |
| `Generation` | Source epoch for reset/recreate detection. |

Backends that store entries and source status together must write the entry
payload and the latest source status in one transaction. A peer can only use a
snapshot safely when the captured status describes the same committed view as
the entry rows it copied.

## Store Indexes And Projections

Store-owned indexes accelerate entry lookup or snapshot scanning. They belong to
the latest-state store.

Projection indexes belong to app/query read models. A `PNetProtoProjection`
reads store entries, writes projection rows, and tracks progress with a
projection checkpoint. Projection checkpoint/index names should stay separate
from store entry and source-status names even when the same physical database
holds both sets of tables.

## Backend Expectations

SQLite and LMDB-style backends should use MVCC read transactions for
`PNetProtoStoreSnapshot`. The snapshot transaction must keep entry scans and
source-status reads at the same committed point while concurrent writes continue
outside the snapshot.

JetStream-like sources remain stream-backed catch-up sources. The store snapshot
copies current latest state; `PNetProtoConsumerCursor` then resumes stream reads
from the captured source sequence and generation.

Backends may use one database, multiple column families, or stream metadata, but
the conceptual owner stays the PNet proto store: entries and source status form
the snapshot; projections derive from it.

## Package Contract

This guide is included in the `pnet-schema` package at
`docs/pnet-schema/proto-store.md`.
