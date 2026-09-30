using System.Diagnostics;
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
        Debug.Assert(
            body is not null || source.Content is null,
            "A request with content must be cloned with its buffered body; the handler buffers " +
            "before executing the pipeline.");

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
