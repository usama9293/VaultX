# VaultX — Product Development & Security Roadmap

**Project:** VaultX  
**Document:** Product Development & Security Roadmap  
**Version:** 1.0  
**Status:** Development Baseline  
**Purpose:** Define how VaultX will be designed, implemented, tested, secured, and evolved into a production-ready multi-platform product.

---

# 1. Development Philosophy

VaultX will **not** be developed by completing the entire backend first and building the frontend afterward.

The project will use a **vertical-slice development model**.

For every meaningful feature:

```text
Requirement
    ↓
Design
    ↓
Security Analysis
    ↓
Backend Implementation
    ↓
Frontend Implementation
    ↓
Integration
    ↓
End-to-End Testing
    ↓
Security Testing
    ↓
Documentation
    ↓
Feature Complete
```

The frontend and backend should therefore evolve together.

For example, registration will not be considered complete when the API works. It is complete only when:

- The backend registration flow works.
- The frontend registration UI works.
- Frontend and backend communicate correctly.
- Validation works.
- Errors are handled correctly.
- The database state is correct.
- Security requirements are tested.
- The complete user flow works.

---

# 2. Core Development Rule

## Build Features, Not Separate Layers

We do **not** follow:

```text
Finish entire backend
        ↓
Finish entire frontend
        ↓
Connect them
```

We follow:

```text
Feature 1
Backend → Frontend → Integration → Testing → Security Review
        ↓
Feature 2
Backend → Frontend → Integration → Testing → Security Review
        ↓
Feature 3
Backend → Frontend → Integration → Testing → Security Review
```

This keeps the product usable throughout development and exposes architectural problems early.

---

# 3. Feature Development Lifecycle

Every significant VaultX feature should follow this lifecycle.

## Step 1 — Understand

Define:

- What are we building?
- Why does the user need it?
- What problem does it solve?
- What are the acceptance criteria?

## Step 2 — Design

Define:

- Backend behavior
- Frontend behavior
- API contract
- Data model
- Component responsibilities
- Error states

## Step 3 — Security Design

Identify:

- What information is sensitive?
- Who should have access?
- What can an attacker do?
- What happens if the database is compromised?
- What happens if a user is unauthorized?
- What data must never be logged or persisted in plaintext?

## Step 4 — Backend

Implement only the backend functionality required for the feature.

## Step 5 — Frontend

Implement the corresponding user interface and client-side behavior.

## Step 6 — Integration

Connect:

```text
Frontend
   ↓
HTTP/API
   ↓
ASP.NET Core
   ↓
Application
   ↓
Infrastructure
   ↓
PostgreSQL
```

## Step 7 — End-to-End Testing

Test the feature through the actual application.

## Step 8 — Security Testing

Attempt to break the feature.

Examples:

- Unauthorized access
- Invalid input
- IDOR
- Token misuse
- Sensitive-data exposure
- Injection
- Replay or abuse where applicable

## Step 9 — Documentation

Record:

- Design
- Security decisions
- Important trade-offs
- Testing results
- Architecture decisions

## Step 10 — Complete

Only after the feature passes its required checks do we move forward.

---

# 4. Phase 0 — Product & Security Foundation

**Status: Complete**

## Goal

Establish VaultX's product vision, security principles, threat model, and long-term architecture before implementing sensitive functionality.

## Completed Areas

- Product vision
- Product principles
- Security principles
- Threat model
- Client/server responsibilities
- Zero-knowledge-oriented architecture
- Cryptographic direction
- Key hierarchy direction
- Platform roadmap
- Security requirements
- Development principles
- Long-term feature roadmap

## Main Document

**VaultX Product & Security Specification**

This document acts as the source of truth for major product and security decisions.

## Result

We now have a defined architectural direction rather than implementing features without understanding their long-term security implications.

---

# 5. Phase 1 — Architecture Foundation

**Status: Complete**

## Goal

Build the technical foundation that future VaultX functionality will use.

## Backend

Implement and establish:

- Clean Architecture
- Application layer
- Application contracts
- DTOs
- Repository abstractions
- Service abstractions
- Dependency injection
- Validation foundation
- Error-handling foundation
- API structure
- Configuration structure
- Testing structure

Current backend architecture:

```text
PasswordManager
├── PasswordManager.API
├── PasswordManager.Application
├── PasswordManager.Domain
└── PasswordManager.Infrastructure
```

Dependency direction:

```text
API
 ↓
Application
 ↓
Domain

Infrastructure
 ↓
Application
 ↓
Domain
```

## Frontend

Set up:

- React
- TypeScript
- Routing
- Application layout
- API client
- Environment configuration
- Authentication page structure
- Basic UI architecture
- Frontend testing foundation

## Integration

Establish the initial communication path:

```text
React
  ↓
ASP.NET Core API
  ↓
Application
  ↓
Infrastructure
  ↓
PostgreSQL
```

## Testing

- Backend unit-test foundation
- API integration-test foundation
- Frontend test foundation
- First frontend-to-backend integration

## Completion Criteria

At the end of Phase 1:

- Backend architecture is established.
- Frontend architecture is established.
- API communication works.
- Testing infrastructure exists.
- The project can begin implementing complete vertical slices.

---

# 6. Phase 2 — Account & Authentication

**Status: Complete ✅**

## Goal

Create the first complete user-facing VaultX functionality.

This phase is the first major vertical-slice phase.

---

## 6.1 Registration

**Status: Complete (Vertical Slice Completed) ✅**

### Vertical Slice Lifecycle Summary

- **Step 1: Requirement** — Complete (Established user registration needs, acceptance criteria, scope boundaries)
- **Step 2: Design** — Complete (Architectural flow, contract definition, component roles)
- **Step 3: Security Analysis** — Complete (Threat modeling, password handling, validation boundaries)
- **Step 4: Backend Implementation** — Complete (`08c031b`)
- **Step 5: Frontend Implementation** — Complete (`bbbb311`)
- **Step 6: Integration** — Complete (`026b129`)
- **Step 7: End-to-End Testing** — Complete (Verified registration flow from UI to database)
- **Step 8: Security Testing** — Complete (`deacfc5` — validation bypass, mass assignment, race conditions, exposure tests)
- **Step 9: Documentation** — Complete (`docs/features/registration.md`)
- **Step 10: Complete** — Complete (112/112 tests passing, Definition of Done verified)

### Backend

Implement:

- Registration endpoint
- Email validation
- Password validation
- Password hashing
- User creation
- Database persistence

### Frontend

Implement:

- Registration page
- Form fields
- Client-side validation
- Loading states
- Error states
- Success handling

### Integration

```text
User
 ↓
Registration UI
 ↓
API
 ↓
Application
 ↓
Password hashing
 ↓
PostgreSQL
```

### Testing

Test:

- Valid registration
- Duplicate email
- Invalid email
- Invalid password
- Database persistence
- Password is not stored in plaintext

---

## 6.2 Login

**Status: Backend, Frontend, Integration, End-to-End & Security Testing Complete (Steps 4, 5, 6, 7 & 8 Complete) ✅**

### Vertical Slice Lifecycle Summary

- **Step 1: Requirement** — Complete (Defined user login needs, single-session & multi-device requirements, acceptance criteria)
- **Step 2: Design** — Complete (Architectural contract, JWT short-lived access token, opaque refresh token with HttpOnly cookie)
- **Step 3: Security Analysis** — Complete (Threat modeling: user enumeration, token theft, token replay, XSS/CSRF mitigations)
- **Step 4: Backend Implementation** — Complete (Auth API, LoginUserHandler, TokenService, RefreshTokens EF Core migration, 128 passing backend tests)
- **Step 5: Frontend Implementation** — Complete (Login Page, LoginForm, Client Validation, in-memory AuthState/Token storage, 68 passing frontend tests)
- **Step 6: Integration** — Complete (Verified end-to-end integration flow from React UI through API to DB, cookie security, and in-memory auth state)
- **Step 7: End-to-End Testing** — Complete (7/7 Playwright browser tests passed against live Vite frontend, ASP.NET Core API, and real PostgreSQL database)
- **Step 8: Security Testing** — Complete (Adversarial security assessment: auth bypass, credential attacks, user enumeration invariance, JWT tamper/claims resistance, SQL injection defense, mass assignment defense, and XSS isolation; 228 total automated tests across frontend, backend, and browser E2E, including 34 security-specific tests)
- **Step 9: Documentation** — Complete (`docs/features/login.md` updated with dedicated Step 8 security testing section and threat matrix)
- **Step 10: Complete** — Complete (Login vertical slice fully implemented, tested, and security verified)

### Backend Implementation Summary

Implemented:

- `POST /api/auth/login` endpoint
- Email normalization and validation
- Credential verification with generic error response (`Invalid email or password.`)
- Short-lived JWT access token generation (HMAC-SHA256, 15 min)
- Opaque cryptographically secure refresh token (CSPRNG, SHA-256 hashed persistence)
- Secure HttpOnly refresh token cookie
- ASP.NET Core JWT authentication & authorization middleware
- `RefreshTokens` PostgreSQL database migration
- Clean Architecture contracts (`ILoginUserHandler`, `ITokenService`, `IRefreshTokenRepository`)

### Integration Summary

Verified:

- End-to-end pipeline: Browser React UI -> HTTP POST -> ASP.NET Core API -> LoginUserHandler -> PostgreSQL DB -> JWT access token + HttpOnly cookie -> in-memory AuthContext -> Authenticated UI
- Real database persistence: deterministic SHA-256 refresh token hash stored with user relation and valid expiration
- Cookie security: `HttpOnly`, `Path=/api/auth`, `SameSite=Lax`, inaccessible to client JavaScript
- Memory isolation: access token held exclusively in React state, zero persistence in `localStorage`, `sessionStorage`, `IndexedDB`, or URLs
- Error resilience: RFC 9110 ProblemDetails for 401 Unauthorized (user enumeration prevention) and 400 Bad Request (validation errors)
- Test suite: 128 backend tests (100% passing) + 68 frontend tests (100% passing)

### End-to-End Testing Summary

Verified in Real Browser:

- Real Chromium browser user journey from login form submission to authenticated state card
- Real HTTP 200 contract with structural validation of `accessToken` and `expiresAt`
- Refresh cookie security attributes (`HttpOnly`, `Path=/api/auth`, `SameSite=Lax`, expiration, `Secure=false` in local HTTP)
- Total absence of access token and refresh token from `localStorage`, `sessionStorage`, URLs, and `document.cookie`
- 401 Unauthorized handling for invalid passwords and unknown emails displaying identical generic error
- Real UI validation rejecting empty and malformed credentials
- Network failure graceful error presentation (`Unable to connect to the server. Please check your connection and try again.`)
- Test suite: 7 / 7 Playwright browser E2E tests (100% passing) against live frontend, API, and PostgreSQL

### Security Testing Summary (Step 8)

Verified:

- Authentication bypass resistance: missing credentials, empty payloads, and mass-assignment unexpected fields are strictly rejected or isolated
- User enumeration invariance: identical HTTP 401 ProblemDetails and omission of cookies across existing vs nonexistent accounts
- Boundary & injection defense: oversized emails (324 chars) rejected with 400 Bad Request; 10,000-character passwords handled without crash or 500 error; SQL injection payloads were safely handled without authentication bypass, SQL errors, or database corruption
- JWT security & tamper resistance: claims audit verified zero sensitive credential leakage; forged signatures, expired tokens, tampered payload claims, "none" algorithm tokens, and malformed Bearer headers all rejected with 401 Unauthorized
- Storage & XSS isolation: access token strictly confined to React memory; zero persistent storage leakage; malicious HTML inputs rendered safely as plain text without script execution
- Refresh-token persistence foundation: current Login slice stores refresh-token hashes and establishes the persistence foundation, while actual refresh-token rotation/replay handling belongs to the future refresh endpoint/session-security slice
- Rate limiting / brute-force protection: Not currently implemented; deferred security hardening
- Security findings: No vulnerabilities were identified within the tested Login attack surface (conclusion limited strictly to tested scenarios)
- Test metrics: 228 total automated tests across frontend, backend, and browser E2E (including 34 security-specific tests)

Flow:

```text
Login UI
   ↓
Login API
   ↓
Authentication
   ↓
Session / Token (DB & Cookie)
   ↓
Authenticated Frontend
```

---

## 6.3 Logout

**Status: Step 10 — Complete ✅ (Steps 1–10 Complete)**

### Vertical Slice Lifecycle Summary

- **Step 1: Requirement** — Complete (Single-session revocation, idempotent logout, cookie clearing, stateless JWT lifetime boundary)
- **Step 2: Design** — Complete (API contract: `POST /api/auth/logout`, cookie-based session identification, HTTP 204 No Content response)
- **Step 3: Security Analysis** — Complete (Threat modeling: session isolation, token oracle defense, sensitive data exposure defense, CSRF boundary)
- **Step 4: Backend Implementation** — Complete (`POST /api/auth/logout`, `ILogoutUserHandler`, `LogoutUserHandler`, single-session revocation, HttpOnly cookie deletion, idempotent 204 response)
- **Step 5: Frontend Implementation** — Complete (`logoutUser` API client, `AuthContext.logout`, authenticated Log Out control, local auth cleared even on API failure, memory-only access token, HttpOnly refresh cookie untouched by JS)
- **Step 6: Integration** — Complete (Verified end-to-end integration flow from React UI through API to DB, cookie deletion contract, environment-aware secure policy, and in-memory auth state clearing)
- **Step 7: End-to-End Testing** — Complete (Verified in real Chromium browser using Playwright: complete user journey, cookie lifecycle, storage/URL boundary, network failure resilience, and loading/duplicate prevention; 12/12 passing E2E tests)
- **Step 8: Security Testing** — Complete (32/32 Logout-filtered security/integration test cases passed; attack surface and results documented in `docs/features/logout.md`)
- **Step 9: Documentation** — Complete (`docs/features/logout.md` records Step 8 coverage, results, deferred features, and limitations)
- **Step 10: Complete** — Complete (Logout lifecycle acceptance criteria and final verification passed)

### Backend Implementation Summary

Implemented:

- `POST /api/auth/logout` endpoint in `AuthController`
- `LogoutCommand` and `ILogoutUserHandler` / `LogoutUserHandler` in Application layer
- Session identification exclusively via `refreshToken` HttpOnly cookie
- Single-session revocation (only target session is revoked; other sessions for the user remain active)
- Idempotent revocation behavior (already-revoked and expired tokens return 204 without side effects)
- Token oracle defense (missing, unknown, or malformed tokens return 204 without exposing database status)
- HttpOnly cookie clearing matching creation configuration (`Path=/api/auth`, `SameSite=Lax`, `HttpOnly=true`)
- HTTP 204 No Content response with empty body (no sensitive token or user leakage)
- Rejection of unsupported HTTP methods (`GET`, `PUT`, `PATCH`, `DELETE`) with HTTP 405 Method Not Allowed
- Safe unhandled error handling without stack trace or connection string exposure

### Frontend Implementation Summary

Implemented:

- `logoutUser()` in `frontend/src/api/auth.ts` — `POST /api/auth/logout` with `credentials: 'include'`, no body
- `AuthContext.logout()` — attempts server logout, always clears in-memory `accessToken` / `expiresAt` / status to `unauthenticated` (including on network failure); rethrows API errors after local cleanup
- Log Out button on authenticated session view with loading/disabled state to prevent duplicate requests
- Access token remains memory-only; refresh token remains HttpOnly and is never read or deleted by frontend JavaScript
- No protected routes, dashboard, vault, or session-management UI introduced in this step

### Integration Summary (Step 6)

Verified:

- Real end-to-end lifecycle: Registration -> Login -> Session establishment -> DB verification -> POST /api/auth/logout -> HTTP 204 No Content -> Cookie deletion response -> DB session revocation (`RevokedAt` populated, `IsActive=false`).
- Session isolation: Logout of Session A revokes Session A while Session B remains active and unrevoked (`RevokedAt=null`, `IsActive=true`).
- Missing cookie resilience: `POST /api/auth/logout` without cookie returns HTTP 204, issues cookie deletion header, and leaves all DB sessions unmodified.
- Revocation idempotency: Logout on already-revoked session returns HTTP 204 without exception and preserves the original `RevokedAt` timestamp.
- Expired session handling: Logout with expired session returns HTTP 204 without reactivating the expired record.
- Data preservation: Target user, password hash, and unrelated user records & sessions are preserved intact across logout; session records are updated, not deleted.
- Cookie deletion contract & environment-awareness: Cookie deletion header specifies `refreshToken`, `Path=/api/auth`, `HttpOnly=true`, `SameSite=Lax`, expiration (`max-age=0` / `expires=1970`), with `Secure` flag dynamically adapting based on HTTPS / environment context.
- Frontend application integration: React application tree transitions from authenticated state to Sign In form, wipes in-memory access token and expiration from `AuthContext`, leaves zero trace in `localStorage`, `sessionStorage`, or URL, safely handles network failures without raw error leakage, and prevents duplicate submissions while request is in flight.
- Test metrics: 192 total backend tests (100% passing; 32 Logout-filtered security/integration test cases passed) + 87 total frontend tests (100% passing, including 4 Logout integration tests).

### End-to-End Testing Summary (Step 7)

Verified in Real Browser:

- Real Chromium browser user journey from authenticated session view to unauthenticated Sign In state upon clicking Log Out
- Real HTTP 204 No Content contract issued by browser on `POST /api/auth/logout` with empty body
- Complete removal of `refreshToken` cookie from the browser cookie jar after logout
- Verified that `refreshToken` HttpOnly cookie is never exposed to client JavaScript via `document.cookie`
- Access token is never stored in `localStorage`, `sessionStorage`, URL search parameters, or URL hash during or after logout
- Network failure resilience: aborting `POST /api/auth/logout` still clears local in-memory auth state, returns UI to Sign In, and prevents exposure of raw exceptions, stack traces, or tokens
- Loading and duplicate prevention: pending logout request displays "Signing Out...", disables the button, sets `aria-busy="true"`, and suppresses duplicate clicks/requests
- Test metrics: 12 / 12 Playwright browser E2E tests (100% passing) across Login (7) and Logout (5) running against real Vite frontend, ASP.NET Core API, and PostgreSQL database

### Security Testing Summary (Step 8)

Dedicated Logout security testing was completed. Tests verified cookie-only refresh-token selection against JSON body, query-string, Authorization-header, and security-sensitive body-field injection; concurrent-session isolation; cross-user impersonation attempts; tampered-token handling; response/error disclosure boundaries; sensitive logging checks; unsupported HTTP methods; refresh-cookie security and deletion behavior; and frontend authentication-state/storage boundaries.

- No vulnerabilities were identified within the tested Logout attack surface. This finding is limited to the tested surface and is not a universal security claim.
- Test results: **32/32 Logout-filtered security/integration tests**, **192/192 full backend tests** (114 integration, 78 unit), **87/87 frontend tests**, and **12/12 Playwright E2E tests** passed.
- Frontend lint, frontend production build, and `git diff --check` passed.
- Sensitive-log assertions cover the exercised valid, unknown/tampered, malformed, and expired paths for raw token and cookie-form values; they are not a general audit of every log field, derived hash, or logging level.

### Architectural Constraints & Limitations

- **Stateless JWT Access Token Limitation:** Logout revokes the refresh session and clears client authentication state, but does not immediately invalidate an already-issued stateless access JWT. The access token remains cryptographically valid until its expiration (approximately 15 minutes).
- **Multiple Session Isolation:** Only the session matching the provided `refreshToken` cookie is revoked. Other active sessions belonging to the user remain active. Global logout is deferred to future session management work.
- **Local vs Server Logout:** Frontend always clears local auth state after logout is initiated. A failed logout API request does not leave the UI authenticated, and does not claim that the server session was successfully revoked.
- **Deferred Features:** Refresh-token rotation/replay detection, global/all-device logout, and JWT blacklisting are not implemented as part of Logout Step 8.

---

## 6.4 Protected Routes

Protect application areas such as:

```text
/dashboard
/vault
/settings
```

Verify that unauthenticated users cannot access protected resources.

---

## Phase 2 Completion Criteria

```text
Registration       ✓
Login              ✓
Logout             ✓
Protected routes   ✓
User persistence   ✓
Authentication     ✓
Basic security     ✓
```

VaultX should now behave like a real authenticated application.

---

# 7. Phase 3 — Vault Creation & Access

**Status: Complete**

## Goal

Introduce the central VaultX concept: the user's vault.

## Features

### Vault Creation

```text
User
 ↓
Authenticated Session
 ↓
Vault
```

Backend:

- Create vault
- Retrieve vault
- Authorize vault access

Frontend:

- Vault initialization
- Dashboard
- Empty-vault state
- Loading/error states

Phase 3 creates and accesses an empty, metadata-only vault. Search and Add Login shown in the initial dashboard sketch are deferred to Phase 4 and must not be functional in Phase 3.

---

## Vault Dashboard

Initial interface:

```text
┌──────────────────────────────┐
│ VaultX                  User │
├──────────────────────────────┤
│ (Search arrives in Phase 4)  │
│                              │
│ (Add Login arrives in Phase 4)│
│                              │
│ No passwords yet             │
└──────────────────────────────┘
```

---

## Authorization Testing

Explicitly test:

```text
User A → Vault A ✓
User A → Vault B ✗
```

This phase begins serious testing for:

- Broken access control
- IDOR
- Resource ownership
- Unauthorized API access

---

# 8. Phase 4 — Password Entries

**Status: In Progress — Metadata-Only Design Approved**

## Goal

Build the first usable entry lifecycle for descriptive login metadata. Phase 4 does not persist passwords or claim that entries are encrypted.

The approved design is documented in [docs/architecture/phase-4-password-entries-design.md](docs/architecture/phase-4-password-entries-design.md). It allows only title, optional HTTP(S) website URL, username, and optional notes. Search covers title, website URL, and username; notes are excluded from list responses and search.

Each operation will be implemented as a complete vertical slice.

---

## 8.1 Create Entry

Backend:

```text
POST /vault/entries
```

Frontend:

- Add Login form
- Validation
- Save action
- Loading state
- Error handling
- Success state

Complete flow:

```text
Frontend
 ↓
API
 ↓
Application
 ↓
Database
```

---

## 8.2 Get Entry

Implement:

- Entry retrieval
- Vault ownership verification
- Entry details page/view

Frontend:

- Entry list
- Entry details
- Loading/error states

---

## 8.3 Update Entry

Implement:

- Edit API
- Edit UI
- Validation
- Persistence
- UI synchronization

---

## 8.4 Delete Entry

Implement:

- Delete API
- Authorization
- Delete UI
- Confirmation
- UI update

---

## 8.5 Search

Implement an initial search mechanism appropriate to the current security architecture.

The search architecture must later be revisited once client-side encryption is introduced.

---

## 8.6 Favorites / Tags / Categories

Add only after the core entry lifecycle works.

Potential features:

- Favorites
- Tags
- Categories
- Sorting

---

## Phase 4 Completion Criteria

Password persistence, cryptographic primitives, key derivation, encryption, decryption, and zero-knowledge behavior are explicitly outside this phase. See Phases 5, 6, and 7.

```text
Create      ✓
Read        ✓
Update      ✓
Delete      ✓
Search      ✓
Authorization ✓
Frontend    ✓
Integration ✓
Testing     ✓
```

---

# 9. Phase 5 — Cryptographic Core

## Goal

Build VaultX's cryptographic foundation before attempting full zero-knowledge encryption.

This phase requires careful design before implementation.

## Topics

Study and implement:

- Cryptographically secure random generation
- Argon2id
- AES-256-GCM
- Nonces
- Authentication tags
- Key derivation
- Key encryption keys
- Vault keys
- Item keys
- Key wrapping
- Key rotation
- Cryptographic versioning

---

## Target Key Hierarchy

```text
Master Password / User Secret
            ↓
          Argon2id
            ↓
    Key Encryption Key
            ↓
        Vault Key
            ↓
     ┌──────┼──────┐
     ↓      ↓      ↓
  ItemKey ItemKey ItemKey
```

The master password itself must never become a stored encryption key.

---

## Testing

Test:

- Correct key derivation
- Wrong password
- Wrong key
- Tampered ciphertext
- Authentication-tag failure
- Nonce requirements
- Randomness requirements
- Encryption/decryption round trips
- Key isolation

---

# 10. Phase 6 — Encrypted Vault

## Goal

Move sensitive VaultX data toward the intended encrypted architecture.

Current conceptual direction:

```text
Client
  ↓
Encrypt
  ↓
Encrypted Payload
  ↓
API
  ↓
Server
  ↓
Database
```

## Implementation Strategy

Do not encrypt everything at once.

Incrementally:

### Step 1

Encrypt one sensitive field/type.

### Step 2

Verify ciphertext is what reaches persistent storage.

### Step 3

Decrypt on the authorized client.

### Step 4

Test incorrect keys.

### Step 5

Test ciphertext tampering.

### Step 6

Test nonce handling.

### Step 7

Test authentication-tag failures.

### Step 8

Expand the encrypted data model.

---

# 11. Phase 7 — Zero-Knowledge Architecture

## Goal

Move VaultX toward a true client-centric cryptographic architecture.

Before implementation, explicitly define:

- What the client knows
- What the server knows
- What the database stores
- What a database attacker obtains
- What a compromised server can access
- How keys are created
- How keys are stored
- How keys are transferred
- How devices obtain keys
- How recovery works

Target conceptual model:

```text
User Secret
     ↓
Argon2id
     ↓
Key Encryption Key
     ↓
Vault Key
     ↓
Item Keys
     ↓
AES-256-GCM
     ↓
Encrypted Items
```

This phase requires a dedicated threat-model review before production implementation is considered complete.

---

# 12. Phase 8 — Password Generator & Security UX

## Goal

Make VaultX practically useful as a password manager.

## Features

- Secure password generator
- Configurable password length
- Character selection
- Passphrase generation
- Password strength feedback
- Show/hide password
- Secure copy
- Clipboard protection where applicable
- Password history where justified
- Security indicators

All randomness must come from a cryptographically secure random number generator.

---

# 13. Phase 9 — TOTP / 2FA

## Goal

Allow VaultX to securely store TOTP secrets and generate authentication codes.

Flow:

```text
Add TOTP
   ↓
Encrypt Secret
   ↓
Store
   ↓
Local Decryption
   ↓
TOTP Algorithm
   ↓
6-Digit Code
```

## Testing

- Correct code generation
- Time synchronization
- Invalid secret
- Incorrect configuration
- Secret protection
- Expired/rotated codes where applicable

The server should not require the plaintext TOTP secret merely to generate a code.

---

# 14. Phase 10 — Multi-Device & Synchronization

## Goal

Allow users to securely use VaultX across multiple devices.

Target:

```text
                 VaultX Server
                /      |                     /       |                   Web     Desktop    Mobile
```

## Design Areas

- Device identity
- Device registration
- Sync protocol
- Change tracking
- Versioning
- Conflict resolution
- Offline changes
- Deleted entries
- Device revocation
- Key changes

Synchronization must be designed around encrypted data and the VaultX security model.

---

# 15. Phase 11 — Device & Session Security

## Goal

Give users control over where and how their VaultX account is accessed.

## Device Management

Display:

```text
Windows PC       Active
Android Phone    Active
MacBook          Active
Old Laptop       Revoked
```

Potential information:

- Device name
- Platform
- Last active time
- Session status

Users should be able to revoke devices.

## Session Management

Implement:

- Session listing
- Session revocation
- Refresh-token rotation
- Expiration
- Logout
- Suspicious-session handling

---

# 16. Phase 12 — Browser Extension

## Goal

Turn VaultX into an integrated password manager for browsers.

## Features

- Detect login forms
- Autofill
- Save credentials
- Generate passwords
- Website matching
- Secure communication with VaultX

## Security Topics

Study and test:

- Content scripts
- Extension permissions
- Origin validation
- Secure message passing
- DOM attacks
- Malicious websites
- Credential exfiltration
- Phishing scenarios

The browser extension is a security-sensitive client and must receive dedicated security testing.

---

# 17. Phase 13 — Secure Sharing

## Goal

Allow users to securely share selected vault information without exposing their entire vault.

Potential capabilities:

- Share individual items
- Share folders
- Read-only access
- Expiration
- Revocation
- Recipient identity
- Encrypted recipient access

Per-item keys should provide a foundation for granular sharing.

---

# 18. Phase 14 — Recovery & Emergency Access

## Goal

Solve the difficult problem of account and encryption-key recovery without silently creating a decryption backdoor.

Potential approaches to evaluate:

- Recovery key
- Recovery phrase
- Device-based recovery
- Social recovery
- Emergency contact
- Time-delayed access

Possible emergency-access flow:

```text
User
 ↓
Emergency Contact
 ↓
Access Request
 ↓
Waiting Period
 ↓
User Can Cancel
 ↓
Emergency Access
```

Every recovery mechanism must have a documented threat model before implementation.

---

# 19. Phase 15 — Advanced Vault Types

## Goal

Expand VaultX beyond traditional passwords.

Potential entry types:

### Secure Notes

- Encrypted text
- Sensitive information

### Identity

- Name
- Address
- Phone
- Email
- Other identity fields

### Payment Information

- Card information
- Billing information
- Notes

### Recovery Codes

- Backup authentication codes

### Developer Secrets

- API keys
- Tokens
- SSH credentials
- Database credentials
- Cloud credentials
- Certificates

### Documents

Potential encrypted document/file storage may be considered later.

Each new type follows the same vertical-slice lifecycle.

---

# 20. Phase 16 — Password Security Center

## Goal

Help users identify security weaknesses in their vault.

Potential areas:

```text
Security Center
│
├── Weak passwords
├── Reused passwords
├── Old passwords
├── Security recommendations
└── Account security
```

The architecture must avoid unnecessarily exposing plaintext passwords to the server.

Security analysis should be designed around the encrypted/client-side architecture.

---

# 21. Phase 17 — Breach Monitoring

## Goal

Provide privacy-conscious monitoring for compromised credentials.

Potential features:

- Compromised credential detection
- Breach notifications
- Privacy-preserving breach checks

Before implementation, evaluate:

- External services
- Data exposure
- Privacy implications
- API trust
- Client-side processing
- Rate limits
- Cost

---

# 22. Phase 18 — Desktop Application

## Goal

Provide native-style VaultX clients for desktop platforms.

Target platforms:

- Windows
- macOS
- Linux

Potential features:

- Offline vault
- Local encrypted storage
- Auto-lock
- Clipboard protection
- System tray
- OS keychain integration
- Secure synchronization

The desktop application must follow the same cryptographic architecture as the web client.

---

# 23. Phase 19 — Mobile Applications

## Goal

Provide secure VaultX clients for mobile devices.

Target:

- Android
- iOS

Potential features:

- Biometric unlock
- Autofill
- TOTP
- Password generation
- Offline vault
- Secure synchronization
- Device management

Mobile key storage must use appropriate platform security mechanisms.

---

# 24. Phase 20 — CLI & Developer Vault

## Goal

Provide secure access to VaultX secrets for developers.

Example commands:

```text
vaultx login
vaultx list
vaultx get github-token
vaultx get aws-key
```

Potential use cases:

- Development environments
- API credentials
- SSH credentials
- CI/CD
- Cloud credentials
- Local development

Security requirements will include:

- Secure authentication
- Secure local credential storage
- Minimal secret exposure
- Clipboard/stdout safety
- Shell history considerations

---

# 25. Phase 21 — Production Infrastructure

## Goal

Prepare VaultX for real-world deployment.

## Infrastructure

Potential components:

- Docker
- Container orchestration where justified
- PostgreSQL
- Redis where required
- Reverse proxy
- HTTPS/TLS
- Firewall
- WAF where appropriate
- Secrets management
- Monitoring
- Alerting
- Backups
- Disaster recovery

## DevOps

Implement:

- CI/CD
- Automated tests
- Dependency scanning
- Container scanning
- Security checks
- Deployment environments
- Configuration management
- Infrastructure-as-code where justified

---

# 26. Phase 22 — Security Testing & Hardening

## Goal

Attempt to break VaultX before production release.

## Application Security

Test:

- Authentication attacks
- Authorization bypass
- IDOR
- SQL injection
- XSS
- CSRF where applicable
- SSRF
- File/upload attacks
- Rate-limit bypass
- Session attacks
- Token attacks
- Sensitive-data exposure

## Cryptographic Security

Test:

- Key handling
- Nonce misuse
- Authentication-tag failures
- Wrong-key behavior
- Ciphertext tampering
- Key rotation
- Key isolation

## Infrastructure Security

Test:

- Network exposure
- TLS configuration
- Database exposure
- Container configuration
- Secrets exposure
- Firewall rules
- Production configuration

## Security Tools

As appropriate:

- OWASP ZAP
- Burp Suite
- Nmap
- Wireshark
- Vulnerability scanners
- Dependency scanners
- Container scanners

This phase connects VaultX directly to the cybersecurity skills being learned alongside the project.

---

# 27. Phase 23 — Production Release

## Goal

Release VaultX as a real product.

Deployment progression:

```text
Development
     ↓
Staging
     ↓
Automated Testing
     ↓
Security Testing
     ↓
Performance Testing
     ↓
Backup/Restore Testing
     ↓
Production
```

## Production Requirements

Before release, verify:

- Authentication security
- Authorization
- Encryption
- Key management
- TLS
- Database security
- Secrets management
- Rate limiting
- Monitoring
- Backups
- Disaster recovery
- Error handling
- Logging
- Security alerts
- Dependency security
- Deployment security

---

# 28. Post-Launch Development

VaultX development does not end at production release.

After launch:

- Monitor security events
- Fix vulnerabilities
- Update dependencies
- Improve performance
- Improve UX
- Review architecture
- Test backups
- Test disaster recovery
- Review threat model
- Add features incrementally
- Conduct security reviews
- Maintain documentation

Major architectural changes should update the Product & Security Specification.

---

# 29. Complete VaultX Roadmap

```text
PHASE 0
Product & Security Foundation
        ✓
        ↓
PHASE 1
Architecture Foundation
        ↓
PHASE 2
Account & Authentication
        ↓
PHASE 3
Vault Creation & Access
        ↓
PHASE 4
Password Entries
        ↓
PHASE 5
Cryptographic Core
        ↓
PHASE 6
Encrypted Vault
        ↓
PHASE 7
Zero-Knowledge Architecture
        ↓
PHASE 8
Password Generator & Security UX
        ↓
PHASE 9
TOTP / 2FA
        ↓
PHASE 10
Multi-Device & Synchronization
        ↓
PHASE 11
Device & Session Security
        ↓
PHASE 12
Browser Extension
        ↓
PHASE 13
Secure Sharing
        ↓
PHASE 14
Recovery & Emergency Access
        ↓
PHASE 15
Advanced Vault Types
        ↓
PHASE 16
Password Security Center
        ↓
PHASE 17
Breach Monitoring
        ↓
PHASE 18
Desktop Application
        ↓
PHASE 19
Mobile Applications
        ↓
PHASE 20
CLI & Developer Vault
        ↓
PHASE 21
Production Infrastructure
        ↓
PHASE 22
Security Testing & Hardening
        ↓
PHASE 23
Production Release
        ↓
Post-Launch Development
```

---

# 30. Current Project Status

```text
Phase 0 — Product & Security Foundation     ✓ COMPLETE

Phase 1 — Architecture Foundation          ✓ COMPLETE

Phase 2 — Account & Authentication         ✓ COMPLETE
Phase 3 — Vault Creation & Access           ✓ COMPLETE
Phase 4 — Password Entries                  ← CURRENT
Phase 5 — Cryptographic Core                ⏳
Phase 6 — Encrypted Vault                   ⏳
Phase 7 — Zero-Knowledge Architecture       ⏳
Phase 8 — Password Generator                ⏳
Phase 9 — TOTP / 2FA                        ⏳
Phase 10 — Multi-Device Sync                ⏳
Phase 11 — Device & Session Security        ⏳
Phase 12 — Browser Extension                ⏳
Phase 13 — Secure Sharing                   ⏳
Phase 14 — Recovery                         ⏳
Phase 15 — Advanced Vault Types             ⏳
Phase 16 — Security Center                  ⏳
Phase 17 — Breach Monitoring                ⏳
Phase 18 — Desktop                          ⏳
Phase 19 — Mobile                           ⏳
Phase 20 — CLI / Developer Vault            ⏳
Phase 21 — Production Infrastructure        ⏳
Phase 22 — Security Hardening               ⏳
Phase 23 — Production Release               ⏳
```

---

# 31. VaultX Learning Roadmap

VaultX is also the main practical learning project.

The project should simultaneously develop skills in:

```text
                VAULTX
                   │
       ┌───────────┼───────────┐
       ↓           ↓           ↓
    Backend     Frontend    Security
       │           │           │
     .NET         React      Networking
     APIs       TypeScript   Cryptography
     EF Core      UI/UX      Web Security
     PostgreSQL   Testing     Threat Modeling
       │           │           │
       └───────────┼───────────┘
                   ↓
             System Design
                   ↓
                DevOps
                   ↓
              Production
```

## Backend Learning

- ASP.NET Core
- Clean Architecture
- REST APIs
- EF Core
- PostgreSQL
- Authentication
- Authorization
- Distributed systems
- Caching
- Background processing

## Frontend Learning

- React
- TypeScript
- State management
- API integration
- Authentication state
- Form validation
- Security-conscious UI
- Browser APIs

## Cybersecurity Learning

- Networking
- HTTP/HTTPS
- Authentication
- Authorization
- OWASP
- Threat modeling
- Vulnerability testing
- Nmap
- Wireshark
- Burp Suite
- OWASP ZAP

## Cryptography Learning

- Hashing
- Password hashing
- Argon2id
- Symmetric encryption
- AES-GCM
- Nonces
- Authentication tags
- Key derivation
- Key hierarchy
- Public-key cryptography
- Digital signatures
- WebAuthn/passkeys

## DevOps Learning

- Linux
- Docker
- CI/CD
- TLS
- Reverse proxies
- Cloud deployment
- Monitoring
- Logging
- Backups
- Disaster recovery

## System Design Learning

- API design
- Data modeling
- Scaling
- Caching
- Synchronization
- Conflict resolution
- Distributed systems
- Multi-device architecture
- Key management
- Secure sharing

---

# 32. Non-Negotiable Development Rules

VaultX development should follow these rules.

## Rule 1

**Do not build the entire backend first.**

Build vertical slices.

## Rule 2

**Every major backend feature gets its frontend implementation.**

## Rule 3

**Every feature is tested through the complete application.**

## Rule 4

**Security-sensitive functionality gets security analysis before implementation.**

## Rule 5

**Never invent cryptography.**

Use established algorithms and well-reviewed libraries.

## Rule 6

**Do not store plaintext secrets unnecessarily.**

## Rule 7

**Do not expose secrets through logs, errors, URLs, or telemetry.**

## Rule 8

**Frontend security checks never replace backend authorization.**

## Rule 9

**Do not add advanced architecture simply because it sounds impressive.**

Every technology must have a reason.

## Rule 10

**Update the specification when major architectural decisions change.**

---

# 33. Definition of a Completed VaultX Feature

A feature is considered complete when:

```text
Requirement
    ↓
Design
    ↓
Security Review
    ↓
Backend
    ↓
Frontend
    ↓
Integration
    ↓
Unit Tests
    ↓
Integration Tests
    ↓
End-to-End Tests
    ↓
Security Tests
    ↓
Documentation
    ↓
Feature Complete ✓
```

A feature being "coded" is not the same as a feature being "complete."

---

# 34. Final Development Direction

VaultX will be built as a **real product from the beginning**, while remaining a structured learning project.

The project will evolve incrementally:

```text
Foundation
    ↓
Working Application
    ↓
Secure Application
    ↓
Encrypted Application
    ↓
Zero-Knowledge-Oriented Application
    ↓
Multi-Device Platform
    ↓
Cross-Platform Product
    ↓
Production System
```

The objective is not to implement every feature as quickly as possible.

The objective is to build VaultX in a way where each phase:

- Works
- Is understood
- Is tested
- Is secure
- Integrates with the previous phases
- Provides a foundation for the next phase
