using System.Net.Http;

namespace Resilion.Http.Internal;

/// <summary>
/// Well-known <see cref="ResilienceContext"/> property keys used by the HTTP handlers.
/// </summary>
/// <remarks>
/// Internal on purpose: exposing the key would make its string a permanent part of the public API.
/// Callers reach the request through <see cref="HttpResilienceContextExtensions.GetRequestMessage"/>
/// instead.
/// </remarks>
internal static class HttpResilienceKeys
{
    internal static readonly ResiliencePropertyKey<HttpRequestMessage> RequestMessage =
        new("Resilion.Http.RequestMessage");
}
