using Xunit;

namespace Resilion.Tests;

public class ResilienceEventHandlerTests
{
    private readonly record struct TestEvent(string Message);

    [Fact]
    public async Task SyncHandler_InvokedViaInvokeAsync()
    {
        string? captured = null;
        Action<TestEvent> action = e => { captured = e.Message; };
        ResilienceEventHandler<TestEvent> handler = action;

        Assert.True(handler.HasHandler);
        await handler.InvokeAsync(new TestEvent("hello"));

        Assert.Equal("hello", captured);
    }

    [Fact]
    public async Task AsyncHandler_InvokedViaInvokeAsync()
    {
        string? captured = null;
        Func<TestEvent, ValueTask> func = async e =>
        {
            await Task.Yield();
            captured = e.Message;
        };
        ResilienceEventHandler<TestEvent> handler = func;

        Assert.True(handler.HasHandler);
        await handler.InvokeAsync(new TestEvent("async"));

        Assert.Equal("async", captured);
    }

    [Fact]
    public void SyncHandler_InvokedViaInvoke()
    {
        string? captured = null;
        Action<TestEvent> action = e => { captured = e.Message; };
        ResilienceEventHandler<TestEvent> handler = action;

        handler.Invoke(new TestEvent("sync"));

        Assert.Equal("sync", captured);
    }

    [Fact]
    public void DefaultHandler_HasNoHandler()
    {
        ResilienceEventHandler<TestEvent> handler = default;

        Assert.False(handler.HasHandler);
    }

    [Fact]
    public async Task DefaultHandler_InvokeAsync_DoesNotThrow()
    {
        ResilienceEventHandler<TestEvent> handler = default;

        // Should not throw
        await handler.InvokeAsync(new TestEvent("ignored"));
    }
}
