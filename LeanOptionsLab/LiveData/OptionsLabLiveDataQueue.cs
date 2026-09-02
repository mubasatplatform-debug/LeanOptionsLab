using System;
using System.Collections.Generic;
using LeanOptionsLab.Domain;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Packets;

namespace LeanOptionsLab.LiveData;

/// <summary>
/// Fail-closed live data socket for the paper environment. LEAN's built-in
/// LiveDataQueue is a cut-out that throws a generic NotImplementedException;
/// this replaces it with a message that names the missing dependency, so a
/// paper run cannot be mistaken for a run that lacked market access silently.
/// No synthetic prices are produced here by design: a fabricated fill would be
/// indistinguishable from a real one in the results directory.
/// </summary>
public sealed class OptionsLabLiveDataQueue : IDataQueueHandler
{
    public static readonly string UnavailableMessage =
        LivePaperReadinessGate.NoApprovedDataProviderReason +
        " PaperBrokerage fills orders, but nothing feeds it prices. Implement IDataQueueHandler against" +
        " an audited real-time feed and point 'data-queue-handler' in lean.json at that type." +
        " Synthetic or randomly generated prices are not an accepted substitute.";

    public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
    {
        var symbol = dataConfig?.Symbol.Value ?? "unknown";
        throw new NotSupportedException($"{UnavailableMessage} Requested symbol: {symbol}.");
    }

    // Deliberately silent: Unsubscribe runs during teardown, and throwing here
    // would mask the original Subscribe failure that caused the shutdown.
    public void Unsubscribe(SubscriptionDataConfig dataConfig)
    {
    }

    public void SetJob(LiveNodePacket job)
    {
    }

    public bool IsConnected => false;

    public void Dispose()
    {
    }
}
