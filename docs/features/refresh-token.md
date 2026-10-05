# Feature: Refresh Token Rotation (`POST /api/auth/refresh`)

## Overview

The refresh endpoint rotates opaque refresh tokens using the HttpOnly `refreshToken` cookie. Each login starts an independent token family. A successful rotation stores only the replacement token hash, revokes the presented token, and records its replacement ID. Reuse of a rotated token revokes active tokens in the same family and returns the same generic unauthorized response as other invalid refresh attempts.

## API Contract

- **Method and path:** `POST /api/auth/refresh`
- **Credential:** The `refreshToken` cookie only; body, query, and authorization-header values are ignored.
- **Success:** `200 OK` with `{ accessToken, expiresAt }` and a replacement HttpOnly cookie.
- **Failure:** `401 Unauthorized` with a generic ProblemDetails response and cookie deletion.
- **Cookie attributes:** `HttpOnly`, `SameSite=Lax`, `Path=/api/auth`, and `Secure` in production or HTTPS development requests.

## Rotation and replay behavior

```text
Cookie token
  → SHA-256 hash lookup
  → begin database transaction
  → lock the owning Users row
  → reload token state after acquiring the lock
  → if active: issue access JWT and replacement token in the same FamilyId
  → revoke predecessor, link ReplacedByTokenId, persist atomically
```

If the token was already rotated, the handler revokes all active records in its family and commits that revocation before returning the generic `401`. PostgreSQL serializes rotations for the same user by locking the owner row. SQLite is used for functional endpoint tests only; it does not provide the concurrency proof.

The PostgreSQL integration test sends two concurrent requests with the same original cookie and verifies that exactly one rotates successfully, the replay fails, and every record in the family is revoked. The test requires `VAULTX_POSTGRES_TEST_CONNECTION_STRING`; CI provisions PostgreSQL and supplies this variable.

## Security boundaries and limitations

- Raw refresh tokens are returned only in HttpOnly cookies and are not persisted or logged; only their SHA-256 hashes are stored.
- Independent login families are not affected by another family's replay or logout.
- The access JWT remains stateless and valid until its short expiration even if its refresh-token family is revoked.
- This feature does not add refresh-token rate limiting, account lockout, access-token revocation, or changes to JWT claims.
