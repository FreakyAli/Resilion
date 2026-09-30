# Known Tradeoffs

Accepted design imperfections where the fix is worse than the problem, or the issue is theoretical rather than practical. Each entry explains what the tradeoff is, why it's acceptable, and what to watch for.

For items we *plan to fix*, see [future-plans.md](future-plans.md).

---

## Null implicit conversion creates "successful" null outcome

`Outcome<string> o = (string)null;` creates a success outcome with a null result.

**Why it's acceptable:** Consistent with `Task.FromResult<string>(null)` — a completed task with null is valid in .NET. For non-nullable value types this can't happen. For reference types, null is a legal value.

**Watch for:** User code that assumes `outcome.IsSuccess` means `outcome.Result` is non-null. Use `TryGetResult` and null-check the result.

**Location:** [Outcome.cs](../src/Resilion/Outcome.cs) — `implicit operator`

---

## DelegatingComponent does not dispose composed pipeline resources

When pipelines are composed via `AddPipeline()`, the `DelegatingComponent` does not dispose the inner component.

**Why it's acceptable:** The inner pipeline may be shared — the same pre-built `Pipeline` can be flattened into multiple builders. Disposing the inner would break all other compositions sharing it. Ownership belongs to whoever created the original pipeline.

**Watch for:** If you compose pipelines, dispose the source pipelines separately when they're no longer needed. The composed pipeline's `Dispose()` only disposes strategies it directly owns.

**Location:** [PipelineBuilder.cs](../src/Resilion/PipelineBuilder.cs) — `DelegatingComponent.Dispose()`

---

## ResilienceContextPool cap is approximate

The cap check (`Interlocked.Increment(ref _count) <= _maxPoolSize`) followed by `_pool.Add(context)` is a TOCTOU race — concurrent `Return()` calls can overshoot `_maxPoolSize` unboundedly. The excess contexts are held in the `ConcurrentBag` and are never collected — `Rent` via `_pool.TryTake` is the only removal path.

`_maxPoolSize` defaults to 256 but is configurable via `new ResilienceContextPool(maxPoolSize)` — the shared `ResilienceContextPool.Shared` instance always uses the default.

**Why it's acceptable:** The cap is a heuristic, not a hard limit. Enforcing it exactly would require a lock on every return for zero practical benefit. Under burst traffic the pool might grow beyond the target, but this is transient and trades memory for lock-free concurrent access. The contexts are small (~200 bytes) and in steady state the pool converges to size. The alternative — `ConcurrentBag.Count` with exact enforcement — is worse because `Count` itself is expensive (it enumerates every thread-local queue).

**Watch for:** Don't rely on the pool staying at or under its configured cap. It's a soft cap. Under sustained traffic, the pool size may exceed the target.

**Location:** [ResilienceContextPool.cs](../src/Resilion/ResilienceContextPool.cs) — `Return()`

---

## ~~Timeout cancellation classification has a narrow race window~~ — MOVED

This was a solvable issue, not a true tradeoff. Moved to [future-plans.md](future-plans.md) as **#46** (P2 fix).

---

## Timeout pays an allocation for race-free cancellation classification

Each Timeout execution allocates a small object recording *which* cause cancelled its linked token
first, and — when the incoming token can be cancelled — one `CancellationToken` registration.

Measured on an Apple M4 Pro, `ShortRun`, `net8.0`:

| Shape | Before | After |
|-------|-------:|------:|
| Timeout alone (non-cancellable caller token) | 360 B | 392 B |
| Timeout → Retry → CB → Timeout | 976 B | 1120 B |

The composite grows by 144 B rather than 64 B because the outer Timeout replaces the context token
with its own linked token, so the **inner** Timeout sees a cancellable token and takes the
registration as well. The canonical recommended pipeline has exactly that shape.

**Why it's acceptable:** the alternative is what shipped before — reading
`linkedCts.IsCancellationRequested && !userToken.IsCancellationRequested` as two separate reads, so
a user cancellation landing between them is reported as `TimeoutRejectedException` instead of
`OperationCanceledException`. Callers branch on that distinction to decide whether to retry, so
getting it wrong turns a deliberate shutdown into a retry storm. 144 B on a path that is protecting
real I/O is a good trade for an exception type that is always correct.

**Watch for:** if you run a timeout-heavy pipeline at very high throughput with no I/O, this is the
one allocation added in 1.0.

One alternative is *worth measuring* but is not known to be cheaper: cancel a dedicated timer-only
`CancellationTokenSource` and link *that* together with the caller's token, so "was it the timeout?"
becomes a single read of a source only the timer can touch. That removes this object and the
registration, but `CreateLinkedTokenSource` over two tokens registers on both, so it trades one
registration for two plus an extra source. Benchmark it before assuming it wins.

**Location:** [TimeoutStrategy.cs](../src/Resilion/Timeout/TimeoutStrategy.cs) — `CancellationCause`

---

## ~~Per-call delegate allocation in pipeline chain~~ — MOVED

This was tracked simultaneously here as an accepted imperfection *and* in
[future-plans.md](future-plans.md) as an open improvement, so the repo claimed both that the fix was
worse than the problem and that the fix was planned. It now lives in one place only:
[future-plans.md](future-plans.md) as **#4** (P2, unscheduled), which records the benchmark that
would settle it and the decision rule for closing it as "measured, won't fix".

The short version, for anyone who arrived here from a link: each `StrategyComponent` creates a
closure `ctx => _next.ExecuteAsync(callback, ctx)` per execution, so N strategies means N small
closure allocations per call. This is inherent to the middleware pattern and Polly v8 has the same
cost. If you think it matters for your workload — unlikely below 100K+ executions/sec with no I/O —
reduce pipeline depth or use `ExecuteOutcomeAsync` with a manually composed chain.

**Location:** [PipelineComponent.cs](../src/Resilion/Internal/PipelineComponent.cs) — `StrategyComponent.ExecuteAsync`
