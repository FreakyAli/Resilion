# Contributing to Resilion

Thank you for your interest in contributing to Resilion!

## Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later

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

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Resilion.Tests

# Run with filter
dotnet test --filter "FullyQualifiedName~RetryTests"
```

### Running Benchmarks

Benchmarks are not part of the solution file. Run them directly:

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

## Reporting Issues

- Use the [bug report](https://github.com/FreakyAli/Resilion/issues/new?template=bug_report.md) template for bugs
- Use the [feature request](https://github.com/FreakyAli/Resilion/issues/new?template=feature_request.md) template for enhancements
