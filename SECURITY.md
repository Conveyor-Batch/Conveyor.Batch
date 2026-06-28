# Security Policy

## Supported Versions

Only the most recent release series receives security fixes.

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |
| < 0.1   | No        |

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please use [GitHub Security Advisories](https://github.com/conveyor-dotnet/conveyor.batch/security/advisories/new) to report vulnerabilities privately. This keeps details confidential until a fix is available.

If you are unable to use GitHub Security Advisories, email **aqelyazid@gmail.com** with the subject line `[SECURITY] Conveyor.Batch — <brief description>`.

## Response Timeline

| Milestone | Target |
|-----------|--------|
| Acknowledge receipt | Within 48 hours |
| Confirm or dispute the vulnerability | Within 7 days |
| Release fix (critical/high severity) | Within 90 days |
| Release fix (medium/low severity) | Within 180 days |
| Public disclosure | After fix is released and users have had time to update |

We will keep you informed throughout the process. If we need more information, we will reach out via the same channel.

## What to Include in Your Report

A good vulnerability report helps us respond faster. Please include:

1. **Description** — a clear explanation of the vulnerability and which component is affected.
2. **Reproduction steps** — a minimal, self-contained code sample or sequence of steps that demonstrates the issue.
3. **Impact** — what an attacker could achieve by exploiting this vulnerability (e.g., data loss, privilege escalation, denial of service).
4. **Affected versions** — which versions of Conveyor.Batch (or its sub-packages) are affected.
5. **Suggested fix** (optional) — if you have ideas on how to address it.

## Scope

The following packages are in scope:

- `Conveyor.Batch`
- `Conveyor.Batch.EntityFrameworkCore`
- `Conveyor.Batch.IO`
- `Conveyor.Batch.Http`
- `Conveyor.Batch.Hosting`
- `Conveyor.Batch.Testing`

Third-party dependencies (Polly, EF Core, etc.) should be reported upstream to their respective projects.

## Disclosure Policy

We follow coordinated disclosure. We will:

- Work with you to understand and validate the report.
- Develop and test a fix in a private branch.
- Release the fix and publish a GitHub Security Advisory.
- Credit you in the advisory (unless you prefer to remain anonymous).

Thank you for helping keep Conveyor.Batch and its users safe.
