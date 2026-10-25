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

**Sync callbacks don't pay for `ValueTask`.** Polly's callbacks (`OnRetry`, `OnOpened`, ...) always
return `ValueTask`, even for a callback that just increments a counter or writes a log line — the
90% case. Resilion's `ResilienceEventHandler<TArgs>` accepts a plain `Action<TArgs>` *or* an async
`Func<TArgs, ValueTask>` via implicit conversion, so the common synchronous case runs inline with
no wrapping.

Honest caveat on the ergonomics, as opposed to the cost: because `ResilienceEventHandler<TArgs>` is
a wrapper rather than a delegate type, you must name the delegate — `new Action<RetryAttemptEvent>(ctx => ...)`,
an inline cast, or a typed local. A bare lambda or a method group will not convert. Polly's
plain-delegate properties take a lambda directly, so Polly wins on brevity here while Resilion wins
on not allocating a `ValueTask` for a log line. Tracked for a future version — see
[future-plans.md](future-plans.md).

**Simpler API surface.** One static factory (`Pipeline.Create`), one builder per pipeline
kind, options classes that read the same way every time. No mandatory `PredicateBuilder` (there
is one if you want it, but a plain `Func<Outcome<T>, bool>` is always enough), no
`ResiliencePropertyKey` juggling beyond what you actually need. Less to learn before you're
productive.

**Always free.** No paid tier, no "enterprise edition," no plans to add one — see the README.

## Where Polly is better today

**HTTP specifics Polly's handler still does better.** Both libraries now give you
`AddStandardResilienceHandler()` on `IHttpClientBuilder` (see [http.md](http.md)), but Polly's
package additionally honours `Retry-After` on 429 and 503 responses, supports per-endpoint hedging
routing, works with synchronous `HttpClient.Send`, and binds its standard options from
`IConfiguration`. Resilion does none of those yet; `Retry-After` is the one most likely to matter,
since both libraries retry 429 by default and Resilion does so on blind exponential backoff.

**Chaos engineering.** Polly ships Simmy — fault, outcome, latency, and behavior injection for
resilience testing in non-production environments. Resilion has no equivalent.

**A dedicated testing package.** Polly has patterns and (community) packages for asserting on
pipeline behavior in tests. Resilion doesn't have a `Resilion.Testing` package yet — you write
tests the way this repo's own test suite does (see [docs/testing.md](testing.md)), which works
fine but isn't packaged up for you.

**Years of battle-testing and a large ecosystem.** Polly has been in production across a huge
number of .NET codebases since well before Resilion existed. If your organization needs that
track record as a prerequisite, Polly is the safer choice right now.

## Gaps that have closed

These were listed here as reasons to choose Polly and no longer are:

| Was | Now |
|-----|-----|
| `IHttpClientFactory` integration | `Resilion.Http` — see [http.md](http.md) |
| `PredicateBuilder<T>` fluent API | `PredicateBuilder<T>` and `PredicateBuilder` ship in core |
| Dynamic reload via `IOptionsMonitor` | `AddResiliencePipeline<TOptions>` rebuilds on options change |
| A way to observe circuit state | `CircuitBreakerStateProvider` |
| `IConfiguration` binding for strategy options | `Resilion.Extensions.Configuration` DTOs with `ToOptions()` |

## Roadmap to close the gap

Every item above that Resilion doesn't have yet is tracked with a concrete design in
[future-plans.md](future-plans.md):

| Feature | Priority | Target | future-plans.md item |
|---------|----------|--------|----------------------|
| `Resilion.Chaos` — chaos engineering | Medium | unscheduled | #40 |
| Telemetry enrichment | Lower | unscheduled | #45 |

**None of these ship in 1.0.** 1.0 is scoped to making what the library already claims actually
true — see the P0 section of [future-plans.md](future-plans.md). These are capabilities Resilion
*lacks*, which you can plan around; a 1.0 should not ship promises it doesn't keep.

Numbers refer to entries in [future-plans.md](future-plans.md). If a number here has no entry
there, that is a bug in the docs — see [verification.md](verification.md).

A `Resilion.Testing` package is **not** on this list any more. It was deferred rather than
scheduled: the two things it would actually have provided that nothing else could — a way to read
circuit state, and a public pipeline name — shipped in the core library instead, and the rest
(`FakeTimeProvider`, `ResilienceContextPool.Shared.Rent`, `MeterListener`) already works without a
package. See [testing.md](testing.md).

If one of these is a hard blocker for you today, Polly remains the right choice until Resilion
catches up. If it isn't, Resilion is a smaller, simpler, equally-capable core for everything
else — and it's free forever either way.
