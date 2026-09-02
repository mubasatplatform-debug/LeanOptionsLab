using System;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.CSharp;

/// <summary>
/// Backtest-only US equity options laboratory.
///
/// This v1 algorithm deliberately never submits an order. The tracked experiment
/// configuration leaves entry, exit, and execution-cost rules unapproved, so any
/// future contract selection must remain fail-closed until those rules are reviewed.
/// LEAN's default option exercise and assignment models remain in use. Order events
/// and assignment callbacks are written to the backtest log for the external report.
/// </summary>
public sealed class LeanOptionsLab : QCAlgorithm
{
    private int _assignmentEventCount;
    private bool _reportedNoTradeGate;
    private Symbol? _optionSymbol;
    private long _dataSliceCount;
    private long _optionChainSliceCount;
    private long _optionContractObservationCount;

    public override void Initialize()
    {
        SetStartDate(2021, 1, 1);
        SetEndDate(2025, 12, 31);
        SetCash(100000);

        AddEquity("SPY", Resolution.Minute);

        var option = AddOption("SPY", Resolution.Minute);
        _optionSymbol = option.Symbol;
        option.SetFilter(-10, 10, TimeSpan.Zero, TimeSpan.FromDays(45));

        Debug("OPTIONS_LAB|initialized|underlying=SPY|resolution=Minute|"
            + "period=2021-01-01..2025-12-31|mode=backtest-only");
        Debug("OPTIONS_LAB|gate=no-orders|reason=entry_exit_and_execution_rules_not_approved");
        Debug("OPTIONS_LAB|lifecycle=default-lean-exercise-and-assignment-models");
    }

    public override void OnData(Slice data)
    {
        _dataSliceCount++;
        if (_optionSymbol is not null && data.OptionChains.TryGetValue(_optionSymbol, out var chain))
        {
            _optionChainSliceCount++;
            _optionContractObservationCount += chain.Count();
        }

        // No rules are approved in v1. Do not infer a strike, expiry, quote, fee, or
        // slippage assumption from the incoming chain. The pure C# selection gate is
        // tested separately and must approve a candidate before later versions trade.
        if (_reportedNoTradeGate)
        {
            return;
        }

        _reportedNoTradeGate = true;
        Debug("OPTIONS_LAB|no-trade|reason=rules-not-approved|"
            + "contract-selection-remains-fail-closed");
    }

    public override void OnOrderEvent(OrderEvent orderEvent)
    {
        // LEAN emits option exercise lifecycle events through the general order-event
        // channel. Preserve every event and add a second explicit marker only when
        // LEAN's own OptionExercise token is present in the event representation.
        var eventText = orderEvent.ToString();
        Debug($"OPTIONS_LAB|order-event|{eventText}");

        if (eventText.Contains("OptionExercise", StringComparison.Ordinal))
        {
            Debug($"OPTIONS_LAB|exercise-event|{eventText}");
        }
    }

    public override void OnAssignmentOrderEvent(OrderEvent assignmentEvent)
    {
        _assignmentEventCount++;
        Debug($"OPTIONS_LAB|assignment-event|{assignmentEvent}");
    }

    public override void OnEndOfAlgorithm()
    {
        Debug($"OPTIONS_LAB|completed|assignment-events={_assignmentEventCount}|"
            + $"data-slices={_dataSliceCount}|option-chain-slices={_optionChainSliceCount}|"
            + $"option-contract-observations={_optionContractObservationCount}|orders-submitted=0|"
            + "ranking=blocked-until-data-and-rules-are-approved");
    }
}
