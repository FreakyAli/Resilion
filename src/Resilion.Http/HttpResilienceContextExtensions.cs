using System.Net.Http;
using Resilion.Http.Internal;

namespace Resilion.Http;

/// <summary>
/// <see cref="ResilienceContext"/> helpers for code running inside an HTTP resilience pipeline.
/// </summary>
public static class HttpResilienceContextExtensions
{
    /// <summary>
    /// Gets the <see cref="HttpRequestMessage"/> for the attempt currently executing, for use from
    /// a strategy callback such as <c>OnRetry</c>.
    /// </summary>
    /// <param name="context">The resilience context.</param>
    /// <returns>
    /// The current attempt's request — the clone, when
    /// <see cref="HttpRequestReplay.CloneRequest"/> is in effect — or <see langword="null"/> if the
    /// pipeline was not executed by a Resilion HTTP handler.
    /// </returns>
    public static HttpRequestMessage? GetRequestMessage(this ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Properties.TryGetValue(HttpResilienceKeys.RequestMessage, out var request)
            ? request
            : null;
    }
}
