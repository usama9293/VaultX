# Phase 4 — Password Entries Design Decision Record

- **Status:** Approved
- **Phase:** 4
- **Scope:** Password Entries
- **Date:** 2026-10-06
- **Related phases:** Phase 3 — Vault Creation & Access; Phase 5 — Cryptographic Core; Phase 6 — Encrypted Vault; Phase 7 — Zero-Knowledge Architecture

This document is the design authority for Phase 4 implementation unless a later approved decision supersedes it. It records design decisions only; it does not authorize implementing cryptography or changing the current schema by itself.

## 1. Goal and scope

Phase 4 introduces the first usable vault-entry lifecycle while preserving VaultX's security architecture:

```text
Authenticated user
    ↓
Own Vault
    ↓
Entry metadata
    ↓
Create / Read / Update / Delete / Search
```

Phase 4 includes authenticated metadata entry create, read, update, delete, list, and initial search; server-side ownership enforcement; a usable frontend flow; and unit, integration, security, frontend, and end-to-end tests.

Phase 4 does **not** establish VaultX's final cryptographic secret-storage architecture. Under this decision, entries do not persist passwords. The absence of password persistence is intentional and is not considered an incomplete implementation of the approved Phase 4 boundary. Password persistence is deferred until the cryptographic and encrypted-vault architecture is designed in later phases.

Favorites, tags, categories, and user-selectable sorting are secondary features and are excluded from the core lifecycle unless separately approved.

## 2. Password and secret storage decision

The current `EncryptedPassword`, `PasswordNonce`, and `PasswordAuthenticationTag` names do not prove that cryptography exists. The current model accepts nonempty arbitrary byte arrays; it does not establish encryption, key custody, authenticity, or a security boundary.

| Option | Security and architectural consequences | Phase implications |
|---|---|---|
| **A. Persist plaintext temporarily** | Exposes credentials to the API process, database, backups, administrators, logs if mishandled, and any server compromise. It contradicts VaultX's principle not to intentionally persist plaintext secrets in the eventual product. | Creates sensitive data and migration liability for Phase 6; does not solve the key/trust-boundary questions required by Phase 5/7. Not approved. |
| **B. Persist placeholder/fake ciphertext** | Misrepresents arbitrary bytes as protected data, creates a false security claim, and risks values being mistaken for valid ciphertext, nonce, or authentication tag later. | Pollutes the schema and makes later migration/verification ambiguous. Not approved. |
| **C. Metadata-only entries** | Stores only the entry's descriptive fields. It does not provide password storage, but avoids placing unprotected credentials in the database or API. Metadata itself remains visible to the server and database. | Keeps Phase 4 separate from cryptographic work; permits Phase 5/6 to define real secret and key-envelope semantics. **Approved.** |
| **D. Real client-encrypted storage now** | Could protect secrets from the server, but requires a complete cryptographic, key-management, payload, and recovery contract—not just an encryption call. | Pulls Phase 5/6 responsibilities into Phase 4 and risks constraining Phase 7's trust-boundary design. Not approved. |

Phase 4 must not persist:

- plaintext passwords;
- fake ciphertext, fake nonces, or fake authentication tags;
- master passwords, vault keys, or item encryption keys.

The legacy byte-array fields are not used by Phase 4 operations and are never exposed in API responses. No placeholder values are written. The approved Phase 4 model removes these properties from the active domain model while the migration strategy below preserves their existing database columns and values temporarily.

## 3. Approved metadata-only data model

The logical Phase 4 `VaultEntry` model is:

| Field | Type | Required | Maximum | Purpose | List response | Detail response | Searchable |
|---|---|---:|---:|---|---:|---:|---:|
| `Id` | `Guid` | Yes | — | Stable entry identifier | Yes | Yes | No |
| `VaultId` | `Guid` | Yes | — | Owning vault relationship; set only by server-side application logic | No | No | No |
| `Title` | String | Yes | 255 characters | User-visible label | Yes | Yes | Yes |
| `WebsiteUrl` | String | No | 2048 characters | Optional site URL | Yes | Yes | Yes |
| `Username` | String | Yes | 255 characters | Login name or account identifier | Yes | Yes | Yes |
| `Notes` | String | No | 2000 characters | User-entered descriptive notes; potentially sensitive free text | No | Yes | No |
| `CreatedAt` | UTC timestamp | Yes | — | Creation time | Yes | Yes | No |
| `UpdatedAt` | UTC timestamp | Yes | — | Last update time | Yes | Yes | No |

These limits reuse the existing EF configuration rather than inventing broader values. They bound storage and request sizes while maintaining compatibility with the existing schema. API validation, database constraints, and frontend validation must agree.

### Website URL nullability

`WebsiteUrl` is **optional** at every layer:

- **Database:** nullable column after the Phase 4 migration.
- **API:** omitted or `null` is accepted; blank/whitespace input normalizes to `null`.
- **Frontend:** optional field; no URL is required to create an entry.

This resolves the current mismatch between the nullable domain property and the required database/request definitions. The migration must preserve existing values while allowing nulls.

### Username nullability

`Username` is **required**, consistent with the current required model and database configuration. It must contain a non-whitespace value after trimming and fit the 255-character limit. If product requirements later need entries without usernames, that change requires a separate schema/API decision.

## 4. Legacy encrypted-looking fields and migration strategy

The existing `EncryptedPassword`, `PasswordNonce`, and `PasswordAuthenticationTag` columns are **retained temporarily but unused**, not treated as encryption, not selected into Phase 4 DTOs, and not populated for new entries.

The Phase 4 implementation is expected to:

1. Redesign the active domain model and application requests so these fields are not accepted or written.
2. Add a forward migration that makes the legacy byte-array columns nullable, allowing new metadata-only rows without fabricated values.
3. Preserve every existing legacy value unchanged. Do not drop columns, rewrite byte arrays, infer that they are ciphertext, or claim that they can be decrypted.
4. Make `WebsiteUrl` nullable in the database in the same migration or an explicitly sequenced migration.
5. Verify actual deployment data and take/verify backups before rollout. If existing records are present, preserve them and do not silently discard or convert them.
6. Keep rollback considerations explicit: application rollback must not require invented key bytes. Schema rollback that would make nullable legacy fields required again is unsafe if null Phase 4 rows have been created and must be blocked or handled by a separately approved recovery plan.

The existing `VaultEntries` table already exists in the initial schema. Phase 4 must not recreate it. The current schema's required byte-array columns and required `WebsiteUrl` column do not match the approved model; a migration is therefore expected. Phase 4 must fail deployment safely if the migration cannot preserve data or apply consistently. Phase 6 must reassess, classify, and migrate or retire the legacy columns only under a separately designed and verified plan.

## 5. Ownership and authorization

The invariant is:

> A user can access only entries belonging to that user's vault.

The client is never authoritative for `UserId`, `VaultId`, or entry ownership. The approved flow is:

```text
Authenticated request
    ↓
ICurrentUser.UserId
    ↓
Current user's Vault
    ↓
Entry scoped to that Vault
    ↓
Operation
```

Every entry endpoint requires authentication with `[Authorize]`. Application handlers obtain the user identity from `ICurrentUser`, resolve that user's vault server-side, and pass the vault scope to repository operations. Persistence queries and mutations must enforce both the entry ID and authenticated owner's vault scope. An ID-only entry fetch followed by reliance on UI checks is explicitly rejected.

| Operation | Ownership enforcement |
|---|---|
| Create | Resolve the current user's vault on the server and assign its ID. Ignore/reject body ownership fields; never accept a client-selected vault. If the user has no vault, return `404` without creating one as a side effect. |
| List | Resolve the current user's vault and filter the query by that `VaultId`. |
| Get | Query by both `entryId` and the current user's `VaultId`. |
| Update | Apply the update only to an entry selected by both `entryId` and the current user's `VaultId`. |
| Delete | Delete only an entry selected by both `entryId` and the current user's `VaultId`. |
| Search | Apply the current user's `VaultId` predicate before/with all search predicates. Never search across vaults and filter results afterward. |

If the current user identity is missing or invalid, follow the Phase 3 behavior and reject the request. Authorization is a server/application/persistence responsibility; frontend route guards are only UX.

## 6. API contract

All routes require authentication. The current user's vault is resolved server-side. No authoritative `UserId` or `VaultId` is accepted from a body, header, or query string.

### Create

`POST /api/vault/entries`

Request:

```json
{
  "title": "Example account",
  "websiteUrl": "https://example.com",
  "username": "user@example.com",
  "notes": "Optional notes"
}
```

`websiteUrl` and `notes` may be omitted or `null`. `title` and `username` are required. Password, `encryptedPassword`, nonce, authentication tag, `userId`, `vaultId`, and owner information are not accepted as fields in the contract.

Return `201 Created` with a `Location` header for `GET /api/vault/entries/{entryId}` and the detail response shape. Invalid fields return `400`; unauthenticated access returns `401`; missing current-user vault returns `404`. A create retry is a new create unless a separate idempotency contract is later approved.

### List and search

`GET /api/vault/entries`

Optional query parameters:

- `q`: search query, maximum 128 characters.
- `page`: one-based page number, default `1`.
- `pageSize`: default `20`, maximum `100`.

Return `200 OK` and an empty `items` array when the current user's vault exists but has no matching entries. If the authenticated user's vault is absent, return `404`, consistently with Phase 3's non-creating GET behavior. Invalid pagination or an overlong query returns `400`. No client-selected vault parameter is supported.

The response envelope is:

```json
{
  "items": [
    {
      "id": "guid",
      "title": "Example account",
      "websiteUrl": "https://example.com",
      "username": "user@example.com",
      "createdAt": "UTC timestamp",
      "updatedAt": "UTC timestamp"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "hasMore": false
}
```

The list omits `notes` to reduce unnecessary free-text disclosure. It includes no secret fields. Use stable server-defined ordering by `CreatedAt` descending and then `Id` ascending. Phase 4 has no client-selectable sort fields; favorites, tags, categories, and sorting remain secondary/out of core scope.

### Get

`GET /api/vault/entries/{entryId}`

Return `200 OK` with the detail allowlist below. A missing entry and an entry belonging to another user's vault both return the same `404 Not Found` status and generic body. An invalid route identifier returns `400 Bad Request` through normal route validation. The query must scope by the current user's vault.

### Update

`PUT /api/vault/entries/{entryId}`

Full replacement of editable metadata: `title`, optional `websiteUrl`, required `username`, and optional `notes`. `Id`, `VaultId`, ownership, and `CreatedAt` are immutable and cannot be changed by the request. Missing or foreign entries return indistinguishable `404`; invalid fields return `400`. Return `200 OK` with the detail response. `UpdatedAt` is set by the server.

Optimistic concurrency is deferred for Phase 4 metadata. Concurrent writes are atomic at the database row level, with last successful update winning; clients may overwrite another recent edit. This is an explicit trade-off acceptable for the initial single-user metadata workflow. If collaboration or material stale-edit risk is introduced, add an explicit version/ETag contract before relying on it.

### Delete

`DELETE /api/vault/entries/{entryId}`

Delete only within the current user's vault. Return `204 No Content` on success and the same generic `404 Not Found` for missing or foreign entries. The frontend must request confirmation before deletion. Repeated deletion returns `404`; deletion is not silently treated as successful for an unknown ID.

### Common error behavior

- Anonymous request: `401`.
- Malformed/expired JWT: rejected by existing authentication middleware as `401`.
- Invalid authenticated identity: `401`, following Phase 3.
- Invalid entry ID or request validation: `400`.
- Missing current user's vault: `404`; GET-like operations do not initialize a vault.
- Missing or foreign entry: indistinguishable `404`.
- Unexpected database/server failure: generic `500`; do not return SQL, schema, constraint, or stack details.

Validation/logging code must not include submitted values, notes, URLs, or request bodies in exception messages or logs. Follow the generic error behavior in the existing `ExceptionHandlingMiddleware`; do not create an existence-revealing error for foreign entries.

## 7. Search design

Initial search is limited to `Title`, `WebsiteUrl`, and `Username`. It does not search `Notes`, password material, or any legacy byte columns. Excluding notes reduces exposure of potentially sensitive free text and keeps the initial contract narrow.

- Search is case-insensitive using the configured PostgreSQL behavior and tested against the supported deployment collation.
- Entry text is normalized to Unicode NFC on write; the query is trimmed and normalized to NFC. Do not lowercase stored display values.
- An omitted or whitespace-only `q` means no text filter.
- A nonempty query longer than 128 characters returns `400`.
- Search uses parameterized queries. `%`, `_`, and the chosen escape character in user input are treated literally, not as attacker-controlled wildcard patterns.
- Search is always scoped to the current user's vault, with the same stable pagination and ordering as list.
- No full-text or trigram index is added for the initial feature. The existing per-vault index bounds the candidate set; measure performance before adding specialized indexing.

This server-side metadata search is temporary and exposes searchable metadata to the server/database. It must be revisited when client-side encryption and zero-knowledge boundaries are designed in Phases 6/7. Search over encrypted secret content requires a separately approved leakage/indexing design and is not part of Phase 4.

## 8. Validation and request limits

Input validation improves data quality and reliability; it is not authorization or an XSS defense.

| Field | Required | Rules |
|---|---:|---|
| `Title` | Yes | Trim leading/trailing whitespace; reject empty/whitespace-only values; maximum 255 Unicode scalar values; normalize to NFC; reject NUL and control characters. |
| `WebsiteUrl` | No | Trim; blank becomes `null`; maximum 2048 Unicode scalar values; if supplied, require an absolute URI with `http` or `https` scheme and a host; reject user-info and control characters. Do not fetch or resolve the URL server-side. |
| `Username` | Yes | Trim; reject empty/whitespace-only values; maximum 255 Unicode scalar values; normalize to NFC; reject NUL and control characters. |
| `Notes` | No | Preserve meaningful internal whitespace; blank becomes `null`; maximum 2000 Unicode scalar values; normalize to NFC; allow line breaks and tabs, reject NUL and other control characters. Notes may contain sensitive user text. |
| `q` | No | Trim and normalize to NFC; whitespace-only means no filter; maximum 128 Unicode scalar values; use literal, parameterized matching. |

The API is authoritative for validation; frontend validation mirrors it for usability. Oversized JSON request bodies must be bounded at the API host and any reverse proxy at **32 KiB**; excess returns `413 Payload Too Large`. Field and query limits still apply inside that bound. Validation failures must not echo the submitted values.

Render all entry fields as text, never injected HTML. If the UI makes `WebsiteUrl` clickable, allow only validated HTTP(S) URLs, use safe link attributes, and do not automatically navigate or fetch them.

## 9. Response allowlists and sensitive-data handling

Never serialize EF entities. Map results explicitly to response DTOs.

**List response fields:** `id`, `title`, `websiteUrl`, `username`, `createdAt`, `updatedAt`; page envelope fields `items`, `page`, `pageSize`, `hasMore`.

**Detail/create/update response fields:** `id`, `title`, `websiteUrl`, `username`, `notes`, `createdAt`, `updatedAt`.

Never return:

- `password`, `EncryptedPassword`, nonce, or authentication tag;
- `VaultId`, `UserId`, or internal ownership fields;
- user password hashes, refresh tokens, vault keys, or internal database fields.

Do not put secret values or sensitive entry material in URLs, query strings, logs, telemetry, exception details, or browser storage. Phase 4 does not accept or persist password material, but the API and UI must still enforce the DTO allowlists so future fields cannot leak by default. Swagger/OpenAPI examples must use synthetic metadata and must not suggest password fields are accepted or securely stored.

## 10. Database model, constraints, and indexes

- Primary key: `Id`.
- Required `VaultId` foreign key to `Vaults.Id`; deleting a vault cascades to its entries, matching existing relationship behavior.
- Required `Title`, required `Username`, nullable `WebsiteUrl`, nullable `Notes`.
- `CreatedAt` and `UpdatedAt` are required UTC timestamps set/updated by server/domain logic.
- No uniqueness constraint on title, username, or URL. Duplicate entries are valid.
- Preserve the existing entry table and legacy secret-looking column values during the Phase 4 migration described above.

Index decisions:

1. Retain an index supporting `VaultId` filtering; the current index is useful for owner-scoped listing and cascade/reference operations.
2. The `Id` primary-key index supports direct entry lookup, but every lookup still includes the vault predicate. Do not add a redundant entry-ID index.
3. For stable paginated listing, evaluate replacing the standalone `VaultId` index with a composite `(VaultId, CreatedAt DESC, Id ASC)` index if query plans show it benefits the fixed ordering. Do not keep redundant indexes without measured need.
4. Initial contains-style case-insensitive search is not efficiently served by a normal B-tree text index. Do not add trigram/full-text indexes until measured data volume and a leakage/performance review justify them.

## 11. Concurrency

- **Simultaneous create:** each request creates an independent entry; duplicate titles are allowed. No uniqueness race exists by design.
- **Simultaneous update:** row updates are atomic; without a version token, last successful update wins. This lost-update trade-off is explicitly accepted for Phase 4.
- **Update/delete race:** the operation that finds and mutates the owner-scoped row first succeeds; the losing operation observes no row and returns `404`.
- **Delete/read race:** a read may return the entry if it observes it before deletion; afterward it returns `404`. No cross-user information is exposed.
- **Stale update:** no optimistic concurrency token is introduced in Phase 4. Reassess before collaboration, multi-device editing, or higher-impact data behavior.

Ownership scope must be part of the atomic query/update/delete predicate, not a separate frontend decision or an unscoped ID fetch.

## 12. Frontend design and security

Phase 4 UI flow:

```text
Dashboard
  ↓
Metadata entry list / search
  ↓
Add Entry
  ↓
Entry detail
  ↓
Edit metadata
  ↓
Delete confirmation
```

Provide loading, empty, validation-error, API-error, and success states. Clear form fields and entry state on logout; reset account-scoped state when the authenticated account changes. Use the existing in-memory authentication pattern and do not persist sensitive entry data in `localStorage` or `sessionStorage`.

There is no password field, password reveal control, or encryption UX in Phase 4. The UI must plainly state that it stores entry metadata only and does not yet save passwords. Avoid presenting entries as securely stored credentials when they contain no persisted password.

Treat the API—not client route guards—as the authorization boundary. Do not put notes or other entry data in URLs or telemetry. Render user-controlled text safely; allow only validated HTTP(S) for displayed links.

## 13. Testing strategy

### Unit tests

- Create, get, list, update, delete, and search handlers.
- Current-user vault resolution, missing/invalid identity behavior, and owner scoping.
- Required/optional field validation, limits, normalization, URL scheme, and control-character rules.
- Search normalization, wildcard escaping, pagination boundaries, and stable ordering.
- List/detail DTO mapping and response allowlists.
- Tests proving password, ciphertext, nonce, and tag values are neither accepted nor persisted by the metadata-only Phase 4 contract.

### Integration tests

- Authenticated create/list/get/update/delete and search.
- Empty vault/empty list, missing current-user vault without GET side effect, malformed IDs, nonexistent entries, and foreign entries.
- Validation errors, request-size boundary, persistence, UTC timestamps, and cascade behavior.
- Cross-user tests for each operation; response equivalence for foreign versus missing entry.
- Search scope, literal wildcard behavior, pagination, stable ordering, and notes/password exclusion.
- Migration against empty and populated `VaultEntries`; existing rows and legacy byte values remain unchanged; new metadata-only rows can omit legacy byte columns.

### Security tests

- Anonymous, malformed JWT, expired JWT, and invalid current identity for every route.
- User A cannot list, get, update, delete, or search User B's entries.
- Forged `VaultId`/`UserId` in bodies, headers, and query strings cannot influence ownership.
- Entry enumeration behavior is indistinguishable for foreign and nonexistent IDs.
- Parameterized search resists SQL injection attempts; HTML/script-like metadata renders as text.
- Oversized fields and request bodies are rejected; no sensitive values appear in response errors/log captures.
- EF entities are never serialized; allowlist tests reject legacy secret-looking fields and user/authentication secrets.

### Frontend tests

- List, create, edit, delete confirmation, search, loading, empty, error, validation, and success states.
- Safe rendering of untrusted title/notes/URLs.
- No entry or secret data in local/session storage; clearing on logout and state isolation on account switch.

### End-to-end tests

- Login, create metadata entry, list, open detail, edit, search, delete, and logout.
- Protected access after logout.
- Two-user isolation across list, detail, update/delete attempts, and search.
- Explicit UI assertion that no password is stored or represented as encrypted in Phase 4.

## 14. Phase boundaries

| Concern | Phase 4 | Phase 5 — Cryptographic Core | Phase 6 — Encrypted Vault | Phase 7 — Zero-Knowledge |
|---|---|---|---|---|
| Entry CRUD and metadata | Implement | Reassess metadata visibility | Reassess protected storage | Reassess trust-boundary exposure |
| Password persistence | No | Design implications only | Design/implement only under approved contract | Verify server cannot learn secrets per protocol |
| Cryptographic primitives | No | Design/implement primitives | Use approved primitives | Review protocol use |
| Key derivation | No | Design/implement | Integrate as approved | Review trust assumptions |
| Vault key / item key | No | Define lifecycle and hierarchy | Persist/use only per approved encrypted-vault design | Review exposure and protocol |
| Encryption / decryption | No | Define core primitives | Implement encrypted storage/client behavior | Validate zero-knowledge properties |
| Encrypted storage | No | No persistence contract assumed | Implement under approved design | Evaluate observable leakage |
| Client-side encryption | No | Prepare cryptographic design | Implement if approved | Evaluate against zero-knowledge model |
| Zero-knowledge architecture | No | No claim | No claim unless separately approved | Design and verify |
| Sharing, TOTP, recovery, device sync | No | No | No | No; separate approved scope required |

Existing scaffolding includes byte-array properties named for encrypted password material, command/request records that accept those values, and an ID-only repository lookup. Treat these as incomplete legacy scaffolding, not an invitation to implement encryption or trust client-supplied ownership. The Phase 4 design requires their removal from active request/domain behavior and owner-scoped persistence.

## 15. Documentation plan

- This Phase 4 DCR is the decision authority for implementation.
- Update the roadmap to mark Phase 4 current only when implementation is authorized; preserve explicit Phase 5/6/7 boundaries.
- Update the README only if user-facing status or metadata-only limitations need to be surfaced.
- Update architecture documentation to describe the entry ownership/query shape, metadata exposure, and migration handling.
- Document the API/OpenAPI routes, request/response allowlists, validation, pagination, and errors with synthetic metadata examples.
- Document the security test matrix and the explicit absence of password persistence. Do not claim Phase 4 provides encrypted password storage.

## 16. Decision summary

| Decision | Approved direction |
|---|---|
| Secret storage | Metadata-only |
| Password persistence | Deferred |
| Encryption | Phase 5/6, under later approved designs |
| Zero knowledge | Phase 7 |
| Ownership | Derived from `ICurrentUser`; current user's vault resolved server-side |
| Client `VaultId` | Not accepted as authoritative |
| Foreign entry | Same generic `404` as nonexistent entry |
| Website URL | Optional, validated HTTP(S) only |
| Search | Initial metadata-only search over title, website URL, username |
| Notes | Detail only; not listed or searched |
| Pagination/order | Page-based, bounded; fixed stable order by creation descending then ID ascending |
| Frontend storage | No sensitive entry persistence in browser storage |
| Authorization | Server-side and vault-scoped in repository queries/mutations |
| Legacy byte columns | Retain temporarily, nullable and unused; preserve values |
| Concurrency | No optimistic token initially; document last-write-wins trade-off |
| API | `/api/vault/entries` |
| Testing | Unit + Integration + Security + Frontend + E2E |

## Final Verdict

**READY FOR PHASE 4 IMPLEMENTATION**
