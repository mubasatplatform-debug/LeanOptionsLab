using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LeanOptionsLab.Domain;

public sealed record ConfigurationValidationIssue(string Code, string Message);

public sealed class ConfigurationValidationResult
{
    public ConfigurationValidationResult(IReadOnlyList<ConfigurationValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ConfigurationValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}

public static class ExperimentConfigurationValidator
{
    private static readonly DateRange ExpectedTraining = new(new DateOnly(2021, 1, 1), new DateOnly(2023, 12, 31));
    private static readonly DateRange ExpectedValidation = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
    private static readonly DateRange ExpectedOutOfSample = new(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
    private static readonly string[] ExpectedTemplates =
    [
        "Put Credit Vertical",
        "Call Credit Vertical",
        "Directional Debit Vertical"
    ];

    public static ConfigurationValidationResult Validate(ExperimentConfiguration configuration)
    {
        var issues = new List<ConfigurationValidationIssue>();

        if (!string.Equals(configuration.Underlying, "SPY", StringComparison.Ordinal))
        {
            issues.Add(new("underlying", "v1 is fixed to SPY."));
        }

        if (!string.Equals(configuration.Resolution, "Minute", StringComparison.Ordinal))
        {
            issues.Add(new("resolution", "v1 requires minute-resolution data."));
        }

        if (configuration.StartDate != new DateOnly(2021, 1, 1)
            || configuration.EndDate != new DateOnly(2025, 12, 31))
        {
            issues.Add(new("experiment-period", "The experiment period must be 2021-01-01 through 2025-12-31."));
        }

        ValidateWindow(issues, "training-window", configuration.Windows.Training, ExpectedTraining);
        ValidateWindow(issues, "validation-window", configuration.Windows.Validation, ExpectedValidation);
        ValidateWindow(issues, "out-of-sample-window", configuration.Windows.OutOfSample, ExpectedOutOfSample);

        if (!IsOrderedWithoutOverlap(configuration.Windows))
        {
            issues.Add(new("window-overlap", "Training, validation, and out-of-sample windows must be ordered and non-overlapping."));
        }

        var configuredNames = configuration.StrategyTemplates.Select(template => template.Name).ToArray();
        if (!configuredNames.SequenceEqual(ExpectedTemplates, StringComparer.Ordinal))
        {
            issues.Add(new("strategy-templates", "v1 requires exactly the three approved comparison template names in the documented order."));
        }

        if (!string.Equals(NormalizeRelativePath(configuration.ResultsRoot), "results", StringComparison.Ordinal))
        {
            issues.Add(new("results-root", "Results must be written below the relative results/<run-id> root."));
        }

        if (configuration.EnableLiveTrading)
        {
            issues.Add(new("execution-mode", "Live trading is prohibited in this research lab."));
        }

        if (configuration.EnablePaperTrading
            && (configuration.StrategyTemplates.Count == 0
                || configuration.StrategyTemplates.Any(template => !template.Rules.IsCompleteAndApproved)
                || !configuration.ExecutionCosts.IsComplete))
        {
            issues.Add(new(
                "paper-readiness",
                "Paper trading requires complete approved strategy rules and execution-cost assumptions."));
        }

        if (configuration.UseGreeks || configuration.UseImpliedVolatility || configuration.UseOptionUniverseData)
        {
            issues.Add(new("unsupported-data", "Greeks, IV, and option-universe data are outside v1."));
        }

        return new(issues);
    }

    private static void ValidateWindow(
        ICollection<ConfigurationValidationIssue> issues,
        string code,
        DateRange actual,
        DateRange expected)
    {
        if (actual.StartDate != expected.StartDate || actual.EndDate != expected.EndDate)
        {
            issues.Add(new(code, $"Expected {expected.StartDate:yyyy-MM-dd} through {expected.EndDate:yyyy-MM-dd}."));
        }
    }

    private static bool IsOrderedWithoutOverlap(EvaluationWindows windows) =>
        windows.Training.StartDate <= windows.Training.EndDate
        && windows.Validation.StartDate <= windows.Validation.EndDate
        && windows.OutOfSample.StartDate <= windows.OutOfSample.EndDate
        && windows.Training.EndDate < windows.Validation.StartDate
        && windows.Validation.EndDate < windows.OutOfSample.StartDate;

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }
}
