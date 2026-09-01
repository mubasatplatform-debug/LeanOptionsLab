using System;
using System.Collections.Generic;

namespace LeanOptionsLab.Domain;

public enum ContractRejectionReason
{
    None,
    RulesNotApproved,
    MissingOptionChain,
    MissingUnderlyingQuote,
    MissingLegQuote,
    InvalidBidAsk,
    MissingExpiry,
    ExpiryRulesNotApproved,
    ExpiryOutsideApprovedRange
}

public sealed record ContractSelectionRequest
{
    public bool EntryAndExitRulesApproved { get; init; }
    public bool HasOptionChain { get; init; }
    public bool HasUnderlyingQuote { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
    public DateOnly? Expiry { get; init; }
    public int? DaysToExpiry { get; init; }
    public int? MinimumApprovedDaysToExpiry { get; init; }
    public int? MaximumApprovedDaysToExpiry { get; init; }
}

public sealed record ContractSelectionDecision(
    bool IsEligible,
    ContractRejectionReason Reason,
    string Message)
{
    public static ContractSelectionDecision Reject(ContractRejectionReason reason, string message) =>
        new(false, reason, message);

    public static ContractSelectionDecision Accept() =>
        new(true, ContractRejectionReason.None, "Contract is eligible under approved rules.");
}

/// <summary>
/// A deliberately narrow, data-only gate. It does not select a strike or expiry by
/// itself; it only makes a candidate eligible after every required input is present.
/// </summary>
public static class ContractSelectionGate
{
    public static ContractSelectionDecision Evaluate(ContractSelectionRequest request)
    {
        if (!request.EntryAndExitRulesApproved)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.RulesNotApproved,
                "Contract selection is disabled until entry and exit rules are approved.");
        }

        if (!request.HasOptionChain)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.MissingOptionChain,
                "No option chain is available for the candidate.");
        }

        if (!request.HasUnderlyingQuote)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.MissingUnderlyingQuote,
                "The underlying quote is required before selecting an option contract.");
        }

        if (request.Expiry is null || request.DaysToExpiry is null)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.MissingExpiry,
                "The candidate expiry and days-to-expiry are required.");
        }

        if (request.MinimumApprovedDaysToExpiry is null || request.MaximumApprovedDaysToExpiry is null)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.ExpiryRulesNotApproved,
                "No approved days-to-expiry range exists.");
        }

        if (request.DaysToExpiry < request.MinimumApprovedDaysToExpiry
            || request.DaysToExpiry > request.MaximumApprovedDaysToExpiry)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.ExpiryOutsideApprovedRange,
                "The candidate expiry is outside the approved days-to-expiry range.");
        }

        if (request.Bid is null || request.Ask is null)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.MissingLegQuote,
                "Both bid and ask quotes are required for every candidate leg.");
        }

        if (request.Bid <= 0m || request.Ask <= 0m || request.Bid > request.Ask)
        {
            return ContractSelectionDecision.Reject(
                ContractRejectionReason.InvalidBidAsk,
                "Bid and ask must be positive and bid must not exceed ask.");
        }

        return ContractSelectionDecision.Accept();
    }
}
