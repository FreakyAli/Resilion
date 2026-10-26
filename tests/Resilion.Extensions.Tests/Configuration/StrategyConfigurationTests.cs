using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resilion.Extensions.Configuration;
using Xunit;

namespace Resilion.Extensions.Tests;

/// <summary>
/// The bindable DTO layer — future-plans #44.
/// </summary>
public class RetryDelayConfigurationTests
{
    [Fact]
    public void DefaultKind_IsExponential()
        => Assert.Equal(RetryDelayKind.Exponential, new RetryDelayConfiguration().Kind);

    [Theory]
    [InlineData(RetryDelayKind.Constant)]
    [InlineData(RetryDelayKind.Linear)]
    [InlineData(RetryDelayKind.Exponential)]
    public void ToRetryDelay_EveryKind_ProducesADelay(RetryDelayKind kind)
    {
        var config = new RetryDelayConfiguration { Kind = kind, BaseDelay = TimeSpan.FromSeconds(2) };

        Assert.NotNull(config.ToRetryDelay());
    }

    [Theory]
    [InlineData(RetryDelayKind.Constant)]
    [InlineData(RetryDelayKind.Linear)]
    [InlineData(RetryDelayKind.Exponential)]
    public void ToRetryDelay_ProjectsToTheMatchingFactory(RetryDelayKind kind)
    {
        // RetryDelay is a record, so value equality compares the variant and its parameters —
        // enough to prove the projection without reaching into the internal ComputeDelay.
        var baseDelay = TimeSpan.FromSeconds(3);
        var fromConfig = new RetryDelayConfiguration { Kind = kind, BaseDelay = baseDelay }.ToRetryDelay();

        var fromFactory = kind switch
        {
            RetryDelayKind.Constant => RetryDelay.Constant(baseDelay),
            RetryDelayKind.Linear => RetryDelay.Linear(baseDelay),
            _ => RetryDelay.Exponential(baseDelay),
        };

        Assert.Equal(fromFactory, fromConfig);
    }

    [Fact]
    public void ToRetryDelay_DifferentKinds_AreNotEqual()
    {
        var baseDelay = TimeSpan.FromSeconds(3);

        Assert.NotEqual(
            new RetryDelayConfiguration { Kind = RetryDelayKind.Linear, BaseDelay = baseDelay }.ToRetryDelay(),
            new RetryDelayConfiguration { Kind = RetryDelayKind.Exponential, BaseDelay = baseDelay }.ToRetryDelay());
    }

    [Fact]
    public void RetryDelayKind_HasNoCustomMember()
    {
        // Custom takes a delegate, which cannot come from configuration. Offering it would mean a
        // bindable value that silently does nothing.
        Assert.DoesNotContain("Custom", Enum.GetNames<RetryDelayKind>());
    }

    [Fact]
    public void ToRetryDelay_UnknownKind_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => new RetryDelayConfiguration { Kind = (RetryDelayKind)99 }.ToRetryDelay());
}

/// <summary>
/// Binding nothing must be identical to passing no options. Drift here is a silent behaviour
/// change, which is the whole reason these exist.
/// </summary>
public class StrategyConfigurationDefaultsTests
{
    [Fact]
    public void Retry_DefaultsMatchTheOptionsRecord()
    {
        var fromConfig = new RetryStrategyConfiguration().ToOptions();
        var fromRecord = new RetryStrategyOptions();

        Assert.Equal(fromRecord.MaxRetryAttempts, fromConfig.MaxRetryAttempts);
        Assert.Equal(fromRecord.UseJitter, fromConfig.UseJitter);
        Assert.Equal(fromRecord.MaxDelay, fromConfig.MaxDelay);
        Assert.Equal(fromRecord.Delay, fromConfig.Delay);
    }

    [Fact]
    public void Timeout_DefaultsMatchTheOptionsRecord()
        => Assert.Equal(
            new TimeoutStrategyOptions().Timeout,
            new TimeoutStrategyConfiguration().ToOptions().Timeout);

    [Fact]
    public void CircuitBreaker_DefaultsMatchTheOptionsRecord()
    {
        var fromConfig = new CircuitBreakerStrategyConfiguration().ToOptions();
        var fromRecord = new CircuitBreakerStrategyOptions();

        Assert.Equal(fromRecord.FailureRatioThreshold, fromConfig.FailureRatioThreshold);
        Assert.Equal(fromRecord.SamplingDuration, fromConfig.SamplingDuration);
        Assert.Equal(fromRecord.MinimumThroughput, fromConfig.MinimumThroughput);
        Assert.Equal(fromRecord.BreakDuration, fromConfig.BreakDuration);
    }

    [Fact]
    public void Hedging_DefaultsMatchTheOptionsRecord()
    {
        var fromConfig = new HedgingStrategyConfiguration().ToOptions<string>();
        var fromRecord = new HedgingStrategyOptions<string>();

        Assert.Equal(fromRecord.MaxHedgedAttempts, fromConfig.MaxHedgedAttempts);
        Assert.Equal(fromRecord.HedgingDelay, fromConfig.HedgingDelay);
    }

    [Fact]
    public void ToOptions_LeavesDelegatesUnset_SoStrategyDefaultsApply()
    {
        Assert.Null(new RetryStrategyConfiguration().ToOptions().ShouldHandle);
        Assert.Null(new RetryStrategyConfiguration().ToOptions().OnRetry);
        Assert.Null(new CircuitBreakerStrategyConfiguration().ToOptions().ShouldHandle);
        Assert.Null(new TimeoutStrategyConfiguration().ToOptions().TimeoutGenerator);
        Assert.Null(new HedgingStrategyConfiguration().ToOptions<string>().ShouldHandle);
    }
}

/// <summary>Binding from a real <see cref="IConfiguration"/>, and composing with delegates.</summary>
public class StrategyConfigurationBindingTests
{
    private static IConfigurationRoot Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void BareNumber_BindsAsDays_NotSeconds()
    {
        // Pins the foot-gun documented in configuration.md: ConfigurationBinder goes through
        // TimeSpan.Parse, which reads an unqualified number as days. "5" is five days, and
        // nothing warns. If this ever starts throwing or binding as seconds, the doc is wrong.
        var config = Config(("Timeout", "5"));
        var dto = new TimeoutStrategyConfiguration();
        config.Bind(dto);

        Assert.Equal(TimeSpan.FromDays(5), dto.Timeout);
        Assert.NotEqual(TimeSpan.FromSeconds(5), dto.Timeout);
    }

    [Fact]
    public void FullTimeSpanForm_BindsAsWritten()
    {
        var config = Config(("Timeout", "00:00:05"));
        var dto = new TimeoutStrategyConfiguration();
        config.Bind(dto);

        Assert.Equal(TimeSpan.FromSeconds(5), dto.Timeout);
    }

    [Fact]
    public void BindsFromConfiguration_IncludingTimeSpanStrings()
    {
        var config = Config(
            ("Retry:MaxRetryAttempts", "7"),
            ("Retry:UseJitter", "false"),
            ("Retry:MaxDelay", "00:00:30"),
            ("Retry:Delay:Kind", "Linear"),
            ("Retry:Delay:BaseDelay", "00:00:05"));

        var bound = new RetryStrategyConfiguration();
        config.GetSection("Retry").Bind(bound);

        Assert.Equal(7, bound.MaxRetryAttempts);
        Assert.False(bound.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(30), bound.MaxDelay);
        Assert.Equal(RetryDelayKind.Linear, bound.Delay.Kind);
        Assert.Equal(TimeSpan.FromSeconds(5), bound.Delay.BaseDelay);
    }

    [Fact]
    public void MaxDelay_CapsEveryKind_IncludingConstant()
    {
        // The reason there is one cap on the strategy rather than one per backoff kind:
        // RetryDelay.Constant's factory takes no maximum at all.
        var options = new RetryStrategyConfiguration
        {
            MaxDelay = TimeSpan.FromSeconds(1),
            Delay = new RetryDelayConfiguration
            {
                Kind = RetryDelayKind.Constant,
                BaseDelay = TimeSpan.FromMinutes(5),
            },
        }.ToOptions();

        Assert.Equal(TimeSpan.FromSeconds(1), options.MaxDelay);
    }

    [Fact]
    public async Task Async_BoundOptions_DriveARealPipeline()
    {
        var config = Config(
            ("Retry:MaxRetryAttempts", "2"),
            ("Retry:Delay:Kind", "Constant"),
            ("Retry:Delay:BaseDelay", "00:00:00"));

        var bound = new RetryStrategyConfiguration();
        config.GetSection("Retry").Bind(bound);

        var attempts = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(bound.ToOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));

        Assert.Equal(3, attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Async_WithExpression_LayersDelegatesOntoBoundOptions()
    {
        // The documented composition: configuration carries the numbers, code carries the
        // delegates. No extra API needed because the options are records.
        var bound = new RetryStrategyConfiguration
        {
            MaxRetryAttempts = 1,
            Delay = new RetryDelayConfiguration { Kind = RetryDelayKind.Constant, BaseDelay = TimeSpan.Zero },
        };

        var attempts = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(
            bound.ToOptions() with { ShouldHandle = ex => ex is TimeoutException }));

        // Not a TimeoutException, so the predicate rejects it and no retry happens.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ToOptions_InvalidValue_IsRejectedByTheRecordsOwnValidation()
    {
        // The DTO does not re-implement validation; the options record still owns it.
        var options = new RetryStrategyConfiguration { MaxRetryAttempts = -1 }.ToOptions();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Pipeline.Create(b => b.AddRetry(options)));
    }

    [Fact]
    public async Task Async_ConfigurationReload_RebuildsThePipeline()
    {
        // #44 composes with #41: bind the DTO, and the pipeline follows the file.
        var source = new Dictionary<string, string?> { ["Retry:MaxRetryAttempts"] = "1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(source).Build();

        var services = new ServiceCollection();
        services.Configure<RetryStrategyConfiguration>(config.GetSection("Retry"));
        services.AddResiliencePipeline<RetryStrategyConfiguration>("api", (b, c) =>
            b.AddRetry(c.ToOptions() with { Delay = RetryDelay.None }));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();

        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.GetPipeline("api").ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));
        Assert.Equal(2, attempts);

        config["Retry:MaxRetryAttempts"] = "4";
        config.Reload();

        attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.GetPipeline("api").ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));
        Assert.Equal(5, attempts);
    }
}
