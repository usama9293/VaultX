# VaultX

VaultX is a security-focused password and secrets manager being developed as a learning and portfolio project. It is currently in the architecture foundation stage; user-facing security features are not yet complete.

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

The project is establishing its architecture and development foundation. Authentication, vault workflows, and frontend functionality are future work.

## Branch Strategy

`main` represents stable project state. Feature, bug-fix, and security work should use branches named `feature/<name>`, `fix/<name>`, and `security/<name>`, respectively. When a shared integration branch is needed, use `develop`; merge reviewed work into `main` only when it is stable. No additional branches are created until needed.

## Security

- Plaintext master passwords must never be persisted; authentication flows are not yet implemented.
- Secrets and local credentials must not be committed to Git.
- Cryptography will use established primitives and libraries.
- Security decisions are documented before sensitive functionality is implemented.

VaultX is a development and learning project, not a claim of production security or independent security audit.
