# Security Policy

## Supported Versions

Security fixes are applied to the current minor release and the one before it. Pre-release versions
are not supported — if you are on a `-pre` build, upgrade to the latest stable release first.

| Version | Supported |
|---------|-----------|
| 1.0.x   | ✅ |
| < 1.0   | ❌ (pre-release; upgrade to 1.0.x) |

## Reporting a Vulnerability

**Please do not open a public issue for a security vulnerability.** A public report tells everyone
about the problem, including people who would exploit it, before a fix exists.

Instead, report it privately through GitHub's security advisories:

**https://github.com/FreakyAli/Resilion/security/advisories/new**

That creates a private thread visible only to you and the maintainers.

Please include, as far as you can:

- The affected package and version (`Resilion`, `Resilion.Extensions`, `Resilion.RateLimiting`).
- What an attacker can achieve — the impact, not just the mechanism.
- A minimal reproduction: the pipeline configuration and the calling pattern that triggers it.
- Whether it is already public anywhere.

## What to Expect

| Stage | Target |
|-------|--------|
| Acknowledgement that we've seen the report | 3 days |
| Initial assessment, with a severity and a plan | 7 days |
| Fix released for a critical issue | 7 days from assessment |
| Fix released for a lower-severity issue | next scheduled release |

We will keep you updated in the advisory thread, credit you in the release notes and the advisory
unless you'd rather stay anonymous, and publish the advisory once a fixed version is available.

If you don't hear anything within 7 days, please bump the advisory thread — it means the
notification was missed, not that the report was dismissed.

## Scope

Resilion is a library: it runs inside your process, has no network listeners of its own, and the
core package has no external dependencies. In practice that makes the plausible issue classes:

- A resilience strategy failing to enforce its own contract in a way an attacker can drive — for
  example a rate limiter or circuit breaker that can be made not to trip.
- Unbounded resource growth reachable from untrusted input (memory, threads, timers, sockets).
- Sensitive data leaking into telemetry tags, spans, or exception messages.
- A denial of service achievable through configuration a caller controls.

Reports that amount to "you can configure this library badly" are not vulnerabilities, but we do
want to hear about them as ordinary issues if the bad configuration is easy to reach by accident or
the documentation encourages it.
