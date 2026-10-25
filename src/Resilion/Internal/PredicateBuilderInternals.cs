namespace Resilion.Internal;

/// <summary>
/// Exception-chain walking shared by both <c>PredicateBuilder</c> variants, so the two cannot
/// disagree about what "inner" means.
/// </summary>
internal static class PredicateBuilderInternals
{
    /// <summary>
    /// Determines whether <paramref name="exception"/>, or any exception nested within it, is a
    /// <typeparamref name="TException"/> matching <paramref name="predicate"/>.
    /// </summary>
    /// <remarks>
    /// Walks <see cref="Exception.InnerException"/> and flattens
    /// <see cref="AggregateException.InnerExceptions"/>. Depth is bounded to avoid spinning on a
    /// self-referencing chain, which is malformed but observable in the wild.
    /// </remarks>
    internal static bool MatchesInner<TException>(Exception exception, Func<TException, bool> predicate)
        where TException : Exception
        => MatchesInner(exception, predicate, depth: 0);

    private const int MaxDepth = 32;

    private static bool MatchesInner<TException>(Exception? exception, Func<TException, bool> predicate, int depth)
        where TException : Exception
    {
        if (exception is null || depth > MaxDepth)
        {
            return false;
        }

        if (exception is TException match && predicate(match))
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (MatchesInner(inner, predicate, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchesInner(exception.InnerException, predicate, depth + 1);
    }
}
