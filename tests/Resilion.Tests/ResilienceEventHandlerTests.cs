using Xunit;

namespace Resilion.Tests;

public class ResilienceEventHandlerTests
{
    private readonly record struct TestEvent(string Message);

    private static string? _fromMethodGroup;

    private static ValueTask RecordIt(TestEvent e)
    {
        _fromMethodGroup = e.Message;
        return default;
    }

    // ──────────────────────────────────────────────────────────────────
    // The shapes that must compile — this is the whole point of the type
    // being a delegate rather than a wrapper struct (future-plans #62)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BareLambda_SynchronousBody_Converts()
    {
        // Previously CS1660: a lambda cannot convert to a non-delegate type, so every callback
        // had to be written as new Action<TestEvent>(e => ...).
        string? captured = null;
        ResilienceEventHandler<TestEvent> handler = e => { captured = e.Message; return default; };

        await handler(new TestEvent("hello"));

        Assert.Equal("hello", captured);
    }

    [Fact]
    public async Task BareLambda_AsyncBody_Converts()
    {
        string? captured = null;
        ResilienceEventHandler<TestEvent> handler = async e =>
        {
            await Task.Yield();
            captured = e.Message;
        };

        await handler(new TestEvent("async"));

        Assert.Equal("async", captured);
    }

    [Fact]
    public async Task MethodGroup_Converts()
    {
        // Previously CS0428.
        _fromMethodGroup = null;
        ResilienceEventHandler<TestEvent> handler = RecordIt;

        await handler(new TestEvent("method group"));

        Assert.Equal("method group", _fromMethodGroup);
    }

    // ──────────────────────────────────────────────────────────────────
    // InvokeSync — the synchronous execution path
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void InvokeSync_SynchronousHandler_RunsInlineOnTheCallingThread()
    {
        var callingThread = Environment.CurrentManagedThreadId;
        var ranOn = 0;
        ResilienceEventHandler<TestEvent> handler = _ =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            return default;
        };

        handler.InvokeSync(new TestEvent("sync"));

        Assert.Equal(callingThread, ranOn);
    }

    [Fact]
    public void InvokeSync_AsynchronousHandler_CompletesAndObservesResult()
    {
        string? captured = null;
        ResilienceEventHandler<TestEvent> handler = async e =>
        {
            await Task.Yield();
            captured = e.Message;
        };

        handler.InvokeSync(new TestEvent("awaited"));

        Assert.Equal("awaited", captured);
    }

    [Fact]
    public void InvokeSync_HandlerThrows_PropagatesSynchronously()
    {
        ResilienceEventHandler<TestEvent> handler = _ => throw new InvalidOperationException("boom");

        var ex = Assert.Throws<InvalidOperationException>(() => handler.InvokeSync(new TestEvent("x")));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void InvokeSync_AsyncHandlerThrows_PropagatesSynchronously()
    {
        ResilienceEventHandler<TestEvent> handler = async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        };

        var ex = Assert.Throws<InvalidOperationException>(() => handler.InvokeSync(new TestEvent("x")));
        Assert.Equal("async boom", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────
    // SynchronizationContext — the deadlock this path exists to avoid
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A context that never runs posted work. Any continuation that captures it is lost, so a
    /// handler whose continuation is posted here can never complete — which is exactly the
    /// deadlock a WPF or WinForms caller would hit.
    /// </summary>
    private sealed class DeadEndSynchronizationContext : SynchronizationContext
    {
        private int _posts;

        internal int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);

        public override void Send(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
    }

    private static (bool Completed, T? Result, Exception? Error) RunOnContext<T>(
        SynchronizationContext context,
        Func<T> body,
        TimeSpan timeout)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { result = body(); }
            catch (Exception ex) { error = ex; }
        })
        { IsBackground = true };

        thread.Start();
        return (thread.Join(timeout), result, error);
    }

    [Fact]
    public async Task InvokeSync_AsyncHandlerUnderSynchronizationContext_DoesNotDeadlock()
    {
        // Without clearing the context, the handler's continuation would be posted back to the
        // very thread blocked waiting for it, and this would never return.
        var context = new DeadEndSynchronizationContext();
        var captured = new TaskCompletionSource<string>();

        ResilienceEventHandler<TestEvent> handler = async e =>
        {
            await Task.Yield();
            captured.TrySetResult(e.Message);
        };

        var (completed, _, error) = RunOnContext(
            context,
            () => { handler.InvokeSync(new TestEvent("no deadlock")); return true; },
            TimeSpan.FromSeconds(10));

        Assert.True(completed, "InvokeSync deadlocked under a SynchronizationContext.");
        Assert.Null(error);

        // The thread has been joined, so InvokeSync has already returned and the handler it
        // waited on has finished; awaiting here just reads the value across the memory barrier.
        Assert.True(captured.Task.IsCompleted);
        Assert.Equal("no deadlock", await captured.Task);
    }

    [Fact]
    public void InvokeSync_SynchronousHandlerUnderSynchronizationContext_DoesNotHopThreads()
    {
        // The reason the context is cleared rather than the handler being pushed onto the thread
        // pool: a synchronous handler must not pay for a thread hop it never needed.
        var context = new DeadEndSynchronizationContext();
        var ranOn = 0;
        var invokedOn = 0;

        ResilienceEventHandler<TestEvent> handler = _ =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            return default;
        };

        var (completed, _, error) = RunOnContext(
            context,
            () =>
            {
                invokedOn = Environment.CurrentManagedThreadId;
                handler.InvokeSync(new TestEvent("inline"));
                return true;
            },
            TimeSpan.FromSeconds(10));

        Assert.True(completed);
        Assert.Null(error);
        Assert.Equal(invokedOn, ranOn);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public void InvokeSync_RestoresTheAmbientSynchronizationContext()
    {
        var context = new DeadEndSynchronizationContext();
        ResilienceEventHandler<TestEvent> handler = async _ => await Task.Yield();

        var (completed, same, error) = RunOnContext(
            context,
            () =>
            {
                handler.InvokeSync(new TestEvent("x"));
                return ReferenceEquals(SynchronizationContext.Current, context);
            },
            TimeSpan.FromSeconds(10));

        Assert.True(completed);
        Assert.Null(error);
        Assert.True(same, "InvokeSync must restore the caller's SynchronizationContext.");
    }

    // ──────────────────────────────────────────────────────────────────
    // Cost
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SynchronousHandler_AllocatesNothingPerInvocation()
    {
        // The claim that replaced the old wrapper struct: returning a completed ValueTask costs
        // no more than an Action would, because ValueTask is a struct.
        ResilienceEventHandler<TestEvent> handler = _ => default;
        var args = new TestEvent("x");

        handler.InvokeSync(args);   // JIT warm-up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            handler.InvokeSync(args);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
