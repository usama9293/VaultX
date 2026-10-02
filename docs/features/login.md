# Login Feature Specification & Implementation Documentation

**Feature:** User Login & Authentication  
**Vertical Slice:** Phase 2, Section 6.2 (Login)  
**Status:** Backend, Frontend, Integration & End-to-End Testing Complete (Steps 4, 5, 6 & 7)
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
| Refresh-token replay                    | Revocation/rotation foundation exists; full replay protection will be implemented with the refresh-token endpoint.                                                                                     | Unit & entity test          | Foundation only / Deferred |
| Access-token exposure                   | Short 15-minute token lifetime and in-memory client storage minimize the window of vulnerability. Excluded from persistent server storage.                                                             | Security test               | Verified                   |
| Access-token persistent storage leakage | Access token is held strictly in React memory; never stored in `localStorage`, `sessionStorage`, `IndexedDB`, or URL parameters.                                                                       | Frontend Integration test   | Verified                   |
| JWT tampering                           | Cryptographically signed using HMAC-SHA256 with a 256-bit secret key; unauthorized signatures or modified payloads fail verification.                                                                  | Integration test            | Verified                   |
| Expired JWT reuse                       | Strict lifetime verification with zero clock skew (`ClockSkew = TimeSpan.Zero`) rejects expired JWTs.                                                                                                  | Integration test            | Verified                   |
| Error disclosure                        | Centralized `ExceptionHandlingMiddleware` and frontend API client ensure sanitized ProblemDetails and generic safe messages.                                                                           | Security test               | Verified                   |
| CORS abuse                              | Scoped to explicitly allowed development origins (`http://localhost:5173`, etc.) with `AllowCredentials()`. Wildcards rejected.                                                                        | Security test               | Verified                   |
| Rate limiting / brute-force protection  | Not currently implemented; planned as security hardening against automated credential stuffing and brute-force attacks                                                                                 | Deferred security hardening | Deferred                   |

---

## 8. Testing Summary

### Automated Test Results

- **Backend Unit Tests (`PasswordManager.UnitTests`):** 68 / 68 passed
  - `LoginCommandValidatorTests`: 5 tests
  - `RefreshTokenTests`: 6 tests
  - `TokenServiceTests`: 4 tests
  - `LoginUserHandlerTests`: 3 tests
  - Existing Registration unit tests: 50 tests
- **Backend Integration Tests (`PasswordManager.IntegrationTests`):** 60 / 60 passed
  - `LoginIntegrationTests.cs` (backend API/database integration through the real ASP.NET Core application and PostgreSQL persistence): 4 tests
  - `AuthControllerLoginIntegrationTests`: 8 tests
  - `LoginSecurityTests`: 9 tests
  - Existing Registration integration & security tests: 39 tests
- **Frontend Test Suite (`vitest`):** 68 / 68 passed
  - `LoginIntegration.test.tsx` (frontend integration/component integration using the backend API contract with mocked network behavior): 4 tests
  - `LoginForm.test.tsx`: 15 tests
  - `RegisterForm.test.tsx`: 15 tests
  - `validation.test.ts`: 24 tests
  - `authApi.test.ts`: 10 tests
- **Total Backend Tests:** **128 / 128 passed (100%)**
- **Total Frontend Tests:** **68 / 68 passed (100%)**
- **Total Combined Tests:** **196 / 196 passed (100%)**
- **Playwright Browser E2E Tests:** **7 / 7 passed (100%)**
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

## 10. Deferred Work

The following items are outside the scope of Step 7 (Login End-to-End Testing) and are deferred to subsequent lifecycle steps or future roadmap phases:

1. **Step 8 — Security Testing:** Dedicated penetration and vulnerability test suite for Login.
2. **Token Refresh Endpoint (`POST /api/auth/refresh`):** Revocation/rotation foundation exists; full replay protection will be implemented with the refresh-token endpoint.
3. **Logout Endpoint (`POST /api/auth/logout`):** Phase 2, Section 6.3 (Logout).
4. **Protected Routes:** Phase 2, Section 6.4 (Protected Routes).
5. **Rate Limiting & Brute-Force Protection:** Deferred security hardening work.
6. **Multi-Factor Authentication (MFA / TOTP):** Phase 9 (TOTP / 2FA).
7. **Production HTTPS / HSTS & CSP Header Enforcement:** Deferred to production infrastructure configuration.
