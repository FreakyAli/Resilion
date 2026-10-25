using System.Net.Http;

namespace Resilion.Http.Internal;

/// <summary>
/// Produces an independent copy of an <see cref="HttpRequestMessage"/> so each attempt can send its
/// own.
/// </summary>
internal static class HttpRequestMessageCloner
{
    /// <summary>
    /// Clones <paramref name="source"/>, giving the copy its own content built from
    /// <paramref name="body"/>.
    /// </summary>
    /// <param name="source">The request to copy.</param>
    /// <param name="body">
    /// The body bytes captured once up front, or <see langword="null"/> when the source has no
    /// content. Sharing one array across concurrent <see cref="ByteArrayContent"/> instances is
    /// safe: it carries no mutable position.
    /// </param>
    internal static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
    {
        // A real check, not Debug.Assert. Violating this silently drops the caller's request body,
        // and Debug.Assert is compiled out of the Release build that ships — which is exactly how
        // an earlier version of the handler shipped a body-dropping retry past a Release test run.
        if (body is null && source.Content is not null)
        {
            throw new InvalidOperationException(
                "Cannot clone a request with content without its buffered body. This is a bug in " +
                "Resilion.Http: the handler must buffer the body before executing the pipeline " +
                "whenever HttpRequestReplay.CloneRequest is in effect.");
        }

        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        // TryAddWithoutValidation: these values already passed validation on the source, and
        // re-validating can reject headers a server legitimately sent us.
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)source.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        if (body is not null)
        {
            var content = new ByteArrayContent(body);

            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = content;
        }

        return clone;
    }
}
