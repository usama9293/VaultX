# VaultX

VaultX is a security-focused password and secrets manager being developed as a learning and portfolio project. Phase 2 account and authentication functionality is complete; Phase 3 is implementing authenticated vault creation and access.

## Project Goals

VaultX explores secure backend architecture, Clean Architecture, cryptography, authentication, encrypted storage, zero-knowledge-oriented design, frontend security, testing, DevOps, cybersecurity, and multi-platform development.

## Architecture

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

The API composes the application and infrastructure layers. Application depends on Domain, while Infrastructure implements application contracts. Domain remains independent of infrastructure concerns.

## Development Approach

VaultX uses vertical-slice development. Each meaningful feature moves through:

```text
Requirement
↓
Design
↓
Security Analysis
↓
Backend
↓
Frontend
↓
Integration
↓
Testing
↓
Security Testing
↓
Documentation
↓
Complete
```

## Current Status

Phase 1 (Architecture Foundation) and Phase 2 (Account & Authentication) are complete, including registration, login, refresh-token rotation, logout, protected user resources, rate limiting, account lockout, and JWT signing-key hardening. Phase 3 (Vault Creation & Access) is current. Its initial vault is metadata-only; password entries belong to Phase 4, cryptographic primitives to Phase 5, encrypted-vault functionality to Phase 6, and zero-knowledge architecture to Phase 7.

## Branch Strategy

`main` represents stable project state. Feature, bug-fix, and security work should use branches named `feature/<name>`, `fix/<name>`, and `security/<name>`, respectively. When a shared integration branch is needed, use `develop`; merge reviewed work into `main` only when it is stable. No additional branches are created until needed.

## Security

- Plaintext master passwords are never persisted; password hashing uses PBKDF2-HMAC-SHA256 (future Argon2id migration planned).
- Secrets and local credentials must not be committed to Git.
- The API requires `JwtSettings__SecretKey` from secure configuration (at least 32 UTF-8 bytes) and fails startup if it is missing or too short; no usable signing-key default is included in the repository.
- Cryptography will use established primitives and libraries.
- Security decisions are documented before sensitive functionality is implemented.

VaultX is a development and learning project, not a claim of production security or independent security audit.
