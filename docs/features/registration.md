# Registration Feature Specification & Documentation

**Feature:** User Registration  
**Vertical Slice:** Phase 2, Slice 2.1  
**Status:** Feature Complete  
**Date:** September 2026  

---

## 1. Purpose

The Registration feature provides the foundational identity creation mechanism for VaultX. It allows new users to register a secure account using their email address and a high-entropy master password. As a vertical slice, Registration encompasses backend domain logic, persistence, client-side UI, bidirectional API integration, automated end-to-end verification, and rigorous security testing.

---

## 2. User Flow

The complete end-to-end registration flow operates as follows:

```text
User
  │
  ▼
Registration UI (React / Vite)
  │
  ├─ User enters Email, Master Password, and Confirmation
  ├─ Password strength checklist evaluates requirements in real time
  │
  ▼
Client-Side Validation (UX Barrier)
  │
  ├─ If invalid: display field-specific error messages; abort API call
  │
  ▼
POST /api/auth/register (HTTPS / JSON)
  │
  ▼
ASP.NET Core API Pipeline
  │
  ├─ CORS Policy Check (strictly scoped development origin)
  ├─ Exception Handling Middleware (RFC 9110 ProblemDetails boundary)
  ├─ AuthController receives RegisterUserRequest
  │
  ▼
Application Layer (Command & Handler Architecture)
  │
  ├─ Application Command Validator (Authoritative Security Boundary)
  │    - Email syntax & length <= 320 chars
  │    - Password complexity (12-128 chars, uppercase, lowercase, digit, special)
  │    - ConfirmPassword match
  │
  ├─ Email Normalization (trim and convert to lowercase)
  │
  ├─ Pre-emptive Duplicate Check via UserRepository.GetByEmailAsync
  │    - If exists: throw DuplicateEmailException -> 409 Conflict
  │
  ▼
Security Services & Hashing
  │
  ├─ CSPRNG generates unique 128-bit cryptographic salt
  ├─ PBKDF2-HMAC-SHA256 (100,000 iterations) derives 256-bit subkey
  ├─ Compact modular format formatted: $pbkdf2-sha256$i=100000$<salt>$<hash>
  │
  ▼
Infrastructure Persistence
  │
  ├─ User entity instantiated with new GUID and UTC timestamp
  ├─ UserRepository adds entity to EF Core ApplicationDbContext
  ├─ UnitOfWork executes CommitAsync / SaveChangesAsync
  │    - PostgreSQL unique index (IX_Users_Email) enforces final race-condition defense
  │
  ▼
Safe Response Generation
  │
  ├─ Map to UserResponse containing only non-sensitive account metadata
  ├─ Return HTTP 201 Created with Location header
  │
  ▼
Registration UI Success State
  │
  ├─ Clear sensitive inputs from form state
  ├─ Render "Registration Successful" confirmation view
  └─ Direct user to upcoming Login workflow
```

---

## 3. API Contract

### Endpoint

- **Method:** `POST`
- **Path:** `/api/auth/register`
- **Consumes:** `application/json`
- **Produces:** `application/json`

### Request Payload

```json
{
  "email": "user@example.com",
  "password": "SecurePassword123!",
  "confirmPassword": "SecurePassword123!"
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `email` | `string` | Yes | Valid email format, max 320 characters |
| `password` | `string` | Yes | 12–128 characters, >=1 uppercase, >=1 lowercase, >=1 digit, >=1 special character |
| `confirmPassword` | `string` | Yes | Must match `password` exactly |

### Successful Response: `HTTP 201 Created`

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "createdAt": "2026-09-30T12:00:00Z",
  "updatedAt": "2026-09-30T12:00:00Z"
}
```

*Note: The response never includes password, password hash, encryption keys, auth tokens, session state, or vault data.*

### Validation Error Response: `HTTP 400 Bad Request`

Compliant with RFC 9110 Problem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": [
      "Email format is invalid."
    ],
    "Password": [
      "Password must be at least 12 characters long.",
      "Password must contain at least one uppercase letter."
    ]
  }
}
```

### Duplicate Email Conflict Response: `HTTP 409 Conflict`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "A user with this email already exists."
}
```

### Internal Server Error Response: `HTTP 500 Internal Server Error`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "detail": "An unexpected error occurred."
}
```

---

## 4. Validation Rules

Validation operates on both tiers with clear boundaries of trust:

1. **Client-Side Validation (Frontend UX):**
   - Validates input formats on submit and dynamically updates password criteria checklist during input.
   - Prevents unnecessary network requests when constraints are unmet.
   - Never trusted as a security boundary.

2. **Server-Side Validation (Backend Authority):**
   - Enforced by `RegisterUserCommandValidator` (custom pure C# validator) in the application pipeline.
   - Authoritative security boundary against direct HTTP attacks, bypasses, or malformed tools.

### Rule Matrix

| Rule | Constraint | Error Message |
|---|---|---|
| Email Requirement | Non-empty, non-whitespace | `Email is required.` |
| Email Length | <= 320 characters | `Email must not exceed 320 characters.` |
| Email Format | RFC standard email regex pattern | `Email format is invalid.` |
| Password Requirement | Non-empty | `Password is required.` |
| Password Min Length | >= 12 characters | `Password must be at least 12 characters long.` |
| Password Max Length | <= 128 characters | `Password must not exceed 128 characters.` |
| Uppercase Letter | At least 1 character in `[A-Z]` | `Password must contain at least one uppercase letter.` |
| Lowercase Letter | At least 1 character in `[a-z]` | `Password must contain at least one lowercase letter.` |
| Digit | At least 1 character in `[0-9]` | `Password must contain at least one digit.` |
| Special Character | At least 1 symbol | `Password must contain at least one special character.` |
| Confirmation | Must match `password` | `Passwords do not match.` |

---

## 5. Backend Architecture

The backend strictly conforms to Clean Architecture and DDD principles:

```text
PasswordManager.API (Presentation)
  │
  ├── Controllers/AuthController.cs         # Thin endpoint delegating to IRegisterUserHandler
  ├── Middleware/ExceptionHandlingMiddleware # Centralized RFC 9110 error mapping
  └── Program.cs                            # Pipeline, DI, scoped CORS, and EF Core setup
       │
       ▼
PasswordManager.Application (Use Cases)
  │
  ├── Features/Authentication/Register/
  │    ├── RegisterUserCommand.cs           # Command record (Email, Password, ConfirmPassword)
  │    ├── RegisterUserCommandValidator.cs  # Pure C# static validation logic with compiled regex
  │    ├── IRegisterUserHandler.cs          # Handler abstraction
  │    └── RegisterUserHandler.cs           # Orchestrates normalization, hashing, persistence
  ├── Interfaces/Persistence/
  │    ├── IUserRepository.cs               # User domain repository contract
  │    └── IUnitOfWork.cs                   # Transaction & change-commit contract
  └── Interfaces/Security/
       └── IPasswordHasher.cs               # Cryptographic hashing contract
       │
       ▼
PasswordManager.Domain (Core)
  │
  └── Entities/User.cs                      # Aggregate root with Guid Id, Email, PasswordHash, timestamps
       ▲
       │
PasswordManager.Infrastructure (Persistence & Services)
  │
  ├── Persistence/ApplicationDbContext.cs   # EF Core DbContext
  ├── Persistence/Configurations/
  │    └── UserConfiguration.cs             # Unique constraint (IX_Users_Email), column types
  ├── Repositories/UserRepository.cs        # EF Core implementation with AsNoTracking lookups
  ├── Persistence/UnitOfWork.cs             # DbContext transaction wrapper
  └── Services/PasswordHasher.cs            # Cryptographic PBKDF2-HMAC-SHA256 implementation
```

---

## 6. Frontend Architecture

Built with React 19, TypeScript, and Vite:

```text
frontend/src/
  ├── components/
  │    ├── RegisterForm.tsx     # Controlled component for user registration
  │    └── RegisterForm.css     # Accessible, high-contrast, responsive CSS styling
  ├── api/
  │    └── auth.ts              # Fetch client wrapper with strongly typed ApiError mapping
  ├── utils/
  │    └── validation.ts        # Pure validation logic & password criteria evaluation
  ├── types/
  │    └── auth.ts              # TypeScript interfaces (RegisterUserRequest, UserResponse)
  └── __tests__/
       ├── RegisterForm.test.tsx # UI component, integration, and security assertion tests
       ├── authApi.test.ts      # HTTP mock tests for 201, 400, 409, 500, network errors
       └── validation.test.ts   # Comprehensive unit test suite for validation functions
```

---

## 7. Security Controls

### Password Handling
- **No Plaintext Persistence:** Plaintext passwords exist only transiently in memory during hashing and are immediately discarded.
- **No Logging:** Master passwords and password confirmations are strictly excluded from logging frameworks and exception traces.
- **No Response Disclosure:** API responses return only non-sensitive account metadata.
- **No Client Storage:** Neither `localStorage` nor `sessionStorage` stores passwords, hashes, or tokens.
- **No URL / Query Exposure:** Passwords are never serialized into URLs, query parameters, or client history.
- **Separation of Concerns:** Password hashing authenticates account identity; it is strictly separate from future client-side zero-knowledge vault encryption keys.

### Password Hashing Specification
- **Algorithm:** PBKDF2-HMAC-SHA256
- **Work Factor / Iterations:** 100,000 iterations
- **Salt:** 128 bits (16 bytes) generated via `RandomNumberGenerator` (CSPRNG)
- **Subkey Length:** 256 bits (32 bytes)
- **Format:** `$pbkdf2-sha256$i=100000$<base64-salt>$<base64-hash>`
- **Verification:** Uses constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to eliminate timing side-channel attacks.
- *Roadmap Notice:* PBKDF2-HMAC-SHA256 represents the current vertical slice baseline. Argon2id migration and production cryptographic parameter tuning will be executed during the dedicated Cryptographic Strategy Phase according to the VaultX specification.

### Mass Assignment Protection
- The API accepts only the dedicated `RegisterUserRequest` DTO containing `email`, `password`, and `confirmPassword`.
- Attacker-supplied fields (such as `id`, `passwordHash`, `createdAt`, `updatedAt`, `isAdmin`) in the JSON payload are completely ignored. The server generates its own primary keys, timestamps, and cryptographic hashes.

### Race Condition & Duplicate Email Protection
- Application-level check via `UserRepository.GetByEmailAsync` provides early feedback.
- Database-level unique index constraint (`IX_Users_Email` in PostgreSQL) provides the definitive protection against concurrent race conditions. Verified by automated tests issuing 5 simultaneous concurrent registrations for the same identity.

### HTTP Method & CORS Scoping
- Non-POST HTTP verbs (`GET`, `PUT`, `PATCH`, `DELETE`) on `/api/auth/register` are rejected with `405 Method Not Allowed`.
- CORS policy is scoped specifically to the local development frontend origin (`http://localhost:5173`). Wildcard origins (`*`) and arbitrary untrusted origins are rejected.

---

## 8. Error Handling & Information Disclosure

- **RFC 9110 ProblemDetails:** All API exceptions and client errors are caught by `ExceptionHandlingMiddleware` and transformed into structured Problem Details.
- **Information Sanitization:** Error responses never disclose:
  - Database schema or table names
  - SQL query text or PostgreSQL driver exceptions
  - Stack traces or code line numbers
  - Local host file system paths
  - Database connection strings or infrastructure credentials
- **Frontend Safe Banners:** The React UI catches network and server errors and displays sanitized, user-friendly notices without exposing raw network error internals.

---

## 9. Automated Test Coverage

| Test Suite | Total Tests | Passed | Coverage Focus |
|---|---|---|---|
| **Frontend Unit & Integration** | 39 | 39 | Form rendering, field validation, password toggle, criteria checklist, API integration, 400/409/500 handling, network drops, storage safety |
| **Backend Unit Tests** | 39 | 39 | `RegisterUserCommandValidator`, `RegisterUserHandler`, `PasswordHasher`, `UserRepository`, `UnitOfWork` |
| **Backend Integration & Security** | 34 | 34 | End-to-end API pipeline, validation bypass, mass assignment, race conditions, response disclosure, error disclosure, CORS, HTTP methods |
| **Total Test Suite** | **112** | **112** | **100% Passing** |

### Additional Tooling Verification
- **Frontend Build (`npm run build`):** PASS (Clean TypeScript compilation and Vite bundle generation)
- **Frontend Lint (`npm run lint`):** PASS (0 errors, 0 warnings across all files via Oxlint)
- **Database Cleanup:** PASS (Database verified with 0 lingering test artifacts)

---

## 10. Architectural Decisions

1. **Strict Clean Architecture Direction:** Dependency flow points inward: Presentation -> Application -> Domain, with Infrastructure implementing Application abstractions.
2. **Decoupled Handler Pattern (`IRegisterUserHandler`):** Keeps `AuthController` exceptionally thin and focused solely on HTTP concerns, delegating use-case execution directly to the Application handler interface.
3. **Defense in Depth for Validation:** Application-level validation (`RegisterUserCommandValidator`) is the authoritative validation authority; client-side validation is solely an interactive UX enhancement.
4. **Separation of Hashing and Encryption:** The user's master password hash in the database is only for account authentication; it is architecturally distinct from client-derived vault encryption keys.
5. **No Automatic Authentication on Registration:** Registration completes by confirming account creation and prompting the user to log in, rather than automatically generating authentication tokens or sessions.

---

## 11. Deferred Security Work

The following items were identified and explicitly deferred to their appropriate future roadmap phases:

1. **Registration Rate Limiting / Abuse Protection:** Deferred to Phase 4 (API Security Hardening) to implement IP/identity rate limiting with distributed cache support.
2. **IDOR / Authorization Verification:** Registration is an unauthenticated resource creation endpoint; IDOR authorization testing is deferred until authenticated resource endpoints exist.
3. **Production HTTPS & HSTS:** Deferred to production infrastructure and deployment environment configuration.
4. **Production CSP & Advanced Security Headers:** Deferred to production reverse proxy / gateway configuration.
5. **Argon2id Cryptographic Migration:** PBKDF2-HMAC-SHA256 serves as the verified registration baseline; migration to Argon2id will take place during the dedicated Cryptographic Strategy Phase.

---

## 12. Completion Status

| Step | Requirement | Status |
|---|---|---|
| 6A | Application Foundation | **Complete** |
| 6B | Registration Design / Threat Model | **Complete** |
| 6C | Registration Backend Implementation | **Complete** |
| 6D | Registration Frontend Implementation | **Complete** |
| 6E | Registration Integration | **Complete** |
| 6F | Registration E2E & Security Testing | **Complete** |
| 6G | Registration Documentation & Review | **Complete** |

### Definition of Done Checklist

- [x] Requirement documented
- [x] Security analysis documented
- [x] Backend implemented
- [x] Frontend implemented
- [x] API integration implemented
- [x] Database persistence verified
- [x] Unit tests passing (39 frontend, 39 backend)
- [x] Integration tests passing (34 backend)
- [x] Frontend tests passing (39 passed)
- [x] E2E tests passing
- [x] Security tests passing
- [x] Build passing
- [x] Lint passing
- [x] Security findings documented
- [x] Deferred work documented
- [x] Feature documentation updated
- [x] Roadmap status updated
- [x] Product/security specification status updated: **NOT APPLICABLE** (No standalone specification file exists in repository; baseline architectural principles and status are maintained across `README.md`, `VaultX-Product-Development-Security-Roadmap.md`, and `docs/features/registration.md`)
- [x] Git working tree clean
- [x] Feature branch pushed

**Definition of Done:** **SATISFIED**  
The Registration vertical slice is complete, fully tested, accurately documented, and ready for PR review into `develop`.
