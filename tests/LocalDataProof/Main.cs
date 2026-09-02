using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Option;

namespace QuantConnect.Algorithm.CSharp;

/// <summary>
/// Counts real option data delivered from the sample files that ship with the
/// pinned LEAN source. It deliberately submits no orders.
/// </summary>
public sealed class LocalDataProof : QCAlgorithm
{
    private readonly HashSet<Symbol> _contracts = new();
    private Symbol _optionSymbol = null!;
    private long _chainSlices;
    private long _contractQuotes;

    public override void Initialize()
    {
        SetStartDate(2015, 12, 24);
        SetEndDate(2015, 12, 28);
        SetCash(100000);

        Equity equity = AddEquity("GOOG");
        Option option = AddOption("GOOG");
        _optionSymbol = option.Symbol;
        option.SetFilter(universe => universe.StandardsOnly().Strikes(-2, 2).Expiration(0, 180));
        SetBenchmark(equity.Symbol);

        Debug("LOCAL_DATA_PROOF|initialized|underlying=GOOG|resolution=Minute|orders=0");
    }

    public override void OnData(Slice slice)
    {
        if (!slice.OptionChains.TryGetValue(_optionSymbol, out var chain))
        {
            return;
        }

        _chainSlices++;
        foreach (OptionContract contract in chain)
        {
            _contractQuotes++;
            _contracts.Add(contract.Symbol);
        }
    }

    public override void OnEndOfAlgorithm()
    {
        Debug($"LOCAL_DATA_PROOF|completed|chainSlices={_chainSlices}|contractQuotes={_contractQuotes}|uniqueContracts={_contracts.Count}|orders=0");
    }
}
