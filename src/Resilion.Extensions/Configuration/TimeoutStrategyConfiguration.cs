namespace Resilion.Extensions.Configuration;

/// <summary>A bindable description of <see cref="TimeoutStrategyOptions"/>.</summary>
/// <remarks>
/// <c>TimeoutGenerator</c> is absent: it is a delegate and cannot be configured from a file.
/// </remarks>
public sealed class TimeoutStrategyConfiguration
{
    /// <summary>Gets or sets the timeout. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Projects this description into options, leaving all delegates unset.</summary>
    public TimeoutStrategyOptions ToOptions() => new() { Timeout = Timeout };
}
