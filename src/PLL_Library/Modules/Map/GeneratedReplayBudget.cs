using System;
using System.Collections.Generic;

namespace PLL.Library;

internal sealed class GeneratedReplayBudget
{
    readonly Dictionary<object, int> Reservations = new();
    int RetainedBytes;

    internal int Bytes(object Owner) => Reservations.TryGetValue(Owner, out var Bytes) ? Bytes : 0;

    internal void Reserve(object Owner, int Bytes, object? Transfer = null)
    {
        if (Bytes < 0) throw new ArgumentOutOfRangeException(nameof(Bytes));
        var Previous = this.Bytes(Owner);
        var Transferred = Transfer is null || ReferenceEquals(Transfer, Owner) ? 0 : this.Bytes(Transfer);
        var Total = checked(RetainedBytes - Previous - Transferred + Bytes);
        if (Total > GeneratedRpcDeferral.MaximumRetainedCatalogBytes)
            throw new InvalidOperationException("Generated replay state exceeds the retained network budget.");
        if (Transfer is not null && !ReferenceEquals(Transfer, Owner)) Reservations.Remove(Transfer);
        if (Bytes == 0) Reservations.Remove(Owner);
        else Reservations[Owner] = Bytes;
        RetainedBytes = Total;
    }
}
