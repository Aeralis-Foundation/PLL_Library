using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class PluginEntityFactoryRegistry
{
    static readonly Dictionary<(string PluginGuid, string FactoryId), Provider> Providers = new();
    static readonly ConditionalWeakTable<IPllPluginEntityInstance, LeaseOwnership> Instances = new();
    static bool Sealed;

    internal static void Register(BaseUnityPlugin Plugin, string FactoryId,
        string Revision, IPllPluginEntityFactory Factory)
    {
        if (Sealed) throw new InvalidOperationException("Entity factory registration is sealed.");
        if (!Plugin) throw new ArgumentNullException(nameof(Plugin));
        if (!PllAuthoringValueContract.IsLocalId(FactoryId)
            || !PllAuthoringValueContract.IsLocalId(Revision))
            throw new ArgumentException("Entity factory and revision must be stable local IDs.");
        if (Factory is null) throw new ArgumentNullException(nameof(Factory));
        var Info = Plugin.Info;
        var Guid = Info.Metadata.GUID;
        if (!PllAuthoringValueContract.IsLocalId(Guid)
            || !Chainloader.PluginInfos.TryGetValue(Guid, out var Installed)
            || !ReferenceEquals(Installed, Info))
            throw new ArgumentException("Entity factory provider must be an installed plugin.", nameof(Plugin));
        var Key = (Guid, FactoryId);
        if (Providers.ContainsKey(Key))
            throw new InvalidOperationException($"Entity factory '{Guid}/{FactoryId}' is already registered.");
        Providers.Add(Key, new Provider(Plugin, Info, Revision, Factory));
    }

    internal static void Seal() => Sealed = true;

    internal static Provider Require(string PluginGuid, string FactoryId, string Revision)
    {
        if (!Sealed) throw new InvalidOperationException("Entity factory registration is not sealed.");
        if (!Providers.TryGetValue((PluginGuid, FactoryId), out var Provider))
            throw new InvalidOperationException($"Entity factory '{PluginGuid}/{FactoryId}' is unavailable.");
        if (Provider.Revision != Revision)
            throw new InvalidOperationException($"Entity factory '{PluginGuid}/{FactoryId}' revision '{Provider.Revision}' does not match '{Revision}'.");
        Provider.ConfirmInstallation();
        return Provider;
    }

    internal sealed class Provider
    {
        readonly BaseUnityPlugin Plugin;
        readonly PluginInfo Info;
        readonly string FactoryType;
        readonly Guid PluginModule;
        readonly Guid FactoryModule;
        bool InstallationConfirmed;

        internal Provider(BaseUnityPlugin Plugin, PluginInfo Info,
            string Revision, IPllPluginEntityFactory Factory)
        {
            this.Plugin = Plugin; this.Info = Info; this.Revision = Revision;
            this.Factory = Factory;
            var Type = Factory.GetType();
            FactoryType = DeterministicObjectIdentity.StableTypeName(Type);
            PluginModule = Plugin.GetType().Assembly.ManifestModule.ModuleVersionId;
            FactoryModule = Type.Assembly.ManifestModule.ModuleVersionId;
        }

        internal string Revision { get; }
        internal IPllPluginEntityFactory Factory { get; }
        internal Transform Root => Plugin.transform;

        internal IPllPluginEntityInstance Bind(GameObject Entity, PllPluginEntitySpawn Spawn)
        {
            var Instance = Factory.Bind(Entity, Spawn)
                ?? throw new InvalidOperationException("Entity factory returned no instance lifetime.");
            try { Instances.Add(Instance, new LeaseOwnership(this)); }
            catch (ArgumentException Failure)
            {
                throw new InvalidOperationException("Entity factory reused an instance lifetime.", Failure);
            }
            return Instance;
        }

        internal void Release(IPllPluginEntityInstance Instance)
        {
            if (!Instances.TryGetValue(Instance, out var Ownership) || Ownership.Owner != this || Ownership.Released)
                throw new InvalidOperationException("Entity factory lifetime has no release owner.");
            Ownership.Released = true;
            Instance.Dispose();
        }

        internal void ConfirmInstallation()
        {
            if (InstallationConfirmed) return;
            if (!Plugin || !ReferenceEquals(Info.Instance, Plugin))
                throw new InvalidOperationException($"Entity factory provider '{Info.Metadata.GUID}' did not finish loading.");
            InstallationConfirmed = true;
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(FactoryType);
            Writer.Write(PluginModule.ToByteArray()); Writer.Write(FactoryModule.ToByteArray());
        }
    }

    sealed class LeaseOwnership
    {
        internal LeaseOwnership(Provider Owner) => this.Owner = Owner;
        internal Provider Owner { get; }
        internal bool Released;
    }
}

internal sealed class PluginEntityRecipe : EntityZoneContract.GroundRecipe
{
    readonly string PluginGuid;
    readonly string FactoryId;
    readonly string Revision;
    readonly GameObject Prefab;
    readonly byte[] SourceFingerprint;
    PluginEntityPrototype? Prototype;

    internal PluginEntityRecipe(PllPluginEntityZoneSource Source)
        : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
    {
        if (PllPluginEntityZoneValueContract.PrefabFailure(Source.Prefab) is { } Failure)
            throw new ArgumentException(Failure, nameof(Source));
        PluginGuid = Source.PluginGuid; FactoryId = Source.FactoryId;
        Revision = Source.Revision; Prefab = Source.Prefab;
        SourceFingerprint = DeterministicObjectIdentity.HashObjectHierarchy(Prefab.transform, "PLL/PluginEntitySource/v1");
    }

    internal void Resolve()
    {
        if (!Enabled) return;
        var Provider = PluginEntityFactoryRegistry.Require(PluginGuid, FactoryId, Revision);
        var Model = Prefab.TryGetComponent<PllEntityAuthoring>(out var Entity) ? Entity.ModelRoot
            : Prefab.TryGetComponent<PllHazardAuthoring>(out var Hazard) ? Hazard.ModelRoot : Prefab;
        Prototype = PluginEntityPrototype.Create(Provider, Model, Count);
    }

    internal override bool Matches(PllEntityZoneSource Source) => Source is PllPluginEntityZoneSource Plugin
        && Plugin.PluginGuid == PluginGuid && Plugin.FactoryId == FactoryId && Plugin.Revision == Revision
        && Plugin.Prefab == Prefab
        && Matches(Plugin.MaximumInitialCount, Plugin.SpawnChance, Plugin.MaximumSlope, Plugin.MinimumSpacing);

    internal override EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
        Enabled ? new PluginEntityPopulation(Zone, Output, Volume,
            Prototype ?? throw new InvalidOperationException("The plugin Entity Zone has no prepared prototype."),
            Count, Chance, Slope, Spacing) : EntityZoneBinding.Empty;

    internal override void WriteManifest(BinaryWriter Writer, bool External)
    {
        Writer.Write((byte)3); WriteGround(Writer);
        Writer.Write(PluginGuid); Writer.Write(FactoryId); Writer.Write(Revision);
        Writer.Write(SourceFingerprint.Length); Writer.Write(SourceFingerprint);
        if (External && Enabled)
            (Prototype ?? throw new InvalidOperationException("The plugin Entity Zone has no live prototype.")).WriteManifest(Writer);
    }

    internal override void ClearExternal()
    {
        var Owned = Prototype; Prototype = null; Owned?.Dispose();
    }

}

internal sealed class PluginEntityPrototype : IDisposable
{
    readonly GameObject Holder;
    readonly byte[] NetworkFingerprint;
    readonly int ViewCapacity;
    readonly int MaximumCount;

    PluginEntityPrototype(PluginEntityFactoryRegistry.Provider Provider,
        GameObject Holder, GameObject Prefab, GeneratedNetworkContract Network, int ViewCapacity, int MaximumCount)
    {
        this.Provider = Provider; this.Holder = Holder; this.Prefab = Prefab;
        NetworkFingerprint = Network.Hash; this.ViewCapacity = ViewCapacity; this.MaximumCount = MaximumCount;
    }

    internal PluginEntityFactoryRegistry.Provider Provider { get; }
    internal GameObject Prefab { get; }

    internal static PluginEntityPrototype Create(
        PluginEntityFactoryRegistry.Provider Provider, GameObject Source, int Count)
    {
        var Capacity = PhotonNetwork.MAX_VIEW_IDS - 1;
        if (Count > Capacity)
            throw new InvalidOperationException($"Plugin entity count {Count} exceeds the static PhotonView capacity {Capacity}.");
        var Parent = Provider.Root;
        var Holder = new GameObject("__PLL_PluginEntityPrototype");
        Holder.SetActive(false); Holder.transform.SetParent(Parent, false);
        GameObject? Prefab = null;
        try
        {
            Prefab = UnityEngine.Object.Instantiate(Source, Holder.transform, false);
            Prefab.SetActive(false);
            Provider.Factory.ConfigurePrototype(Prefab);
            if (!Prefab || Prefab.activeSelf || Prefab.transform.parent != Holder.transform)
                throw new InvalidOperationException("Entity factory changed its inactive prototype ownership.");
            var Views = new List<PhotonView>();
            foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
            {
                if (!Component)
                    throw new InvalidOperationException("Entity factory prototype contains a missing component.");
                if (Component is LevelGenStep or ILevelGenFinalizer or ISpawner)
                    throw new InvalidOperationException("Entity factory prototype contains an unsupported generator or dynamic spawner.");
                if (Component is not PhotonView View) continue;
                if (!View.enabled || View.ViewID != 0 || !PhotonCloneIdentity.IsBatchOrigin(View))
                    throw new InvalidOperationException("Entity factory prototype requires enabled, unregistered static PhotonViews.");
                if (View.observableSearch != PhotonView.ObservableSearch.Manual)
                    throw new InvalidOperationException("Entity factory prototype requires explicitly configured Photon observables.");
                Views.Add(View);
            }
            if (Views.Count == 0)
                throw new InvalidOperationException("Entity factory prototype requires a PhotonView.");
            var MaximumCount = Capacity / Views.Count;
            if (Count > MaximumCount)
                throw new InvalidOperationException($"Plugin entity count {Count} exceeds the prepared PhotonView capacity {MaximumCount}.");
            return new PluginEntityPrototype(Provider, Holder, Prefab,
                GeneratedNetworkContract.CreatePrefab(Prefab, Views), Capacity, MaximumCount);
        }
        catch
        {
            if (Prefab) UnityEngine.Object.DestroyImmediate(Prefab!);
            UnityEngine.Object.DestroyImmediate(Holder);
            throw;
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Provider.WriteManifest(Writer); Writer.Write(ViewCapacity);
        Writer.Write(NetworkFingerprint.Length); Writer.Write(NetworkFingerprint);
    }

    internal void AdmitCount(int Count)
    {
        if (Count > MaximumCount)
            throw new InvalidOperationException($"Plugin entity count {Count} exceeds the prepared PhotonView capacity {MaximumCount}.");
    }

    internal EntityBinding Bind(PllEntityAuthoring Source, FeatureOccurrence Occurrence, int Ordinal) =>
        new FixedEntity(Source, Occurrence, this, Ordinal);

    internal IPllPluginEntityInstance BindInstance(GameObject Entity, PllPluginEntitySpawn Spawn)
    {
        var Parent = Entity.transform.parent;
        var Lease = Provider.Bind(Entity, Spawn);
        try
        {
            if (!Entity || Entity.activeSelf || Entity.transform.parent != Parent)
                throw new InvalidOperationException("Entity factory changed its inactive instance ownership.");
            var Views = new List<PhotonView>();
            foreach (var Component in Entity.GetComponentsInChildren<Component>(true))
            {
                if (!Component || Component is LevelGenStep or ILevelGenFinalizer or ISpawner)
                    throw new InvalidOperationException("Entity factory changed its fixed instance graph.");
                if (Component is not PhotonView View) continue;
                if (!View.enabled || View.ViewID != 0 || !PhotonCloneIdentity.IsBatchOrigin(View))
                    throw new InvalidOperationException("Entity factory changed its unregistered static Photon graph.");
                Views.Add(View);
            }
            if (!DeterministicObjectIdentity.SameHash(NetworkFingerprint,
                GeneratedNetworkContract.CreatePrefab(Entity, Views).Hash))
                throw new InvalidOperationException("Entity factory changed its prepared Photon graph.");
            return Lease;
        }
        catch (Exception Failure)
        {
            try { Provider.Release(Lease); }
            catch (Exception Cleanup)
            {
                throw new AggregateException("Entity factory binding and cleanup both failed.", Failure, Cleanup);
            }
            throw;
        }
    }

    internal static HazardBehaviorContract PrepareHazard(PllHazardAuthoring Source) =>
        new FixedHazardRecipe(Source);

    static PllPluginEntitySpawn Spawn(FeatureOccurrence Occurrence, Component Source, string Id, int Ordinal)
    {
        var Slot = Occurrence.Slots[0];
        var Placement = Source.GetComponentInParent<PllGeneratedPrefabIdentity>()?.ActorPlacement;
        var Seed = Placement is { } Identity ? FeatureGeneration.FeatureSeed(
            Identity.GenerationSeed, Identity.Revision, Identity.GenerationOwner) : Occurrence.GenerationSeed;
        return new(Slot.Source.StableId, Slot.Index, Id, Ordinal,
            FeatureGeneration.FeatureSeed(Seed, Ordinal, Id));
    }

    internal GameObject CreateBody(GameObject SourceRoot, GameObject Model)
    {
        var Body = UnityEngine.Object.Instantiate(Prefab, SourceRoot.transform, false);
        try { PhotonCloneIdentity.Reset(Body); Model.SetActive(false); return Body; }
        catch { UnityEngine.Object.DestroyImmediate(Body); throw; }
    }

    sealed class FixedEntity : EntityBinding
    {
        readonly FixedBody Instance;

        internal FixedEntity(PllEntityAuthoring Source, FeatureOccurrence Occurrence,
            PluginEntityPrototype Prototype, int Ordinal) : base(Source, Occurrence)
            => Instance = new(Prototype, Source.gameObject, Source.ModelRoot, Spawn(Occurrence, Source, Source.Id, Ordinal));

        internal override void Publish()
        {
            if (IsReady) Instance.Publish();
        }
        internal override void SetEnabled(bool Value)
        {
            if (!Value) throw new InvalidOperationException("Installed fixed behavior does not support reversible source disabling.");
        }
        internal override void Suspend() { if (Instance.Published) Retire(); }
        internal override void Resume() => Publish();
        internal override void Retire()
        {
            if (Retired) return;
            Retired = true; Instance.Retire();
        }
    }

    sealed class FixedHazardRecipe : HazardBehaviorContract
    {
        readonly string PluginGuid;
        readonly string FactoryId;
        readonly string Revision;
        readonly GameObject Model;
        PluginEntityPrototype? Prototype;

        internal FixedHazardRecipe(PllHazardAuthoring Source)
        {
            var Behavior = (PllPluginHazardBehavior)Source.Behavior;
            if (PllPluginEntityZoneValueContract.DefinitionFailure(
                Source.gameObject, Source.ModelRoot, Source.InitiallyEnabled) is { } Failure)
                throw new ArgumentException(Failure, nameof(Source));
            PluginGuid = Behavior.PluginGuid; FactoryId = Behavior.FactoryId; Revision = Behavior.Revision;
            Model = Source.ModelRoot;
        }

        internal override void ResolveExternal(NativeMechanicalHazards? Providers) => Prototype = Create(
            PluginEntityFactoryRegistry.Require(PluginGuid, FactoryId, Revision), Model, 1);
        internal override void ValidateMetadata(PllHazardAuthoring Source)
        {
            if (Source.Behavior is not PllPluginHazardBehavior Behavior || Behavior.PluginGuid != PluginGuid
                || Behavior.FactoryId != FactoryId || Behavior.Revision != Revision)
                throw new InvalidOperationException("Hazard changed its installed factory definition.");
        }
        internal override HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal) =>
            new FixedHazard(Source, Occurrence, Prototype
                ?? throw new InvalidOperationException("Hazard plugin prototype is not prepared."), Ordinal);
        internal override void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(PluginGuid); Writer.Write(FactoryId); Writer.Write(Revision);
            Writer.Write(Prototype is not null); Prototype?.WriteManifest(Writer);
        }
        internal override void ClearExternal() { var Owned = Prototype; Prototype = null; Owned?.Dispose(); }
    }

    sealed class FixedHazard : HazardInstance
    {
        readonly FixedBody Instance;

        internal FixedHazard(PllHazardAuthoring Source, FeatureOccurrence Occurrence,
            PluginEntityPrototype Prototype, int Ordinal) : base(Source, Occurrence)
            => Instance = new(Prototype, Source.gameObject, Source.ModelRoot, Spawn(Occurrence, Source, Source.Id, Ordinal));
        internal override void Publish()
        {
            if (IsReady) Instance.Publish();
        }
        internal override void SetEnabled(bool Value)
        {
            if (!Value) throw new InvalidOperationException("Installed fixed behavior does not support reversible source disabling.");
        }
        internal override void Suspend() { if (Instance.Published) Retire(); }
        internal override void Resume() => Publish();
        internal override void Retire()
        {
            if (Retired) return;
            Retired = true; Instance.Retire();
        }
    }

    sealed class FixedBody
    {
        readonly PluginEntityFactoryRegistry.Provider Provider;
        readonly GameObject Body;
        IPllPluginEntityInstance? Lease;
        internal bool Published { get; private set; }

        internal FixedBody(PluginEntityPrototype Prototype, GameObject Root, GameObject Model, PllPluginEntitySpawn Spawn)
        {
            Provider = Prototype.Provider; Body = Prototype.CreateBody(Root, Model);
            try { Lease = Prototype.BindInstance(Body, Spawn); }
            catch { UnityEngine.Object.DestroyImmediate(Body); throw; }
        }
        internal void Publish()
        {
            if (Published) return;
            Published = true; Body.SetActive(true); Lease!.Admit();
        }
        internal void Retire()
        {
            var Owned = Lease; Lease = null;
            try { if (Owned is not null) Provider.Release(Owned); }
            finally { if (Body) UnityEngine.Object.DestroyImmediate(Body); }
        }
    }

    public void Dispose() => UnityEngine.Object.DestroyImmediate(Holder);
}

internal sealed class PluginEntityPopulation : EntityZoneBinding.GroundPopulation
{
    readonly PllEntityZoneAuthoring Zone;
    readonly PluginEntityPrototype Prototype;
    readonly List<(GameObject Entity, IPllPluginEntityInstance Lease)> Instances;
    FeatureOccurrence Occurrence = null!;
    bool AdmissionConsumed;

    internal PluginEntityPopulation(PllEntityZoneAuthoring Zone, Transform Output,
        PllZoneVolume Volume, PluginEntityPrototype Prototype, int Count, float Chance, float Slope, float Spacing)
        : base(Zone.transform, Output, Volume, Prototype.Prefab, Count, Chance, Slope, Spacing)
    {
        this.Zone = Zone; this.Prototype = Prototype; Instances = new(Count);
    }

    internal void AttachOccurrence(FeatureOccurrence Occurrence) => this.Occurrence = Occurrence;

    internal override IEnumerator Generate()
    {
        var Routine = base.Generate();
        try { while (Routine.MoveNext()) yield return Routine.Current; }
        finally { (Routine as IDisposable)?.Dispose(); }
        BindInstances();
    }

    internal override void GenerateImmediately() { base.GenerateImmediately(); BindInstances(); }

    void BindInstances()
    {
        try
        {
            var Root = Output.GetChild(0);
            var Slot = Occurrence.Slots[0];
            for (var Ordinal = 0; Ordinal < Root.childCount; Ordinal++)
            {
                var Entity = Root.GetChild(Ordinal).gameObject;
                var Spawn = new PllPluginEntitySpawn(Slot.Source.StableId, Slot.Index, Zone.Id, Ordinal,
                    FeatureGeneration.FeatureSeed(Occurrence.GenerationSeed, Ordinal, Zone.Id));
                var Lease = Prototype.BindInstance(Entity, Spawn);
                Instances.Add((Entity, Lease));
            }
        }
        catch (Exception Failure)
        {
            try { Clear(); }
            catch (Exception Cleanup)
            {
                throw new AggregateException("Plugin entity binding and cleanup both failed.", Failure, Cleanup);
            }
            throw;
        }
    }

    internal override void Publish()
    {
        if (AdmissionConsumed) return;
        AdmissionConsumed = true;
        foreach (var Instance in Instances)
        {
            Instance.Entity.SetActive(true);
            Instance.Lease.Admit();
        }
    }

    internal void Retire()
    {
        AdmissionConsumed = false;
        List<Exception>? Failures = null;
        while (Instances.Count != 0)
        {
            var Index = Instances.Count - 1;
            var Instance = Instances[Index]; Instances.RemoveAt(Index);
            try { Prototype.Provider.Release(Instance.Lease); }
            catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
        }
        if (Failures is not null)
            throw new AggregateException("Plugin entity cleanup failed.", Failures);
    }

    internal override void Clear()
    {
        Exception? Failure = null;
        try { Retire(); }
        catch (Exception Exception) { Failure = Exception; }
        try { base.Clear(); }
        catch (Exception Cleanup)
        {
            if (Failure is not null)
                throw new AggregateException("Plugin entity resources and output cleanup both failed.", Failure, Cleanup);
            throw;
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

}
