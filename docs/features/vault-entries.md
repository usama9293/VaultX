# Vault Entry Metadata

Phase 4 implements an authenticated CRUD and search flow for descriptive entry metadata. It does not accept or persist passwords, ciphertext, nonces, authentication tags, or keys. The approved design boundary is [the Phase 4 DCR](../architecture/phase-4-password-entries-design.md).

## Ownership

Every route requires a valid access token. The API obtains the owner only from `ICurrentUser.UserId`, resolves the user's vault on the server, and scopes every entry read, update, and delete by both entry ID and vault ID. Client-supplied `UserId` or `VaultId` values are ignored and are not part of the request DTOs. A missing vault does not get initialized by entry operations.

Missing and foreign entries return the same fixed `404 Not Found` Problem Details body.

## API

| Method and route | Success | Behavior |
|---|---|---|
| `POST /api/vault/entries` | `201 Created` | Creates metadata in the authenticated user's vault and returns a `Location` for the entry. |
| `GET /api/vault/entries` | `200 OK` | Lists the current vault's entries; accepts `q`, `page` (default 1), and `pageSize` (default 20, maximum 100). |
| `GET /api/vault/entries/{entryId}` | `200 OK` | Returns one owner-scoped entry, including notes. |
| `PUT /api/vault/entries/{entryId}` | `200 OK` | Replaces editable metadata; ownership and creation fields are immutable. |
| `DELETE /api/vault/entries/{entryId}` | `204 No Content` | Deletes only an entry in the authenticated user's vault. |

Create and update accept:

```json
{
  "title": "Example account",
  "websiteUrl": "https://example.com",
  "username": "user@example.com",
  "notes": "Optional metadata"
}
```

`title` and `username` are required. `websiteUrl` and `notes` are optional; blank values normalize to `null`. The API request DTOs do not define password or owner fields. List items omit notes; detail/create/update responses contain only entry ID, metadata, and timestamps. No response contains vault ownership or legacy byte-column values.

Search (`q`) is case-insensitive literal matching over title, website URL, and username only. It is vault-scoped before searching, excludes notes, and uses stable ordering by creation time descending then ID ascending. `pageSize` is bounded to 100 and list responses include a `hasMore` flag.

## Persistence and validation

The domain entity contains only metadata and server timestamps. EF retains the old password-named database columns as nullable shadow properties so existing stored bytes survive the migration; application reads project only the approved metadata fields, and Phase 4 writes no legacy values. Website URLs are nullable. Rollback refuses to make these columns required if any row contains null values.

Input is trimmed and normalized to Unicode NFC; title and username are limited to 255 Unicode scalar values, website URLs to 2048, notes to 2000, and search text to 128. Website URLs must be absolute HTTP(S) URLs without user information; the API does not fetch URLs. Request bodies are capped at 32 KiB. Validation errors do not echo submitted content.

## Frontend and security

The dashboard supports list/search, detail, create, edit, and confirmed delete with loading, empty, validation, and request-error states. It clearly states that passwords are not stored. Entry data remains only in React memory and is cleared on logout/account change; it is never written to local or session storage. User-controlled content is rendered as text, and displayed links are restricted to validated HTTP(S).

## Phase boundaries

- **Phase 4:** metadata lifecycle and metadata search only.
- **Phase 5:** cryptographic primitive and key-lifecycle design/implementation.
- **Phase 6:** encrypted-vault design/implementation under a separately approved contract.
- **Phase 7:** zero-knowledge trust-boundary and protocol design.

This feature does not implement password storage, Argon2id, encryption/decryption, key derivation, vault/item keys, key wrapping, zero-knowledge behavior, sharing, TOTP, recovery, or device synchronization.
