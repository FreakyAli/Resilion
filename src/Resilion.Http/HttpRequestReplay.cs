namespace Resilion.Http;

/// <summary>
/// Controls whether each attempt sends the caller's <c>HttpRequestMessage</c> or an independent
/// copy of it.
/// </summary>
public enum HttpRequestReplay
{
    /// <summary>
    /// Every attempt sends the caller's original request instance.
    /// </summary>
    /// <remarks>
    /// Zero overhead, and what <c>Microsoft.Extensions.Http.Resilience</c> does. Safe only for
    /// <em>replayable</em> content — <c>StringContent</c>, <c>ByteArrayContent</c>,
    /// <c>JsonContent</c>, <c>FormUrlEncodedContent</c>, or no content at all. A
    /// <c>StreamContent</c> over a non-seekable stream cannot be sent twice, and an inner handler
    /// that <em>adds</em> rather than sets a header (some auth handlers do) will throw on the
    /// second attempt. Use <see cref="CloneRequest"/> in either case.
    /// </remarks>
    ReuseRequest = 0,

    /// <summary>
    /// The request body is buffered once up front and every attempt sends an independent clone.
    /// </summary>
    /// <remarks>
    /// Required whenever attempts can overlap, and the fix for non-replayable content or
    /// header-mutating inner handlers. It buffers the <em>entire</em> body in memory, so do not use
    /// it for large or streaming uploads.
    /// </remarks>
    CloneRequest = 1,
}
