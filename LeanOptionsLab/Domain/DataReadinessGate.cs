using System.Collections.Generic;

namespace LeanOptionsLab.Domain;

public sealed class DataReadinessEvidence
{
    public bool EquitySecurityMasterAvailable { get; init; }
    public bool UnderlyingMinuteTradeAvailable { get; init; }
    public bool OptionMinuteTradeAvailable { get; init; }
    public bool OptionMinuteQuoteAvailable { get; init; }
    public int DataRequestFailures { get; init; }
    public List<string> FailureMessages { get; init; } = new();
}

public sealed class DataReadinessDecision
{
    public DataReadinessDecision(bool isReady, IReadOnlyList<string> reasons)
    {
        IsReady = isReady;
        Reasons = reasons;
    }

    public bool IsReady { get; }
    public IReadOnlyList<string> Reasons { get; }
}

public static class DataReadinessGate
{
    public static DataReadinessDecision Evaluate(DataReadinessEvidence evidence)
    {
        var reasons = new List<string>();

        if (!evidence.EquitySecurityMasterAvailable)
        {
            reasons.Add("US Equity Security Master is unavailable.");
        }

        if (!evidence.UnderlyingMinuteTradeAvailable)
        {
            reasons.Add("SPY minute trade data is unavailable.");
        }

        if (!evidence.OptionMinuteTradeAvailable)
        {
            reasons.Add("US equity option minute trade data is unavailable.");
        }

        if (!evidence.OptionMinuteQuoteAvailable)
        {
            reasons.Add("US equity option minute quote data is unavailable.");
        }

        if (evidence.DataRequestFailures > 0)
        {
            reasons.Add($"LEAN recorded {evidence.DataRequestFailures} failed data request(s).");
        }

        reasons.AddRange(evidence.FailureMessages);
        return new DataReadinessDecision(reasons.Count == 0, reasons);
    }
}
