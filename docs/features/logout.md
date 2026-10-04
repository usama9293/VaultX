# Feature: Logout (`POST /api/auth/logout`)

## 1. Overview & Current Status

**Status: Step 10 — Complete ✅ (Steps 1–10 Complete)**

Logout provides secure session termination for authenticated users by revoking the persistent refresh-token session identified by the incoming `refreshToken` cookie and instructing the user agent to clear the cookie.

### Vertical Slice Lifecycle Summary

- **Step 1: Requirement** — Complete (Single-session revocation, idempotent logout, cookie clearing, stateless JWT lifetime boundary)
- **Step 2: Design** — Complete (API contract: `POST /api/auth/logout`, cookie-based session identification, HTTP 204 No Content response)
- **Step 3: Security Analysis** — Complete (Threat modeling: session isolation, token oracle defense, sensitive data exposure defense, CSRF boundary)
- **Step 4: Backend Implementation** — Complete (`POST /api/auth/logout`, `ILogoutUserHandler`, `LogoutUserHandler`, single-session revocation, HttpOnly cookie deletion, idempotent 204 response)
- **Step 5: Frontend Implementation** — Complete (`logoutUser` API client, `AuthContext.logout`, authenticated-view Log Out control, local state cleared even on API failure, memory-only access token preserved, HttpOnly refresh cookie untouched by JavaScript)
- **Step 6: Integration** — Complete (End-to-end integration verified across frontend state, HTTP contract, cookie deletion, and database session revocation)
- **Step 7: End-to-End Testing** — Complete (Verified complete logout user journey in real Chromium browser using Playwright against live Vite dev server, ASP.NET Core API, and PostgreSQL database)
- **Step 8: Security Testing** — Complete (32/32 Logout-filtered security/integration test cases passed; see Section 13)
- **Step 9: Documentation** — Complete (this document records Step 8 scope, results, limitations, and deferred work)
- **Step 10: Complete** — Complete (Logout lifecycle acceptance criteria and final verification passed)

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

1. **`AuthController.Logout`** ([AuthController.cs](../../src/PasswordManager.API/Controllers/AuthController.cs)): Thin controller action; extracts `refreshToken` cookie, invokes `_logoutUserHandler.HandleAsync`, issues cookie deletion instruction with matching configuration, returns `NoContent()`.
2. **`ILogoutUserHandler` / `LogoutUserHandler`** ([LogoutUserHandler.cs](../../src/PasswordManager.Application/Features/Authentication/Logout/LogoutUserHandler.cs)): Application service responsible for hashing the raw refresh token, retrieving the matching entity from `IRefreshTokenRepository`, and revoking it if active.
3. **`RefreshToken` Domain Entity** ([RefreshToken.cs](../../src/PasswordManager.Domain/Entities/RefreshToken.cs)): Encapsulates `RevokedAt` timestamp and `IsActive` logic (`!IsRevoked && !IsExpired`).
4. **`RefreshTokenRepository`** ([RefreshTokenRepository.cs](../../src/PasswordManager.Infrastructure/Repositories/RefreshTokenRepository.cs)): Queries `RefreshTokens` by `TokenHash`.

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
4. **Sensitive Logging Checks:** Step 8 captured logs for exercised logout paths and checked that raw refresh-token values and cookie-form values were absent; the test does not assert every log field or perform a comprehensive hash/log-level audit.
5. **HTTP Method Restriction:** Only `POST` is accepted; `GET`, `PUT`, `PATCH`, and `DELETE` return `405 Method Not Allowed`.
6. **Centralized Error Sanitization:** Unexpected database or runtime exceptions are intercepted by `ExceptionHandlingMiddleware` and return RFC 9110 ProblemDetails (`500 Internal Server Error`) without disclosing stack traces, database drivers, or connection details.

---

## 9. Backend Test Suite Coverage

### Unit Tests ([LogoutUserHandlerTests.cs](../../tests/PasswordManager.UnitTests/Application/LogoutUserHandlerTests.cs)) — 9 Tests
- `HandleAsync_NullCommand_ThrowsArgumentNullException`
- `HandleAsync_NullOrWhitespaceToken_ReturnsWithoutInteractingWithRepository` (Theory: null, empty, whitespace)
- `HandleAsync_ValidActiveToken_RevokesMatchingSessionAndSaves`
- `HandleAsync_TokenNotFound_DoesNotThrowAndDoesNotSave`
- `HandleAsync_AlreadyRevokedToken_DoesNotSaveAgain`
- `HandleAsync_ExpiredToken_DoesNotRevokeOrSave`
- `HandleAsync_MalformedTokenCausingArgumentException_DoesNotThrowAndDoesNotSave`

### Integration Tests ([AuthControllerLogoutIntegrationTests.cs](../../tests/PasswordManager.IntegrationTests/Controllers/AuthControllerLogoutIntegrationTests.cs))
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
- **Integration Tests:** 114 / 114 passed (100%)
- **Total Backend Tests:** **192 / 192 passed (100%)**

---

## 10. Frontend Implementation (Step 5)

### Frontend Logout Flow

```text
User clicks Log Out
       ↓
AuthContext.logout()
       ↓
POST /api/auth/logout  (credentials: 'include', no body)
       ↓
Browser automatically attaches HttpOnly refreshToken cookie
       ↓
Backend revokes refresh session + clears cookie (HTTP 204)
       ↓
Frontend clears in-memory AuthState:
  status = 'unauthenticated'
  accessToken = null
  expiresAt = null
       ↓
Authenticated UI disappears; Sign In UI becomes available
```

### Components & Responsibilities

1. **`logoutUser()`** ([auth.ts](../../frontend/src/api/auth.ts)): Dedicated API client function. Sends `POST /api/auth/logout` with `credentials: 'include'` and **no request body**. Never reads, sends, or stores the refresh token from JavaScript. Treats HTTP 204 as success.
2. **`AuthContext.logout()`** ([AuthContext.tsx](../../frontend/src/context/AuthContext.tsx)): Attempts server logout, then **always** clears local in-memory auth state in a `finally` block. If the API call fails, local state is still cleared and the error is rethrown so callers do not falsely claim server session revocation succeeded.
3. **Log Out control** ([LoginForm.tsx](../../frontend/src/components/LoginForm.tsx)): Rendered only in the existing authenticated session view. Button semantics with loading/disabled state (`Signing Out...`) to prevent duplicate requests. Swallows API errors after local cleanup without rendering stack traces, tokens, or server internals.

### Local Cleanup on Network Failure

| Outcome | Server session revoked? | Local auth cleared? | UI state |
| :--- | :---: | :---: | :--- |
| HTTP 204 success | Yes (requested) | Yes | Unauthenticated |
| Network / API failure | Unknown / not confirmed | Yes | Unauthenticated |

Local logout (frontend state clearing) is unconditional. Server logout confirmation is never claimed when the request fails.

### Token & Cookie Security (Frontend)

- Access token remains **memory-only** in React `AuthContext` state — never written to `localStorage`, `sessionStorage`, IndexedDB, or URL.
- Refresh token remains **HttpOnly** and browser-managed — frontend never reads `document.cookie` for `refreshToken`, never deletes the cookie from JavaScript, and never places it in body/headers/URL.
- Cookie transmission relies on the same `credentials: 'include'` pattern established by Login, with the Vite `/api` proxy preserving same-origin cookie behavior in development.
- No second authentication store; reuses existing `AuthContext` / `useAuth` architecture.
- Protected routes, dashboard, vault, and session-management UI are **not** implemented in Step 5.

### Frontend Test Coverage (Step 5)

- **API client** (`authApi.test.ts`): `POST /api/auth/logout` contract, no body, `credentials: 'include'`, HTTP 204 handling, safe 500 message, network failure.
- **AuthContext** (`Logout.test.tsx`): Successful logout clears `status` / `accessToken` / `expiresAt`; **mandatory** network-failure test still clears local state; no storage/URL/cookie exposure.
- **UI control** (`Logout.test.tsx`): Log Out rendered when authenticated; click invokes logout; loading/disabled prevents duplicates; failure still transitions to Sign In without exposing sensitive details.

---

## 11. Integration Testing (Step 6)

### Integration Architecture

The Step 6 integration verifies that frontend client actions, HTTP cookie transports, ASP.NET Core API pipeline, application handlers, and PostgreSQL database persistence work together in harmony without leaking sensitive data or modifying unrelated records:

```text
┌────────────────────────────────────────────────────────┐
│                   React Frontend Tree                  │
│  [LoginForm] ──> [useAuth / AuthContext] ──> [auth.ts] │
└───────────────────────────┬────────────────────────────┘
                            │ POST /api/auth/logout
                            │ credentials: 'include'
                            │ Cookie: refreshToken=<token>
                            ▼
┌────────────────────────────────────────────────────────┐
│                   ASP.NET Core API                     │
│  [AuthController.Logout]                               │
│    ├─> Cookie Parsing (Request.Cookies["refreshToken"])│
│    ├─> Mediator Command (LogoutCommand)                │
│    └─> Cookie Deletion (Response.Cookies.Delete)       │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│                  Application Layer                     │
│  [LogoutUserHandler]                                   │
│    ├─> ITokenService.HashRefreshToken (SHA-256)        │
│    └─> IRefreshTokenRepository.GetByHashAsync         │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│               Infrastructure & Database                │
│  [RefreshTokenRepository / ApplicationDbContext]       │
│    ├─> RefreshToken.Revoke() (RevokedAt = UtcNow)      │
│    └─> PostgreSQL ("RefreshTokens" table)              │
└────────────────────────────────────────────────────────┘
```

### Actual Integration Flows

1. **Full Logout Lifecycle Flow:**
   - Registration creates a persistent User record.
   - Login generates a cryptographically secure refresh token, stores its SHA-256 hash in the database (`IsActive=true`, `RevokedAt=null`), and issues an HttpOnly cookie.
   - Client sends `POST /api/auth/logout` with the cookie attached.
   - Backend queries database by token hash, sets `RevokedAt` to `DateTime.UtcNow`, and commits changes.
   - Backend issues `Set-Cookie: refreshToken=; Path=/api/auth; HttpOnly; SameSite=Lax; Expires=Thu, 01 Jan 1970 00:00:00 GMT` (or `Max-Age=0`).
   - Server returns `HTTP 204 No Content` with zero response body bytes.
   - Database confirms the session is revoked and inactive, while the user entity remains completely intact.

2. **Session Isolation Flow:**
   - User logs in from two independent clients (e.g., Session A and Session B).
   - Calling logout for Session A revokes only Session A's record (`IsRevoked=true`, `RevokedAt!=null`).
   - Session B remains fully active and unrevoked (`IsActive=true`, `RevokedAt=null`).
   - User profile and credentials remain unaltered.

3. **Missing & Malformed Cookie Resilience Flow:**
   - Client sends `POST /api/auth/logout` without a `refreshToken` cookie.
   - Server returns `HTTP 204 No Content`, emits cookie deletion header to clear any stale client state, and makes zero modifications to database records.

4. **Revocation Idempotency Flow:**
   - Subsequent logout requests using an already-revoked session return `HTTP 204 No Content`.
   - The initial `RevokedAt` timestamp is preserved and not overwritten.

5. **Expired Session Flow:**
   - Sending an expired session token returns `HTTP 204 No Content`.
   - The session record remains expired and is not reactivated.

6. **Data Integrity & Non-Deletion Flow:**
   - Logout operations never delete rows from `Users` or `RefreshTokens`.
   - Record counts remain identical before and after logout; existing records are updated rather than deleted.
   - Unrelated users and their active sessions are completely untouched.

7. **Frontend Application Integration Flow:**
   - Mounting the application authentication tree and triggering Log Out executes `POST /api/auth/logout` with `credentials: 'include'` and no request body.
   - Authenticated UI transitions seamlessly back to the Sign In form.
   - In-memory `accessToken` and `expiresAt` are immediately wiped from React state.
   - No token data is written to or retained in `localStorage`, `sessionStorage`, or URL parameters.
   - Network failure triggers local cleanup in a `finally` block, returns the UI to Sign In, suppresses raw error displays, and prevents token exposure.
   - Pending logout requests display "Signing Out..." loading state and disable the button, preventing duplicate submissions.

### Test Coverage & Results

#### Backend Integration Tests ([LogoutIntegrationTests.cs](../../tests/PasswordManager.IntegrationTests/Integration/LogoutIntegrationTests.cs)) — 7 Tests
- `Registration_Then_Login_Then_Logout_FullLifecycle_Succeeds_WithDatabaseRevocation_AndCookieDeletion` — Verifies complete lifecycle from account registration to session revocation and cookie deletion.
- `Logout_CookieDeletion_AdheresToContract_AndEnvironmentAwareSecurePolicy` — Verifies cookie deletion contract (`refreshToken`, `/api/auth`, `HttpOnly`, `SameSite=Lax`, expiration) and dynamic `Secure` flag policy across HTTP and HTTPS schemes.
- `Logout_MultipleSessions_RevokesOnlyTargetSession_LeavingOtherSessionsActive` — Verifies single-session revocation and multi-session isolation.
- `Logout_WithoutCookie_Returns204NoContent_WithoutModifyingDatabase` — Verifies missing cookie resilience and database immutability.
- `Logout_AlreadyRevokedSession_Returns204NoContent_IdempotentlyPreservingTimestamp` — Verifies idempotent 204 response and timestamp preservation.
- `Logout_ExpiredSession_Returns204NoContent_DoesNotReactivate` — Verifies expired session handling without reactivation.
- `Logout_PreservesUserData_PasswordHash_AndUnrelatedRecords` — Verifies user and session row preservation, password hash immutability, and isolation of unrelated user sessions.

#### Frontend Integration Tests ([LogoutIntegration.test.tsx](../../frontend/src/__tests__/LogoutIntegration.test.tsx)) — 4 Tests
- `1. Authenticated App Logout Flow: executes POST /api/auth/logout with credentials, transitions UI to Sign In, wipes in-memory auth state, and leaves no storage or URL trace`
- `1b. Full App Flow: user logs in through App, establishes session, logs out, and App transitions back to Sign In`
- `2. Network Failure: clears local auth state, returns UI to Sign In, suppresses raw exception, and prevents token exposure`
- `3. Loading & Duplicate Prevention: shows Signing Out... state, disables button, and prevents duplicate API calls while request is in flight`

#### Test Suite Execution Summary
- **Backend Unit Tests:** 78 / 78 passed (100%)
- **Backend Integration Tests:** 102 / 102 passed (100%, including all 20 Logout tests)
- **Frontend Tests:** 87 / 87 passed (100%, including all 4 Logout integration tests)
- **Total Automated Tests:** 267 / 267 passed (100%)
- **Frontend Linter:** 0 warnings, 0 errors
- **Frontend Build:** Successful production bundle

### Important Integration Decisions

1. **Strict Cookie-Only Session Identification:** The integration confirms that session termination relies exclusively on the HttpOnly `refreshToken` cookie. No client-supplied IDs or body payloads are inspected or permitted.
2. **Dynamic Secure Cookie Policy:** The cookie deletion policy matches the server environment and connection scheme: in development HTTP, the Secure flag is omitted for browser compatibility, while in HTTPS requests and non-development environments, the Secure flag is enforced.
3. **Unconditional Local Cleanup with Error Propagation:** The frontend architecture clears React in-memory authentication state regardless of whether the backend request succeeds or fails, while the UI swallows the raw exception so as not to expose internal network details or claim server-side revocation when it could not be confirmed.
4. **Idempotence and Non-Disclosure:** All non-standard scenarios (missing cookie, already revoked, expired session, unknown token) return identical `HTTP 204 No Content` responses, preventing session enumeration or state oracle attacks.

### Step 6 Acceptance Criteria & Results

| Criterion | Requirement | Result | Evidence |
| :--- | :--- | :---: | :--- |
| **AC-6.1: Full Lifecycle** | Complete registration -> login -> logout cycle with DB revocation & cookie deletion | **PASS** | `Registration_Then_Login_Then_Logout_FullLifecycle_Succeeds_WithDatabaseRevocation_AndCookieDeletion` |
| **AC-6.2: Session Isolation** | Revoking session A leaves session B active; user remains unchanged | **PASS** | `Logout_MultipleSessions_RevokesOnlyTargetSession_LeavingOtherSessionsActive` |
| **AC-6.3: Missing Cookie Resilience** | Logout without cookie returns 204 and clears cookie without DB mutations | **PASS** | `Logout_WithoutCookie_Returns204NoContent_WithoutModifyingDatabase` |
| **AC-6.4: Revocation Idempotency** | Logging out already-revoked session returns 204 and preserves `RevokedAt` | **PASS** | `Logout_AlreadyRevokedSession_Returns204NoContent_IdempotentlyPreservingTimestamp` |
| **AC-6.5: Expired Session Handling** | Logging out expired session returns 204 without reactivating record | **PASS** | `Logout_ExpiredSession_Returns204NoContent_DoesNotReactivate` |
| **AC-6.6: Data Preservation** | User record, password hash, and unrelated rows are preserved intact | **PASS** | `Logout_PreservesUserData_PasswordHash_AndUnrelatedRecords` |
| **AC-6.7: Cookie Deletion Contract** | Name, path, HttpOnly, SameSite, and environment-aware Secure attributes verified | **PASS** | `Logout_CookieDeletion_AdheresToContract_AndEnvironmentAwareSecurePolicy` |
| **AC-6.8: Frontend App Flow** | React app transitions to Sign In, clears in-memory state, zero persistent storage | **PASS** | `LogoutIntegration.test.tsx` (Tests 1 & 1b) |
| **AC-6.9: Network Failure Resilience**| Local auth state cleared, UI returns to Sign In, raw errors suppressed | **PASS** | `LogoutIntegration.test.tsx` (Test 2) |
| **AC-6.10: Duplicate Prevention** | Loading state disables button and prevents duplicate in-flight API requests | **PASS** | `LogoutIntegration.test.tsx` (Test 3) |

---

## 12. Browser End-to-End Testing (Step 7)

### E2E Architecture

Step 7 validates the Logout vertical slice through a real browser and the complete running application stack:

```text
Playwright (Chromium)
        ↓
Vite Dev Server (http://localhost:5173)
        ↓ (Proxy /api -> http://localhost:5071)
ASP.NET Core API (http://localhost:5071)
        ↓
Application Services & Handlers (LogoutUserHandler)
        ↓
Infrastructure Repositories & EF Core
        ↓
PostgreSQL Database ("Users", "RefreshTokens")
```

The test runner interacts with the actual UI DOM via accessible selectors, receives real HTTP responses, checks actual browser cookie jar state via `page.context().cookies()`, and validates JavaScript storage/URL boundaries.

### Test Scenarios & Matrix ([logout.spec.ts](../../e2e/auth/logout.spec.ts))

The browser E2E test suite covers five scenarios:

1. **Complete Browser Logout (`logout.spec.ts:33`):**
   - Creates a unique test user via real API registration (`createE2EUser`).
   - Navigates to `/`, submits credentials through the real UI, receives `200 OK` on `POST /api/auth/login`.
   - Verifies the authenticated session region is displayed with "Session Established".
   - Clicks "Log Out", captures the real `POST /api/auth/logout` response, and asserts `HTTP 204 No Content`.
   - Verifies the UI navigates back to "Sign In", the authenticated session region is removed from the DOM, and no raw errors are displayed.

2. **Refresh Cookie Lifecycle (`logout.spec.ts:64`):**
   - Logs in through the real browser UI and inspects `page.context().cookies()`.
   - Asserts `refreshToken` cookie exists with a non-empty value, `HttpOnly=true`, `Path=/api/auth`, `SameSite=Lax`, valid positive expiration, and `Secure=false` in local HTTP development.
   - Evaluates `document.cookie` to confirm `refreshToken` is completely inaccessible from client JavaScript.
   - Executes real browser logout and asserts `HTTP 204`.
   - Re-inspects `page.context().cookies()` and verifies `refreshToken` has been completely deleted from the browser cookie jar.

3. **Access Token Storage & URL Boundary (`logout.spec.ts:115`):**
   - Captures `accessToken` from the real login response.
   - Validates that `accessToken` is never stored in `localStorage`, `sessionStorage`, URL search parameters, URL hash, or `document.cookie` while authenticated.
   - Executes real browser logout.
   - Validates that after logout, `localStorage` and `sessionStorage` remain completely empty (`length === 0`), and URL parameters contain no token data.

4. **Logout Network Failure Resilience (`logout.spec.ts:156`):**
   - Establishes a real authenticated session.
   - Intercepts `POST /api/auth/logout` via `page.route` and aborts the request to simulate a network outage.
   - Clicks "Log Out".
   - Confirms that the frontend contract (`AuthContext.logout` `finally` block) clears in-memory auth state, causing the UI to return to "Sign In" and removing the authenticated session region.
   - Confirms that no raw exceptions, stack traces (`TypeError`, `Failed to fetch`), or tokens are exposed to the user.
   - Confirms that tokens are not leaked into persistent browser storage on failure.

5. **Loading State & Duplicate Request Prevention (`logout.spec.ts:194`):**
   - Establishes a real authenticated session.
   - Intercepts `POST /api/auth/logout` and defers fulfillment using a pending Promise.
   - Clicks "Log Out" and immediately asserts the button transitions to "Signing Out...", is `disabled`, and has `aria-busy="true"`.
   - Attempts additional clicks while the request is in flight.
   - Fulfills the intercepted route and verifies the UI returns to "Sign In".
   - Asserts that exactly one `POST /api/auth/logout` network request was issued throughout the interaction.

### Test Results

- **Command:** `npm run test:e2e` (from `frontend/`)
- **Framework:** Playwright v1.63.0 (Chromium, 1 worker, fullyParallel: false)
- **Suite Results:**
  - `login.spec.ts`: 7 passed
  - `logout.spec.ts`: 5 passed
  - **Total E2E Tests:** **12 passed (100%)**
  - Execution duration: **10.2s**

### Step 7 Acceptance Criteria & Results

| Criterion | Requirement | Result | Evidence |
| :--- | :--- | :---: | :--- |
| **AC-7.1: Real Browser Journey** | Real browser registers, logs in, and logs out through actual UI | **PASS** | `logout.spec.ts` (Test 1) |
| **AC-7.2: Real API Logout** | `POST /api/auth/logout` is issued by browser and returns HTTP 204 | **PASS** | `logout.spec.ts` (Test 1) |
| **AC-7.3: UI State Transition** | UI transitions from authenticated session back to Sign In | **PASS** | `logout.spec.ts` (Test 1) |
| **AC-7.4: Cookie Jar Deletion** | `refreshToken` exists after login and is deleted after logout | **PASS** | `logout.spec.ts` (Test 2) |
| **AC-7.5: Cookie Inaccessibility** | `refreshToken` cannot be read via `document.cookie` | **PASS** | `logout.spec.ts` (Test 2) |
| **AC-7.6: Storage & URL Isolation** | Access token never present in storage, URLs, or cookies | **PASS** | `logout.spec.ts` (Test 3) |
| **AC-7.7: Network Failure Safety** | Network failure clears local state and returns UI to Sign In | **PASS** | `logout.spec.ts` (Test 4) |
| **AC-7.8: Duplicate Prevention** | Loading state disables button and prevents duplicate requests | **PASS** | `logout.spec.ts` (Test 5) |
| **AC-7.9: Regression Integrity** | Existing tests (login E2E, frontend unit/integration, backend) pass | **PASS** | Full suites: 192 backend, 87 frontend, 12 E2E |
| **AC-7.10: Security Boundary** | Step 7 browser coverage remains distinct from dedicated Step 8 security tests | **PASS** | Step 8 results recorded in Section 13 |
| **AC-7.11: Documentation** | Step 7 remains complete and subsequent lifecycle steps are accurately recorded | **PASS** | This document and the roadmap |

---

## 13. Security Testing (Step 8)

Dedicated Logout security testing was completed after Step 7. The existing logout implementation and security tests were reviewed and exercised without changing production behavior.

### Security Properties Verified by Tests

The test coverage exercised:

- Cookie-only refresh-token selection, including attempts to override the selected session through a JSON body, query string, Authorization header, and security-sensitive body fields such as `userId`, `refreshTokenId`, `tokenHash`, `revokedAt`, `expiresAt`, `replacedByTokenId`, and `sessionId`.
- Session isolation: logging out one of a user's concurrent sessions leaves the other active.
- Cross-user impersonation attempts: client-supplied identity/session fields do not select another user's session.
- Unknown and tampered cookie values, including random values, trailing or only whitespace, empty values, long values, and a value with an appended segment; the genuine unrelated session remains active.
- Non-disclosive logout responses and sanitized unexpected-error responses.
- Sensitive logging checks that capture the exercised valid, unknown/tampered, malformed, and expired paths and assert that their raw refresh-token values and cookie-form values are absent. These assertions do not constitute a general audit of every log field or logging level.
- Unsupported `GET`, `PUT`, `PATCH`, and `DELETE` methods returning `405 Method Not Allowed`.
- Refresh-cookie lifecycle behavior: HttpOnly, `/api/auth` path, `SameSite=Lax`, `Secure=false` in local HTTP development, and deletion on logout. The production Secure policy was not exercised in this verification. E2E coverage also confirms the refresh cookie is unavailable to JavaScript.
- Frontend logout cleanup of in-memory authentication state, with no access token left in persistent browser storage or URL.

**No vulnerabilities were identified within the tested Logout attack surface.** This is limited to the stated implementation and test coverage; it is not a claim that VaultX is universally secure or immune to attacks.

### Verified Results

| Verification | Result |
| :--- | :---: |
| Logout-filtered security/integration tests | **32 / 32 passed** |
| Full backend suite | **192 / 192 passed** (114 integration, 78 unit) |
| Frontend tests | **87 / 87 passed** |
| Playwright E2E suite | **12 / 12 passed** |
| Frontend lint | **PASS** |
| Frontend production build | **PASS** |
| `git diff --check` | **PASS** |

### Deferred Features and Known Limitations

- **Stateless JWT limitation:** Logout revokes the refresh session and clears client authentication state, but does not immediately invalidate an already-issued stateless access JWT. The JWT remains cryptographically valid until expiration.
- **Session scope:** Logout revokes only the session identified by its refresh-token cookie. Global/all-device logout is deferred to future session-management work.
- **Refresh-token rotation/replay detection:** Not implemented as part of Logout Step 8; deferred to the refresh/session-management roadmap work.
- **JWT blacklisting:** Not implemented; this remains intentionally outside Logout's stateless access-token design.

---

## 14. Deferred Work

The following work remains outside the completed Logout lifecycle steps:

1. **Step 10: Feature Complete:** Complete (Logout lifecycle acceptance verified).
2. **Token Refresh Endpoint (`POST /api/auth/refresh`):** Replay detection and token rotation.
3. **Protected Routes:** Phase 2, Section 6.4.
4. **Global Logout / All-Device Session Revocation:** Phase 2, Section 6.5+ / Session Management.
