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

**Real synchronous execution for every built-in strategy.** Polly's synchronous surface exists, but
under the hood several code paths still route through `Task`/`ValueTask` machinery. Resilion's
`Execute()` path uses real synchronous primitives — `Thread.Sleep`, `WaitHandle` — in every
built-in strategy. This matters for ASP.NET Framework, WinForms/WPF UI threads, and any code that
can't safely call `.GetAwaiter().GetResult()` on an async path without risking deadlock.

Stated precisely, because this is the reason some people choose Resilion and they deserve the
exact shape of it: the *strategies* are synchronous, but async work you hand them is still async.
An async fallback factory or an async event handler is run via a thread-pool hop when a
`SynchronizationContext` is present — deadlock-safe, but sync-over-async. Sync hedging is
sequential-only and throws for parallel/latency modes. Sync retry delays bypass `TimeProvider`, so
they aren't fake-clock testable. Custom strategies must override `Execute` themselves. See the
"Sync and Async" section of the [README](../README.md) for the list.

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

| Feature | Priority | Target | future-plans.md item |
|---------|----------|--------|----------------------|
| `PredicateBuilder<T>` fluent API | High | post-1.0 | #43 |
| Dynamic reload via `IOptionsMonitor` | High | post-1.0 | #41 |
| `Resilion.Http` — `IHttpClientFactory` integration | Highest | post-1.0 | #39 |
| `Resilion.Chaos` — chaos engineering | Medium | unscheduled | #40 |
| `IConfiguration` binding for strategy options | Lower | unscheduled | #44 |
| Telemetry enrichment | Lower | unscheduled | #45 |
| `Resilion.Testing` — test doubles, assertions | Lower | deferred | #10 |

**None of these ship in 1.0.** 1.0 is scoped to making what the library already claims actually
true — see the P0 section of [future-plans.md](future-plans.md). These are capabilities Resilion
*lacks*, which you can plan around; a 1.0 should not ship promises it doesn't keep.

Numbers refer to entries in [future-plans.md](future-plans.md). If a number here has no entry
there, that is a bug in the docs — see [verification.md](verification.md).

If one of these is a hard blocker for you today, Polly remains the right choice until Resilion
catches up. If it isn't, Resilion is a smaller, simpler, equally-capable core for everything
else — and it's free forever either way.
