# Login Feature Specification & Implementation Documentation

**Feature:** User Login & Authentication  
**Vertical Slice:** Phase 2, Section 6.2 (Login)  
**Status:** Backend, Frontend, Integration, End-to-End & Security Testing Complete (Steps 4, 5, 6, 7 & 8 Complete) ✅  
**Date:** October 2026

---

## 1. Purpose

The Login feature provides secure identity verification and session establishment for registered VaultX users. It authenticates credentials (email and master password), generates short-lived JWT access tokens for API authorization, and issues cryptographically secure opaque refresh tokens stored via secure `HttpOnly` cookies to manage long-term sessions. HttpOnly prevents JavaScript from directly reading the refresh-token cookie; XSS itself is not fully prevented by this feature.

---

## 2. Authentication Flow

The backend authentication pipeline operates as follows:

```text
Client
  │
  ▼
POST /api/auth/login (JSON payload: email, password)
  │
  ├─ CORS Check (FrontendDevPolicy with scoped origin and credentials allowed)
  ├─ ExceptionHandlingMiddleware (RFC 9110 ProblemDetails boundary)
  ├─ AuthController receives LoginRequest
  │
  ▼
Application Layer
  │
  ├─ LoginCommandValidator (Pure C# static validator)
  │    - Validates email presence, length (<= 320), and format
  │    - Validates password presence
  │
  ├─ Normalize Email (trim and lowercase invariant)
  │
  ├─ User Lookup via UserRepository.GetByEmailAsync
  │    - If user not found: throw InvalidCredentialsException ("Invalid email or password.")
  │
  ├─ Password Verification via IPasswordHasher.VerifyPasswordAsync
  │    - Compares supplied password against stored PBKDF2 hash using constant-time comparison
  │    - If invalid: throw InvalidCredentialsException ("Invalid email or password.")
  │
  ▼
Session & Token Generation
  │
  ├─ TokenService.GenerateAccessToken(user)
  │    - Generates 15-minute JWT signed with HMAC-SHA256 (256-bit secret key)
  │    - Claims: sub (UserId), email, jti (unique GUID), iat, exp
  │
  ├─ TokenService.GenerateRefreshToken()
  │    - Generates 64 cryptographically secure random bytes (CSPRNG)
  │    - Encodes as URL-safe Base64 string (rawRefreshToken)
  │    - Hashes using SHA-256 to produce deterministic 64-character hex tokenHash
  │    - Sets 7-day expiration timestamp
  │
  ▼
Persistence & Response
  │
  ├─ RefreshToken entity instantiated with (UserId, tokenHash, expiresAt)
  ├─ Persisted to PostgreSQL RefreshTokens table via IRefreshTokenRepository and IUnitOfWork
  │    - Previous active sessions for the user remain valid (multiple concurrent devices supported)
  │
  ├─ AuthController sets secure HttpOnly cookie:
  │    - Name: refreshToken
  │    - Value: rawRefreshToken
  │    - HttpOnly: true
  │    - Secure: true in production (and HTTPS in development)
  │    - SameSite: Lax
  │    - Path: /api/auth
  │    - Expires: RefreshTokenExpiresAt
  │
  ▼
Safe JSON Response
  │
  └─ Returns HTTP 200 OK with { accessToken, expiresAt }
```

---

## 3. API Contract

### Endpoint

- **Method:** `POST`
- **Path:** `/api/auth/login`
- **Consumes:** `application/json`
- **Produces:** `application/json`

### Request Payload

```json
{
  "email": "user@example.com",
  "password": "StrongPassword123!"
}
```

| Field      | Type     | Required | Constraints                                 |
| ---------- | -------- | -------- | ------------------------------------------- |
| `email`    | `string` | Yes      | Non-empty, valid format, max 320 characters |
| `password` | `string` | Yes      | Non-empty                                   |

### Successful Response: `HTTP 200 OK`

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-10-01T16:15:00Z"
}
```

#### Response Headers

```http
Set-Cookie: refreshToken=dGhpcy1pcy1hLXJhbmRvbS1yZWZyZXNoLXRva2Vu...; expires=Thu, 08 Oct 2026 16:00:00 GMT; path=/api/auth; samesite=lax; httponly
```

_Note: The response body contains only the short-lived access token and its expiration timestamp. It never contains the raw refresh token, token hash, password, password hash, or internal database metadata._

### Invalid Credentials Response: `HTTP 401 Unauthorized`

Compliant with RFC 9110 Problem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Invalid email or password."
}
```

_Note: Identical response is returned whether the email does not exist or the password is wrong, preventing user enumeration._

### Validation Error Response: `HTTP 400 Bad Request`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email is required."]
  }
}
```

---

## 4. Authentication & Session Architecture

### JWT Access Token

- **Lifetime:** 15 minutes (configurable via `JwtSettings:AccessTokenExpirationMinutes`).
- **Algorithm:** HMAC-SHA256 (`HmacSha256`) using symmetric key (minimum 256 bits).
- **Claims:**
  - `sub` / `NameIdentifier`: User ID (GUID).
  - `email`: User email.
  - `jti`: Unique token GUID identifier.
  - `iat`: Issued at timestamp.
  - `exp`: Expiration timestamp.
- **Storage:** Handled in frontend memory; never stored in persistent server databases.

### Opaque Refresh Token

- **Source:** 64 random bytes from `RandomNumberGenerator` (CSPRNG), Base64Url-encoded.
- **Lifetime:** 7 days (configurable via `JwtSettings:RefreshTokenExpirationDays`).
- **Delivery:** Delivered strictly via `HttpOnly` cookie. HttpOnly prevents JavaScript from directly reading the refresh-token cookie; XSS itself is not fully prevented by this feature.
- **Persistence:** The raw refresh token is not stored in PostgreSQL; only its SHA-256 hash is stored, so database contents do not directly expose the refresh-token cookie value.
- **Data Model:**
  ```text
  RefreshToken
  ├── Id (Guid, PK)
  ├── UserId (Guid, FK -> Users.Id)
  ├── TokenHash (varchar(128), Unique Index)
  ├── ExpiresAt (timestamptz)
  ├── CreatedAt (timestamptz)
  ├── RevokedAt (timestamptz, nullable)
  └── ReplacedByTokenId (Guid, nullable)
  ```
- **Session Isolation:** Multiple concurrent sessions for the same user are supported without overwriting prior valid tokens.
- **Replay Protection Foundation:** Revocation/rotation foundation exists; full replay protection will be implemented with the refresh-token endpoint.

### ASP.NET Core JWT Middleware

Configured via `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`:

- Validates token signature, issuer, audience, and expiration.
- Configured with `ClockSkew = TimeSpan.Zero` for strict lifetime enforcement.
- Integrated into the pipeline via `UseAuthentication()` before `UseAuthorization()`.

---

## 5. Frontend Architecture & In-Memory State

### Component Architecture

- **LoginForm (`src/components/LoginForm.tsx`):**
  - Controlled inputs for `email` and `password`.
  - Accessible master password visibility toggle button ("Show" / "Hide").
  - Form validation errors displayed beneath corresponding inputs with `aria-describedby` and `aria-invalid`.
  - Global error alert banner with `role="alert"` for authentication failures (generic 401) and connectivity issues.
  - Loading state disables submission button and renders a spinner while in flight.
  - Sensitive password fields are wiped from component state immediately upon submission.
  - When authenticated, renders a clean session indicator card without exposing tokens or internal identifiers.
- **LoginForm Styles (`src/components/LoginForm.css`):**
  - Reuses design system CSS custom properties from `index.css` and layout patterns from `RegisterForm.css`.

### Client-Side Validation

- Pure TypeScript functions in `src/utils/validation.ts`:
  - `validateEmail(email)`: Ensures presence, max length (320), and format validity.
  - `validateLoginPassword(password)`: Ensures presence without client-enforcing registration complexity policies.
  - `validateLoginForm(data)`: Combines checks into a strongly-typed `LoginFormErrors` object.

### Login API Client

- `loginUser(request: LoginRequest): Promise<LoginResponse>` in `src/api/auth.ts`:
  - Sends `POST /api/auth/login` with `Content-Type: application/json` and `credentials: 'include'`.
  - `credentials: 'include'` allows the browser to receive and manage the backend's `HttpOnly` refresh token cookie.
  - Maps HTTP 401 to generic `Invalid email or password.` message to preserve user-enumeration resistance.
  - Maps HTTP 400 validation errors to clean client error messages.
  - Maps unexpected HTTP 500 / server responses to safe generic text: `Unable to sign in right now. Please try again.`

### Access Token Storage — In-Memory React State

- Managed via `AuthProvider` and `useAuth` hook in `src/context/`:
  - Access token and expiration timestamp are held **strictly in React component memory (`useState`)**.
  - **Zero Persistent Storage:** The access token is never written to `localStorage`, `sessionStorage`, `IndexedDB`, or JavaScript-accessible cookies.
  - **Zero URL Exposure:** The access token is never placed in URL query parameters, hash fragments, or navigation paths.
  - **Zero Token Logging:** The token is never written to `console.log`, `console.info`, or error outputs.
  - **Refresh Token Boundary:** The refresh token cookie is `HttpOnly`; JavaScript cannot and does not read it (`document.cookie` is untouched).

---

## 6. Full-Stack Integration Architecture (Step 6)

Step 6 documents the React client and backend ASP.NET Core API contract, along with the separate frontend and backend integration coverage for the data, session, and security pipeline. It does not yet verify a real browser-to-live-frontend-to-live-API-to-live-database flow:

```text
User Submits LoginForm
        │
        ▼
React LoginForm (Client-side validation via validateLoginForm)
        │
        ▼
API Client (loginUser in src/api/auth.ts)
  - Method: POST /api/auth/login
  - Headers: Content-Type: application/json
  - Credentials: include (allows browser cookie jar management)
        │
        ▼
ASP.NET Core API Pipeline
  - CORS Policy (FrontendDevPolicy with origin scoping and AllowCredentials)
  - ExceptionHandlingMiddleware (RFC 9110 ProblemDetails translation)
  - AuthController.Login receives strongly-typed LoginRequest
        │
        ▼
Application Core
  - LoginCommandValidator (Pure C# static email format & presence validator)
  - Email normalization (Trim + Lowercase invariant)
  - UserRepository.GetByEmailAsync
  - IPasswordHasher.VerifyPasswordAsync (Constant-time PBKDF2 hash verification)
  - Generic InvalidCredentialsException ("Invalid email or password.") on mismatch
        │
        ▼
Security Token & Session Services
  - TokenService.GenerateAccessToken(user) -> 15-minute HMAC-SHA256 JWT
  - TokenService.GenerateRefreshToken() -> 64-byte CSPRNG opaque token + SHA-256 hash
  - RefreshToken entity persisted to PostgreSQL via IRefreshTokenRepository and IUnitOfWork
        │
        ▼
HTTP 200 OK Response + Set-Cookie Header
  - Response Body: { accessToken, expiresAt }
  - Set-Cookie: refreshToken=<raw>; Path=/api/auth; SameSite=Lax; HttpOnly; Secure (prod/HTTPS)
        │
        ▼
Frontend AuthContext State Update
  - Access token and expiration stored strictly in React in-memory state (useState)
  - Zero exposure to localStorage, sessionStorage, IndexedDB, or URL parameters
  - Client JavaScript cannot read the HttpOnly refresh token cookie
  - UI re-renders to show authenticated session indicator
```

### Integration Verification Highlights

1. **Frontend API Contract Coverage:**

- `LoginIntegration.test.tsx` verifies the React login flow and API-client integration against the backend API contract using mocked network behavior.
- Client request expectations include JSON payloads and `credentials: 'include'`.

2. **Database Integrity:**

- `LoginIntegrationTests.cs` exercises the real ASP.NET Core application and PostgreSQL persistence through the backend API.
- Raw refresh tokens are never persisted. Only the deterministic 64-character SHA-256 hash is stored in `RefreshTokens`.
- Foreign key constraint to `Users.Id` is enforced; multiple device sessions can coexist without clobbering existing valid sessions.

3. **In-Memory Token Isolation:**
   - Access tokens are stored exclusively in React memory (`useState`).

- Frontend integration tests verify that `localStorage`, `sessionStorage`, and URL paths contain zero token or sensitive credential residues.

4. **Resilience & Error Handling:**
   - Generic 401 ProblemDetails returned for non-existent users, wrong passwords, and casing variations, effectively thwarting user enumeration.
   - 400 Bad Request returned with RFC 9110 validation errors for missing or malformed inputs.
   - Network connectivity failures trigger clean client-side alert banners without crashing the application.

---

## 7. Security Threats & Defenses

| Threat                                  | Defense                                                                                                                                                                                                | Verification                | Status                     |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------- | -------------------------- |
| User enumeration                        | Generic authentication failure (`"Invalid email or password."`) returned for both nonexistent emails and wrong passwords.                                                                              | Integration & Frontend test | Verified                   |
| Password exposure                       | Plaintext password is never logged, persisted, returned in responses, or stored in cookies. Processed only in-memory and wiped from component state.                                                   | Security test               | Verified                   |
| JavaScript access to refresh token      | Transmitted solely via secure `HttpOnly` cookie with `Path=/api/auth`. HttpOnly prevents JavaScript from directly reading the refresh-token cookie; XSS itself is not fully prevented by this feature. | Security & Integration test | Verified                   |
| Database exposure of refresh tokens     | The raw refresh token is not stored in PostgreSQL; only its SHA-256 hash is stored, so database contents do not directly expose the refresh-token cookie value.                                        | Integration & DB test       | Verified                   |
| Refresh-token replay                    | The current Login slice stores refresh-token hashes and establishes the persistence foundation, while actual refresh-token rotation/replay handling belongs to the future refresh endpoint/session-security slice. | Domain entity & unit test   | Foundation only / Deferred |
| Access-token exposure                   | Short 15-minute token lifetime and in-memory client storage minimize the window of vulnerability. Excluded from persistent server storage.                                                             | Security test               | Verified                   |
| Access-token persistent storage leakage | Access token is held strictly in React memory; never stored in `localStorage`, `sessionStorage`, `IndexedDB`, or URL parameters.                                                                       | Frontend Integration test   | Verified                   |
| JWT tampering                           | Cryptographically signed using HMAC-SHA256 with a 256-bit secret key; unauthorized signatures or modified payloads fail verification.                                                                  | Integration test            | Verified                   |
| Expired JWT reuse                       | Strict lifetime verification with zero clock skew (`ClockSkew = TimeSpan.Zero`) rejects expired JWTs.                                                                                                  | Integration test            | Verified                   |
| Error disclosure                        | Centralized `ExceptionHandlingMiddleware` and frontend API client ensure sanitized ProblemDetails and generic safe messages.                                                                           | Security test               | Verified                   |
| CORS abuse                              | Scoped to explicitly allowed development origins (`http://localhost:5173`, etc.) with `AllowCredentials()`. Wildcards rejected.                                                                        | Security test               | Verified                   |
| Rate limiting / brute-force protection  | Not currently implemented; deferred security hardening.                                                                                                                                                | Deferred security hardening | Deferred                   |

---

## 8. Testing Summary

### Automated Test Results

- **Backend Unit Tests (`PasswordManager.UnitTests`):** 69 / 69 passed
  - `LoginCommandValidatorTests`: 5 tests
  - `RefreshTokenTests`: 6 tests
  - `TokenServiceTests`: 5 tests (+1 security claims audit test)
  - `LoginUserHandlerTests`: 3 tests
  - Existing Registration unit tests: 50 tests
- **Backend Integration Tests (`PasswordManager.IntegrationTests`):** 82 / 82 passed
  - `LoginIntegrationTests.cs` (backend API/database integration through the real ASP.NET Core application and PostgreSQL persistence): 4 tests
  - `AuthControllerLoginIntegrationTests`: 8 tests
  - `LoginSecurityTests`: 31 tests (+22 Step 8 security tests across boundary, tamper, injection, and enumeration scenarios)
  - Existing Registration integration & security tests: 39 tests
- **Frontend Test Suite (`vitest`):** 70 / 70 passed
  - `LoginIntegration.test.tsx` (frontend integration/component integration using the backend API contract with mocked network behavior): 4 tests
  - `LoginForm.test.tsx`: 17 tests (+2 Step 8 advanced security & XSS boundary tests)
  - `RegisterForm.test.tsx`: 15 tests
  - `validation.test.ts`: 24 tests
  - `authApi.test.ts`: 10 tests
- **Total Backend Tests:** **151 / 151 passed (100%)**
- **Total Frontend Tests:** **70 / 70 passed (100%)**
- **Playwright Browser E2E Tests:** **7 / 7 passed (100%)**
- **Overall Suite Metric:** **228 total automated tests across frontend, backend, and browser E2E**
- **Security-Specific Verification:** **34 security-specific tests** (all passed)
- **Linter & Typecheck:** 0 warnings, 0 errors (`oxlint` + `tsc -b`)

---

## 9. End-to-End Testing (Step 7)

**Status:** Complete

Step 7 verifies the actual login application flow in a real Chromium browser using the local runtime topology:

```text
Chromium browser
  ↓
Vite frontend: http://localhost:5173
  ↓ /api proxy
ASP.NET Core API: http://localhost:5071
  ↓
PostgreSQL: localhost:5432 / VaultXDb
  ↓
React AuthContext and authenticated UI
```

### E2E Architecture

- Framework: Playwright (`@playwright/test`), using the installed Chrome channel as Chromium.
- Configuration: `frontend/playwright.config.ts`.
- Tests: `e2e/auth/login.spec.ts`.
- Test-user helper: `e2e/fixtures/test-user.ts`.
- Frontend command: `npm run dev -- --host localhost --port 5173`.
- Backend command: `dotnet run --project src/PasswordManager.API/PasswordManager.API.csproj --launch-profile http`.
- PostgreSQL migrations were applied with `dotnet ef database update` before the verified run; the API does not apply migrations automatically.

### E2E Scenarios

The suite contains seven passing browser tests:

1. Valid login observes the real `POST /api/auth/login` response, verifies HTTP 200 and response structure, and displays the authenticated UI.
2. Refresh-cookie security verifies `HttpOnly`, `Path=/api/auth`, `SameSite=Lax`, expiration, and Development HTTP `Secure=false` without logging the cookie value.
3. Access-token storage verifies the token is absent from `localStorage`, `sessionStorage`, URL query/hash, and JavaScript-visible cookies while the authenticated UI is present.
4. Invalid password receives the real HTTP 401 response and displays the generic authentication error.
5. Unknown email receives the same generic HTTP 401 response.
6. Empty and malformed input are rejected by the real browser UI validation.
7. An intentionally aborted login request displays the safe network-failure message.

Each successful-login test creates a unique `e2e-<uuid>@vaultx.local` user through the real registration endpoint. The generated users are intentionally namespaced because the current application has no test-only deletion endpoint or database reset utility.

### E2E Security Boundaries

- Access-token and refresh-token values are never logged, snapshotted, or included in failure messages.
- Cookie assertions inspect metadata only; the raw cookie value is not reported.
- Test credentials remain in process memory and are not stored in environment files.
- The access token remains in React memory; Step 7 does not inspect React internals.

### E2E Limitations

- Refresh, logout, protected routes, vault workflows, MFA, and other later lifecycle work remain out of scope.
- Existing backend integration tests use SQLite; Step 7 uses the real local PostgreSQL database.
- E2E-created users remain in the local database unless the database is reset separately.
- The repository does not provide Docker orchestration for PostgreSQL.

### E2E Results

- `npm run test:e2e`: **7 / 7 passed (100%)** against the live frontend, API, and PostgreSQL.

---

## 10. Login Security Testing (Step 8)

**Status:** Complete

Step 8 executes an adversarial security assessment of the completed Login vertical slice. It evaluates the attack surface across authentication boundaries, credential handling, token mechanics, input validation, injection resistance, and browser data isolation.

### Security-Test Scope

The security assessment rigorously exercises the actual implementation across the following areas:
1. **Authentication Bypass & Payload Integrity:** Missing parameters, empty JSON payloads, and mass-assignment / unexpected property injections.
2. **Credential Attacks:** Wrong passwords, nonexistent emails, boundary-length inputs, oversized emails, 10,000-character passwords, and Unicode/special-character credentials.
3. **User Enumeration Invariance:** Structural and informational equivalence between existing and nonexistent user authentication failures.
4. **Password Handling & Data Exposure:** Zero plaintext password or hash disclosure across API responses, error payloads, browser storage, URLs, or console output.
5. **JWT & Access-Token Security:** Claims minimalism, token structure, expiration enforcement, and rejection of forged signatures, expired tokens, tampered payloads, "none" algorithm tokens, and malformed Bearer headers.
6. **Refresh-Token Security:** CSPRNG entropy, SHA-256 hash persistence, HttpOnly/Path=/api/auth/SameSite=Lax cookie boundaries, and cookie omission on failed attempts.
7. **Injection Resistance:** Safe handling and rejection of SQL/ORM injection vectors in email and password fields without database corruption, information leakage, or 500 errors.
8. **Error Disclosure & Stack Traces:** Sanitized RFC 9110 ProblemDetails and zero disclosure of server paths, database drivers, or exception call stacks.
9. **HTTP / API Protocol Boundaries:** Unsupported HTTP verbs (GET, PUT, PATCH, DELETE) and unsupported media types (`application/x-www-form-urlencoded`).
10. **CORS & Browser Boundaries:** Origin whitelisting and rejection of arbitrary untrusted origins.
11. **XSS & DOM Isolation:** Safe rendering of malicious script tags and HTML in form fields as plain text.

---

### Security Test Matrix

| # | Threat | Attack Scenario | Relevant Component | Expected Secure Behavior | Existing Test Coverage | Additional Test Required? | Test Layer | Result |
|---|---|---|---|---|---|---|---|---|
| 1 | Auth bypass via missing credentials | Submit JSON with missing `password` or `email` property | `POST /api/auth/login` | HTTP 400 Bad Request with RFC 9110 validation errors; no token or cookie issued | Partial (empty string tested) | Yes (`Login_MissingEmailOrPasswordProperty_Returns400BadRequest`) | Integration / API | Passed |
| 2 | Auth bypass via mass assignment | Submit extra JSON fields (`isAdmin`, `roles`, `id`, `__proto__`) | `POST /api/auth/login` | Extra fields ignored; authentication succeeds strictly on credentials; no escalated roles in JWT | None for login | Yes (`Login_MassAssignment_UnexpectedJsonFields_IgnoredAndServerControlled`) | Integration / API | Passed |
| 3 | User enumeration | Compare responses for existing email + wrong password vs nonexistent email | `POST /api/auth/login` | Identical HTTP 401, identical Content-Type, identical ProblemDetails title & detail (`"Invalid email or password."`), no Set-Cookie | Separate tests existed | Yes (`Login_UserEnumeration_ExistingVsNonexistentUser_IndistinguishableResponses`) | Integration / API | Passed |
| 4 | Buffer overrun / DoS via oversized email | Send email > 320 characters (`324` chars) | `POST /api/auth/login` | HTTP 400 Bad Request (`"Email must not exceed 320 characters."`) | Unit test existed | Yes (`Login_BoundaryLength_OversizedEmail_Returns400BadRequest`) | Integration / API | Passed |
| 5 | DoS / Crash via huge password | Submit 10,000-character password string | `POST /api/auth/login`, `PasswordHasher` | Handled safely by PBKDF2; returns HTTP 401 without 500 error, crash, or stack trace | None | Yes (`Login_BoundaryLength_VeryLongPassword_HandlesSafelyWithout500OrCrash`) | Integration / API | Passed |
| 6 | Character set corruption | Register and log in with multibyte Unicode & emojis (`🔒P@$$w0rd_With_Üñîçødé_&_Emojis!🔑`) | `POST /api/auth/login` | Full UTF-8 fidelity; authentication succeeds with HTTP 200 and access token | None explicit | Yes (`Login_UnicodeAndSpecialCharacters_AuthenticatesSuccessfully`) | Integration / API | Passed |
| 7 | SQL Injection in Email | Submit `' OR '1'='1`, `' UNION SELECT...`, `test@vaultx.local'; DROP TABLE "Users";--` | `POST /api/auth/login`, `UserRepository` | Rejected by validator (400) or fails safely (401); no SQL syntax error, no 500, no data dropped | None | Yes (`Login_SqlInjectionInEmail_SafelyRejectedWithout500OrDbLeak`) | Integration / API | Passed |
| 8 | SQL Injection in Password | Submit SQL injection string in password against registered user | `POST /api/auth/login`, `LoginUserHandler` | Treated as literal string; constant-time PBKDF2 comparison fails with 401; database unmodified | None | Yes (`Login_SqlInjectionInPassword_SafelyHandledWithoutDatabaseExecution`) | Integration / API | Passed |
| 9 | JWT Sensitive Data Exposure | Audit issued access token claims for sensitive fields | `TokenService`, `POST /api/auth/login` | Contains only intended claims (`sub`, `email`, `jti`, `iat`, `exp`); no password, hash, or refresh token | Partial | Yes (`Login_AccessToken_ContainsOnlyIntendedClaims_NoSensitiveData`, `GenerateAccessToken_DoesNotIncludeSensitiveClaims`) | Unit & Integration | Passed |
| 10 | JWT "None" Algorithm Attack | Submit token crafted with `"alg": "none"` to protected endpoint | `JwtBearer` middleware | HTTP 401 Unauthorized; token rejected | None | Yes (`JwtMiddleware_NoneAlgorithm_Returns401Unauthorized`) | Integration / API | Passed |
| 11 | JWT Tampered Payload | Modify payload claim (`sub`) while retaining original signature | `JwtBearer` middleware | Signature verification failure; HTTP 401 Unauthorized | None | Yes (`JwtMiddleware_TamperedPayload_Returns401Unauthorized`) | Integration / API | Passed |
| 12 | Malformed Bearer Token | Send `Bearer not-a-jwt` or garbage token string | `JwtBearer` middleware | HTTP 401 Unauthorized; no unhandled exception | None | Yes (`JwtMiddleware_MalformedBearerToken_Returns401Unauthorized`) | Integration / API | Passed |
| 13 | Sensitive Data Exposure in Responses | Inspect success (200) and failure (400, 401) responses | `AuthController`, `ExceptionHandlingMiddleware` | Plaintext passwords, password hashes (`$pbkdf2`), and token hashes never appear in bodies | Partial (200 tested) | Yes (`Login_Response_NeverExposesPasswordOrHashesInFailureOrSuccess`) | Integration / API | Passed |
| 14 | Unsupported Content-Type | Send `application/x-www-form-urlencoded` body | `POST /api/auth/login` | HTTP 415 Unsupported Media Type | None | Yes (`Login_UnsupportedContentType_Returns415UnsupportedMediaType`) | Integration / API | Passed |
| 15 | Cross-Site Scripting (XSS) in Login Form | Type `<img src="x" />` into email/password fields | `LoginForm.tsx` | Handled as plain text; validator catches format; no executable DOM nodes created | None | Yes (`LoginForm.test.tsx` XSS tests) | Frontend Unit | Passed |
| 16 | Safe Error Rendering | Trigger unexpected 500 error from API | `LoginForm.tsx` | Displays sanitized generic error (`"Unable to sign in right now."`); no HTML injection | None | Yes (`LoginForm.test.tsx` safe error test) | Frontend Unit | Passed |
| 17 | Forged JWT Signature | Present token signed with untrusted attacker key | `JwtBearer` middleware | HTTP 401 Unauthorized | `LoginSecurityTests` | No | Integration / API | Passed |
| 18 | Expired JWT Reuse | Present token past expiration timestamp | `JwtBearer` middleware | HTTP 401 Unauthorized (`ClockSkew = TimeSpan.Zero`) | `LoginSecurityTests` | No | Integration / API | Passed |
| 19 | Refresh Cookie Boundary | Inspect Set-Cookie header on successful login | `AuthController` | `HttpOnly`, `Path=/api/auth`, `SameSite=Lax`, `Expires` set | `LoginSecurityTests`, E2E | No | Integration & E2E | Passed |
| 20 | Cookie Omission on Auth Failure | Inspect headers when login fails | `AuthController` | No `Set-Cookie` header present | `AuthControllerLoginIntegrationTests` | No | Integration | Passed |
| 21 | CORS Development Policy | Request with `Origin: http://localhost:5173` | ASP.NET Core CORS | Allowed with `Access-Control-Allow-Credentials: true` | `LoginSecurityTests` | No | Integration | Passed |
| 22 | CORS Untrusted Origin | Request with `Origin: http://untrusted-attacker.com` | ASP.NET Core CORS | Origin not allowed; wildcard rejected | `LoginSecurityTests` | No | Integration | Passed |
| 23 | Unsupported HTTP Methods | Send GET, PUT, PATCH, DELETE to `/api/auth/login` | ASP.NET Core Routing | HTTP 405 Method Not Allowed | `LoginSecurityTests` | No | Integration | Passed |
| 24 | Error Disclosure (Malformed JSON) | Send malformed JSON (`{ not valid json }`) | `ExceptionHandlingMiddleware` | HTTP 400 Bad Request; zero stack traces, server paths, or driver names | `LoginSecurityTests` | No | Integration | Passed |
| 25 | Storage Boundary Isolation | Check browser storage after successful login | React `AuthContext` | Access token held only in React memory; 0 occurrences in `localStorage`, `sessionStorage`, cookies, URLs | `LoginForm.test.tsx`, E2E | No | Frontend & E2E | Passed |

---

### Implemented vs. Verified vs. Deferred Breakdown

To maintain strict architectural transparency and documentation precision, the security properties related to VaultX Login are categorized as follows:

#### Tested and Verified Security Controls
- **Email Normalization & Validation:** Static C# validator enforces length ($\le 320$) and RFC email syntax; lowercased and trimmed before database query.
- **Constant-Time Password Verification:** PBKDF2-SHA256 (100,000 iterations, 128-bit salt, 256-bit subkey) with `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- **User Enumeration Invariance:** Confirmed that existing-user and nonexistent-user login failures yield bitwise-indistinguishable RFC 9110 ProblemDetails payloads (`"Invalid email or password."`), identical 401 status codes, and omission of Set-Cookie headers.
- **Refresh-Token Persistence Foundation:** Raw 64-byte CSPRNG refresh token is never stored in PostgreSQL; only its deterministic SHA-256 hex hash is persisted. The current Login slice stores refresh-token hashes and establishes the persistence foundation, while actual refresh-token rotation/replay handling belongs to the future refresh endpoint/session-security slice.
- **Cookie Security Defense:** `HttpOnly`, `Path=/api/auth`, `SameSite=Lax` restrict cookie exposure and prevent JavaScript access.
- **In-Memory Access Token Storage:** Confirmed across both Vitest and Playwright real-browser tests that access tokens are held strictly in React memory; 0 occurrences in `localStorage`, `sessionStorage`, `document.cookie`, or browser URLs.
- **JWT Cryptographic Integrity:** Strict verification rejects tampered payloads, "none" algorithm tokens, expired tokens, forged signatures, and malformed Bearer headers.
- **SQL / ORM Injection Defense:** SQL injection payloads were safely handled without authentication bypass, SQL errors, or database corruption.
- **Mass Assignment Defense:** Extra submitted JSON properties (`isAdmin`, `roles`, `__proto__`, etc.) are ignored and do not pollute domain entities or escalate token claims.
- **Centralized Exception Sanitization:** `ExceptionHandlingMiddleware` catches unhandled exceptions and returns generic RFC 9110 ProblemDetails without stack traces, driver names, or internal server paths.
- **CORS Origin Whitelisting:** Development policy explicitly binds to configured frontend hosts with credentials allowed; rejects arbitrary untrusted origins.
- **XSS & DOM Isolation:** Malicious script tags and HTML in form fields are handled safely as plain text without script execution.

#### Functionality Not Yet Implemented
- **Token Refresh Endpoint (`POST /api/auth/refresh`):** Replay detection and token rotation foundation is established in domain entities; actual endpoint implementation belongs to the future refresh slice.
- **Logout Endpoint (`POST /api/auth/logout`):** Session invalidation and cookie revocation endpoint; belongs to Phase 2, Section 6.3.
- **Protected Routes & Resource Authorization:** Application route authorization; belongs to Phase 2, Section 6.4. Token validation is tested via integration test controller.
- **Multi-Factor Authentication (MFA / TOTP):** Second-factor challenge flow; belongs to Phase 9.

#### Deferred Security Hardening
- **Rate Limiting & Brute-Force Protection:** Not currently implemented; deferred security hardening. (Not an implementation defect or vulnerability discovered in Step 8, but a planned infrastructure hardening control).
- **Production HTTPS / HSTS & CSP Header Enforcement:** Deferred to production infrastructure configuration and hosting environment.

#### Vulnerabilities Actually Discovered
- **Findings:** No vulnerabilities were identified within the tested Login attack surface.
- **Scope Limit:** All evaluated scenarios confirmed that tested security controls operate as specified. This conclusion is strictly limited to the tested attack scenarios and does not imply that unexamined attack surfaces or future features are proven vulnerability-free.
- **Vulnerabilities Fixed:** None required.
- **Regression Impact:** None; all 228 total automated tests across frontend, backend, and browser E2E continue to pass.

---

### Security Findings & Resolution

1. **Vulnerabilities Discovered:** No vulnerabilities were identified within the tested Login attack surface. The evaluated scenarios confirmed that all tested security controls operate as specified. This conclusion is strictly limited to the tested attack scenarios and does not imply that unexamined attack surfaces or future features are proven vulnerability-free.
2. **Vulnerabilities Fixed:** None (no production code defects or security vulnerabilities were identified within the tested scenarios).
3. **Regression Impact:** None (all existing tests continue to pass with 100% success).

---

### Final Security Test Suite Results

- **Backend Unit Tests:** 69 / 69 passed (100%)
- **Backend Integration & Security Tests:** 82 / 82 passed (100%)
- **Total Backend Tests:** **151 / 151 passed (100%)**
- **Frontend Unit & Security Tests:** **70 / 70 passed (100%)**
- **Playwright Browser E2E Tests:** **7 / 7 passed (100%)**
- **Overall Suite Metric:** **228 total automated tests across frontend, backend, and browser E2E**
- **Security-Specific Verification:** **34 security-specific tests** (all passed)
- **Linter & Typecheck:** 0 warnings, 0 errors

---

## 11. Deferred Work

The following items are outside the scope of Login (Steps 1 through 8) and are deferred to subsequent lifecycle steps or future roadmap phases:

1. **Token Refresh Endpoint (`POST /api/auth/refresh`):** The current Login slice stores refresh-token hashes and establishes the persistence foundation, while actual refresh-token rotation/replay handling belongs to the future refresh endpoint/session-security slice.
2. **Logout Endpoint (`POST /api/auth/logout`):** Phase 2, Section 6.3 (Logout).
3. **Protected Routes:** Phase 2, Section 6.4 (Protected Routes).
4. **Rate Limiting & Brute-Force Protection:** Not currently implemented; deferred security hardening.
5. **Multi-Factor Authentication (MFA / TOTP):** Phase 9 (TOTP / 2FA).
6. **Production HTTPS / HSTS & CSP Header Enforcement:** Deferred to production infrastructure configuration.

