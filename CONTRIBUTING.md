# Contributing to Conveyor.Batch

Thank you for your interest in contributing! This document covers everything you need to get started.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Ways to Contribute](#ways-to-contribute)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Commit Messages](#commit-messages)
- [Opening a Pull Request](#opening-a-pull-request)
- [Architecture Decisions](#architecture-decisions)

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to uphold it. Please report unacceptable behaviour to the maintainers.

---

## Ways to Contribute

- **Bug reports** — open an [issue](https://github.com/Conveyor-Batch/Conveyor.Batch/issues) with a minimal reproduction
- **Feature requests** — open an issue with the `enhancement` label before writing code
- **Documentation** — typos, missing examples, unclear explanations are all fair game
- **Tests** — additional test cases for edge cases or new components
- **Code** — bug fixes and features tied to an approved issue

If you plan to work on something significant, **open an issue first** so we can discuss the approach before you invest time writing code.

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- Git

### Clone and build

```bash
git clone https://github.com/Conveyor-Batch/Conveyor.Batch.git
cd Conveyor.Batch
dotnet build ConveyorBatch.slnx
dotnet test ConveyorBatch.slnx --framework net9.0
```

### Run the getting-started sample

```bash
dotnet run --project samples/GettingStarted
```

---

## Development Workflow

1. **Fork** the repository and create a branch from `main`:
   ```bash
   git checkout -b feat/my-feature
   ```

2. **Make your changes** — keep each commit focused on a single concern.

3. **Add or update tests** — all new behaviour must be covered by tests. The quality gate is ≥ 80% code coverage.

4. **Ensure the build is clean:**
   ```bash
   dotnet build ConveyorBatch.slnx --configuration Release
   dotnet test ConveyorBatch.slnx --framework net9.0
   ```
   There must be **zero warnings** (warnings are treated as errors).

5. **Open a pull request** against `main`.

---

## Coding Standards

These are enforced by the build — any violation causes a build failure.

| Rule | Setting |
|---|---|
| Nullable reference types | `<Nullable>enable</Nullable>` |
| Warnings as errors | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| XML documentation | Required on all public APIs |
| Async model | `Task`, `ValueTask`, or `IAsyncEnumerable` only — no sync-over-async |
| `CancellationToken` | Always the last parameter |
| Namespace root | `Conveyor.Batch` |
| Internal types | `internal sealed` where the type is not part of the public API |

### Style

- Follow existing patterns in the file you are editing.
- No `throw new NotImplementedException()` in committed code.
- Use `readonly record struct` for value objects (e.g. `JobParameters`).
- Keep methods short and focused — extract private helpers rather than nesting logic.
- Comments should explain **why**, not what. Well-named identifiers explain what.

---

## Commit Messages

Use the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>: <short summary>

[optional body]
```

Common types:

| Type | When to use |
|---|---|
| `feat` | New feature or capability |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `test` | Adding or updating tests |
| `refactor` | Code change that is neither a fix nor a feature |
| `chore` | Build, tooling, or dependency updates |
| `perf` | Performance improvement |

Example: `feat: add FlatFileItemReader with header-skip support`

---

## Opening a Pull Request

- **One concern per PR** — a PR that fixes a bug and adds a feature is harder to review.
- **Fill in the PR template** — describe what changed, why, and how to test it.
- **Link the related issue** — use `Closes #123` in the PR description.
- **Keep the diff small** — large PRs take longer to review and are more likely to conflict. Split if possible.
- **Do not force-push** to a PR branch after review has started — use additional commits instead.

All PRs must pass CI (build + tests on Linux, Windows, and macOS across .NET 8 and .NET 9) before they can be merged.

---

## Architecture Decisions

Significant design choices are recorded as Architecture Decision Records in [`/docs/adr/`](docs/adr/). If your contribution changes a core design (public API surface, async model, persistence strategy, dependency policy), please open a new ADR alongside the code change and reference it in your PR description.

The existing ADRs are a good reference for the level of detail expected.

---

Thank you for helping make Conveyor.Batch better!
