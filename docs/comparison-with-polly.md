# Why Resilion over Polly?

Polly is great software that moved the .NET ecosystem forward. Years of production use, a huge
ecosystem, and a large team behind it. If you're evaluating alternatives, you deserve an honest
answer to "why would I pick the newer, smaller library?" — not a sales pitch. Here it is.

## Where Resilion is better today

**Zero dependencies in the core package.** `Resilion` pulls in nothing. `Polly.Core` is also
lean, but the moment you want DI integration or HTTP resilience, you're pulling in
`Microsoft.Extensions.*` packages Polly itself depends on. Resilion's `Resilion.Extensions` and
`Resilion.RateLimiting` are opt-in, separate packages — the core stays dependency-free no matter
what you add.

**True synchronous execution, not sync-over-async.** Polly's synchronous surface exists, but
under the hood several code paths still route through `Task`/`ValueTask` machinery.
Resilion's `Execute()` path uses real synchronous primitives — `Thread.Sleep`, `WaitHandle` — for
every built-in strategy. This matters for ASP.NET Framework, WinForms/WPF UI threads, and any
code that can't safely call `.GetAwaiter().GetResult()` on an async path without risking deadlock.

**`RetryDelay` as a discriminated union.** Polly's retry options have `Delay`, `BackoffType`,
`UseJitter`, and `DelayGenerator` as independent properties — nothing stops you from setting
`DelayGenerator` and `BackoffType` at the same time with unclear precedence. Resilion's
`RetryDelay.Constant/Linear/Exponential/Custom` are mutually exclusive by construction. There's
exactly one way to configure the delay strategy, and the compiler enforces it.

**Sync callback ergonomics.** Polly's callbacks (`OnRetry`, `OnOpened`, ...) always return
`ValueTask`, even for a callback that just increments a counter or writes a log line — the 90%
case. Resilion's `ResilienceEventHandler<TArgs>` accepts a plain `Action<TArgs>` *or* an async
`Func<TArgs, ValueTask>` via implicit conversion, so the common synchronous case pays nothing
for `ValueTask` wrapping.

**Simpler API surface.** One static factory (`Pipeline.Create`), one builder per pipeline
kind, options classes that read the same way every time. No `PredicateBuilder`, no
`ResiliencePropertyKey` juggling beyond what you actually need. Less to learn before you're
productive.

**Always free.** No paid tier, no "enterprise edition," no plans to add one — see the README.

## Where Polly is better today

**`IHttpClientFactory` integration.** Polly's `Microsoft.Extensions.Http.Resilience` package
gives you `AddStandardResilienceHandler()` — a pre-configured 5-strategy pipeline wired
directly into `HttpClient` with one line. This is the single most common resilience use case,
and Resilion doesn't have an equivalent yet. If you need this today, use Polly (or wrap
`Resilion` in your own `DelegatingHandler` in the meantime).

**Chaos engineering.** Polly ships Simmy — fault, outcome, latency, and behavior injection for
resilience testing in non-production environments. Resilion has no equivalent.

**Dynamic reload.** Polly pipelines can auto-recreate when bound `IOptionsMonitor<T>` options
change. Resilion's pipelines are immutable once built; changing configuration means building a
new one and swapping it in yourself.

**A dedicated testing package.** Polly has patterns and (community) packages for asserting on
pipeline behavior in tests. Resilion doesn't have a `Resilion.Testing` package yet — you write
tests the way this repo's own test suite does (see [docs/testing.md](testing.md)), which works
fine but isn't packaged up for you.

**Years of battle-testing and a large ecosystem.** Polly has been in production across a huge
number of .NET codebases since well before Resilion existed. If your organization needs that
track record as a prerequisite, Polly is the safer choice right now.

**`PredicateBuilder<T>` fluent API.** Polly's `new PredicateBuilder<T>().Handle<TException>().HandleResult(...)`
composes predicates without writing the lambda by hand. Resilion requires the full
`Func<Outcome<T>, bool>` — more explicit, more verbose for complex predicates.

## Roadmap to close the gap

Every item above that Resilion doesn't have yet is tracked with a concrete design in
[future-plans.md](future-plans.md):

| Feature | Priority | future-plans.md item |
|---------|----------|----------------------|
| `Resilion.Http` — `IHttpClientFactory` integration | Highest | #39 |
| `Resilion.Testing` — test doubles, assertions | High | #10 |
| Dynamic reload via `IOptionsMonitor` | Medium | #41 |
| `Resilion.Chaos` — chaos engineering | Medium | #40 |
| `PredicateBuilder<T>` fluent API | Lower | #43 |
| `IConfiguration` binding for strategy options | Lower | #44 |
| Telemetry enrichment | Lower | #45 |

If one of these is a hard blocker for you today, Polly remains the right choice until Resilion
catches up. If it isn't, Resilion is a smaller, simpler, equally-capable core for everything
else — and it's free forever either way.
