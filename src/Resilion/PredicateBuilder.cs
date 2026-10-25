using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Composes a result-aware predicate for a strategy's <c>ShouldHandle</c>, as an alternative to
/// writing the <see cref="Func{T, TResult}"/> by hand.
/// </summary>
/// <typeparam name="TResult">The pipeline's result type.</typeparam>
/// <remarks>
/// Clauses combine with OR: the built predicate handles an outcome if <em>any</em> clause matches.
/// <para>
/// This is additive sugar, never a second route to the default predicate. A strategy falls back to
/// "handle every exception except <see cref="OperationCanceledException"/>" only when its
/// <c>ShouldHandle</c> is <see langword="null"/>, and a builder always produces a non-null
/// delegate. An empty builder would blur that line, so <see cref="Build"/> throws rather than
/// quietly meaning "handle everything".
/// </para>
/// <para>
/// <strong>This does not inherit the default's cancellation carve-out.</strong>
/// <c>new PredicateBuilder&lt;T&gt;().Handle&lt;Exception&gt;()</c> matches
/// <see cref="OperationCanceledException"/>, because an explicit clause should mean what it says.
/// Exclude it yourself if you don't want retries on cancellation:
/// <c>.Handle&lt;Exception&gt;(ex =&gt; ex is not OperationCanceledException)</c>.
/// </para>
/// <para>
/// Unlike <see cref="PipelineBuilder"/> this is not single-use: it produces a value rather than
/// configuring one thing, so calling <see cref="Build"/> more than once is expected and each call
/// snapshots the clauses added so far.
/// </para>
/// </remarks>
public sealed class PredicateBuilder<TResult>
{
    private readonly List<Func<Outcome<TResult>, bool>> _clauses = [];

    /// <summary>Handles any outcome carrying a <typeparamref name="TException"/>.</summary>
    public PredicateBuilder<TResult> Handle<TException>()
        where TException : Exception
        => Handle<TException>(static _ => true);

    /// <summary>
    /// Handles any outcome carrying a <typeparamref name="TException"/> for which
    /// <paramref name="predicate"/> returns <see langword="true"/>.
    /// </summary>
    public PredicateBuilder<TResult> Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _clauses.Add(outcome => outcome.Exception is TException ex && predicate(ex));
        return this;
    }

    /// <summary>
    /// Handles any outcome whose exception, or any exception nested within it, is a
    /// <typeparamref name="TException"/>.
    /// </summary>
    public PredicateBuilder<TResult> HandleInner<TException>()
        where TException : Exception
        => HandleInner<TException>(static _ => true);

    /// <summary>
    /// Handles any outcome whose exception, or any exception nested within it, is a
    /// <typeparamref name="TException"/> matching <paramref name="predicate"/>.
    /// </summary>
    /// <remarks>
    /// Walks the <see cref="Exception.InnerException"/> chain and flattens
    /// <see cref="AggregateException"/>, so a fault wrapped by a library boundary is still matched.
    /// </remarks>
    public PredicateBuilder<TResult> HandleInner<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _clauses.Add(outcome => outcome.Exception is not null
            && PredicateBuilderInternals.MatchesInner(outcome.Exception, predicate));
        return this;
    }

    /// <summary>
    /// Handles any successful outcome whose result matches <paramref name="predicate"/>.
    /// </summary>
    /// <remarks>
    /// Evaluated only for successful outcomes, so the predicate never sees a default value
    /// standing in for a failure.
    /// </remarks>
    public PredicateBuilder<TResult> HandleResult(Func<TResult, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _clauses.Add(outcome => outcome.TryGetResult(out var result) && predicate(result));
        return this;
    }

    /// <summary>
    /// Handles any successful outcome whose result equals <paramref name="result"/>, compared with
    /// <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public PredicateBuilder<TResult> HandleResult(TResult result)
        => HandleResult(candidate => EqualityComparer<TResult>.Default.Equals(candidate, result));

    /// <summary>Builds the predicate.</summary>
    /// <exception cref="InvalidOperationException">No clause has been added.</exception>
    public Func<Outcome<TResult>, bool> Build()
    {
        if (_clauses.Count == 0)
        {
            throw new InvalidOperationException(
                "PredicateBuilder has no clauses, so the predicate it would build is meaningless. " +
                "Add at least one Handle/HandleInner/HandleResult clause, or leave ShouldHandle " +
                "null to use the default (handle every exception except OperationCanceledException).");
        }

        // Snapshot so the returned delegate is unaffected by later mutation of this builder.
        var clauses = _clauses.ToArray();

        if (clauses.Length == 1)
        {
            return clauses[0];
        }

        return outcome =>
        {
            foreach (var clause in clauses)
            {
                if (clause(outcome))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>Builds the predicate, so a builder can be assigned directly to <c>ShouldHandle</c>.</summary>
    /// <exception cref="InvalidOperationException">No clause has been added.</exception>
    public static implicit operator Func<Outcome<TResult>, bool>(PredicateBuilder<TResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build();
    }
}

/// <summary>
/// Composes an exception predicate for the <c>ShouldHandle</c> of a non-generic strategy's options,
/// which sees exceptions only and never a result.
/// </summary>
/// <remarks>
/// The same rules as <see cref="PredicateBuilder{TResult}"/> apply: clauses combine with OR,
/// <see cref="Build"/> throws on an empty builder, the cancellation carve-out is not inherited, and
/// reuse is expected.
/// </remarks>
public sealed class PredicateBuilder
{
    private readonly List<Func<Exception, bool>> _clauses = [];

    /// <summary>Handles any <typeparamref name="TException"/>.</summary>
    public PredicateBuilder Handle<TException>()
        where TException : Exception
        => Handle<TException>(static _ => true);

    /// <summary>
    /// Handles any <typeparamref name="TException"/> for which <paramref name="predicate"/> returns
    /// <see langword="true"/>.
    /// </summary>
    public PredicateBuilder Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _clauses.Add(exception => exception is TException ex && predicate(ex));
        return this;
    }

    /// <summary>
    /// Handles any exception which, or any exception nested within which, is a
    /// <typeparamref name="TException"/>.
    /// </summary>
    public PredicateBuilder HandleInner<TException>()
        where TException : Exception
        => HandleInner<TException>(static _ => true);

    /// <summary>
    /// Handles any exception which, or any exception nested within which, is a
    /// <typeparamref name="TException"/> matching <paramref name="predicate"/>.
    /// </summary>
    public PredicateBuilder HandleInner<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _clauses.Add(exception => PredicateBuilderInternals.MatchesInner(exception, predicate));
        return this;
    }

    /// <summary>Builds the predicate.</summary>
    /// <exception cref="InvalidOperationException">No clause has been added.</exception>
    public Func<Exception, bool> Build()
    {
        if (_clauses.Count == 0)
        {
            throw new InvalidOperationException(
                "PredicateBuilder has no clauses, so the predicate it would build is meaningless. " +
                "Add at least one Handle/HandleInner clause, or leave ShouldHandle null to use the " +
                "default (handle every exception except OperationCanceledException).");
        }

        var clauses = _clauses.ToArray();

        if (clauses.Length == 1)
        {
            return clauses[0];
        }

        return exception =>
        {
            foreach (var clause in clauses)
            {
                if (clause(exception))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>Builds the predicate, so a builder can be assigned directly to <c>ShouldHandle</c>.</summary>
    /// <exception cref="InvalidOperationException">No clause has been added.</exception>
    public static implicit operator Func<Exception, bool>(PredicateBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build();
    }
}
