---
name: security
description: Considerations to be upheld as non-negotiable during any planning, designing, implementing of code solutions.
---

# Security
## Security Essentials
- Secrets Management: No API keys or connection strings in code. Use User Secrets for local dev and Environment Variables for prod.
- Data Protection: PII must be encrypted; passwords must be hashed using industry standards (Identity defaults).
- Input Validation: Strictly validate all DTOs using FluentValidation on both Client and Server projects.
-- Sanitize inputs to prevent XSS
- Auth Enforcement: Use [Authorize] attributes on Controllers/Actions. Avoid manual User.Identity.IsAuthenticated checks in business logic; prefer Policy-based authorization.