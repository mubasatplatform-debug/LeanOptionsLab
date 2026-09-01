using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LeanOptionsLab.Domain;

public sealed class ExperimentConfiguration
{
    public string SchemaVersion { get; init; } = "1.0";
    public string ExperimentId { get; init; } = "us-equity-options-lab-v1";
    public string Underlying { get; init; } = "SPY";
    public string Resolution { get; init; } = "Minute";
    public DateOnly StartDate { get; init; } = new(2021, 1, 1);
    public DateOnly EndDate { get; init; } = new(2025, 12, 31);
    public EvaluationWindows Windows { get; init; } = new();
    public List<StrategyTemplateDefinition> StrategyTemplates { get; init; } = new();
    public ExecutionCostAssumptions ExecutionCosts { get; init; } = new();
    public string ResultsRoot { get; init; } = "results";
    public bool EnableLiveTrading { get; init; }
    public bool EnablePaperTrading { get; init; }
    public bool UseGreeks { get; init; }
    public bool UseImpliedVolatility { get; init; }
    public bool UseOptionUniverseData { get; init; }
}

public sealed class EvaluationWindows
{
    public DateRange Training { get; init; } = new(new DateOnly(2021, 1, 1), new DateOnly(2023, 12, 31));
    public DateRange Validation { get; init; } = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
    public DateRange OutOfSample { get; init; } = new(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
}

public sealed class DateRange
{
    public DateRange()
    {
    }

    public DateRange(DateOnly startDate, DateOnly endDate)
    {
        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

public sealed class StrategyTemplateDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Structure { get; init; } = string.Empty;
    public StrategyRules Rules { get; init; } = new();
}

public sealed class StrategyRules
{
    public bool Approved { get; init; }
    public string? EntryRuleReference { get; init; }
    public string? ExitRuleReference { get; init; }
    public string? PositionSizingRuleReference { get; init; }

    public bool IsCompleteAndApproved =>
        Approved
        && !string.IsNullOrWhiteSpace(EntryRuleReference)
        && !string.IsNullOrWhiteSpace(ExitRuleReference)
        && !string.IsNullOrWhiteSpace(PositionSizingRuleReference);
}

public sealed class ExecutionCostAssumptions
{
    public decimal? CommissionPerContract { get; init; }
    public decimal? SlippagePerContract { get; init; }
    public string? Source { get; init; }

    public bool IsComplete =>
        CommissionPerContract is >= 0m
        && SlippagePerContract is >= 0m
        && !string.IsNullOrWhiteSpace(Source);
}

public static class ExperimentConfigurationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static ExperimentConfiguration Load(string path)
    {
        var configuration = JsonSerializer.Deserialize<ExperimentConfiguration>(
            File.ReadAllText(path),
            Options);

        return configuration ?? throw new InvalidDataException(
            $"Experiment configuration '{path}' is empty or invalid.");
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
