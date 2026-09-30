using System.Diagnostics;

namespace Resilion;

/// <summary>
/// Shared span scaffolding for strategies.
/// </summary>
/// <remarks>
/// This exists so the tag set is written in exactly one place. Every strategy entry point — typed
/// and non-generic, async and synchronous — must emit an identically shaped span, and the original
/// gap this type closes was caused by the span code being duplicated per entry point and then
/// simply not copied into the typed ones.
/// </remarks>
internal static class StrategyActivity
{
    /// <summary>
    /// Starts a span for a strategy execution, tagged with the strategy, pipeline and operation.
    /// Returns <see langword="null"/> when nothing is listening, in which case the caller pays
    /// nothing beyond the null check.
    /// </summary>
    internal static Activity? Start(string strategyName, ResilienceContext context)
    {
        var activity = ResilionTelemetry.ActivitySource.StartActivity(strategyName);
        if (activity is not null)
        {
            activity.SetTag("strategy.name", strategyName);
            activity.SetTag(ResilionTelemetry.PipelineNameTag, context.PipelineName);
            activity.SetTag(ResilionTelemetry.OperationKeyTag, context.OperationKey);
        }

        return activity;
    }

    /// <summary>
    /// Records how the strategy execution finished. The vocabulary is documented in
    /// <c>docs/telemetry.md</c> and pinned by <c>SpanOutcomeTag_MatchesDocumentedVocabulary</c>.
    /// </summary>
    internal static void SetOutcome(Activity? activity, string outcome)
        => activity?.SetTag("outcome", outcome);
}
