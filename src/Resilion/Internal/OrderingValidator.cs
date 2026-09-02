namespace Resilion.Internal;

/// <summary>
/// Validates strategy ordering at Build() time and returns diagnostic warnings
/// for common misorderings. Warnings only — never blocks the build.
/// </summary>
internal static class OrderingValidator
{
    /// <summary>
    /// Validates the order of strategies and returns any warnings.
    /// </summary>
    internal static List<string> Validate(IReadOnlyList<StrategyType> strategies)
    {
        var warnings = new List<string>();

        if (strategies.Count < 2)
        {
            return warnings;
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

        // Rule 1: CircuitBreaker should be inside (after) Retry, not outside (before).
        // If CB is before Retry, retries bypass the breaker entirely.
        if (positions.TryGetValue(StrategyType.CircuitBreaker, out var cbPositions)
            && positions.TryGetValue(StrategyType.Retry, out var retryPositions))
        {
            foreach (var cbPos in cbPositions)
            {
                foreach (var retryPos in retryPositions)
                {
                    if (cbPos < retryPos)
                    {
                        warnings.Add(
                            "CircuitBreaker is outside Retry (position " + cbPos + " vs " + retryPos + "). " +
                            "This means retries bypass the circuit breaker. Usually CircuitBreaker should be " +
                            "inside (after) Retry so each attempt is tracked independently.");
                    }
                }
            }
        }

        // Rule 2: Fallback should typically be outermost (first) to catch all failures.
        if (positions.TryGetValue(StrategyType.Fallback, out var fallbackPositions))
        {
            foreach (var fbPos in fallbackPositions)
            {
                if (fbPos > 0)
                {
                    warnings.Add(
                        "Fallback is at position " + fbPos + ", not outermost (position 0). " +
                        "Fallback typically goes first so it catches failures from all inner strategies.");
                }
            }
        }

        // Rule 3: Hedging and Retry together — may cause excessive load.
        if (positions.ContainsKey(StrategyType.Hedging) && positions.ContainsKey(StrategyType.Retry))
        {
            warnings.Add(
                "Both Hedging and Retry are present. Hedging launches parallel attempts, and Retry " +
                "re-executes on failure. Together they can generate a large number of requests. " +
                "Verify this is intentional.");
        }

        // Rule 4: Multiple Timeout strategies without clear total/per-attempt separation.
        if (positions.TryGetValue(StrategyType.Timeout, out var timeoutPositions) && timeoutPositions.Count > 2)
        {
            warnings.Add(
                "More than 2 Timeout strategies detected. Typically you need at most 2: " +
                "one total timeout (outermost) and one per-attempt timeout (innermost).");
        }

        // Rule 5: Retry as outermost strategy with no Timeout outside it.
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

        return warnings;
    }
}
