using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanOptionsLab.Domain;

[JsonConverter(typeof(ComparisonStatusJsonConverter))]
public enum ComparisonStatus
{
    Ranked,
    NotRankable,
    InvalidData
}

public static class ComparisonStatusTokens
{
    public static string ToToken(ComparisonStatus status) => status switch
    {
        ComparisonStatus.Ranked => "ranked",
        ComparisonStatus.NotRankable => "not-rankable",
        ComparisonStatus.InvalidData => "invalid-data",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static ComparisonStatus FromToken(string token) => token switch
    {
        "ranked" => ComparisonStatus.Ranked,
        "not-rankable" => ComparisonStatus.NotRankable,
        "invalid-data" => ComparisonStatus.InvalidData,
        _ => throw new JsonException($"Unsupported comparison status '{token}'.")
    };
}

public sealed class ComparisonStatusJsonConverter : JsonConverter<ComparisonStatus>
{
    public override ComparisonStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Comparison status must be a string.");
        }

        return ComparisonStatusTokens.FromToken(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ComparisonStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ComparisonStatusTokens.ToToken(value));
}

public sealed class PeriodMetrics
{
    public bool DataComplete { get; init; }
    public decimal? RiskAdjustedReturn { get; init; }
    public decimal? MaxDrawdown { get; init; }
}

public sealed class StrategyEvaluation
{
    public string TemplateName { get; init; } = string.Empty;
    public PeriodMetrics Training { get; init; } = new();
    public PeriodMetrics Validation { get; init; } = new();
    public PeriodMetrics OutOfSample { get; init; } = new();
}

public sealed record RankedStrategy(
    string TemplateName,
    decimal OutOfSampleRiskAdjustedReturn,
    decimal OutOfSampleMaxDrawdown);

public sealed class RankingDecision
{
    public ComparisonStatus Status { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RankedStrategy> RankedStrategies { get; init; } = Array.Empty<RankedStrategy>();

    public static RankingDecision NotRankable(params string[] reasons) =>
        new() { Status = ComparisonStatus.NotRankable, Reasons = reasons };

    public static RankingDecision InvalidData(params string[] reasons) =>
        new() { Status = ComparisonStatus.InvalidData, Reasons = reasons };
}

/// <summary>
/// Ranks only out-of-sample values. Training and validation values are carried as
/// audit evidence but never participate in the ordering expression below.
/// </summary>
public static class ComparisonRankingService
{
    public static RankingDecision Rank(
        ExperimentConfiguration configuration,
        IReadOnlyList<StrategyEvaluation> evaluations)
    {
        var configurationValidation = ExperimentConfigurationValidator.Validate(configuration);
        if (!configurationValidation.IsValid)
        {
            return RankingDecision.NotRankable(configurationValidation.Issues
                .Select(issue => $"{issue.Code}: {issue.Message}")
                .ToArray());
        }

        if (!configuration.ExecutionCosts.IsComplete)
        {
            return RankingDecision.NotRankable(
                "Commission, slippage, and their approved source must be defined before ranking.");
        }

        if (configuration.StrategyTemplates.Any(template => !template.Rules.IsCompleteAndApproved))
        {
            return RankingDecision.NotRankable(
                "Every strategy needs approved entry, exit, and position-sizing rule references before ranking.");
        }

        var configuredNames = configuration.StrategyTemplates
            .Select(template => template.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var evaluatedNames = evaluations
            .Select(evaluation => evaluation.TemplateName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (!configuredNames.SequenceEqual(evaluatedNames, StringComparer.Ordinal))
        {
            return RankingDecision.NotRankable(
                "A complete evaluation is required for every configured strategy template.");
        }

        var incomplete = evaluations
            .Where(evaluation => !evaluation.Training.DataComplete
                || !evaluation.Validation.DataComplete
                || !evaluation.OutOfSample.DataComplete)
            .Select(evaluation => evaluation.TemplateName)
            .ToArray();

        if (incomplete.Length > 0)
        {
            return RankingDecision.InvalidData(
                $"Incomplete data evidence for: {string.Join(", ", incomplete)}.");
        }

        var missingOutOfSampleMetrics = evaluations
            .Where(evaluation => evaluation.OutOfSample.RiskAdjustedReturn is null
                || evaluation.OutOfSample.MaxDrawdown is null
                || evaluation.OutOfSample.MaxDrawdown < 0m)
            .Select(evaluation => evaluation.TemplateName)
            .ToArray();

        if (missingOutOfSampleMetrics.Length > 0)
        {
            return RankingDecision.InvalidData(
                $"Missing or invalid out-of-sample metrics for: {string.Join(", ", missingOutOfSampleMetrics)}.");
        }

        var ranking = evaluations
            .OrderByDescending(evaluation => evaluation.OutOfSample.RiskAdjustedReturn!.Value)
            .ThenBy(evaluation => evaluation.OutOfSample.MaxDrawdown!.Value)
            .ThenBy(evaluation => evaluation.TemplateName, StringComparer.Ordinal)
            .Select(evaluation => new RankedStrategy(
                evaluation.TemplateName,
                evaluation.OutOfSample.RiskAdjustedReturn!.Value,
                evaluation.OutOfSample.MaxDrawdown!.Value))
            .ToArray();

        if (ranking.Length > 1
            && ranking[0].OutOfSampleRiskAdjustedReturn == ranking[1].OutOfSampleRiskAdjustedReturn
            && ranking[0].OutOfSampleMaxDrawdown == ranking[1].OutOfSampleMaxDrawdown)
        {
            return RankingDecision.NotRankable(
                "The top out-of-sample risk-adjusted return and maximum drawdown are tied; no winner is declared.");
        }

        return new RankingDecision
        {
            Status = ComparisonStatus.Ranked,
            RankedStrategies = ranking
        };
    }
}
