# Feature: Logout (`POST /api/auth/logout`)

## 1. Overview & Current Status

**Status: Step 4 — Backend Implementation Complete ✅ (Steps 5–10 Pending)**

Logout provides secure session termination for authenticated users by revoking the persistent refresh-token session identified by the incoming `refreshToken` cookie and instructing the user agent to clear the cookie.

### Vertical Slice Lifecycle Summary

- **Step 1: Requirement** — Complete (Single-session revocation, idempotent logout, cookie clearing, stateless JWT lifetime boundary)
- **Step 2: Design** — Complete (API contract: `POST /api/auth/logout`, cookie-based session identification, HTTP 204 No Content response)
- **Step 3: Security Analysis** — Complete (Threat modeling: session isolation, token oracle defense, sensitive data exposure defense, CSRF boundary)
- **Step 4: Backend Implementation** — Complete (`POST /api/auth/logout`, `ILogoutUserHandler`, `LogoutUserHandler`, single-session revocation, HttpOnly cookie deletion, idempotent 204 response, 173 passing backend tests including 22 Logout-specific tests)
- **Step 5: Frontend Implementation** — Pending
- **Step 6: Integration** — Pending
- **Step 7: End-to-End Testing** — Pending
- **Step 8: Security Testing** — Pending
- **Step 9: Documentation** — In Progress (this document)
- **Step 10: Complete** — Pending

---

## 2. API Contract

### Endpoint Specification

- **Method:** `POST`
- **Path:** `/api/auth/logout`
- **Request Body:** None (empty)
- **Content-Type:** Not required; request body is neither accepted nor parsed
- **Session Identification:** Extracted exclusively from the HttpOnly `refreshToken` cookie
- **Success Status:** `204 No Content`
- **Success Response Body:** Empty (zero bytes)

### Cookie Requirement

The refresh token must **only** be obtained from the `refreshToken` HttpOnly cookie:

```http
POST /api/auth/logout HTTP/1.1
Host: localhost:5071
Cookie: refreshToken=c7xK...[opaque CSPRNG token]...
```

The server rejects or ignores all client-controlled identity parameters:
- No `userId`
- No `sessionId`
- No `refreshTokenId`
- No `tokenHash`
- No query parameters
- No JSON body fields
- No custom authorization headers for session lookup

---

## 3. Application Architecture

Following VaultX Clean Architecture and the custom handler pattern:

```text
HTTP Request (POST /api/auth/logout)
       ↓
AuthController.Logout
       ↓ (reads Request.Cookies["refreshToken"])
ILogoutUserHandler / LogoutUserHandler
       ↓
ITokenService.HashRefreshToken (SHA-256)
       ↓
IRefreshTokenRepository.GetByHashAsync
       ↓
RefreshToken.Revoke() (if active)
       ↓
IUnitOfWork.SaveChangesAsync()
       ↓
PostgreSQL ("RefreshTokens" table)
       ↓
Response.Cookies.Delete("refreshToken", options)
       ↓
HTTP 204 No Content
```

### Key Components

1. **`AuthController.Logout`** ([AuthController.cs](file:///c:/Users/User/source/repos/VaultX/src/PasswordManager.API/Controllers/AuthController.cs)): Thin controller action; extracts `refreshToken` cookie, invokes `_logoutUserHandler.HandleAsync`, issues cookie deletion instruction with matching configuration, returns `NoContent()`.
2. **`ILogoutUserHandler` / `LogoutUserHandler`** ([LogoutUserHandler.cs](file:///c:/Users/User/source/repos/VaultX/src/PasswordManager.Application/Features/Authentication/Logout/LogoutUserHandler.cs)): Application service responsible for hashing the raw refresh token, retrieving the matching entity from `IRefreshTokenRepository`, and revoking it if active.
3. **`RefreshToken` Domain Entity** ([RefreshToken.cs](file:///c:/Users/User/source/repos/VaultX/src/PasswordManager.Domain/Entities/RefreshToken.cs)): Encapsulates `RevokedAt` timestamp and `IsActive` logic (`!IsRevoked && !IsExpired`).
4. **`RefreshTokenRepository`** ([RefreshTokenRepository.cs](file:///c:/Users/User/source/repos/VaultX/src/PasswordManager.Infrastructure/Repositories/RefreshTokenRepository.cs)): Queries `RefreshTokens` by `TokenHash`.

---

## 4. Request & Session Revocation Behavior

| Scenario | Input Condition | Database Action | Cookie Action | Response Status | Response Body |
| :--- | :--- | :--- | :--- | :---: | :---: |
| **Valid Active Session** | Cookie contains active `refreshToken` | `RevokedAt = DateTime.UtcNow` persisted | Cookie cleared (`expires=1970-01-01`) | `204 No Content` | Empty |
| **Missing Cookie** | No `refreshToken` cookie present | No DB query / no modification | Cookie cleared | `204 No Content` | Empty |
| **Unknown / Tampered Token** | Token hash does not match any record | No modification | Cookie cleared | `204 No Content` | Empty |
| **Already Revoked Token** | Matching record has `RevokedAt != null` | No modification (idempotent) | Cookie cleared | `204 No Content` | Empty |
| **Expired Token** | Matching record has `ExpiresAt <= UtcNow` | No modification (remains expired) | Cookie cleared | `204 No Content` | Empty |
| **Whitespace / Malformed** | Empty or malformed string | No DB query / no modification | Cookie cleared | `204 No Content` | Empty |

### Token Oracle Defense
In all failure, missing, expired, and unknown token scenarios, the endpoint returns identical `HTTP 204 No Content` responses without revealing whether the session existed, belonged to a user, was expired, or was already revoked.

---

## 5. Cookie Deletion Specification

The cookie deletion header matches the configuration used when the cookie was originally set during login:

- **Name:** `refreshToken`
- **Path:** `/api/auth`
- **HttpOnly:** `true` (unreachable from JavaScript)
- **SameSite:** `Lax`
- **Secure:** `true` in production; dynamically matched to HTTPS / environment in development
- **Expiration:** Set to Unix epoch (`Thu, 01 Jan 1970 00:00:00 GMT` or `max-age=0`) to instruct immediate client deletion

---

## 6. Multiple Session Behavior (Session Isolation)

VaultX supports multiple concurrent sessions per user account (e.g., desktop browser and mobile device).

- **Current Session Only:** Calling `POST /api/auth/logout` revokes **only** the single refresh-token record identified by the submitted `refreshToken` cookie.
- **Other Sessions Preserved:** All other active refresh tokens belonging to the user remain active and unrevoked (`RevokedAt == null`, `IsActive == true`).
- **No Global Logout:** Global logout across all user devices is explicitly out of scope for Step 4 and deferred to future session management work.

---

## 7. Stateless JWT Limitation

- **JWT Access Tokens:** Access tokens are short-lived, stateless HMAC-SHA256 JWTs (~15-minute lifespan) containing user claims.
- **No Token Blacklist:** The logout endpoint does **not** implement a centralized JWT blacklist or distributed cache for already-issued JWT access tokens.
- **Cryptographic Validity:** An already-issued JWT access token remains cryptographically verifiable until its expiration timestamp.
- **Immediate Refresh Revocation:** Logout immediately invalidates the persistent refresh token session in PostgreSQL, preventing the client from obtaining any future access tokens via the refresh flow.

---

## 8. Security Controls & Threat Defense

1. **HttpOnly Cookie Defense:** Raw refresh tokens cannot be accessed, read, or deleted via client-side JavaScript.
2. **Hashed Persistence:** Raw refresh tokens are never persisted in the database; only SHA-256 hashes are stored.
3. **No Sensitive Data Disclosure:** Logout responses contain zero response bytes, preventing leakage of tokens, hashes, passwords, or user metadata.
4. **No Sensitive Logging:** Raw refresh tokens and hashes are excluded from application logging.
5. **HTTP Method Restriction:** Only `POST` is accepted; `GET`, `PUT`, `PATCH`, and `DELETE` return `405 Method Not Allowed`.
6. **Centralized Error Sanitization:** Unexpected database or runtime exceptions are intercepted by `ExceptionHandlingMiddleware` and return RFC 9110 ProblemDetails (`500 Internal Server Error`) without disclosing stack traces, database drivers, or connection details.

---

## 9. Backend Test Suite Coverage

### Unit Tests ([LogoutUserHandlerTests.cs](file:///c:/Users/User/source/repos/VaultX/tests/PasswordManager.UnitTests/Application/LogoutUserHandlerTests.cs)) — 9 Tests
- `HandleAsync_NullCommand_ThrowsArgumentNullException`
- `HandleAsync_NullOrWhitespaceToken_ReturnsWithoutInteractingWithRepository` (Theory: null, empty, whitespace)
- `HandleAsync_ValidActiveToken_RevokesMatchingSessionAndSaves`
- `HandleAsync_TokenNotFound_DoesNotThrowAndDoesNotSave`
- `HandleAsync_AlreadyRevokedToken_DoesNotSaveAgain`
- `HandleAsync_ExpiredToken_DoesNotRevokeOrSave`
- `HandleAsync_MalformedTokenCausingArgumentException_DoesNotThrowAndDoesNotSave`

### Integration Tests ([AuthControllerLogoutIntegrationTests.cs](file:///c:/Users/User/source/repos/VaultX/tests/PasswordManager.IntegrationTests/Controllers/AuthControllerLogoutIntegrationTests.cs)) — 13 Tests
- `Logout_ValidSession_Returns204AndRevokesTokenInDatabase`
- `Logout_InstructsBrowserToDeleteRefreshTokenCookie`
- `Logout_MissingCookie_Returns204NoContentWithoutModifyingDatabase`
- `Logout_UnknownOrTamperedToken_Returns204NoContentWithoutModifyingExistingSessions`
- `Logout_AlreadyRevokedSession_Returns204NoContentIdempotently`
- `Logout_ExpiredSession_Returns204NoContentWithoutReactivation`
- `Logout_MultipleSessions_RevokesOnlyTargetSession_PreservingOtherSessions` (Session isolation verified)
- `Logout_Response_NeverExposesSensitiveData`
- `Logout_UnsupportedHttpMethods_Return405MethodNotAllowed` (Theory: GET, PUT, PATCH, DELETE)
- `Logout_UnexpectedException_ReturnsSafe500ProblemDetailsWithoutInternalDisclosure`

### Suite Results
- **Unit Tests:** 78 / 78 passed (100%)
- **Integration Tests:** 95 / 95 passed (100%)
- **Total Backend Tests:** **173 / 173 passed (100%)**
- **Compiler Warnings:** 0 warnings, 0 errors

---

## 10. Deferred Work

The following items are outside the scope of Step 4 (Backend Implementation) and deferred to subsequent lifecycle steps or future roadmap phases:

1. **Step 5: Frontend Logout Implementation:** React UI components, logout button, `AuthContext` state clearing, token removal from memory.
2. **Step 6: Integration Testing:** End-to-end frontend-to-backend logout flow.
3. **Step 7: Browser E2E Testing:** Playwright real-browser logout scenarios.
4. **Step 8: Dedicated Security Testing:** Penetration testing and security hardening for logout.
5. **Token Refresh Endpoint (`POST /api/auth/refresh`):** Replay detection and token rotation.
6. **Protected Routes:** Phase 2, Section 6.4.
7. **Global Logout / All-Device Session Revocation:** Phase 2, Section 6.5+ / Session Management.
8. **JWT Blacklisting:** Explicitly avoided due to stateless architecture.
