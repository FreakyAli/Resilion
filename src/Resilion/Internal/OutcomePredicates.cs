namespace Resilion.Internal;

/// <summary>
/// Shared default predicates for strategy options. Extracted so the "handle everything except
/// cancellation" default isn't copy-pasted across every typed options class.
/// </summary>
internal static class OutcomePredicates
{
    /// <summary>
    /// The default "should handle" predicate used when a strategy's <c>ShouldHandle</c> is not
    /// set: treats any outcome carrying an exception as a failure, except
    /// <see cref="OperationCanceledException"/>, which is never handled by default.
    /// </summary>
    internal static bool DefaultShouldHandle<TResult>(Outcome<TResult> outcome)
        => outcome.Exception is not null and not OperationCanceledException;
}
