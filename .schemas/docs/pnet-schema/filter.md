# PNet Filter Language

PNet filters are an AIP-160-style expression language for service fields named
`filter`.

## Quick Start

| Goal | Filter |
|------|--------|
| Match a string exactly | `name = 'avatar-001'` |
| Match a string prefix | `name = 'avatar-*'` |
| Match a string suffix | `name = '*-raw'` |
| Match a string substring | `name = '*avatar*'` or `name:'avatar'` |
| Test a field is present or non-default | `update_time` or `update_time:*` |
| Test a repeated value | `tags:profile` |
| Test a map key | `annotations:example.com/import-id` |
| Compare typed values | `update_time >= 2026-01-15T00:00:00Z` |
| Group mixed logic | `tags:profile AND (scope = '' OR scope = 'public')` |

Always quote string literals in portable filters. Unquoted `true`, `false`, and
`null` are constants, not strings.

## Syntax

| Form | Meaning |
|------|---------|
| `field` | Presence check. Reference types are non-null; value types are not their default value. |
| `field = value` | Equality. String equality supports `*` wildcards. |
| `field != value` | Inequality. Wildcards are not expanded for `!=`. |
| `field < value` | Less than, for ordered scalar and supported protobuf types. |
| `field <= value` | Less than or equal. |
| `field > value` | Greater than. |
| `field >= value` | Greater than or equal. |
| `field:value` | HAS operator. Strings use contains; repeated fields use contains; maps test keys. |
| `field:*` | Presence for scalar/message fields; any item/key for repeated fields and maps. |
| `NOT expr` or `-expr` | Negation. |
| `(expr)` | Explicit grouping. |

Whitespace between terms behaves like `AND`. PNet currently groups `OR` terms
before `AND`, so parenthesize any mixed `AND`/`OR` expression that clients must
read unambiguously.

## Literals

| Literal | Meaning |
|---------|---------|
| `'text'`, `"text"` | String literal. |
| `true`, `false` | Boolean constants. |
| `null` | Null constant. |
| `'null'`, `"null"` | String value `null`. |
| `123`, `123.45` | Numeric constants. |
| `2026-01-15` | Parsed according to the compared field type. |
| `2026-01-15T10:30:00Z` | Timestamp literal for timestamp-compatible fields. |

`name = null` compares `name` with the null constant. `name = 'null'` compares
`name` with the string value `null`. Non-nullable value fields cannot be compared
with `null`.

## Fields

Use protobuf field names or JSON names:

```text
display_name = 'Avatar'
label = 'Avatar'
```

Nested paths use null-conditional traversal. If a message parent is unset, the
nested leaf evaluates to its default value:

| Filter | Unset parent result |
|--------|---------------------|
| `item.string_value = null` | true for nullable/reference leaves |
| `item.number_value = 0` | true for numeric value leaves |
| `item.bool_value = false` | true for boolean value leaves |
| `item.number_value > 0` | false |

Field-to-field comparisons are supported when both fields resolve to compatible
types:

```text
date <= date2
item.number1 > item.number2
```

## Collections And Maps

| Field shape | Filter | Meaning |
|-------------|--------|---------|
| `repeated string tags` | `tags:profile` | Any tag equals `profile`. |
| `repeated string tags` | `tags:*` | At least one tag exists. |
| `map<string,string> annotations` | `annotations:example.com/import-id` | Map contains that key. |
| `map<string,double> values` | `values:010` | Map contains key `010`; the key is kept as text. |

Deep traversal into repeated message elements is not portable yet. Use only
service-documented repeated-message filters.

## Protobuf Types

The generic filter evaluator supports ordinary scalar fields plus these protobuf
types when present on a service message:

| Type | Operators |
|------|-----------|
| `google.type.Date` | `=`, `!=`, `<`, `<=`, `>`, `>=`, presence, null checks |
| `google.protobuf.Timestamp` | `=`, `!=`, `<`, `<=`, `>`, `>=`, presence, null checks |
| `google.protobuf.Duration` | `=`, `!=`, `<`, `<=`, `>`, `>=`, presence, null checks |
| `google.type.TimeOfDay` | `=`, `!=`, `<`, `<=`, `>`, `>=`, presence, null checks |
| `google.type.Interval` | point-in-interval equality and ordering against timestamp/date input |
| wrapper types | compared by their wrapped value |

## Blob Service Fields

The schema-level `pnet.type.Blob` metadata filter contract uses the exported
message fields:

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Canonical resource name. |
| `tenant` | string | Parsed from `name`. |
| `scope` | string | Empty for tenant-level blobs. |
| `blob_id` | string | Parsed from `name`. |
| `version` | string | Current content version. |
| `size_bytes` | int64 | Current content size. |
| `media_type` | string | Current content media type. |
| `digest` | string | Current content digest. |
| `tags` | repeated string | Use `tags:value` or `tags:*`; `tags:temporary` matches the well-known temporary blob tag. |
| `annotations` | map string/string | Use `annotations:key`. |
| `update_time` | timestamp | Output-only timestamp. |
| `delete_time`, `purge_time` | timestamp | Soft-delete lifecycle timestamps. |

Examples:

```text
scope = ''
scope = 'profile'
blob_id = 'avatar-001'
tags:profile
tags:temporary
annotations:example.com/import-id
update_time >= 2026-01-15T00:00:00Z
```

## Reserved Forms

| Form | Status |
|------|--------|
| Bare global terms such as `avatar` | Parsed for AIP compatibility; portable service semantics are not defined. |
| Functions such as `length(name)` | Syntax is reserved. Services must explicitly document supported functions before clients rely on them. |
| Service-specific aliases | Valid only when the service documentation names them. |

## Package Path

This guide is included in the `pnet-schema` package at
`docs/pnet-schema/filter.md`.
