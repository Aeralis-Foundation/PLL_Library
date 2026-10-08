using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class PropInteractionInstances
{
    readonly MapRun Run;
    readonly GamefeelHandler? Feedback;
    readonly Dictionary<Transform, Entry> Entries = new();
    readonly HashSet<FeatureOccurrence> FixedOccurrences = new();
    readonly HashSet<Entry> Pending = new();
    readonly List<Entry> Admissions = new();
    internal PropInteractionInstances(MapRun Run, GamefeelHandler? Feedback)
    { this.Run = Run; this.Feedback = Feedback; }
    internal bool HasWork => Pending.Count != 0;
    internal bool HasPendingIntent
    {
        get
        {
            foreach (var Entry in Pending)
                if (Entry.Registration.Ready && Run.HookedGeneration.TryGetPropIntent(Entry.Occurrence, Entry.Contract, out var Intent)
                    && Entry.Runtime.Current.IntentRevision < Intent!.Revision) return true;
            return false;
        }
    }

    sealed class Entry : IDisposable
    {
        internal readonly FeatureOccurrence Occurrence;
        internal readonly Component Source;
        internal readonly PropInteractionContract Contract;
        internal readonly PropInteractionRuntime Runtime;
        internal readonly WorldInteractionLedger.Registration Registration;
        bool Released;
        internal Entry(PropInteractionInstances Owner, FeatureOccurrence Occurrence,
            PropInteractionContract Contract, BiomeId Biome, Component Source, Transform BindingRoot,
            IReadOnlyList<TreePlatform>? Platforms, Transform? ConnectionScope)
        {
            this.Occurrence = Occurrence; this.Source = Source; this.Contract = Contract;
            Occurrence.GeometryBudget.ReserveNativeOutput(this, Contract.RequiresVineMask != 0 ? 1 : 0,
                Contract.GeneratedTransformBudget, 0, 0);
            try
            {
                Runtime = Contract.Bind(BindingRoot, Biome,
                    State => Registration!.Request(State.Encode()),
                    () => Registration is { CanRequest: true } && Owner.IntentApplied(this),
                    () => !Released && Source.gameObject.activeInHierarchy && Registration is { Ready: true } && Owner.Run.PresentationClockReady
                        && Owner.Run.IsOccurrenceReady(Occurrence)
                        && Owner.Run.HookedGeneration.InteractionReady(Source.transform)
                        && (Runtime?.NativeView is not { } View || View.ViewID > 0
                            && ViewBatchSynchronization.GeneratedBatchOriginReady(View, true)),
                    () => Owner.Run.PresentationClock, Platforms, ConnectionScope);
                var Key = Owner.Run.NativeSpawns.InteractionSourceKey(Source, Occurrence);
                var Seed = NativeSpawnAdmission.InteractionSourceSeed(Occurrence, Key);
                var Initial = Contract.InitialState(Seed, 0d);
                Registration = Owner.Run.WorldInteractions.Register(Key, WorldInteractionLedger.PropKind, Initial.Encode(),
                    (Bytes, Replay) =>
                    {
                        var Hooks = Runtime.Adopt(PropInteractionState.Decode(Bytes), Replay);
                        if (!ReferenceEquals(AuthoringRuntime.PresentationOccurrence?.Slot.Occurrence, Occurrence)) return;
                        Observe(Hooks.BreakHook); Observe(Hooks.MotionHook);
                    },
                    Clock => Runtime.Current.EncodeCheckpoint(Clock),
                    (Bytes, Clock) => PropInteractionState.Restore(Bytes, Clock),
                    PrepareInitialForPublication: () =>
                    {
                        if (!Owner.Run.HookedGeneration.PropIntentBirthReady) return null;
                        var Now = Owner.Run.PresentationClock;
                        var State = Contract.InitialState(Seed, Now);
                        if (Owner.Run.HookedGeneration.TryGetPropIntent(Occurrence, Contract, out var Intent))
                            State = State.ApplyIntent(Intent!, Contract.Period, Now, Birth: true);
                        return State.Encode();
                    });
            }
            catch
            {
                Runtime?.Dispose(); Occurrence.GeometryBudget.ReleaseNativeOutput(this); throw;
            }

        }
        public void Dispose()
        {
            if (Released) return;
            Released = true;
            List<Exception>? Failures = null;
            AuthoringCleanup.Capture(ref Failures, Registration.Dispose);
            AuthoringCleanup.Capture(ref Failures, Runtime.Dispose);
            Occurrence.GeometryBudget.ReleaseNativeOutput(this);
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }
    }

    internal void Enroll(FeatureOccurrence Occurrence, IReadOnlyList<TreePlatform>? Platforms = null, Transform? ConnectionScope = null)
    {
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.CustomResolved is not { } Resolved) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                throw new InvalidOperationException("Custom occurrence lost its prop interaction contract.");
            if (Contract.PropInteractionContracts.Count == 0) return;
            var Root = Resolved.Root.Value;
            if (FixedOccurrences.Add(Occurrence))
                foreach (var Definition in Contract.PropInteractionContracts)
                    if (!Definition.Generated)
                    {
                        var Source = Definition.ResolveSource(Root);
                        if (!Source) throw new InvalidOperationException("Fixed prop interaction lost its definition.");
                        var Entry = new Entry(this, Occurrence, Definition, Contract.Id, Source, Root, Platforms, ConnectionScope);
                        Entries.Add(Source.transform, Entry); Pending.Add(Entry);
                    }
            foreach (var Source in Root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (Source is not PllBreakablePropAuthoring and not PllSwingingPropAuthoring and not PllVineAuthoring
                    || Entries.ContainsKey(Source.transform)) continue;
                var Matched = false;
                foreach (var Definition in Contract.PropInteractionContracts)
                {
                    if (!Definition.TryMatchGenerated(Source, out var GeneratedRoot)) continue;
                    var CanonicalSource = Definition.ResolveSource(GeneratedRoot);
                    var Entry = new Entry(this, Occurrence, Definition, Contract.Id, CanonicalSource, GeneratedRoot, Platforms, ConnectionScope);
                    Entries.Add(CanonicalSource.transform, Entry); Pending.Add(Entry);
                    Matched = true; break;
                }
                if (!Matched && Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Source.transform))
                    throw new InvalidOperationException("Generated prop interaction has no sealed candidate binding.");
            }
            return;
        }
    }

    bool Reconcile(Entry Entry)
    {
        if (!Entry.Registration.Ready || !Run.PresentationClockReady) return false;
        if (!Run.HookedGeneration.TryGetPropIntent(Entry.Occurrence, Entry.Contract, out var Intent)
            || Entry.Runtime.Current.IntentRevision >= Intent!.Revision) return true;
        if (Entry.Registration.CanRequest)
            Entry.Registration.Request(Entry.Runtime.Current.ApplyIntent(Intent, Entry.Contract.Period, Run.PresentationClock).Encode());
        return Entry.Runtime.Current.IntentRevision >= Intent.Revision;
    }
    bool IntentApplied(Entry Entry) => !Run.HookedGeneration.TryGetPropIntent(Entry.Occurrence, Entry.Contract, out var Intent)
        || Entry.Runtime.Current.IntentRevision >= Intent!.Revision;
    internal void Reconcile(FeatureOccurrence Occurrence)
    {
        foreach (var Entry in Entries.Values)
            if (ReferenceEquals(Entry.Occurrence, Occurrence)) Pending.Add(Entry);
    }

    internal void Admit(FeatureOccurrence Occurrence, Transform? Root = null)
    {
        foreach (var Entry in Entries.Values)
            if (ReferenceEquals(Entry.Occurrence, Occurrence) && !Entry.Runtime.Admitted && Reconcile(Entry)
                && (Root ? TransformOwnership.Contains(Root!, Entry.Source.transform)
                    : Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Entry.Source.transform)))
            {
                if (!Run.PresentationClockReady || !Run.IsOccurrenceReady(Occurrence)
                    || !Run.HookedGeneration.InteractionReady(Entry.Source.transform)
                    || !Entry.Source.gameObject.activeInHierarchy
                    || Entry.Runtime.NativeView is { } View && (View.ViewID <= 0
                        || !ViewBatchSynchronization.GeneratedBatchOriginReady(View, true))) continue;
                Entry.Runtime.Admit(Feedback); Pending.Remove(Entry);
            }
    }
    internal void Tick()
    {
        if (!Run.PresentationClockReady || Pending.Count == 0) return;
        Admissions.Clear();
        foreach (var Entry in Pending)
            if (Reconcile(Entry) && (Entry.Runtime.Admitted || Run.IsOccurrenceReady(Entry.Occurrence)
                && Run.HookedGeneration.InteractionReady(Entry.Source.transform)
                && Entry.Source.gameObject.activeInHierarchy
                && (Entry.Runtime.NativeView is not { } View || View.ViewID > 0
                    && ViewBatchSynchronization.GeneratedBatchOriginReady(View, true)))) Admissions.Add(Entry);
        foreach (var Entry in Admissions)
        { if (!Entry.Runtime.Admitted) Entry.Runtime.Admit(Feedback); Pending.Remove(Entry); }
        Admissions.Clear();
    }
    internal void Retire(GameObject Root)
    {
        var Removed = new List<Transform>();
        List<Exception>? Failures = null;
        foreach (var Pair in Entries)
            if (!Pair.Key || TransformOwnership.Contains(Root.transform, Pair.Key))
            {
                AuthoringCleanup.Capture(ref Failures, Pair.Value.Dispose); Removed.Add(Pair.Key!);
                Pending.Remove(Pair.Value);
            }
        foreach (var Source in Removed) Entries.Remove(Source);
        FixedOccurrences.RemoveWhere(Occurrence => !Occurrence.Container
            || TransformOwnership.Contains(Root.transform, Occurrence.Container));
        GeneratedGeometryBudget.ThrowFailures(Failures);
    }
    internal void Clear()
    {
        List<Exception>? Failures = null;
        foreach (var Entry in Entries.Values) AuthoringCleanup.Capture(ref Failures, Entry.Dispose);
        Entries.Clear(); FixedOccurrences.Clear(); Pending.Clear(); GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    static void Observe(string Hook)
    {
        if (Hook.Length == 0) return;
        AuthoringRuntime.Emit(Hook); AuthoringRuntime.EmitStinger(Hook);
    }
}
