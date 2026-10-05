# Phase 2.4: Rate Limiting and Account Lockout

## Status and source of policy

Phase 2.4 adds API request throttling and per-account login lockout. The product/security roadmap identifies rate limiting and brute-force protection as security hardening, but does not specify numeric thresholds. All limits and durations below are VaultX product/security decisions approved for this phase, not numbers mandated by the specification.

## API rate limiting

ASP.NET Core's built-in in-process rate limiter uses sliding windows and a zero-length queue. Both global and named authentication policies partition requests by normalized `RemoteIpAddress`; arbitrary forwarded headers are not trusted.

| Scope | Permit limit | Window |
|---|---:|---:|
| Global API | 300 | 1 minute |
| `POST /api/auth/login` | 10 | 1 minute |
| `POST /api/auth/register` | 5 | 1 hour |
| `POST /api/auth/refresh` | 30 | 1 minute |
| Logout | Global policy only | 300 per minute |

An over-limit request receives generic HTTP 429 ProblemDetails. The response includes `Retry-After` when the rate limiter supplies a meaningful retry duration. Rejected requests do not enter the application handler and cannot change account or refresh-token state. Rate limiting is independent of account lockout: it limits request volume; it does not count password guesses across changing IP addresses.

The limiter stores counters in the API process. This is suitable for a single API instance, but multiple instances have independent counters; a distributed/shared limiter is a later deployment concern. When deployed behind a proxy, trusted forwarded-header processing and known proxy configuration must be designed explicitly before using proxy-derived client addresses. The application does not trust arbitrary `X-Forwarded-For` input.

## Login lockout

The `Users` row stores `FailedLoginAttempts` (non-null integer, default zero) and nullable UTC `LockedUntil`. Five consecutive incorrect passwords establish a 15-minute lock. Attempts during an active lock return the same generic 401 as other login failures and do not issue tokens. On the next request after expiry, lock state is cleared lazily; a failed password starts a new sequence at one. A successful login resets the count and lock.

Unknown email addresses receive a PBKDF2 verification against a fixed non-account dummy hash before the generic invalid-credentials response. Existing locked accounts perform the normal password verification, then the lock state is rechecked transactionally; neither the password result nor the response reveals lock state. No artificial delay is used.

Temporary lockout reduces repeated guessing but can be abused to deny an account owner access. The API/IP limiter and per-account counter address different parts of this risk and work together.

## Concurrency and persistence

Password hashing happens before a database transaction. For a known account, the application starts a transaction, asks Infrastructure to acquire PostgreSQL `SELECT ... FOR UPDATE` on the user row, and then loads current state without relying on a stale tracked entity. It records a failure, establishes a lock, or resets lockout and creates the refresh session before commit. Concurrent failure updates therefore serialize; later requests re-read an already-established lock rather than overwriting the counter.

The migration adds `FailedLoginAttempts` with a zero default and nullable `LockedUntil`; existing users remain unlocked. No new indexes or account rows are required for lockout.

## Response and enumeration behavior

- Unknown user, wrong password, and active lock all return HTTP 401 with the generic `Invalid email or password.` response and no token cookie.
- **Known login timing limitation:** For an unknown account, login performs dummy PBKDF2 verification and then returns the generic 401. For a known account with an incorrect password, login performs password verification and then updates lockout state under the PostgreSQL user-row lock before returning the same generic 401. These different required code paths can produce different response timings, so timing may still reveal whether an account exists. Generic responses and dummy password hashing reduce disclosure but do not fully prevent timing-based account enumeration. This residual risk is accepted; no artificial delay or fake database write is used to equalize the paths, and the required lockout transaction is not bypassed.
- Rate-limit rejection returns generic HTTP 429 without account-specific details.
- **Registration duplicate-email disclosure:** Duplicate registration intentionally retains the existing HTTP 409 contract. This is a separate account-enumeration channel and is deferred rather than changing the registration API in Phase 2.4.

## Testing

Unit and integration tests cover lockout transitions, generic login errors, limit boundaries and independent IP partitions, state non-mutation on 429, and lazy expiry reset. A PostgreSQL integration test holds the user row lock, sends multiple failed-login HTTP requests, waits until PostgreSQL reports both at the production lock query, releases the lock, and verifies the threshold state and absence of token issuance. SQLite is not used as proof of lockout concurrency.
