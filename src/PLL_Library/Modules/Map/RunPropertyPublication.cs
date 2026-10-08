using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace PLL.Library;

internal sealed class RunPropertyPublication
{
    sealed class PendingRunProperty
    {
        internal PendingRunProperty(object Value, object[] Envelope)
        {
            this.Value = Value;
            this.Envelope = Envelope;
        }

        internal object Value { get; }
        internal object[] Envelope { get; }
        internal RoomPropertyPublication Publication { get; } = new();
    }

    readonly Dictionary<string, PendingRunProperty> Pending = new(StringComparer.Ordinal);
    readonly MapRun Run;

    internal RunPropertyPublication(MapRun Run) => this.Run = Run ?? throw new ArgumentNullException(nameof(Run));

    internal bool HasPending => Pending.Count != 0;

    internal bool IsPending(string Key) => Pending.ContainsKey(Key);

    internal bool Publish(string Key, object Value)
    {
        if (PhotonNetwork.CurrentRoom is not { } Room) return false;
        if (Pending.TryGetValue(Key, out var Existing))
            return PhotonPropertyProtocol.SameValue(Existing.Value, Value);

        var Snapshot = PhotonPropertyProtocol.Snapshot(Value);
        var Envelope = PhotonPropertyProtocol.RunValue(Run.GenerationEpoch, Run.LayoutRevision, Snapshot);
        var Publication = new PendingRunProperty(Snapshot, Envelope);
        Pending.Add(Key, Publication);
        var Confirmed = Publication.Publication.AwaitEcho(
            Room, Key, Envelope, "The synchronized map property could not be published.");
        if (Publication.Publication.Failure is not null)
        {
            Pending.Remove(Key);
            return false;
        }
        if (Confirmed) Pending.Remove(Key);
        return true;
    }

    internal void Confirm(Hashtable Changed)
    {
        if (Pending.Count == 0) return;
        foreach (var Key in new List<string>(Pending.Keys))
            if (Changed.TryGetValue(Key, out var Value)
                && Pending.TryGetValue(Key, out var Expected)
                && PhotonPropertyProtocol.SameValue(Value, Expected.Envelope))
                Pending.Remove(Key);
    }

    internal bool Maintain(Room Room, out string? Failure)
    {
        Failure = null;
        foreach (var Pair in new List<KeyValuePair<string, PendingRunProperty>>(Pending))
        {
            var Key = Pair.Key;
            var PendingValue = Pair.Value;
            if (!Pending.TryGetValue(Key, out var Current) || !ReferenceEquals(Current, PendingValue)) continue;
            if (PendingValue.Publication.AwaitEcho(
                Room, Key, PendingValue.Envelope, "The synchronized map property could not be published."))
            {
                Pending.Remove(Key);
                continue;
            }
            if (PendingValue.Publication.Failure is not { } PublicationFailure) continue;
            Failure = PublicationFailure;
            return false;
        }
        return true;
    }

    internal bool MatchesRoomValue(string Key, object Expected) =>
        PhotonNetwork.CurrentRoom is { } Room
        && PhotonPropertyProtocol.SameRunValue(
            Room.CustomProperties[Key], Run.GenerationEpoch, Run.LayoutRevision, Expected);

    internal void Cancel(string Key)
    {
        if (!Pending.Remove(Key, out var Publication)) return;
        Publication.Publication.Reset();
    }

    internal void CancelAll()
    {
        foreach (var Publication in Pending.Values) Publication.Publication.Reset();
        Pending.Clear();
    }
}
