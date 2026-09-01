using QuantConnect.Algorithm;

namespace QuantConnect.Algorithm.CSharp;

/// <summary>
/// Subscription-free fixture for the local LEAN Launcher path.
/// It never requests market data and never creates an order.
/// </summary>
public sealed class LocalLeanSmoke : QCAlgorithm
{
    public override void Initialize()
    {
        SetStartDate(2021, 1, 1);
        SetEndDate(2021, 1, 1);
        SetCash(100000);
        SetBenchmark(_ => 1m);
        Debug("LOCAL_LEAN_SMOKE|initialized|subscriptions=0|orders=0");
    }
}
