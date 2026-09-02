using System.Collections.Generic;
using System.Linq;

namespace LeanOptionsLab.Domain;

public sealed class LivePaperReadinessDecision
{
    public LivePaperReadinessDecision(bool isReady, IReadOnlyList<string> reasons)
    {
        IsReady = isReady;
        Reasons = reasons;
    }

    public bool IsReady { get; }
    public IReadOnlyList<string> Reasons { get; }
}

/// <summary>
/// Blocks a paper session that would emit orders without an audited basis.
/// Paper trading moves no money, but a paper run still writes a results
/// directory, so an unfounded one is indistinguishable from a real result.
/// </summary>
public static class LivePaperReadinessGate
{
    public const string NoApprovedDataProviderReason =
        "No approved live data provider is configured; LeanOptionsLab ships the paper wiring only.";

    public static LivePaperReadinessDecision Evaluate(
        ExperimentConfiguration configuration,
        bool approvedLiveDataProviderConfigured,
        bool brokerageIsPaperOnly)
    {
        var reasons = new List<string>();

        if (!configuration.EnablePaperTrading)
        {
            reasons.Add("Paper trading is disabled in experiment.v1.json (enablePaperTrading is false).");
        }

        if (!approvedLiveDataProviderConfigured)
        {
            reasons.Add(NoApprovedDataProviderReason);
        }

        if (configuration.StrategyTemplates.Count == 0
            || configuration.StrategyTemplates.Any(template => !template.Rules.IsCompleteAndApproved))
        {
            reasons.Add("Entry, exit, and position sizing rules must be approved before any order is placed.");
        }

        if (!configuration.ExecutionCosts.IsComplete)
        {
            reasons.Add("Commission, slippage, and their approved source must be defined before any order is placed.");
        }

        if (!brokerageIsPaperOnly)
        {
            reasons.Add("The configured brokerage is not PaperBrokerage; this path never places live orders.");
        }

        var validation = ExperimentConfigurationValidator.Validate(configuration);
        foreach (var issue in validation.Issues)
        {
            reasons.Add($"Configuration validation failed [{issue.Code}]: {issue.Message}");
        }

        return new LivePaperReadinessDecision(reasons.Count == 0, reasons);
    }
}
