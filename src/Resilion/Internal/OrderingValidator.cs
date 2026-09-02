namespace Resilion.Internal;

/// <summary>
/// The result of <see cref="OrderingValidator.Validate"/>: dangerous misorderings that are
/// (almost) always wrong, separated from situational ones that merely warrant a second look.
/// </summary>
/// <param name="Errors">
/// Misorderings that are essentially always a bug (CircuitBreaker outside Retry, Fallback not
/// outermost). Callers may choose to throw on these — see <c>PipelineBuilderBase.ThrowOnOrderingErrors</c>.
/// </param>
/// <param name="Warnings">
/// Situational misorderings (3+ Timeouts, Hedging + Retry together, Retry outermost with no
/// Timeout) that are sometimes intentional. Always advisory, never thrown.
/// </param>
internal readonly record struct OrderingValidationResult(List<string> Errors, List<string> Warnings)
{
    /// <summary>Convenience accessor for callers that don't need the error/warning distinction.</summary>
    internal IEnumerable<string> AllMessages => Errors.Concat(Warnings);
}

/// <summary>
/// Validates strategy ordering at Build() time and returns diagnostics for common misorderings.
/// </summary>
internal static class OrderingValidator
{
    /// <summary>
    /// Validates the order of strategies and returns any errors and warnings.
    /// </summary>
    internal static OrderingValidationResult Validate(IReadOnlyList<StrategyType> strategies)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (strategies.Count < 2)
        {
            return new OrderingValidationResult(errors, warnings);
        }

        // Build index maps for quick lookup.
        var positions = new Dictionary<StrategyType, List<int>>();
        for (var i = 0; i < strategies.Count; i++)
        {
            var type = strategies[i];
            if (!positions.TryGetValue(type, out var list))
            {
                list = [];
                positions[type] = list;
            }

            list.Add(i);
        }

        // Error 1: CircuitBreaker should be inside (after) Retry, not outside (before).
        // If CB is before Retry, retries bypass the breaker entirely — this is essentially
        // always a bug, never an intentional design choice.
        if (positions.TryGetValue(StrategyType.CircuitBreaker, out var cbPositions)
            && positions.TryGetValue(StrategyType.Retry, out var retryPositions))
        {
            foreach (var cbPos in cbPositions)
            {
                foreach (var retryPos in retryPositions)
                {
                    if (cbPos < retryPos)
                    {
                        errors.Add(
                            "CircuitBreaker is outside Retry (position " + cbPos + " vs " + retryPos + "). " +
                            "This means retries bypass the circuit breaker. CircuitBreaker must be " +
                            "inside (after) Retry so each attempt is tracked independently.");
                    }
                }
            }
        }

        // Error 2: Fallback should be outermost (first) to catch all failures — a Fallback
        // that isn't outermost fails to catch failures from strategies placed outside it.
        if (positions.TryGetValue(StrategyType.Fallback, out var fallbackPositions))
        {
            foreach (var fbPos in fallbackPositions)
            {
                if (fbPos > 0)
                {
                    errors.Add(
                        "Fallback is at position " + fbPos + ", not outermost (position 0). " +
                        "Fallback must go first so it catches failures from all inner strategies.");
                }
            }
        }

        // Warning 1: Hedging and Retry together — may cause excessive load. Sometimes intentional.
        if (positions.ContainsKey(StrategyType.Hedging) && positions.ContainsKey(StrategyType.Retry))
        {
            warnings.Add(
                "Both Hedging and Retry are present. Hedging launches parallel attempts, and Retry " +
                "re-executes on failure. Together they can generate a large number of requests. " +
                "Verify this is intentional.");
        }

        // Warning 2: Multiple Timeout strategies without clear total/per-attempt separation.
        if (positions.TryGetValue(StrategyType.Timeout, out var timeoutPositions) && timeoutPositions.Count > 2)
        {
            warnings.Add(
                "More than 2 Timeout strategies detected. Typically you need at most 2: " +
                "one total timeout (outermost) and one per-attempt timeout (innermost).");
        }

        // Warning 3: Retry as outermost strategy with no Timeout outside it.
        if (positions.TryGetValue(StrategyType.Retry, out var retryPos2))
        {
            foreach (var rPos in retryPos2)
            {
                if (rPos == 0 && !positions.ContainsKey(StrategyType.Timeout))
                {
                    warnings.Add(
                        "Retry is the outermost strategy with no Timeout. Without a total timeout, " +
                        "retries could run indefinitely if each attempt takes a long time.");
                }
            }
        }

        return new OrderingValidationResult(errors, warnings);
    }
}
