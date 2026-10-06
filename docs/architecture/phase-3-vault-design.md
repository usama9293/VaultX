# Phase 3 — Vault Creation & Access

## Scope

Phase 3 provides authenticated vault initialization and retrieval, ownership enforcement, and an empty-vault dashboard. It does not implement password entries, search, cryptography, encrypted-vault processing, or zero-knowledge architecture.

## Metadata-only vault

The Phase 3 `Vault` contains only:

- `Id`
- `UserId`
- `CreatedAt`
- `UpdatedAt`

Vault key bytes, nonces, and authentication tags are not present. The Phase 3 vault is an ownership and lifecycle record, not an encrypted vault. No placeholder or fabricated cryptographic values are stored.

The one-vault-per-user invariant is enforced by a unique database index on `Vaults.UserId`, together with a required foreign key to `Users.Id`. Deleting a user cascades to their vault under the current relationship configuration.

The forward migration takes an exclusive lock on the legacy `Vaults` table before checking that it is empty, then removes the obsolete key columns only if no vault records exist. The lock prevents concurrent inserts from racing the check; if vault records exist, it aborts before changing the schema, preserving their key material and any dependent entries for a separately planned migration or recovery. It is intentionally irreversible because recreating required key fields without valid cryptographic material would fabricate an invalid vault key envelope.

## Initialization and retrieval contract

Both operations use the authenticated user's identity; callers cannot select or assign an owner.

| Method | Route | Behavior |
| --- | --- | --- |
| `POST` | `/api/vault` | Creates the current user's vault and returns `201 Created`; if it already exists, returns that same vault with `200 OK`. |
| `GET` | `/api/vault` | Returns the current user's vault with `200 OK`; if absent, returns `404 Not Found` and does not create one. |

The response contains only:

```json
{
  "id": "guid",
  "createdAt": "UTC timestamp",
  "updatedAt": "UTC timestamp"
}
```

`UserId`, keys, entry data, navigation properties, password hashes, refresh tokens, and authentication state are not returned. API responses are mapped to DTOs; EF entities are never serialized directly.

## Ownership and concurrency

Vault operations require an authenticated JWT. The owner is taken only from `ICurrentUser.UserId`. Request bodies, headers, and query parameters are not authoritative for ownership.

The application checks for an existing vault for normal idempotent POST requests. Concurrent creation is arbitrated by the unique `UserId` database index. Only a PostgreSQL unique-violation error naming `IX_Vaults_UserId` is recovered: the application reloads and returns the winning vault. If no such vault exists, or a different database error occurs, the error is not converted to success.

## Dashboard boundary

The authenticated dashboard includes session restoration, vault loading, initialization, the empty-vault state, error handling, retry, and logout. It does not expose functional Add Login or Search controls. Password-entry CRUD, search, favorites, tags, and categories are Phase 4.

## Phase boundaries and future work

- **Phase 4:** password entry lifecycle and search.
- **Phase 5:** cryptographic primitives, derivation, key hierarchy, and key lifecycle design.
- **Phase 6:** encrypted vault/key-envelope persistence and client encryption/decryption.
- **Phase 7:** explicit zero-knowledge trust-boundary and protocol design.

Phase 3 stores no master password, derived key, vault key, ciphertext placeholder, or encryption metadata. Phase 5/6 must define any future key-envelope model and migration; Phase 7 must define what clients, server, and database can observe. No zero-knowledge guarantee is made by Phase 3.

## Security verification

The Phase 3 security suite covers:

- Anonymous, malformed-JWT, expired-JWT, and malformed authenticated identity rejection.
- Current-user ownership and ignoring client-supplied `UserId` in headers, query strings, or bodies.
- Missing vault behavior, idempotent initialization, response-field allowlisting, and DTO mapping.
- PostgreSQL foreign-key and unique-owner constraints.
- Concurrent independent HTTP initialization requests that are deterministically held at the PostgreSQL `INSERT` trigger boundary, then released to race against the unique owner index.
- Dashboard access, empty/loading/error/retry states, user isolation, reload/session restoration, and logout.

## Known trade-offs

- The Phase 3 metadata shell is not encrypted and offers no confidentiality for future vault contents.
- The API uses stateless access JWTs. Existing Phase 2 logout revokes the refresh-token family and clears local authentication; an already-issued access JWT remains valid until expiry.
- The existing `VaultEntry` model contains fields whose confidentiality/metadata treatment must be revisited in Phases 6 and 7; Phase 3 does not use those fields.
- The schema migration refuses to discard prior key columns when vault records exist; populated legacy vaults require a separately verified migration or recovery plan. Rollback is intentionally blocked rather than populating fake key material.
