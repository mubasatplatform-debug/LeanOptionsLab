using System;

namespace LeanOptionsLab.Domain;

public enum OptionRight
{
    Call,
    Put
}

public sealed record VerticalSpreadRisk(decimal MaxGain, decimal MaxLoss);

public static class VerticalSpreadPayoff
{
    public static VerticalSpreadRisk CreditVerticalRisk(
        decimal shortStrike,
        decimal longStrike,
        decimal netCreditPerShare,
        decimal contractMultiplier = 100m)
    {
        var width = RequireWidth(shortStrike, longStrike);
        RequirePremiumWithinWidth(netCreditPerShare, width, "net credit");
        RequireMultiplier(contractMultiplier);

        return new(
            MaxGain: netCreditPerShare * contractMultiplier,
            MaxLoss: (width - netCreditPerShare) * contractMultiplier);
    }

    public static VerticalSpreadRisk DebitVerticalRisk(
        decimal longStrike,
        decimal shortStrike,
        decimal netDebitPerShare,
        decimal contractMultiplier = 100m)
    {
        var width = RequireWidth(longStrike, shortStrike);
        RequirePremiumWithinWidth(netDebitPerShare, width, "net debit");
        RequireMultiplier(contractMultiplier);

        return new(
            MaxGain: (width - netDebitPerShare) * contractMultiplier,
            MaxLoss: netDebitPerShare * contractMultiplier);
    }

    public static decimal CreditVerticalPayoffAtExpiry(
        OptionRight right,
        decimal shortStrike,
        decimal longStrike,
        decimal netCreditPerShare,
        decimal underlyingPriceAtExpiry,
        decimal contractMultiplier = 100m)
    {
        RequireWidth(shortStrike, longStrike);
        RequireMultiplier(contractMultiplier);

        var payoff = netCreditPerShare
            - IntrinsicValue(right, shortStrike, underlyingPriceAtExpiry)
            + IntrinsicValue(right, longStrike, underlyingPriceAtExpiry);

        return payoff * contractMultiplier;
    }

    public static decimal DebitVerticalPayoffAtExpiry(
        OptionRight right,
        decimal longStrike,
        decimal shortStrike,
        decimal netDebitPerShare,
        decimal underlyingPriceAtExpiry,
        decimal contractMultiplier = 100m)
    {
        RequireWidth(longStrike, shortStrike);
        RequireMultiplier(contractMultiplier);

        var payoff = -netDebitPerShare
            + IntrinsicValue(right, longStrike, underlyingPriceAtExpiry)
            - IntrinsicValue(right, shortStrike, underlyingPriceAtExpiry);

        return payoff * contractMultiplier;
    }

    private static decimal IntrinsicValue(OptionRight right, decimal strike, decimal underlyingPrice) =>
        right == OptionRight.Call
            ? Math.Max(0m, underlyingPrice - strike)
            : Math.Max(0m, strike - underlyingPrice);

    private static decimal RequireWidth(decimal firstStrike, decimal secondStrike)
    {
        var width = decimal.Abs(firstStrike - secondStrike);
        if (width <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(secondStrike), "Vertical strikes must differ.");
        }

        return width;
    }

    private static void RequirePremiumWithinWidth(decimal premium, decimal width, string label)
    {
        if (premium <= 0m || premium >= width)
        {
            throw new ArgumentOutOfRangeException(label, $"{label} must be positive and smaller than the spread width.");
        }
    }

    private static void RequireMultiplier(decimal contractMultiplier)
    {
        if (contractMultiplier <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier));
        }
    }
}
