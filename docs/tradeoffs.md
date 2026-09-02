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

**Why it's acceptable:** The cap is a heuristic, not a hard limit. Enforcing it exactly would require a lock on every return for zero practical benefit. Under burst traffic the pool might grow beyond the target, but this is transient and trades memory for lock-free concurrent access. The contexts are small (~200 bytes) and in steady state the pool converges to size. The alternative — `ConcurrentBag.Count` with exact enforcement — is worse because `Count` itself is expensive (see future-plans #25).

**Watch for:** Don't rely on the pool staying at or under its configured cap. It's a soft cap. Under sustained traffic, the pool size may exceed the target.

**Location:** [ResilienceContextPool.cs](../src/Resilion/ResilienceContextPool.cs) — `Return()`

---

## ~~Timeout cancellation classification has a narrow race window~~ — MOVED

This was a solvable issue, not a true tradeoff. Moved to [future-plans.md](future-plans.md) as **#46** (P2 fix).

---

## Per-call delegate allocation in pipeline chain

Each `StrategyComponent` in the chain creates a closure `ctx => _next.ExecuteAsync(callback, ctx)` per execution. N strategies = N closure allocations per call.

**Why it's acceptable:** This is inherent to the middleware/chain-of-responsibility pattern. Polly v8 has the same cost. The closures are small (one display class + one delegate), short-lived (Gen0 collected), and negligible relative to the I/O cost of the operations being protected. Pre-composing at build time would require complex generic threading that doesn't compose with the non-generic `Strategy` base.

**Watch for:** If benchmarks show this matters for your workload (unlikely unless you're running 100K+ pipeline executions/sec with no I/O), consider reducing pipeline depth or using `ExecuteOutcomeAsync` with a manually composed chain.

**Location:** [PipelineComponent.cs](../src/Resilion/Internal/PipelineComponent.cs) — `StrategyComponent.ExecuteAsync`
