using System.Net;
using System.Net.Http;

namespace Resilion.Http.Tests;

/// <summary>
/// A primary handler standing in for the network: it records every request it is asked to send and
/// answers from a caller-supplied delegate.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _responder;
    private readonly List<HttpRequestMessage> _requests = [];
    private int _sends;

    internal StubHttpMessageHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder;

    /// <summary>Answers with the given status codes in order, repeating the last one.</summary>
    internal static StubHttpMessageHandler WithStatuses(params HttpStatusCode[] statuses)
        => new((_, attempt, _) => Task.FromResult(
            new HttpResponseMessage(statuses[Math.Min(attempt, statuses.Length - 1)])));

    internal int Sends => Volatile.Read(ref _sends);

    /// <summary>Every request instance the handler was asked to send, in order.</summary>
    internal IReadOnlyList<HttpRequestMessage> Requests
    {
        get { lock (_requests) { return _requests.ToList(); } }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _sends) - 1;
        lock (_requests) { _requests.Add(request); }
        return _responder(request, attempt, cancellationToken);
    }
}
