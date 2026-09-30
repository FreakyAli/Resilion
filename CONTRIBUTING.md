# Contributing to Resilion

Thank you for your interest in contributing to Resilion!

## Getting Started

### Prerequisites

- .NET SDK **8.0.404 or later**. `global.json` pins that floor with `rollForward: latestMajor`, so
  a newer installed major (9.0, 10.0) satisfies it and will be used automatically.

### Local Setup

```text
git clone https://github.com/FreakyAli/Resilion.git
cd Resilion
dotnet restore
dotnet build
```

## Project Structure

```text
src/
    Resilion/                   Core library (zero dependencies)
    Resilion.Extensions/        DI, logging, telemetry integration
    Resilion.RateLimiting/      Rate limiting strategy

tests/
    Resilion.Tests/             Core strategy and pipeline tests
    Resilion.Extensions.Tests/  DI and telemetry tests

benchmarks/
    Resilion.Benchmarks/        Performance benchmarks (not in .sln)

samples/
    Resilion.Samples/           Usage examples
```

## Development Workflow

1. Fork the repository
2. Create a feature branch from `master`
3. Make your changes
4. Write or update tests
5. Ensure all tests pass: `dotnet test`
6. Submit a pull request

### Running Tests

The test projects run on **Microsoft.Testing.Platform** (MTP) via xunit.v3, not classic VSTest.
That changes the command line: anything after `--` goes to the test platform.

```bash
# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/Resilion.Tests

# Filter by class or method — note the `--` separator
dotnet test tests/Resilion.Tests -- --filter-class '*RetryStrategyTests'
dotnet test tests/Resilion.Tests -- --filter-method '*Async_RetriesOnException*'
```

> **Do not use `dotnet test --filter "FullyQualifiedName~..."`.** That is VSTest syntax. Under MTP it
> is **silently ignored and the entire suite runs** — the only signal is a
> `warning MTP0001: VSTest-specific properties are set but will be ignored` buried in the build
> output. Verified 2026-09-30: the VSTest form ran all 223 tests, the `--filter-class` form ran 18.

### Running Benchmarks

Benchmarks are part of the solution (so CI keeps them compiling), but are not run by
`dotnet test`. Run them directly:

```bash
cd benchmarks/Resilion.Benchmarks
dotnet run -c Release
```

## Code Style

This project uses an `.editorconfig` file for consistent formatting:

- File-scoped namespaces
- Allman bracing (new line before all braces)
- `var` everywhere
- Private fields prefixed with `_`
- Nullable reference types enabled

## Submitting Changes

- Rebase onto `master` before submitting
- Write clear commit messages
- Include tests for new functionality
- Update documentation where applicable
- Ensure CI passes

## Future-plans hygiene

[`docs/future-plans.md`](docs/future-plans.md) is a **todo list**, not a status log. Three rules,
no exceptions.

1. **An item leaves `future-plans.md` in the same PR that implements it.**
   Delete the `### <n>. …` entry, delete its Priority Matrix row, add a row to `## Retired items`,
   and add the corresponding `CHANGELOG.md` entry under `[Unreleased]`. Never mark an entry
   "✅ IMPLEMENTED", never set `Status: Complete` — those are `CHANGELOG.md`'s job. A PR that adds a
   completion marker instead of removing the entry will be asked to do the removal.

2. **If a PR only partially closes an item, file the remainder under a NEW number and retire the
   old one.** Do not leave a half-true entry in place, and do not reuse or redefine a number. The
   next free number is the highest in `## Retired items` or the matrix, plus one.

3. **The entries are the source of truth; the Priority Matrix is an index.**
   Every matrix row must have an entry and every entry a matrix row. This is machine-checkable:

   ```bash
   diff <(sed '/^## Retired items/,$d' docs/future-plans.md \
            | grep -oE '^\| [0-9]+ \|' | tr -cd '0-9\n' | sort -n) \
        <(grep -oE '^### [0-9]+\.' docs/future-plans.md | tr -cd '0-9\n' | sort -n)
   ```

   No output means consistent.

**Why these rules exist:** `future-plans.md` once documented three items *twice, with opposite
verdicts* — a "✅ IMPLEMENTED" block was prepended without removing the original open write-ups or
updating the matrix. One of those stale entries was load-bearing: it claimed public API tracking was
working and that "CI builds will fail if public surface changes". It wasn't, and they didn't. The
1.0 API freeze was completely unenforced for as long as that claim stood unchallenged.

### Claims about current behaviour must be re-verified, not remembered

Any sentence asserting what the code does today — "grep confirms zero tag usage", "confirmed via
grep", "targets net8.0 only" — must be backed by a command in
[`docs/verification.md`](docs/verification.md), re-run at the time of writing, with the date and
command recorded in the entry's `**Verified:**` field. **Paste the command and its output into the
PR description.**

`future-plans.md` item #47 is the standard to match: it names the SDK version, the exact error code,
and says how many times the failure was reproduced.

### Moving an item between docs

[`docs/tradeoffs.md`](docs/tradeoffs.md) holds accepted imperfections; `future-plans.md` holds
planned fixes. **An issue must be in exactly one of them** — being in both means the repo claims
simultaneously that the fix isn't worth doing and that it's planned. When an item moves, leave a
struck-through `— MOVED` stub behind; see the two existing examples in `tradeoffs.md`.

## Reporting Issues

- Use the [bug report](https://github.com/FreakyAli/Resilion/issues/new?template=bug_report.md) template for bugs
- Use the [feature request](https://github.com/FreakyAli/Resilion/issues/new?template=feature_request.md) template for enhancements
