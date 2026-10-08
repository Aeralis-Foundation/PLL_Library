using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeSpawnAdmission
{
    const string ActorProtocol = PLLLibrary.PluginGuid + "/native-zone-actor";
    static readonly ConditionalWeakTable<PhotonView, ActorEnvelope> NativeActors = new();
    static string ZombiePropertyKey => PhotonPropertyProtocol.StatePrefix + "zombie-markers";
    readonly MapRun Run;
    readonly RunManager NativeRun;
    readonly Dictionary<Component, FeatureOccurrence> Sources = new();
    readonly Dictionary<Component, ActorSource> ActorSources = new();
    readonly Dictionary<string, ActorSource> ActorSourcesByKey = new(StringComparer.Ordinal);
    readonly Dictionary<HazardZoneBinding, Population> HazardPopulations = new();
    readonly Dictionary<PhotonView, ActorEnvelope> Outputs = new();
    readonly HashSet<FixtureSource> FixtureRequests = new();
    readonly HashSet<string> ConsumedZombies;
    readonly HashSet<string> AllowedZombies = new(StringComparer.Ordinal);
    MushroomZombieSpawner? PendingZombie;
    readonly RoomPropertyPublication ZombiePublication = new();
    object[]? ZombiePublicationValue;
    bool ZombieDeclarationsBound;
    bool ExistingOutputsAdopted;
    bool WasMaster;
    static FixtureSource? CreatingFixture;

    enum ActorKind : byte { Tornado, Tumbleweed, Zombie, FixedRope }

    sealed class Population
    {
        internal Population(int Capacity) => this.Capacity = Capacity;
        internal int Capacity { get; }
        internal HashSet<PhotonView> Actors { get; } = new();
    }

    abstract class ActorSource
    {
        protected ActorSource(Component Component, FeatureOccurrence Occurrence, string Key, Population Population)
        {
            this.Component = Component; this.Occurrence = Occurrence; this.Key = Key;
            this.Population = Population;
        }
        internal Component Component { get; }
        internal FeatureOccurrence Occurrence { get; }
        internal string Key { get; }
        internal abstract ActorKind Kind { get; }
        internal Population Population { get; }
    }

    sealed class ZoneActorSource : ActorSource
    {
        internal ZoneActorSource(Component Component, FeatureOccurrence Occurrence, string Key, Population Population)
            : base(Component, Occurrence, Key, Population) { }
        internal override ActorKind Kind => Component switch
        {
            TornadoSpawner => ActorKind.Tornado,
            NetworkedObjectSpawner => ActorKind.Tumbleweed,
            MushroomZombieSpawner => ActorKind.Zombie,
            _ => throw new InvalidOperationException("A native actor source has an unsupported component.")
        };
    }

    sealed class FixtureSource : ActorSource
    {
        internal FixtureSource(NativeSpawnAdmission Owner, RopeAnchorWithRope Provider, FeatureOccurrence Occurrence, string Key,
            GameObject Prefab, Action<GameObject> BindPeer, Action<GameObject> ReleaseLocal)
            : base(Provider, Occurrence, Key, new Population(1))
        {
            this.Owner = Owner; this.Provider = Provider; this.Prefab = Prefab;
            View = Provider.GetComponent<PhotonView>(); this.BindPeer = BindPeer; this.ReleaseLocal = ReleaseLocal;
        }
        internal override ActorKind Kind => ActorKind.FixedRope;
        internal NativeSpawnAdmission Owner { get; }
        internal RopeAnchorWithRope Provider { get; }
        internal GameObject Prefab { get; }
        internal PhotonView View { get; }
        internal Action<GameObject> BindPeer { get; }
        internal Action<GameObject> ReleaseLocal { get; }
        internal bool Retired;
    }

    internal sealed class NativeFixtureSource
    {
        readonly NativeSpawnAdmission Owner;
        readonly FixtureSource Source;
        internal NativeFixtureSource(NativeSpawnAdmission Owner, RopeAnchorWithRope Provider)
        {
            this.Owner = Owner; Source = (FixtureSource)Owner.ActorSources[Provider];
        }
        internal void Spawn()
        {
            if (Source.Retired) throw new InvalidOperationException("A retired native fixture cannot create an output.");
            Owner.FixtureRequests.Add(Source); Owner.SpawnFixture(Source);
        }
        internal void Retire() => Owner.RetireFixture(Source);
    }

    internal NativeFixtureSource EnrollFixture(FeatureOccurrence Occurrence, RopeAnchorWithRope Provider,
        string RecipeId, GameObject NativePrefab, Action<GameObject> BindPeer, Action<GameObject> ReleaseLocal)
    {
        if (!NativePrefab || NativePrefab.name != NativeHangingRopeSource.ResourceAddress
            || !PllAuthoringValueContract.IsLocalId(RecipeId))
            throw new InvalidOperationException("A hanging rope fixture has an invalid native resource or recipe ID.");
        var Key = SourceKey(Provider, Occurrence, "fixture:" + RecipeId + ":");
        var Source = new FixtureSource(this, Provider, Occurrence, Key, NativePrefab, BindPeer, ReleaseLocal);
        Sources.Add(Provider, Occurrence); Sources.TryAdd(Source.View, Occurrence);
        ActorSources.Add(Provider, Source); ActorSourcesByKey.Add(Key, Source);
        return new NativeFixtureSource(this, Provider);
    }

    internal static bool OwnsFixtureOutput(PhotonView View) => View
        && NativeActors.TryGetValue(View, out var Actor) && Actor.Origin.Kind == ActorKind.FixedRope;

    internal static bool FixtureOutputReady(PhotonView View) => !OwnsFixtureOutput(View)
        || NativeActors.TryGetValue(View, out var Actor) && Actor.Binding is ActorBinding.Bound Bound
            && Map.CurrentRun is { } Run && Run.IsOccurrenceReady(Bound.Source.Occurrence);

    void SpawnFixture(FixtureSource Source)
    {
        if (Source.Retired || !FixtureRequests.Contains(Source) || !Source.Provider || !Source.View.IsMine
            || !ReferenceEquals(Map.CurrentRun, Run) || !Run.IsOccurrenceReady(Source.Occurrence)
            || Source.View.ViewID == 0) return;
        BindPendingOutputs();
        FixtureRequests.Remove(Source);
        if (Source.Population.Actors.Count != 0) return;
        var Previous = CreatingFixture; CreatingFixture = Source;
        try { Source.Provider.SpawnRope(); }
        finally { CreatingFixture = Previous; }
    }

    void RetireFixture(FixtureSource Source)
    {
        if (Source.Retired) return;
        Source.Retired = true; FixtureRequests.Remove(Source);
        foreach (var Actor in new List<ActorEnvelope>(Outputs.Values))
            if (Actor.Binding is ActorBinding.Bound Bound && ReferenceEquals(Bound.Source, Source)
                || Actor.Binding is ActorBinding.Pending && Actor.Origin.SourceKey == Source.Key) Retire(Actor);
        RemoveSource(Source.Provider); Sources.Remove(Source.View);
    }

    sealed class ActorOrigin
    {
        internal ActorOrigin(string Epoch, int Revision, int Seed, int BatchIndex,
            string SourceKey, ActorKind Kind, int SourceViewId)
        {
            this.Epoch = Epoch; this.Revision = Revision; this.Seed = Seed;
            this.BatchIndex = BatchIndex; this.SourceKey = SourceKey;
            this.Kind = Kind; this.SourceViewId = SourceViewId;
        }
        internal string Epoch { get; }
        internal int Revision { get; }
        internal int Seed { get; }
        internal int BatchIndex { get; }
        internal string SourceKey { get; }
        internal ActorKind Kind { get; }
        internal int SourceViewId { get; }
        internal object[] Encode() => new object[]
        {
            ActorProtocol, Epoch, Revision, Seed, BatchIndex, SourceKey, (byte)Kind, SourceViewId
        };
    }

    readonly struct DormantBody
    {
        readonly Rigidbody Body;
        readonly RigidbodyConstraints Constraints;
        readonly bool Kinematic;
        internal DormantBody(Rigidbody Body)
        {
            this.Body = Body; Constraints = Body.constraints; Kinematic = Body.isKinematic;
            Body.isKinematic = true; Body.constraints = RigidbodyConstraints.FreezeAll;
        }
        internal void Restore() { Body.constraints = Constraints; Body.isKinematic = Kinematic; }
    }

    abstract class ActorBinding
    {
        internal sealed class Pending : ActorBinding
        {
            internal Pending(GameObject Actor)
            {
                var Rigs = Actor.GetComponentsInChildren<Rigidbody>(true);
                Bodies = new DormantBody[Rigs.Length];
                for (var Index = 0; Index < Rigs.Length; Index++) Bodies[Index] = new DormantBody(Rigs[Index]);
            }
            internal DormantBody[] Bodies { get; }
            internal List<(Hashtable Data, Photon.Realtime.Player Sender)> Calls { get; } = new();
            internal int CallBytes;
        }
        internal sealed class Bound : ActorBinding
        {
            internal Bound(ActorSource Source) => this.Source = Source;
            internal ActorSource Source { get; }
        }
        internal sealed class Retired : ActorBinding
        {
            internal static Retired Instance { get; } = new();
        }
    }

    sealed class ActorEnvelope
    {
        internal ActorEnvelope(PhotonView View, ActorOrigin Origin)
        {
            this.View = View; this.Origin = Origin; Binding = new ActorBinding.Pending(View.gameObject);
        }
        internal PhotonView View { get; }
        internal ActorOrigin Origin { get; }
        internal ActorBinding Binding;
    }

    internal NativeSpawnAdmission(MapRun Run, RunManager NativeRun)
    {
        this.Run = Run; this.NativeRun = NativeRun; WasMaster = PhotonNetwork.IsMasterClient;
        var Saved = global::Peak.Quicksave.ShouldUseSaveData ? Run.Ingress.LoadedQuicksaveLayout : null;
        ConsumedZombies = Saved is null ? new(StringComparer.Ordinal)
            : new(Saved.ParsedZombieConsumption
                ?? throw new InvalidOperationException("The saved zombie marker state was not parsed before run construction."), StringComparer.Ordinal);
    }

    internal bool Started => NativeRun && NativeRun.runStarted;

    internal static string ZombieZonePrefix(FeatureOccurrence Occurrence, string ZoneId) =>
        SourcePrefix(Occurrence) + "entity:" + ZoneId + ":";

    static string SourcePrefix(FeatureOccurrence Occurrence) =>
        Occurrence.BatchIndex.ToString("X8", CultureInfo.InvariantCulture) + ":"
        + Occurrence.Slots[Occurrence.Slots.Count - 1].Source.StableId + "\n";

    internal static string HookedSourcePrefix(HookedPropTarget Target, int Revision) => SourcePrefix(Target.Occurrence)
        + "hooked:" + Target.TargetIndex.ToString("X8", CultureInfo.InvariantCulture) + ":"
        + Target.Contract.StableTargetKey + ":" + Revision.ToString("X8", CultureInfo.InvariantCulture) + ":";

    string SourceKey(Component Source, FeatureOccurrence Occurrence, string BodyPrefix = "") =>
        Run.HookedGeneration.TryGetSourceLifetime(Source.transform, out var Target, out var Revision)
            ? HookedSourcePrefix(Target, Revision) + BodyPrefix
                + DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Source, Target.Root)
            : SourcePrefix(Occurrence) + BodyPrefix
                + DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Source, Occurrence.Container);

    internal string InteractionSourceKey(Component Source, FeatureOccurrence Occurrence) =>
        SourceKey(Source, Occurrence, "interaction:");

    internal static int InteractionSourceSeed(FeatureOccurrence Occurrence, string Key) =>
        SourceSeed(Occurrence.ActivationSeed, Key);

    internal static bool IsOwnedSourceLifetime(Component Source) => Source is NativeSourceLifetime Lifetime
        && Lifetime.Ownership is { } Binding && Binding.Source
        && Binding.Source.gameObject == Lifetime.gameObject && Binding.Owner.Sources.ContainsKey(Binding.Source);

    internal void EnrollHookedSources(HookedPropTarget Target)
    {
        foreach (var Source in Target.Root.GetComponentsInChildren<MonoBehaviour>(true))
            if (Source is TornadoSpawner or NetworkedObjectSpawner or MushroomZombieSpawner or Condor or SpiderManager)
                EnrollSource(Source, Target.Occurrence);
        RequireZombieCapacity();
    }

    internal void RetireHookedSources(HookedPropTarget Target, int Revision)
    {
        var Prefix = HookedSourcePrefix(Target, Revision);
        foreach (var Source in new List<ActorSource>(ActorSources.Values))
            if (Source.Key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                if (Source is FixtureSource Fixture) RetireFixture(Fixture);
                else RemoveSource(Source.Component);
            }
        AllowedZombies.RemoveWhere(Key => Key.StartsWith(Prefix, StringComparison.Ordinal));
        if (ConsumedZombies.RemoveWhere(Key => Key.StartsWith(Prefix, StringComparison.Ordinal)) != 0
            && PhotonNetwork.IsMasterClient) QueueZombieConsumption();
    }

    void RequireZombieCapacity()
    {
        var Keys = new HashSet<string>(AllowedZombies, StringComparer.Ordinal);
        Keys.UnionWith(ConsumedZombies);
        var Snapshot = new string[Keys.Count]; Keys.CopyTo(Snapshot);
        ZombieValue(Snapshot, "The declared zombie markers exceed the room property capacity.");
    }

    object[] ZombieValue(string[] Keys, string Failure)
    {
        var Value = new object[] { Run.GenerationEpoch, Run.LayoutRevision, Run.Configuration.Layout.Seed, Keys };
        if (GeneratedRpcDeferral.SerializePhotonValue(Value).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException(Failure);
        return Value;
    }

    bool ZombieSourceCurrent(string Key, bool AllowObsolete)
    {
        var Index = int.Parse(Key.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var Occurrence = Run.Slots[Index].Occurrence;
        if (Occurrence.BatchIndex != Index || !Key.StartsWith(SourcePrefix(Occurrence), StringComparison.Ordinal))
            throw new InvalidOperationException("A zombie marker source does not belong to the synchronized layout.");
        var Body = Key.Substring(Key.IndexOf('\n') + 1);
        if (Body.StartsWith("entity:", StringComparison.Ordinal))
        {
            if (!AllowedZombies.Contains(Key)) throw new InvalidOperationException("A zombie marker has no declared Entity Zone source.");
            return true;
        }
        if (!Body.StartsWith("hooked:", StringComparison.Ordinal)) return true;
        if (!TryParseHookedBody(Body, out var TargetIndex, out var TargetKey, out var Revision, out _)
            || (uint)TargetIndex >= (uint)Occurrence.HookedTargets.Length
            || TargetKey != Occurrence.HookedTargets[TargetIndex].Contract.StableTargetKey)
            throw new InvalidOperationException("A zombie marker has an unknown Hooked target.");
        var CurrentRevision = Run.HookedGeneration.SourceLifetimeRevision(Occurrence.HookedTargets[TargetIndex]);
        if (CurrentRevision is null)
        {
            if (AllowObsolete) return false;
            throw new InvalidOperationException("A zombie marker belongs to a retired Hooked target.");
        }
        if (Revision == CurrentRevision) return true;
        if (AllowObsolete && Revision < CurrentRevision) return false;
        throw new InvalidOperationException("A zombie marker has an invalid Hooked source revision.");
    }

    internal void BindZombieDeclarations()
    {
        if (ZombieDeclarationsBound) return;
        foreach (var Slot in Run.Slots)
        {
            if (Slot.Index != Slot.Occurrence.BatchIndex) continue;
            foreach (var Zone in Slot.Occurrence.EntityZones) Zone.DeclareZombieSources(Slot.Occurrence, AllowedZombies);
        }
        RequireZombieCapacity();
        foreach (var Key in ConsumedZombies)
            ZombieSourceCurrent(Key, AllowObsolete: false);
        ZombieDeclarationsBound = true;
        SynchronizeZombieConsumption(); AdoptExistingOutputs();
    }

    internal void EnrollRetained(FeatureOccurrence Occurrence)
    {
        Enroll(Occurrence.Container);
        foreach (var Slot in Occurrence.Slots)
            if (Slot.PeakAttachment && !TransformOwnership.Contains(Occurrence.Container, Slot.PeakAttachment))
                Enroll(Slot.PeakAttachment);
        void Enroll(Transform Root)
        {
            foreach (var Source in Root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (Source is PhotonView View && View.TryGetComponent<Item>(out var Item)
                    && Item.itemState != ItemState.Ground) continue;
                if (Source is PhotonView or TornadoSpawner or NetworkedObjectSpawner
                    or MushroomZombieSpawner or Condor or SpiderManager) EnrollSource(Source, Occurrence);
            }
        }
    }

    FeatureOccurrence? Bind(Component Source)
    {
        if (Sources.TryGetValue(Source, out var Existing)) return Existing;
        foreach (var Slot in Run.Slots)
            if (Slot.IsClone && !Slot.Released && TransformOwnership.Contains(Slot.Container, Source.transform))
            {
                EnrollSource(Source, Slot.Occurrence); return Slot.Occurrence;
            }
        return null;
    }

    void EnrollSource(Component Source, FeatureOccurrence Occurrence)
    {
        if (Sources.TryGetValue(Source, out var Existing))
        {
            if (!ReferenceEquals(Existing, Occurrence)) throw new InvalidOperationException("A native source changed its occurrence owner.");
            return;
        }
        Sources.Add(Source, Occurrence);
        if (Source is not PhotonView)
        {
            Source.gameObject.AddComponent<NativeSourceLifetime>().Ownership = (this, Source);
            if (Source.TryGetComponent<PhotonView>(out var View)) Sources.TryAdd(View, Occurrence);
        }
        var Kind = Source switch
        {
            TornadoSpawner => ActorKind.Tornado,
            NetworkedObjectSpawner Spawner when Spawner.objToSpawn && Spawner.objToSpawn.GetComponent<TumbleWeed>() => ActorKind.Tumbleweed,
            MushroomZombieSpawner Marker when Marker.mushroomZombiePrefab && Marker.mushroomZombiePrefab.isNPCZombie => ActorKind.Zombie,
            _ => (ActorKind?)null
        };
        if (Kind is null) return;
        var Zone = Source.GetComponentInParent<PllHazardZoneAuthoring>();
        Population Population;
        if (Zone)
        {
            var Binding = Zone.Binding!;
            if (!HazardPopulations.TryGetValue(Binding, out Population!))
            {
                Population = new(Binding.MaximumAlive); HazardPopulations.Add(Binding, Population);
            }
        }
        else Population = new(int.MaxValue);
        var Entity = Source is MushroomZombieSpawner ? Source.GetComponentInParent<PllEntityZoneAuthoring>() : null;
        var Key = Entity
            ? ZombieZonePrefix(Occurrence, Entity.Id) + Source.transform.GetSiblingIndex().ToString("X8", CultureInfo.InvariantCulture)
            : SourceKey(Source, Occurrence);
        var Actor = new ZoneActorSource(Source, Occurrence, Key, Population);
        ActorSources.Add(Source, Actor); ActorSourcesByKey.Add(Key, Actor);
        if (Kind == ActorKind.Zombie)
        {
            if (!Entity) AllowedZombies.Add(Key);
            if (Entity && !AllowedZombies.Contains(Key)) throw new InvalidOperationException("A generated zombie marker has no declared stable source.");
            if (ConsumedZombies.Contains(Key)) ((MushroomZombieSpawner)Source).spawned = true;
        }
    }

    internal void WriteSourceNetworkContract(GeneratedNetworkContract.ContractWriter Writer,
        FeatureOccurrence Occurrence, Transform? NativeSourceRoot = null)
    {
        var Entries = new List<ActorSource>();
        if (NativeSourceRoot is null)
            foreach (var Root in Occurrence.Roots)
                foreach (var Source in Root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (Source is TornadoSpawner or NetworkedObjectSpawner or MushroomZombieSpawner
                        && Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Source.transform))
                        EnrollSource(Source, Occurrence);
        foreach (var Source in ActorSources.Values)
            if (ReferenceEquals(Source.Occurrence, Occurrence) && Source.Kind != ActorKind.FixedRope
                && (NativeSourceRoot is not null
                    ? TransformOwnership.Contains(NativeSourceRoot, Source.Component.transform)
                        && Run.HookedGeneration.OwnedByOuter(NativeSourceRoot, Source.Component.transform)
                    : Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Source.Component.transform))) Entries.Add(Source);
        RequireZombieCapacity();
        Entries.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
        foreach (var Source in Entries)
        {
            Writer.StartRecord(GeneratedNetworkContract.NativeActorSourceRecord, "native-actor-source", Source.Key);
            Writer.Write((byte)Source.Kind); WritePose(Source.Component.transform);
            if (Source.Component is TornadoSpawner)
            {
                var Points = Source.Component.transform.Find("TornadoPoints"); Writer.Write(Points.childCount);
                for (var Index = 0; Index < Points.childCount; Index++) WritePose(Points.GetChild(Index));
            }
        }
        void WritePose(Transform Value)
        {
            Writer.WritePose(new Pose(Occurrence.Container.InverseTransformPoint(Value.position),
                Quaternion.Inverse(Occurrence.Container.rotation) * Value.rotation));
        }
    }

    internal static bool Ready(Component Source)
    {
        if (Source is PhotonView View && NativeActors.TryGetValue(View, out var Actor)) return OutputReady(Actor);
        var Admission = Map.CurrentRun?.NativeSpawns;
        return Admission is null || !Admission.Sources.TryGetValue(Source, out var Occurrence) || Admission.Ready(Occurrence);
    }

    internal bool Ready(FeatureOccurrence Occurrence) => ReferenceEquals(ReadyCurrentOccurrence(), Occurrence);

    FeatureOccurrence? ReadyCurrentOccurrence()
    {
        if (!ReferenceEquals(Map.CurrentRun, Run) || !Run.Active || !Started) return null;
        return Run.ReadyCurrentSlot?.Occurrence;
    }

    static bool OutputReady(ActorEnvelope Actor) => Actor.Binding is ActorBinding.Bound Bound
        && Map.CurrentRun is { Active: true } Run && Run.NativeSpawns.Started
        && Run.NativeSpawns.Outputs.TryGetValue(Actor.View, out var Owned) && ReferenceEquals(Owned, Actor)
        && Run.IsOccurrenceReady(Bound.Source.Occurrence);

    void AdoptExistingOutputs()
    {
        if (ExistingOutputsAdopted) return;
        ExistingOutputsAdopted = true;
        foreach (var View in PhotonNetwork.PhotonViewCollection)
            if (NativeActors.TryGetValue(View, out var Actor) && Actor.View == View) Adopt(Actor);
    }

    void Adopt(ActorEnvelope Actor)
    {
        var Origin = Actor.Origin;
        if (Origin.Epoch != Run.GenerationEpoch || Origin.Revision != Run.LayoutRevision
            || Origin.Seed != Run.Configuration.Layout.Seed || (uint)Origin.BatchIndex >= (uint)Run.Slots.Length
            || Run.Slots[Origin.BatchIndex].Occurrence.BatchIndex != Origin.BatchIndex)
            throw new InvalidOperationException("A native actor has invalid map-run provenance.");
        Outputs.Add(Actor.View, Actor);
        if (!Run.Slots[Origin.BatchIndex].FeaturesReady && Run.Active) LazySlotGenerator.GenerateLazySlot(Origin.BatchIndex);
        BindPendingOutputs();
    }

    internal void BindPendingOutputs()
    {
        foreach (var Actor in new List<ActorEnvelope>(Outputs.Values))
        {
            if (!Actor.View || Actor.Binding is not ActorBinding.Pending Pending) continue;
            if (!ActorSourcesByKey.TryGetValue(Actor.Origin.SourceKey, out var Source)) continue;
            if (!Run.IsOccurrenceReady(Source.Occurrence)) continue;
            if (Source.Kind != Actor.Origin.Kind || Source.Occurrence.BatchIndex != Actor.Origin.BatchIndex)
                throw new InvalidOperationException("A native actor changed its declared source kind or occurrence.");
            if (Source is FixtureSource Fixture)
            {
                if (Fixture.Retired) { Retire(Actor); continue; }
                if (Fixture.View.ViewID != Actor.Origin.SourceViewId
                    || !ViewBatchSynchronization.GeneratedBatchOriginReady(Fixture.View))
                    throw new InvalidOperationException("A hanging rope anchor does not match its completed network binding.");
                foreach (var Call in Pending.Calls)
                {
                    if (DeferredMapNetworkOperations.RpcMethod(Call.Data) != nameof(Rope.AttachToAnchor_Rpc)) continue;
                    if (Call.Data[(byte)4] is not object[] { Length: 2 } Parameters
                        || Parameters[0] is not null and not PhotonView
                        || Parameters[1] is not float Length || !ManifestEncoding.CanEncode(Length)
                        || !Length.Equals(Fixture.Provider.ropeSegmentLength))
                        throw new InvalidOperationException("A hanging rope attachment has an invalid native RPC shape.");
                    if (Parameters[0] is PhotonView Anchor && Anchor != Fixture.View)
                        throw new InvalidOperationException("A hanging rope attachment changed its declared anchor.");
                    Parameters[0] = Fixture.View;
                }
                Fixture.BindPeer(Actor.View.gameObject);
            }
            else if (Source.Component is TornadoSpawner TornadoSource)
            {
                var View = TornadoSource.GetComponent<PhotonView>();
                if (View.ViewID != Actor.Origin.SourceViewId || !ViewBatchSynchronization.GeneratedBatchOriginReady(View))
                    throw new InvalidOperationException("A Tornado source does not match its completed network binding.");
                Actor.View.GetComponent<global::Tornado>().RPCA_InitTornado(View.ViewID);
            }
            else if (Source.Component is MushroomZombieSpawner Marker)
            {
                var Zombie = Actor.View.GetComponent<MushroomZombie>();
                Marker.spawned = true; Marker.spawnedZombie = Zombie; Zombie.spawner = Marker; ConsumedZombies.Add(Source.Key);
            }
            Actor.Binding = new ActorBinding.Bound(Source); Source.Population.Actors.Add(Actor.View);
            foreach (var View in Actor.View.GetComponentsInChildren<PhotonView>(true)) Sources[View] = Source.Occurrence;
            foreach (var Body in Pending.Bodies) Body.Restore();
            foreach (var Call in Pending.Calls) PhotonNetwork.ExecuteRpc(Call.Data, Call.Sender);
        }
    }

    ActorOrigin Origin(ActorSource Source) => new(Run.GenerationEpoch, Run.LayoutRevision,
        Run.Configuration.Layout.Seed, Source.Occurrence.BatchIndex, Source.Key, Source.Kind,
        Source is FixtureSource Fixture ? Fixture.View.ViewID
            : Source.Component is TornadoSpawner Tornado ? Tornado.GetComponent<PhotonView>().ViewID : 0);

    bool Admit(ActorSource Source) => PhotonNetwork.IsMasterClient && Ready(Source.Occurrence)
        && Source.Population.Actors.Count < Source.Population.Capacity;

    bool SpawnTornado(TornadoSpawner Source)
    {
        if (!ActorSources.TryGetValue(Source, out var Actor)) return true;
        if (!Admit(Actor)) return false;
        var Output = PhotonNetwork.Instantiate("Tornado", Source.GetSpawnPos(), Quaternion.identity, 0, Origin(Actor).Encode());
        Output.GetComponent<PhotonView>().RPC(nameof(global::Tornado.RPCA_InitTornado), RpcTarget.All, Source.GetComponent<PhotonView>().ViewID);
        return false;
    }

    bool SpawnTumbleweed(NetworkedObjectSpawner Source)
    {
        if (!ActorSources.TryGetValue(Source, out var Actor)) return true;
        if (Admit(Actor)) PhotonNetwork.Instantiate(Source.objToSpawn.name,
            Source.transform.position + Source.spawnOffset, Source.transform.rotation, 0, Origin(Actor).Encode());
        return false;
    }

    bool SpawnZombie(MushroomZombieSpawner Source)
    {
        if (PendingZombie is not null && !ReferenceEquals(PendingZombie, Source)) return false;
        if (!ActorSources.TryGetValue(Source, out var Actor)) return true;
        if (PendingZombie is null)
        {
            if (!PhotonNetwork.IsMasterClient || !Ready(Actor.Occurrence)) return false;
            if (ConsumedZombies.Contains(Actor.Key)) return false;
            PendingZombie = Source; ConsumedZombies.Add(Actor.Key); QueueZombieConsumption();
        }
        else if (!CanCompleteZombie(Actor.Occurrence)) return false;
        if (ZombiePublicationValue is not null) return false;
        PendingZombie = null; Source.spawned = true;
        var Zombie = PhotonNetwork.Instantiate(Source.mushroomZombiePrefab.gameObject.name,
            Source.transform.position, Source.transform.rotation, 0, Origin(Actor).Encode()).GetComponent<MushroomZombie>();
        Source.spawnedZombie = Zombie; Zombie.spawner = Source;
        return false;
    }

    bool CanCompleteZombie(FeatureOccurrence Occurrence) => ZombiePublicationValue is null
        && ReferenceEquals(Map.CurrentRun, Run) && Run.Active && Started
        && PhotonNetwork.IsMasterClient && Run.IsOccurrenceReady(Occurrence);

    internal string[] CaptureConsumedZombieSources()
    {
        var Keys = new string[ConsumedZombies.Count]; ConsumedZombies.CopyTo(Keys);
        Array.Sort(Keys, StringComparer.Ordinal); return Keys;
    }

    void QueueZombieConsumption()
    {
        var Value = ZombieValue(CaptureConsumedZombieSources(), "The consumed zombie markers exceed the room property capacity.");
        if (PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom is null) return;
        ZombiePublicationValue = Value; AdvanceNetworkPublications();
    }

    internal void AdvanceNetworkPublications()
    {
        if (FixtureRequests.Count != 0)
            foreach (var Source in new List<FixtureSource>(FixtureRequests)) SpawnFixture(Source);
        if (ZombiePublicationValue is null) return;
        var Room = PhotonNetwork.CurrentRoom;
        if (Room is null || !PhotonNetwork.IsMasterClient)
        {
            ZombiePublicationValue = null; ZombiePublication.Reset(); PendingZombie = null; return;
        }
        if (ZombiePublication.AwaitEcho(Room, ZombiePropertyKey, ZombiePublicationValue,
                "The consumed zombie markers could not be published.")) ZombiePublicationValue = null;
        else if (ZombiePublication.Failure is { } Failure) throw new InvalidOperationException(Failure);
    }

    void SynchronizeZombieConsumption()
    {
        if (PhotonNetwork.CurrentRoom is not { } Room) return;
        var Raw = Room.CustomProperties[ZombiePropertyKey];
        if (Raw is object[] { Length: 4 } Fields && Fields[0] is string Epoch && Fields[1] is int Revision
            && Fields[2] is int Seed && Fields[3] is string[] Keys)
        {
            if (Epoch == Run.GenerationEpoch && Revision == Run.LayoutRevision)
            {
                if (Seed != Run.Configuration.Layout.Seed) throw new InvalidOperationException("The zombie marker state has invalid run provenance.");
                if (Keys.Length > GeneratedRpcDeferral.MaximumPropertyBytes
                    || GeneratedRpcDeferral.SerializePhotonValue(Fields).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
                    throw new InvalidOperationException("The zombie marker state exceeds the room property capacity.");
                string? Previous = null;
                var CurrentKeys = new List<string>();
                foreach (var Key in Keys)
                {
                    if (!ValidZombieSourceKey(Key, Run.Slots.Length)
                        || Previous is not null && StringComparer.Ordinal.Compare(Previous, Key) >= 0)
                        throw new InvalidOperationException("The zombie marker state has an unknown or noncanonical source.");
                    if (ZombieSourceCurrent(Key, AllowObsolete: true)) CurrentKeys.Add(Key);
                    Previous = Key;
                }
                ConsumedZombies.UnionWith(CurrentKeys);
            }
        }
        else if (Raw is not null && Raw is not object[] { Length: 0 })
            throw new InvalidOperationException("The zombie marker room state has an unsupported shape.");
        foreach (var Pair in ActorSources)
            if (Pair.Key is MushroomZombieSpawner Marker && ConsumedZombies.Contains(Pair.Value.Key)
                && !ReferenceEquals(PendingZombie, Marker)) Marker.spawned = true;
        if (PhotonNetwork.IsMasterClient) QueueZombieConsumption();
    }

    internal void RoomPropertiesUpdated(Hashtable Changed)
    {
        if (Changed.ContainsKey(ZombiePropertyKey)) SynchronizeZombieConsumption();
    }

    internal void MasterClientChanged()
    {
        var Master = PhotonNetwork.IsMasterClient;
        if (Master == WasMaster) return;
        WasMaster = Master; PendingZombie = null; ZombiePublication.Reset(); ZombiePublicationValue = null;
        SynchronizeZombieConsumption(); BindPendingOutputs();
        if (!Master) { FixtureRequests.Clear(); return; }
        foreach (var Source in ActorSources.Values)
            if (Source is FixtureSource Fixture && !Fixture.Retired) FixtureRequests.Add(Fixture);
        var State = UnityEngine.Random.state;
        try
        {
            foreach (var Source in ActorSources.Values)
            {
                UnityEngine.Random.InitState(SourceSeed(Source));
                if (Source.Component is TornadoSpawner Tornado)
                    Tornado.untilNext = UnityEngine.Random.Range(Tornado.minSpawnTime, Tornado.maxSpawnTime);
                else if (Source.Component is NetworkedObjectSpawner Tumbleweed) Tumbleweed.SetCounter();
            }
        }
        finally { UnityEngine.Random.state = State; }
    }

    internal static bool ValidZombieSourceKey(string? Key, int SlotCount) =>
        ValidSourceKey(Key, SlotCount, out _, out var Body)
        && (Body.StartsWith("entity:", StringComparison.Ordinal) ? ValidEntityKey(Body)
            : Body.StartsWith("hooked:", StringComparison.Ordinal)
                ? TryParseHookedBody(Body, out _, out _, out _, out var Tail) && ValidComponentPath(Tail)
                : ValidComponentPath(Body));

    static bool ValidSourceKey(string? Key, int SlotCount, out int Slot, out string Body)
    {
        Slot = -1; Body = string.Empty;
        if (Key is null || Key.Length < 12 || Key.Length > GeneratedRpcDeferral.MaximumPropertyBytes
            || Key[8] != ':' || !UpperHex(Key.AsSpan(0, 8))
            || !int.TryParse(Key.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out Slot)
            || (uint)Slot >= (uint)SlotCount) return false;
        var Delimiter = Key.IndexOf('\n', 9);
        if (Delimiter <= 9 || Delimiter == Key.Length - 1) return false;
        if (!SegmentId.TryParse(Key.Substring(9, Delimiter - 9), out _)) return false;
        Body = Key.Substring(Delimiter + 1); return true;
    }

    static bool ValidEntityKey(string Body)
    {
        var Delimiter = Body.LastIndexOf(':');
        return Delimiter > 7 && PllAuthoringValueContract.IsLocalId(Body.Substring(7, Delimiter - 7))
            && UpperHex(Body.AsSpan(Delimiter + 1));
    }

    static bool ValidFixtureKey(string Body)
    {
        if (!Body.StartsWith("fixture:", StringComparison.Ordinal)) return false;
        var Delimiter = Body.LastIndexOf(':');
        return Delimiter > 8 && PllAuthoringValueContract.IsLocalId(Body.Substring(8, Delimiter - 8))
            && ValidComponentPath(Body.Substring(Delimiter + 1));
    }

    static bool TryParseHookedBody(string Body, out int TargetIndex, out string TargetKey, out int Revision, out string Tail)
    {
        TargetIndex = -1; TargetKey = Tail = string.Empty; Revision = 0;
        if (Body.Length < 99 || !Body.StartsWith("hooked:", StringComparison.Ordinal)
            || Body[15] != ':' || Body[80] != ':' || Body[89] != ':'
            || !UpperHex(Body.AsSpan(7, 8)) || !UpperHex(Body.AsSpan(16, 64), 64)
            || !UpperHex(Body.AsSpan(81, 8))
            || !int.TryParse(Body.AsSpan(7, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out TargetIndex)
            || TargetIndex < 0
            || !int.TryParse(Body.AsSpan(81, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out Revision)
            || Revision <= 0) return false;
        TargetKey = Body.Substring(16, 64); Tail = Body.Substring(90); return true;
    }

    static bool ValidComponentPath(string Body)
    {
        var Index = 0;
        while (Body.Length - Index > 9)
        {
            if (!UpperHex(Body.AsSpan(Index, 8)) || Body[Index + 8] != '/') return false;
            Index += 9;
        }
        return Body.Length - Index == 9 && Body[Index] == '#' && UpperHex(Body.AsSpan(Index + 1));
    }

    static bool UpperHex(ReadOnlySpan<char> Value, int Length = 8)
    {
        if (Value.Length != Length) return false;
        foreach (var Character in Value)
            if (Character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) return false;
        return true;
    }

    internal void RetireOutputs(GameObject Root)
    {
        var Actors = new List<ActorEnvelope>();
        foreach (var Actor in Outputs.Values)
        {
            var Source = (Actor.Binding as ActorBinding.Bound)?.Source;
            if (Source is null && Actor.Binding is ActorBinding.Pending)
                ActorSourcesByKey.TryGetValue(Actor.Origin.SourceKey, out Source);
            if (Source is not null)
            {
                if (TransformOwnership.Contains(Root.transform, Source.Component.transform)
                    && Run.HookedGeneration.OwnedByOuter(Root.transform, Source.Component.transform)) Actors.Add(Actor);
            }
            else if (Actor.Binding is ActorBinding.Pending && (uint)Actor.Origin.BatchIndex < (uint)Run.Slots.Length)
            {
                var Occurrence = Run.Slots[Actor.Origin.BatchIndex].Occurrence;
                if (TransformOwnership.Contains(Root.transform, Occurrence.Container)) Actors.Add(Actor);
                else foreach (var Slot in Occurrence.Slots)
                    if (Slot.Released) { Actors.Add(Actor); break; }
            }
        }
        foreach (var Actor in Actors) Retire(Actor);
        List<Component>? Owners = null;
        foreach (var Source in Sources.Keys)
            if (Source is Condor or MushroomZombieSpawner && Source
                && TransformOwnership.Contains(Root.transform, Source.transform)
                && Run.HookedGeneration.OwnedByOuter(Root.transform, Source.transform)) (Owners ??= new()).Add(Source);
        if (Owners is not null) foreach (var Source in Owners) if (Source) RetireOutput(Source);
    }

    void Retire(ActorEnvelope Actor)
    {
        if (Actor.Binding is ActorBinding.Retired) return;
        var Source = (Actor.Binding as ActorBinding.Bound)?.Source;
        if (Source is null) ActorSourcesByKey.TryGetValue(Actor.Origin.SourceKey, out Source);
        Source?.Population.Actors.Remove(Actor.View);
        Actor.Binding = ActorBinding.Retired.Instance;
        if (!Actor.View) return;
        Exception? LocalFailure = null;
        if (Source is FixtureSource Fixture)
        {
            try { Fixture.ReleaseLocal(Actor.View.gameObject); }
            catch (Exception Failure) { LocalFailure = Failure; }
        }
        try
        {
            if (Actor.View.IsMine && !Actor.View.removedFromLocalViewList) PhotonNetwork.Destroy(Actor.View.gameObject);
            else Actor.View.gameObject.SetActive(false);
        }
        catch (Exception Failure) when (LocalFailure is not null)
        {
            throw new AggregateException("Native fixture release and network destruction both failed.", LocalFailure, Failure);
        }
        if (LocalFailure is not null) ExceptionPropagation.Throw(LocalFailure);
    }

    static void RetireOutput(Component Source)
    {
        if (Source is Condor Bird && Bird._targetChar && Bird._targetChar.data.carrierCondor == Bird) Bird.GiveUp();
        if (Source is MushroomZombieSpawner Marker && Marker.spawnedZombie
            && !Marker.spawnedZombie.view.removedFromLocalViewList && Marker.spawnedZombie.view.IsMine)
        {
            var Zombie = Marker.spawnedZombie; Marker.spawnedZombie = null!; PhotonNetwork.Destroy(Zombie.gameObject);
        }
    }

    void RemoveSource(Component Source)
    {
        Sources.Remove(Source);
        if (ActorSources.Remove(Source, out var Owner))
        {
            ActorSourcesByKey.Remove(Owner.Key);
            if (Owner is FixtureSource Fixture) { Fixture.Retired = true; FixtureRequests.Remove(Fixture); }
            foreach (var Actor in new List<ActorEnvelope>(Outputs.Values))
                if (Actor.Binding is ActorBinding.Bound Bound && ReferenceEquals(Bound.Source, Owner)
                    || Actor.Binding is ActorBinding.Pending && Actor.Origin.SourceKey == Owner.Key) Retire(Actor);
        }
        if (ReferenceEquals(PendingZombie, Source)) PendingZombie = null;
    }

    sealed class NativeSourceLifetime : MonoBehaviour
    {
        internal (NativeSpawnAdmission Owner, Component Source)? Ownership;
        void OnDestroy()
        {
            if (Ownership is { } Binding) Binding.Owner.RemoveSource(Binding.Source);
        }
    }

    internal void Clear()
    {
        var Actors = new List<ActorEnvelope>(Outputs.Values);
        foreach (var Actor in Actors) Retire(Actor);
        Sources.Clear(); ActorSources.Clear(); ActorSourcesByKey.Clear(); HazardPopulations.Clear(); Outputs.Clear();
        ConsumedZombies.Clear(); AllowedZombies.Clear(); PendingZombie = null; FixtureRequests.Clear();
        ZombiePublicationValue = null; ZombiePublication.Reset();
    }

    static ActorOrigin? ParseOrigin(Photon.Pun.InstantiateParameters Parameters)
    {
        if (Parameters.data is not { Length: > 0 } Data || Data[0] is not string Protocol || Protocol != ActorProtocol) return null;
        if (Data.Length != 8 || Data[1] is not string Epoch || string.IsNullOrEmpty(Epoch)
            || Epoch.Length > GeneratedRpcDeferral.MaximumPropertyBytes
            || Data[2] is not int Revision || Revision == 0 || Data[3] is not int Seed
            || Data[4] is not int BatchIndex || BatchIndex < 0 || Data[5] is not string Key
            || !ValidSourceKey(Key, PllLayoutCapacityContract.MaximumOrdinarySegments, out var KeySlot, out var Body)
            || KeySlot != BatchIndex
            || Data[6] is not byte Kind || Kind > (byte)ActorKind.FixedRope || Data[7] is not int SourceView
            || !ValidActorBody(Body, (ActorKind)Kind)
            || (ActorKind)Kind is ActorKind.Tornado or ActorKind.FixedRope && SourceView <= 0
            || (ActorKind)Kind is not (ActorKind.Tornado or ActorKind.FixedRope) && SourceView != 0
            || Parameters.prefabName != ((ActorKind)Kind switch
                { ActorKind.Tornado => "Tornado", ActorKind.Tumbleweed => "tumbleweed", ActorKind.Zombie => "MushroomZombie",
                    ActorKind.FixedRope => NativeHangingRopeSource.ResourceAddress, _ => throw new InvalidOperationException() }))
            throw new InvalidOperationException("A native actor creation has invalid source metadata.");
        if (GeneratedRpcDeferral.SerializePhotonValue(Data).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("A native actor creation exceeds the source metadata capacity.");
        if (Map.CurrentRun is { HasPublishedLayout: true } Run
            && (Epoch != Run.GenerationEpoch || Revision != Run.LayoutRevision || Seed != Run.Configuration.Layout.Seed
                || (uint)BatchIndex >= (uint)Run.Slots.Length || Run.Slots[BatchIndex].Occurrence.BatchIndex != BatchIndex))
            throw new InvalidOperationException("A native actor creation has invalid map-run provenance.");
        return new(Epoch, Revision, Seed, BatchIndex, Key, (ActorKind)Kind, SourceView);
    }

    static bool ValidActorBody(string Body, ActorKind Kind)
    {
        if (Body.StartsWith("hooked:", StringComparison.Ordinal))
        {
            if (!TryParseHookedBody(Body, out _, out _, out _, out var Tail)) return false;
            Body = Tail;
        }
        return Kind == ActorKind.FixedRope ? ValidFixtureKey(Body)
            : Kind == ActorKind.Zombie && Body.StartsWith("entity:", StringComparison.Ordinal)
                ? ValidEntityKey(Body) : ValidComponentPath(Body);
    }

    static bool DeferActorRpc(Hashtable Data, Photon.Realtime.Player Sender)
    {
        if (Data[(byte)0] is not int ViewId) return false;
        var View = PhotonNetwork.GetPhotonView(ViewId);
        if (!View || !NativeActors.TryGetValue(View, out var Actor)) return false;
        if (Actor.Binding is ActorBinding.Retired) return true;
        if (Actor.Binding is not ActorBinding.Pending Pending) return false;
        var Snapshot = new Hashtable(Data.Count);
        foreach (var Pair in Data) Snapshot.Add(Pair.Key, PhotonPropertyProtocol.Snapshot(Pair.Value));
        var Bytes = GeneratedRpcDeferral.SerializePhotonValue(Snapshot).Length;
        if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes - Pending.CallBytes)
            throw new InvalidOperationException("Native actor initialization exceeded the deferred network capacity.");
        Pending.CallBytes += Bytes; Pending.Calls.Add((Snapshot, Sender)); return true;
    }

    static int SourceSeed(ActorSource Source) => SourceSeed(Source.Occurrence.ActivationSeed, Source.Key);

    static int SourceSeed(int Seed, string Key)
    {
        unchecked
        {
            foreach (var Character in Key) Seed = (Seed ^ Character) * 16777619;
            return Seed;
        }
    }

    static UnityEngine.Random.State? BeginSeededStart(Component Source)
    {
        var Admission = Map.CurrentRun?.NativeSpawns;
        if (Admission?.Bind(Source) is null || !Admission.ActorSources.TryGetValue(Source, out var Actor)) return null;
        var State = UnityEngine.Random.state; UnityEngine.Random.InitState(SourceSeed(Actor)); return State;
    }

    static Exception? EndSeededStart(UnityEngine.Random.State? State, Exception? Failure)
    {
        if (State is { } Previous) UnityEngine.Random.state = Previous;
        return Failure;
    }

    [HarmonyPatch(typeof(PhotonNetwork), "NetworkInstantiate", new[] { typeof(Photon.Pun.InstantiateParameters), typeof(bool), typeof(bool) })]
    static class ActorBirthPatch
    {
        [HarmonyPrefix]
        static void Prefix(Photon.Pun.InstantiateParameters parameters, bool roomObject, out ActorOrigin? __state)
        {
            __state = ParseOrigin(parameters);
            if (__state is not null && roomObject)
                throw new InvalidOperationException("A native zone actor cannot use room-object ownership.");
        }
        [HarmonyPostfix]
        static void Postfix(GameObject __result, ActorOrigin? __state)
        {
            if (__state is null || !__result) return;
            var View = __result.GetComponent<PhotonView>();
            var Actor = new ActorEnvelope(View, __state);
            foreach (var Child in __result.GetComponentsInChildren<PhotonView>(true)) NativeActors.Add(Child, Actor);
            if (Map.CurrentRun is { HasPublishedLayout: true } Run) Run.NativeSpawns.Adopt(Actor);
        }
    }

    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.Instantiate),
        new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(byte), typeof(object[]) })]
    static class FixtureBirthMetadataPatch
    {
        [HarmonyPrefix]
        static void Prefix(string prefabName, ref object[] data)
        {
            if (CreatingFixture is not { } Source || prefabName != Source.Prefab.name) return;
            if (data is not null) throw new InvalidOperationException("Native hanging rope creation changed its metadata contract.");
            if (!ReferenceEquals(Map.CurrentRun, Source.Owner.Run) || !Source.View.IsMine)
                throw new InvalidOperationException("Native hanging rope creation lost its map-run authority.");
            data = Source.Owner.Origin(Source).Encode();
        }
    }

    [HarmonyPatch(typeof(PhotonNetwork), "ExecuteRpc", typeof(Hashtable), typeof(Photon.Realtime.Player))]
    static class ActorRpcPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Hashtable rpcData, Photon.Realtime.Player sender) => !DeferActorRpc(rpcData, sender);
    }

    [HarmonyPatch(typeof(global::Tornado), nameof(global::Tornado.RPCA_InitTornado))]
    static class TornadoInitPatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Tornado __instance, int targetViewID)
        {
            var View = __instance.GetComponent<PhotonView>();
            if (!NativeActors.TryGetValue(View, out var Actor)) return true;
            if (targetViewID != Actor.Origin.SourceViewId) throw new InvalidOperationException("A Tornado initialization changed its declared source view.");
            return !__instance.targetParent;
        }
    }

    [HarmonyPatch(typeof(global::Tornado), nameof(global::Tornado.Update))]
    static class TornadoUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Tornado __instance) => Ready(__instance.view);
    }

    [HarmonyPatch(typeof(global::Tornado), nameof(global::Tornado.FixedUpdate))]
    static class TornadoFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Tornado __instance) => Ready(__instance.view);
    }

    [HarmonyPatch(typeof(TumbleWeed), nameof(TumbleWeed.FixedUpdate))]
    static class TumbleweedFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(TumbleWeed __instance) => Ready(__instance.photonView);
    }

    [HarmonyPatch(typeof(MushroomZombie), nameof(MushroomZombie.Update))]
    static class ZombieUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(MushroomZombie __instance) => Ready(__instance.view);
    }

    [HarmonyPatch(typeof(PhotonView), "OnDestroy")]
    static class ActorDestroyPatch
    {
        [HarmonyPrefix]
        static void Prefix(PhotonView __instance)
        {
            var Admission = Map.CurrentRun?.NativeSpawns; Admission?.Sources.Remove(__instance);
            if (Admission is not null)
                foreach (var Source in new List<ActorSource>(Admission.ActorSources.Values))
                    if (Source is FixtureSource Fixture && Fixture.View == __instance) Admission.RetireFixture(Fixture);
            if (!NativeActors.TryGetValue(__instance, out var Actor) || Actor.View != __instance) return;
            var SourceBinding = Actor.Binding as ActorBinding.Bound;
            Actor.Binding = ActorBinding.Retired.Instance; Admission?.Outputs.Remove(__instance);
            if (SourceBinding is { } Bound)
            {
                Bound.Source.Population.Actors.Remove(__instance);
                if (Bound.Source is FixtureSource Fixture && !Fixture.Retired)
                {
                    if (Fixture.View.IsMine) Fixture.Owner.FixtureRequests.Add(Fixture);
                    Fixture.ReleaseLocal(Actor.View.gameObject);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TornadoSpawner), nameof(TornadoSpawner.SpawnTornado))]
    static class TornadoSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(TornadoSpawner __instance) => Map.CurrentRun?.NativeSpawns.SpawnTornado(__instance) ?? true;
    }

    [HarmonyPatch(typeof(NetworkedObjectSpawner), nameof(NetworkedObjectSpawner.SpawnObject))]
    static class TumbleweedSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(NetworkedObjectSpawner __instance) => Map.CurrentRun?.NativeSpawns.SpawnTumbleweed(__instance) ?? true;
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.Spawn))]
    static class ZombieSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MushroomZombieSpawner __instance) => Map.CurrentRun?.NativeSpawns.SpawnZombie(__instance) ?? true;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRun))]
    static class NativeRunStartedPatch
    {
        [HarmonyPostfix]
        static void Postfix(RunManager __instance)
        {
            var Admission = Map.CurrentRun?.NativeSpawns;
            if (Admission is not null && ReferenceEquals(Admission.NativeRun, __instance))
                Admission.ReadyCurrentOccurrence()?.PublishCurrentEntityPopulation();
        }
    }

    [HarmonyPatch(typeof(TornadoSpawner), nameof(TornadoSpawner.Start))]
    static class TornadoSpawnerStartPatch
    {
        [HarmonyPrefix]
        static void Prefix(TornadoSpawner __instance, out UnityEngine.Random.State? __state) => __state = BeginSeededStart(__instance);
        [HarmonyFinalizer]
        static Exception? Finalizer(UnityEngine.Random.State? __state, Exception? __exception) => EndSeededStart(__state, __exception);
    }

    [HarmonyPatch(typeof(NetworkedObjectSpawner), nameof(NetworkedObjectSpawner.Start))]
    static class NetworkedObjectSpawnerStartPatch
    {
        [HarmonyPrefix]
        static void Prefix(NetworkedObjectSpawner __instance, out UnityEngine.Random.State? __state) => __state = BeginSeededStart(__instance);
        [HarmonyFinalizer]
        static Exception? Finalizer(UnityEngine.Random.State? __state, Exception? __exception) => EndSeededStart(__state, __exception);
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.Start))]
    static class MushroomZombieSpawnerStartPatch
    {
        [HarmonyPrefix]
        static void Prefix(MushroomZombieSpawner __instance, out UnityEngine.Random.State? __state) => __state = BeginSeededStart(__instance);
        [HarmonyFinalizer]
        static Exception? Finalizer(UnityEngine.Random.State? __state, Exception? __exception) => EndSeededStart(__state, __exception);
    }

    [HarmonyPatch(typeof(Condor), nameof(Condor.Start))]
    static class CondorStartPatch
    {
        [HarmonyPrefix]
        static void Prefix(Condor __instance) => Map.CurrentRun?.NativeSpawns.Bind(__instance);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.SetState))]
    static class RetainedItemOwnershipPatch
    {
        [HarmonyPostfix]
        static void Postfix(Item __instance)
        {
            if (__instance.itemState is ItemState.Held or ItemState.InBackpack)
                Map.CurrentRun?.NativeSpawns.Sources.Remove(__instance.view);
        }
    }

    [HarmonyPatch(typeof(NetworkedObjectSpawner), nameof(NetworkedObjectSpawner.Update))]
    static class NetworkedObjectSpawnerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(NetworkedObjectSpawner __instance) => Ready(__instance);
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.ReadyToSpawn))]
    static class MushroomZombieReadyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MushroomZombieSpawner __instance, ref bool __result)
        {
            var Admission = Map.CurrentRun?.NativeSpawns;
            if (Admission?.PendingZombie is { } Pending)
            {
                __result = ReferenceEquals(Pending, __instance)
                    && Admission.CanCompleteZombie(Admission.ActorSources[Pending].Occurrence);
                return false;
            }
            if (Ready(__instance)) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.OnDestroy))]
    static class MushroomZombieSpawnerDestroyPatch
    {
        [HarmonyPrefix]
        static void Prefix(MushroomZombieSpawner __instance)
        {
            var Admission = Map.CurrentRun?.NativeSpawns;
            if (Admission is null || !Admission.Sources.ContainsKey(__instance)) return;
            RetireOutput(__instance); Admission.RemoveSource(__instance);
        }
    }
}
