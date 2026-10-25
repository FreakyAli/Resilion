using System.Net;
using System.Net.Http;

namespace Resilion.Http;

/// <summary>
/// The default "is this worth retrying?" predicates for HTTP, matching what
/// <c>Microsoft.Extensions.Http.Resilience</c> treats as transient.
/// </summary>
/// <remarks>
/// Substituted automatically wherever a standard handler's <c>ShouldHandle</c> is left
/// <see langword="null"/>, and exposed publicly so a custom pipeline can compose against the same
/// definition instead of re-deriving it.
/// </remarks>
public static class HttpResiliencePredicates
{
    /// <summary>
    /// Determines whether an outcome represents a transient HTTP failure — either a transient
    /// exception, or a successful call that returned a transient status code.
    /// </summary>
    public static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
        {
            return IsTransientException(outcome.Exception);
        }

        return outcome.TryGetResult(out var response)
            && response is not null
            && IsTransientStatusCode(response.StatusCode);
    }

    /// <summary>
    /// Determines whether a status code is transient: any 5xx, plus 408 Request Timeout and
    /// 429 Too Many Requests.
    /// </summary>
    /// <remarks>
    /// 429 is included to match Polly's default, but note that Resilion cannot yet honour a
    /// <c>Retry-After</c> header, so a 429 is retried on blind exponential backoff. If the server
    /// publishes a retry window, prefer handling 429 yourself.
    /// </remarks>
    public static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => (int)statusCode >= 500
            || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Determines whether an exception represents a transient HTTP failure.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationCanceledException"/> is never transient, and is checked first:
    /// cancellation is a deliberate instruction, and retrying it would defeat the caller's token.
    /// A per-attempt timeout surfaces as <see cref="TimeoutRejectedException"/> — which is not an
    /// <see cref="OperationCanceledException"/> — so timeouts remain retryable while cancellation
    /// does not.
    /// </remarks>
    public static bool IsTransientException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return false;
        }

        return exception is HttpRequestException or TimeoutRejectedException;
    }
}
