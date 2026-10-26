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
An async fallback factory is run via a thread-pool hop when a `SynchronizationContext` is present
— deadlock-safe, but sync-over-async. An async event handler avoids the hop: the context is
cleared for the duration of the call instead, so the handler starts on the calling thread. Sync
hedging is sequential-only and throws for parallel/latency modes. Sync delays — retry backoff and
injected chaos latency — bypass `TimeProvider`, so they aren't fake-clock testable. Custom
strategies must override `Execute` themselves. See the "Sync and Async" section of the
[README](../README.md) for the list.

**`RetryDelay` as a discriminated union.** Polly's retry options have `Delay`, `BackoffType`,
`UseJitter`, and `DelayGenerator` as independent properties — nothing stops you from setting
`DelayGenerator` and `BackoffType` at the same time with unclear precedence. Resilion's
`RetryDelay.Constant/Linear/Exponential/Custom` are mutually exclusive by construction. There's
exactly one way to configure the delay strategy, and the compiler enforces it.

**Callbacks take a lambda, a method group, or an async handler.** One property, three shapes:
`OnRetry = e => { log(e); return default; }`, `OnRetry = LogRetry`, or
`OnRetry = async e => await TrackAsync(e)`. This is the same shape Polly uses, and Resilion used
to differ: `ResilienceEventHandler<TArgs>` was a wrapper struct accepting an `Action<TArgs>`, so a
synchronous handler could skip the `return default`. It was dropped before 1.0 because a struct is
not a delegate type, so C# refused to convert a bare lambda to it at all — every callback had to be
written `new Action<RetryAttemptEvent>(e => ...)`. The wrapper was also documented as avoiding
`ValueTask` wrapping; measured, both shapes allocate 0 B/op for a synchronous handler, so it was
charging a compiler error for syntax rather than buying speed.

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
| Chaos engineering (Polly's Simmy) | `Resilion.Chaos` — fault, outcome, latency and behavior injection |

## Roadmap to close the gap

Every item above that Resilion doesn't have yet is tracked with a concrete design in
[future-plans.md](future-plans.md):

| Feature | Priority | Target | future-plans.md item |
|---------|----------|--------|----------------------|
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
