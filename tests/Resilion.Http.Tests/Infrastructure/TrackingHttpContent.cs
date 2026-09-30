using System.Net;
using System.Net.Http;

namespace Resilion.Http.Tests;

/// <summary>
/// Content that counts how many times it was serialized, so request-replay behaviour can be
/// asserted rather than inferred.
/// </summary>
internal sealed class TrackingHttpContent : HttpContent
{
    private readonly byte[] _body;
    private int _serializations;

    internal TrackingHttpContent(string body) => _body = System.Text.Encoding.UTF8.GetBytes(body);

    internal int Serializations => Volatile.Read(ref _serializations);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        Interlocked.Increment(ref _serializations);
        return stream.WriteAsync(_body, 0, _body.Length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }
}

/// <summary>
/// Response content that records whether it was disposed, so leaked responses are detectable.
/// </summary>
internal sealed class DisposeTrackingContent : HttpContent
{
    private int _disposed;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => Task.CompletedTask;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Exchange(ref _disposed, 1);
        base.Dispose(disposing);
    }
}
