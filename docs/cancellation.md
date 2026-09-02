# Cancellation

How `CancellationToken` flows through Resilion pipelines and how each strategy interacts with it.

## The rule

**If the user's original `CancellationToken` is canceled after passing pre-execution strategy gates (e.g., circuit breaker state check), `OperationCanceledException` propagates immediately. It is never retried, never counted as a circuit breaker failure, never caught by any strategy by default. However, strategies that reject before execution (e.g., open circuit breaker) return their own exceptions (e.g., `CircuitBrokenException`) instead.**

This is the single most important cancellation guarantee for operations that reach the inner callback.

## Token flow

```text
User provides CancellationToken
  │
  Pipeline sets context.CancellationToken = userToken
  │
  [TotalTimeout] creates linkedCTS(userToken + 30s timer)
  │              replaces context.CancellationToken = linkedCTS.Token
  │
  [Retry] checks token before each attempt — does NOT modify token
  │
  [CircuitBreaker] checks state — does NOT modify token
  │
  [AttemptTimeout] creates attemptCTS(context.Token + 5s timer)
  │                replaces context.CancellationToken = attemptCTS.Token
  │
  [UserCallback] receives the most restrictive token
                 (linked to both total and attempt timeouts)
```

Each Timeout strategy creates a linked `CancellationTokenSource`. The inner delegate always gets the most restrictive token. After each strategy completes, it restores the original token.

## Per-strategy behavior

### Retry
- Checks `CancellationToken.IsCancellationRequested` before each attempt
- Checks cancellation during delay waits
- `OperationCanceledException` is never retried by default
- Cancelling during delay stops retries immediately

### Timeout
- Creates linked CTS combining user token + timer
- Timeout fires: catches delegate's `OperationCanceledException`, wraps in `TimeoutRejectedException`
- User cancels: `OperationCanceledException` propagates unchanged (not wrapped)
- Linked CTS disposed in `finally`

### Circuit Breaker
- Does not modify the token
- `OperationCanceledException` is never counted as a failure (not a dependency fault)
- `CircuitBrokenException` takes priority over cancellation (if circuit is open, reject immediately)

### Fallback
- Does not modify the token
- `OperationCanceledException` is not handled by default (propagates, no fallback)
- Can be configured to catch OCE via custom `ShouldHandle` (unusual)

### Rate Limiter
- Passes token to `RateLimiter.AcquireAsync` (cancels queue wait)
- Does not modify the token passed to the inner delegate

### Hedging
- Each attempt gets its own linked CTS
- When one attempt wins, all other CTS instances cancel
- Cancelled tasks are given a bounded cleanup wait of 5 seconds; tasks that do not cooperate with cancellation may continue running
- User cancellation cancels all attempts simultaneously

## Timeout vs user cancellation

The Timeout strategy distinguishes the two by checking which token fired:

| User token cancelled? | Linked CTS cancelled? | Result |
|----------------------|----------------------|--------|
| No | Yes | `TimeoutRejectedException` |
| Yes | Yes (linked) | `OperationCanceledException` |
| No | No | Success (completed in time) |

There is a narrow TOCTOU race where user cancellation between checks could be misclassified as timeout. This is nanoseconds wide and not observable in practice. Tracked as a fix in [future-plans.md](future-plans.md#46-timeout-cancellation-toctou-race-fix).

## Cooperative cancellation

Resilion timeouts are cooperative. The delegate MUST observe the `CancellationToken` for timeout to work. If the delegate ignores the token, timeout cannot abort it.

```csharp
// GOOD — observes token
await pipeline.ExecuteAsync(async ct =>
    await httpClient.GetStringAsync(url, ct));  // HttpClient checks ct

// BAD — ignores token, timeout has no effect
await pipeline.ExecuteAsync(async ct =>
    await SomeBlockingCallThatIgnoresCancellation());
```

There is no "pessimistic timeout" that abandons the operation on a background thread. Pessimistic timeout leaks resources and creates unpredictable state.
