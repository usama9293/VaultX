# Login Feature Specification & Implementation Documentation

**Feature:** User Login & Authentication  
**Vertical Slice:** Phase 2, Section 6.2 (Login)  
**Status:** Backend & Frontend Implementation Complete (Steps 4 & 5)  
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

| Field | Type | Required | Constraints |
|---|---|---|---|
| `email` | `string` | Yes | Non-empty, valid format, max 320 characters |
| `password` | `string` | Yes | Non-empty |

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

*Note: The response body contains only the short-lived access token and its expiration timestamp. It never contains the raw refresh token, token hash, password, password hash, or internal database metadata.*

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

*Note: Identical response is returned whether the email does not exist or the password is wrong, preventing user enumeration.*

### Validation Error Response: `HTTP 400 Bad Request`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": [
      "Email is required."
    ]
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

## 6. Security Threats & Defenses

| Threat | Defense | Verification | Status |
|---|---|---|---|
| User enumeration | Generic authentication failure (`"Invalid email or password."`) returned for both nonexistent emails and wrong passwords. | Integration & Frontend test | Verified |
| Password exposure | Plaintext password is never logged, persisted, returned in responses, or stored in cookies. Processed only in-memory and wiped from component state. | Security test | Verified |
| JavaScript access to refresh token | Transmitted solely via secure `HttpOnly` cookie with `Path=/api/auth`. HttpOnly prevents JavaScript from directly reading the refresh-token cookie; XSS itself is not fully prevented by this feature. | Security test | Verified |
| Database exposure of refresh tokens | The raw refresh token is not stored in PostgreSQL; only its SHA-256 hash is stored, so database contents do not directly expose the refresh-token cookie value. | Integration & DB test | Verified |
| Refresh-token replay | Revocation/rotation foundation exists; full replay protection will be implemented with the refresh-token endpoint. | Unit & entity test | Foundation only / Deferred |
| Access-token exposure | Short 15-minute token lifetime and in-memory client storage minimize the window of vulnerability. Excluded from persistent server storage. | Security test | Verified |
| Access-token persistent storage leakage | Access token is held strictly in React memory; never stored in `localStorage`, `sessionStorage`, `IndexedDB`, or URL parameters. | Frontend Security test | Verified |
| JWT tampering | Cryptographically signed using HMAC-SHA256 with a 256-bit secret key; unauthorized signatures or modified payloads fail verification. | Integration test | Verified |
| Expired JWT reuse | Strict lifetime verification with zero clock skew (`ClockSkew = TimeSpan.Zero`) rejects expired JWTs. | Integration test | Verified |
| Error disclosure | Centralized `ExceptionHandlingMiddleware` and frontend API client ensure sanitized ProblemDetails and generic safe messages. | Security test | Verified |
| CORS abuse | Scoped to explicitly allowed development origins (`http://localhost:5173`, etc.) with `AllowCredentials()`. Wildcards rejected. | Security test | Verified |
| Rate limiting / brute-force protection | Not currently implemented; planned as security hardening against automated credential stuffing and brute-force attacks | Deferred security hardening | Deferred |

---

## 7. Testing Summary

### Automated Test Results

- **Backend Unit Tests (`PasswordManager.UnitTests`):** 68 / 68 passed
  - `LoginCommandValidatorTests`: 5 tests
  - `RefreshTokenTests`: 6 tests
  - `TokenServiceTests`: 4 tests
  - `LoginUserHandlerTests`: 3 tests
  - Existing Registration unit tests: 50 tests
- **Backend Integration Tests (`PasswordManager.IntegrationTests`):** 56 / 56 passed
  - `AuthControllerLoginIntegrationTests`: 8 tests
  - `LoginSecurityTests`: 9 tests
  - Existing Registration integration & security tests: 39 tests
- **Frontend Test Suite (`vitest`):** 64 / 64 passed
  - `validation.test.ts`: 24 tests (includes 5 new login validation tests)
  - `authApi.test.ts`: 10 tests (includes 5 new `loginUser` tests)
  - `LoginForm.test.tsx`: 15 tests (UI rendering, validation, loading state, duplicate submission protection, 401 error handling, in-memory auth state, localStorage/sessionStorage/URL/console isolation)
  - `RegisterForm.test.tsx`: 15 tests (regression suite with 0 regressions)
- **Total Backend Tests:** **124 / 124 passed (100%)**
- **Total Frontend Tests:** **64 / 64 passed (100%)**
- **Linter & Typecheck:** 0 warnings, 0 errors (`oxlint` + `tsc -b`)

---

## 8. Deferred Work

The following items are outside the scope of Step 5 (Login Frontend) and are deferred to subsequent lifecycle steps or future roadmap phases:

1. **Step 6 — Integration:** Live connection between Login UI and API within the Login vertical slice lifecycle.
2. **Token Refresh Endpoint (`POST /api/auth/refresh`):** Revocation/rotation foundation exists; full replay protection will be implemented with the refresh-token endpoint.
3. **Logout Endpoint (`POST /api/auth/logout`):** Phase 2, Section 6.3 (Logout).
4. **Protected Routes:** Phase 2, Section 6.4 (Protected Routes).
5. **Rate Limiting & Brute-Force Protection:** Deferred security hardening work.
6. **Multi-Factor Authentication (MFA / TOTP):** Phase 9 (TOTP / 2FA).
7. **Production HTTPS / HSTS & CSP Header Enforcement:** Deferred to production infrastructure configuration.


