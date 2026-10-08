using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace PLL.Library;

internal sealed class WorldInteractionLedger
{
    internal const byte PropKind = 0, FixtureKind = 1;
    const string Protocol = "PLL/WorldInteraction/v1";
    readonly MapRun Run;
    readonly Dictionary<string, Registration> Entries = new(StringComparer.Ordinal);
    readonly Dictionary<string, Registration> Properties = new(StringComparer.Ordinal);
    readonly HashSet<Registration> Pending = new();
    readonly HashSet<Registration> Waiting = new();
    readonly Dictionary<string, RetainedCheckpoint> Restored = new(StringComparer.Ordinal);
    bool Released;
    int Authority, RetainedBytes;

    internal abstract class RetainedCheckpoint(MapQuicksave.WorldInteractionSave Saved)
    {
        internal readonly MapQuicksave.WorldInteractionSave Saved = Saved;
    }
    sealed class ImportedCheckpoint(MapQuicksave.WorldInteractionSave Saved) : RetainedCheckpoint(Saved);
    sealed class LiveCheckpoint(MapQuicksave.WorldInteractionSave Saved, double Clock) : RetainedCheckpoint(Saved)
    {
        internal readonly double Clock = Clock;
    }

    internal WorldInteractionLedger(MapRun Run)
    {
        this.Run = Run;
        foreach (var Saved in global::Peak.Quicksave.ShouldUseSaveData ? Run.Ingress.LoadedQuicksaveLayout?.worldInteractions
                     ?? Array.Empty<MapQuicksave.WorldInteractionSave>() : Array.Empty<MapQuicksave.WorldInteractionSave>())
        {
            Restored.Add(Saved.key, new ImportedCheckpoint(Saved)); RetainedBytes = checked(RetainedBytes + Saved.ReservedBytes);
        }
        Run.ReplayBudget.Reserve(this, RetainedBytes);
    }

    internal bool HasPending => Pending.Count != 0 || Waiting.Count != 0;
    internal bool IsAuthority => !Released && Run.Network.OwnsCurrentRun
        && (PhotonNetwork.CurrentRoom is not { IsOffline: false } || PhotonNetwork.IsMasterClient);

    internal Registration Register(string Key, byte Kind, byte[] Initial,
        Action<byte[], bool> ApplyConfirmed, Func<double, byte[]> CaptureForSave,
        Func<byte[], double, byte[]>? RestoreForPublication = null, Func<byte[]?>? PrepareInitialForPublication = null)
    {
        if (Released || Kind > FixtureKind || Entries.ContainsKey(Key))
            throw new InvalidOperationException("World interaction registration is duplicated or outside its lifetime.");
        var Entry = new Registration(this, Key, Kind, Initial, ApplyConfirmed, CaptureForSave);
        Entry.PrepareInitialForPublication = PrepareInitialForPublication;
        Entries.Add(Key, Entry); Properties.Add(Entry.PropertyKey, Entry);
        try
        {
            if (Restored.Remove(Key, out var Checkpoint))
            {
                var Saved = Checkpoint.Saved;
                if (Saved.kind != Kind) throw new InvalidOperationException("Saved world interaction has a different behavior.");
                Entry.Initial = (byte[])Saved.payload.Clone(); Entry.InitialReplay = true;
                Entry.Checkpoint = Checkpoint;
                Entry.PrepareInitialForPublication = null;
                Entry.RestoreForPublication = RestoreForPublication
                    ?? throw new InvalidOperationException("Saved world interaction has no checkpoint time translation.");
                RetainedBytes -= Saved.ReservedBytes;
            }
            Entry.Observe(PhotonNetwork.CurrentRoom?.CustomProperties[Entry.PropertyKey]);
            if (!Entry.Ready)
            {
                Waiting.Add(Entry);
                Entry.PublishInitial();
            }
            return Entry;
        }
        catch { Entry.Dispose(); throw; }
    }

    internal void PropertiesChanged(Hashtable Changed)
    {
        if (Released) return;
        foreach (var Pair in Changed)
            if (Pair.Key is string Key && Properties.TryGetValue(Key, out var Entry)) Entry.Observe(Pair.Value);
    }

    internal void MasterChanged()
    {
        if (Released) return;
        Authority = PhotonNetwork.CurrentRoom?.MasterClientId ?? 0;
        Pending.Clear();
        foreach (var Entry in new List<Registration>(Entries.Values))
        {
            Entry.PendingValue = null;
            Entry.Observe(PhotonNetwork.CurrentRoom?.CustomProperties[Entry.PropertyKey]);
            if (!Entry.Ready) Entry.PublishInitial();
        }
    }

    internal void Tick()
    {
        if (Released || !HasPending) return;
        if (PhotonNetwork.CurrentRoom is { IsOffline: false } Room && Authority != Room.MasterClientId)
        { MasterChanged(); return; }
        foreach (var Entry in new List<Registration>(Pending))
        {
            Entry.Observe(PhotonNetwork.CurrentRoom?.CustomProperties[Entry.PropertyKey]);
            if (Entry.PendingValue is not null && UnityEngine.Time.realtimeSinceStartupAsDouble >= Entry.PendingDeadline)
                throw new InvalidOperationException("World interaction confirmation exceeded the Photon transport deadline.");
        }
        foreach (var Entry in new List<Registration>(Waiting)) Entry.PublishInitial();
    }

    internal MapQuicksave.WorldInteractionSave[] CaptureQuicksave(double Clock)
    {
        if (HasPending) throw new InvalidOperationException("World interaction state is not confirmed for saving.");
        var Values = new List<MapQuicksave.WorldInteractionSave>(Entries.Count + Restored.Count);
        foreach (var Entry in Entries.Values)
            Values.Add(new MapQuicksave.WorldInteractionSave
            { key = Entry.Key, kind = Entry.Kind, payload = Entry.CaptureForSave(Clock) });
        foreach (var Checkpoint in Restored.Values)
        {
            var Saved = Checkpoint.Saved;
            var Origin = Checkpoint is LiveCheckpoint Live ? Live.Clock : Run.HookedGeneration.CheckpointRestoreClock
                ?? throw new InvalidOperationException("Saved world checkpoint has no confirmed restoration anchor.");
            var Payload = Saved.kind switch
            {
                PropKind => PropInteractionState.Decode(PropInteractionState.Restore(Saved.payload, Origin)).EncodeCheckpoint(Clock),
                FixtureKind => NativeClimbingFixtureRuntime.RebaseCheckpoint(Saved.payload, Origin, Clock),
                _ => throw new InvalidOperationException("Retained world checkpoint has an unsupported behavior.")
            };
            Values.Add(new MapQuicksave.WorldInteractionSave { key = Saved.key, kind = Saved.kind, payload = Payload });
        }
        Values.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.key, Right.key));
        return Values.ToArray();
    }

    internal void PruneReleased()
    {
        foreach (var Key in new List<string>(Restored.Keys))
        {
            var Colon = Key.IndexOf(':');
            if (Colon != 8 || !int.TryParse(Key.Substring(0, Colon), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var Slot))
                throw new InvalidOperationException("World interaction source has an invalid occurrence key.");
            if (Slot < Run.Slots.Length && Run.Slots[Slot].Released)
            {
                RetainedBytes -= Restored[Key].Saved.ReservedBytes; Restored.Remove(Key);
            }
        }
        Run.ReplayBudget.Reserve(this, RetainedBytes);
    }

    internal void EndSourceLifetime(HookedPropTarget Target, int Revision)
    {
        var Prefix = NativeSpawnAdmission.HookedSourcePrefix(Target, Revision);
        foreach (var Entry in new List<Registration>(Entries.Values))
            if (Entry.Key.StartsWith(Prefix, StringComparison.Ordinal)) Entry.Dispose();
        foreach (var Key in new List<string>(Restored.Keys))
            if (Key.StartsWith(Prefix, StringComparison.Ordinal))
            { RetainedBytes -= Restored[Key].Saved.ReservedBytes; Restored.Remove(Key); }
        Run.ReplayBudget.Reserve(this, RetainedBytes);
    }

    internal void Clear()
    {
        Released = true;
        var Room = PhotonNetwork.CurrentRoom;
        try
        {
            if (Room is { IsOffline: false } && PhotonNetwork.IsMasterClient)
            {
                var Remove = new Hashtable();
                foreach (var Entry in Entries.Values)
                    if (Entry.CurrentValue is { } Value && PhotonPropertyProtocol.SameValue(Room.CustomProperties[Entry.PropertyKey], Value))
                        Remove[Entry.PropertyKey] = null;
                if (Remove.Count != 0 && !Room.SetCustomProperties(Remove))
                    throw new InvalidOperationException("World interaction room cleanup was rejected.");
            }
        }
        finally
        {
            Entries.Clear(); Properties.Clear(); Pending.Clear(); Waiting.Clear(); Restored.Clear();
            Run.ReplayBudget.Reserve(this, 0); RetainedBytes = 0;
        }
    }

    internal sealed class Registration : IDisposable
    {
        readonly WorldInteractionLedger Owner;
        readonly Action<byte[], bool> ApplyConfirmed;
        internal readonly Func<double, byte[]> CaptureForSave;
        internal readonly string Key, PropertyKey;
        internal readonly byte Kind;
        internal byte[] Initial;
        internal object[]? CurrentValue, PendingValue;
        internal double PendingDeadline;
        long Revision;
        int Bytes;
        bool Disposed;
        internal bool InitialReplay;
        internal Func<byte[], double, byte[]>? RestoreForPublication;
        internal Func<byte[]?>? PrepareInitialForPublication;
        internal RetainedCheckpoint? Checkpoint;

        internal Registration(WorldInteractionLedger Owner, string Key, byte Kind, byte[] Initial,
            Action<byte[], bool> ApplyConfirmed, Func<double, byte[]> CaptureForSave)
        {
            this.Owner = Owner; this.Key = Key; this.Kind = Kind;
            this.Initial = (byte[])Initial.Clone(); this.ApplyConfirmed = ApplyConfirmed;
            this.CaptureForSave = CaptureForSave;
            using var Hash = SHA256.Create();
            PropertyKey = PhotonPropertyProtocol.StatePrefix + "interaction/"
                + BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Key))).Replace("-", string.Empty);
        }

        internal bool Ready => CurrentValue is not null && !Disposed;
        internal bool CanRequest => Ready && Owner.IsAuthority && PendingValue is null;

        internal void PublishInitial()
        {
            if (!Owner.IsAuthority || !Owner.Run.PresentationClockReady || PendingValue is not null) return;
            if (RestoreForPublication is { } Restore)
            {
                var Anchor = Checkpoint is LiveCheckpoint Live ? Live.Clock : Owner.Run.HookedGeneration.CheckpointRestoreClock;
                if (Anchor is not { } Clock) return;
                Initial = Restore(Initial, Clock); RestoreForPublication = null;
                Checkpoint = null;
            }
            else if (PrepareInitialForPublication is { } Prepare)
            {
                if (Prepare() is not { } Prepared) return;
                Initial = Prepared; PrepareInitialForPublication = null;
            }
            Request(Initial);
        }

        internal bool Request(byte[] Payload)
        {
            if (Disposed || !Owner.IsAuthority || PendingValue is not null) return false;
            var Value = new object[] { Protocol, Owner.Run.GenerationEpoch, Owner.Run.LayoutRevision,
                Key, Kind, checked(Revision + 1), (byte[])Payload.Clone() };
            var Size = GeneratedRpcDeferral.SerializePhotonValue(Value).Length;
            if (Size > GeneratedRpcDeferral.MaximumPropertyBytes)
                throw new InvalidOperationException("World interaction exceeds the synchronized property capacity.");
            Owner.Run.ReplayBudget.Reserve(Owner, checked(Owner.RetainedBytes - Bytes + Size));
            Owner.RetainedBytes += Size - Bytes; Bytes = Size;
            if (PhotonNetwork.CurrentRoom is not { IsOffline: false } Room)
            { Adopt(Value, InitialReplay && !Ready); return true; }
            Owner.Authority = Room.MasterClientId;
            PendingValue = Value; Owner.Pending.Add(this);
            PendingDeadline = UnityEngine.Time.realtimeSinceStartupAsDouble
                + PhotonNetwork.NetworkingClient.LoadBalancingPeer.DisconnectTimeout / 1000d;
            var Expected = CurrentValue is null ? null : new Hashtable { [PropertyKey] = CurrentValue };
            if (!Room.SetCustomProperties(new Hashtable { [PropertyKey] = Value }, Expected))
                throw new InvalidOperationException("World interaction publication was rejected.");
            Observe(Room.CustomProperties[PropertyKey]);
            return true;
        }

        internal void Observe(object? Value)
        {
            if (Disposed || Value is null or object[] { Length: 0 } || ReferenceEquals(Value, CurrentValue)) return;
            if (PendingValue is { } Candidate)
            {
                if (PhotonPropertyProtocol.SameValue(Value, Candidate))
                { PendingValue = null; Owner.Pending.Remove(this); Adopt(Candidate, InitialReplay && !Ready); return; }
                if (!PhotonPropertyProtocol.SameValue(Value, CurrentValue))
                    throw new InvalidOperationException("World interaction publication lost its expected revision.");
                return;
            }
            Adopt(Value, !Ready);
        }

        void Adopt(object Value, bool Replay)
        {
            if (Value is not object[] { Length: 7 } Fields || Fields[0] is not string Id || Id != Protocol
                || Fields[1] is not string Epoch || Epoch != Owner.Run.GenerationEpoch
                || Fields[2] is not int Layout || Layout != Owner.Run.LayoutRevision
                || Fields[3] is not string Source || Source != Key || Fields[4] is not byte Type || Type != Kind
                || Fields[5] is not long Next || Next <= 0 || Next < Revision
                || Fields[6] is not byte[] Payload || Payload.Length > GeneratedRpcDeferral.MaximumPropertyBytes
                || Next == Revision && !PhotonPropertyProtocol.SameValue(Value, CurrentValue))
                throw new InvalidOperationException("World interaction state violates its source, behavior or revision contract.");
            if (Next == Revision) return;
            var Size = GeneratedRpcDeferral.SerializePhotonValue(Fields).Length;
            Owner.Run.ReplayBudget.Reserve(Owner, checked(Owner.RetainedBytes - Bytes + Size));
            ApplyConfirmed(Payload, Replay);
            Owner.RetainedBytes += Size - Bytes; Bytes = Size;
            CurrentValue = (object[])PhotonPropertyProtocol.Snapshot(Fields); Revision = Next;
            PrepareInitialForPublication = null; RestoreForPublication = null;
            Checkpoint = null;
            Owner.Waiting.Remove(this);
        }

        public void Dispose()
        {
            if (Disposed) return;
            if (Owner.Released) { Disposed = true; return; }
            var Preserve = !Owner.Run.Releasing && !Owner.Run.Released && CurrentValue is not null;
            if (Preserve)
            {
                var Clock = Owner.Run.PresentationClock;
                Owner.Restored[Key] = new LiveCheckpoint(new MapQuicksave.WorldInteractionSave
                    { key = Key, kind = Kind, payload = CaptureForSave(Clock), ReservedBytes = Bytes }, Clock);
            }
            Disposed = true; Owner.Pending.Remove(this); Owner.Waiting.Remove(this);
            Owner.Entries.Remove(Key); Owner.Properties.Remove(PropertyKey);
            if (!Preserve) Owner.RetainedBytes -= Bytes;
            Owner.Run.ReplayBudget.Reserve(Owner, Owner.RetainedBytes);
            if (Owner.IsAuthority && PhotonNetwork.CurrentRoom is { IsOffline: false } Room && CurrentValue is { } Value)
                if (!Room.SetCustomProperties(new Hashtable { [PropertyKey] = null }, new Hashtable { [PropertyKey] = Value }))
                    throw new InvalidOperationException("World interaction retirement was rejected.");
        }
    }
}
