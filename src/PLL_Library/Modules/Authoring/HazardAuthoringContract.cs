using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal abstract class HazardBehaviorContract
{
    internal abstract void ValidateMetadata(PllHazardAuthoring Source);
    internal abstract void WriteManifest(BinaryWriter Writer);
    internal abstract HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal);
    internal virtual int RequiredMechanicalMask => 0;
    internal virtual void ResolveExternal(NativeMechanicalHazards? Providers) { }
    internal virtual void ClearExternal() { }
}

internal abstract class HazardInstance
{
    readonly MapRun Run;
    protected bool Enabled;
    protected bool Retired;

    protected HazardInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence)
    {
        this.Source = Source; this.Occurrence = Occurrence;
        Enabled = Source.InitiallyEnabled;
        Run = Occurrence.Run;
    }

    internal PllHazardAuthoring Source { get; }
    internal FeatureOccurrence Occurrence { get; }
    internal bool AdmissionReady => !Retired && Source && ReferenceEquals(Map.CurrentRun, Run)
        && Run.Active && Run.IsOccurrenceReady(Occurrence);
    internal bool IsReady => AdmissionReady && Enabled && Source.enabled && Source.gameObject.activeInHierarchy;
    internal abstract void Publish();
    internal abstract void SetEnabled(bool Value);
    internal abstract void Suspend();
    internal abstract void Resume();
    internal abstract void Retire();
}

internal sealed class HazardAuthoringContract
{
    readonly ComponentBinding<PllHazardAuthoring> Component;
    readonly ObjectBinding Model;
    readonly byte[] DefinitionFingerprint;
    readonly HazardBehaviorContract Behavior;
    readonly bool InitiallyEnabled;

    HazardAuthoringContract(AuthoringBindingOwner Owner, PllHazardAuthoring Source, int FixedPlacementOrdinal)
    {
        this.Owner = Owner;
        this.FixedPlacementOrdinal = FixedPlacementOrdinal;
        Component = ComponentBinding<PllHazardAuthoring>.Create(Owner.BindingRoot, Source);
        Model = ObjectBinding.Create(Source.transform, Source.ModelRoot);
        Id = Source.Id; InitiallyEnabled = Source.InitiallyEnabled;
        DefinitionFingerprint = DeterministicObjectIdentity.HashObjectHierarchy(Source.transform, "PLL/HazardDefinition/v1");
        Behavior = Source.Behavior switch
        {
            PllContactInjuryBehavior Contact => new ContactInjury(Source, Contact),
            PllArrowShooterBehavior or PllMovingSawBehavior or PllSpikeTrapBehavior => NativeMechanicalHazards.Prepare(Source),
            PllPluginHazardBehavior => PluginEntityPrototype.PrepareHazard(Source),
            _ => throw new ArgumentException("Hazard behavior is unsupported.", nameof(Source))
        };
    }

    internal string Id { get; }
    internal PllHazardAuthoring Source => Component.Source;
    internal AuthoringBindingOwner Owner { get; }
    internal bool Generated => Owner.Generated;
    internal ComponentBinding<PllHazardAuthoring> SourceBinding => Component;
    internal int FixedPlacementOrdinal { get; }
    internal int RequiredMechanicalMask => Behavior.RequiredMechanicalMask;
    internal void ResolveExternal(NativeMechanicalHazards? Providers) => Behavior.ResolveExternal(Providers);
    internal void ClearExternal() => Behavior.ClearExternal();

    internal static HazardAuthoringContract[] CreateAll(Transform Template, Transform Floor,
        Transform Campfire, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PllHazardAuthoring>(Template, Biome, Graph, "Hazard");
        var Result = new HazardAuthoringContract[Entries.Count];
        var FixedOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var Index = 0; Index < Result.Length; Index++)
        {
            var Entry = Entries[Index];
            if (PllHazardValueContract.Failure(Entry.Value) is { } Failure)
                throw new ArgumentException($"{Failure} Biome '{Biome}'.", nameof(Template));
            if (TransformOwnership.HasPropSpawnerInPath(Entry.Owner.BindingRoot, Entry.Value.transform)
                || !Entry.Owner.Generated && (!TransformOwnership.Contains(Floor, Entry.Value.transform)
                    && !TransformOwnership.Contains(Campfire, Entry.Value.transform)))
                throw new ArgumentException($"Biome '{Biome}' Hazard must belong to a physical feature or reachable candidate.", nameof(Template));
            for (var Previous = 0; Previous < Index; Previous++)
                if (ReferenceEquals(Result[Previous].Owner.SourceRoot, Entry.Owner.SourceRoot)
                    && TransformOwnership.Overlaps(Result[Previous].Source.transform, Entry.Value.transform))
                    throw new ArgumentException($"Biome '{Biome}' Hazard definitions must have separate instance roots.", nameof(Template));
            var Ordinal = 0;
            if (!Entry.Owner.Generated)
            {
                FixedOrdinals.TryGetValue(Entry.Value.Id, out Ordinal);
                FixedOrdinals[Entry.Value.Id] = Ordinal + 1;
            }
            Result[Index] = new HazardAuthoringContract(Entry.Owner, Entry.Value, Ordinal);
        }
        return Result;
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Source.transform, Biome, "Hazard");
        Component.Validate(Owner.BindingRoot, Biome, "Hazard");
        Model.Validate(Source.transform, Biome, "Hazard model");
        if (Source.Id != Id || Source.ModelRoot != Model.Source || Source.InitiallyEnabled != InitiallyEnabled
            || !DeterministicObjectIdentity.SameHash(DefinitionFingerprint,
                DeterministicObjectIdentity.HashObjectHierarchy(Source.transform, "PLL/HazardDefinition/v1")))
            throw new InvalidOperationException($"Biome '{Biome}' changed Hazard '{Id}' before sealing.");
        Behavior.ValidateMetadata(Source);
    }

    internal HazardInstance BindFixed(Transform Root, BiomeId Biome, FeatureOccurrence Occurrence, int Ordinal) =>
        Bind(Component.Resolve(Root, Biome, "Hazard"), Occurrence, Ordinal);
    internal PllHazardAuthoring ResolveFixedSource(Transform Root, BiomeId Biome) =>
        Component.Resolve(Root, Biome, "Hazard");

    internal bool TryBindGenerated(PllHazardAuthoring Value, BiomeId Biome,
        FeatureOccurrence Occurrence, int Ordinal, out HazardInstance Instance)
    {
        Instance = null!;
        if (!Value || !Owner.TryGetAdmittedGeneratedRoot(Value.transform, out var Root)
            || !ReferenceEquals(Component.Resolve(Root, Biome, "Hazard"), Value)) return false;
        Instance = Bind(Value, Occurrence, Ordinal); return true;
    }

    HazardInstance Bind(PllHazardAuthoring Value, FeatureOccurrence Occurrence, int Ordinal)
    {
        if (Value.Id != Id || Value.InitiallyEnabled != InitiallyEnabled)
            throw new InvalidOperationException("A placed Hazard changed its sealed definition.");
        Behavior.ValidateMetadata(Value);
        if (Value.Binding is not null)
            throw new InvalidOperationException("A Hazard has two creation owners.");
        var Instance = Behavior.Bind(Value, Occurrence, Ordinal);
        Value.Binding = Instance;
        return Instance;
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Owner.WriteManifest(Writer); Component.WriteManifest(Writer); Model.WriteManifest(Writer);
        Writer.Write(Id); Writer.Write(InitiallyEnabled); Writer.Write(FixedPlacementOrdinal);
        Writer.Write(DefinitionFingerprint.Length); Writer.Write(DefinitionFingerprint);
        Behavior.WriteManifest(Writer);
    }

    sealed class ContactInjury : HazardBehaviorContract
    {
        readonly ComponentBinding<Collider> Contact;
        readonly PllContactInjuryBehavior Settings;

        internal ContactInjury(PllHazardAuthoring Source, PllContactInjuryBehavior Behavior)
        {
            Contact = ComponentBinding<Collider>.Create(Source.transform, Behavior.Contact);
            Settings = new PllContactInjuryBehavior
            {
                InjuryAmount = Behavior.InjuryAmount, CooldownSeconds = Behavior.CooldownSeconds,
                Knockback = Behavior.Knockback, RagdolledKnockback = Behavior.RagdolledKnockback,
                ForwardBias = Behavior.ForwardBias, KnockbackOnlyWhenStanding = Behavior.KnockbackOnlyWhenStanding,
                PreventFallDamage = Behavior.PreventFallDamage
            };
            RequireCompiled(Source, Behavior.Contact);
        }

        internal override void ValidateMetadata(PllHazardAuthoring Source)
        {
            if (Source.Behavior is not PllContactInjuryBehavior Value
                || Value.InjuryAmount != Settings.InjuryAmount || Value.CooldownSeconds != Settings.CooldownSeconds
                || Value.Knockback != Settings.Knockback || Value.RagdolledKnockback != Settings.RagdolledKnockback
                || Value.ForwardBias != Settings.ForwardBias
                || Value.KnockbackOnlyWhenStanding != Settings.KnockbackOnlyWhenStanding
                || Value.PreventFallDamage != Settings.PreventFallDamage)
                throw new InvalidOperationException("Contact Injury changed its sealed settings.");
            if (PllHazardValueContract.ContactFailure(Source.transform, Source.ModelRoot, Value) is { } Failure)
                throw new InvalidOperationException(Failure);
            if (!ReferenceEquals(Contact.Resolve(Source.transform, default, "Hazard contact"), Value.Contact))
                throw new InvalidOperationException("Contact Injury changed its owned collider binding.");
            RequireCompiled(Source, Value.Contact);
        }

        static void RequireCompiled(PllHazardAuthoring Source, Collider Contact)
        {
            var Gates = Contact.GetComponents<PllHazardContact>();
            var Modifiers = Contact.GetComponents<CollisionModifier>();
            if (Gates.Length != 1 || Gates[0].Definition != Source || Modifiers.Length != 1
                || Modifiers[0].onCollide is not null || Modifiers[0].applyPetrify
                || Modifiers[0].useBounceCoroutine || Modifiers[0].additionalMods.Count != 0
                || Modifiers[0].bounceAnimator || Modifiers[0].bounceSFX)
                throw new InvalidOperationException("Contact Injury has no isolated compiled native contact graph.");
        }

        internal override HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal)
        {
            var Value = (PllContactInjuryBehavior)Source.Behavior;
            return new ContactInstance(Source, Occurrence, Value.Contact.GetComponent<PllHazardContact>(), Settings);
        }

        internal override void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write("ContactInjury/v1"); Contact.WriteManifest(Writer);
            ManifestEncoding.WriteScalar(Writer, Settings.InjuryAmount);
            ManifestEncoding.WriteScalar(Writer, Settings.CooldownSeconds);
            ManifestEncoding.WriteScalar(Writer, Settings.Knockback);
            ManifestEncoding.WriteScalar(Writer, Settings.RagdolledKnockback);
            ManifestEncoding.WriteScalar(Writer, Settings.ForwardBias);
            Writer.Write(Settings.KnockbackOnlyWhenStanding); Writer.Write(Settings.PreventFallDamage);
        }
    }

    sealed class ContactInstance : HazardInstance
    {
        readonly PllHazardContact Gate;
        readonly PllContactInjuryBehavior Settings;
        CollisionModifier? Modifier;

        internal ContactInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence,
            PllHazardContact Gate, PllContactInjuryBehavior Settings) : base(Source, Occurrence)
        {
            if (Gate.Binding is not null) throw new InvalidOperationException("A contact Hazard has two instance owners.");
            this.Gate = Gate; this.Settings = Settings;
            Modifier = Gate.GetComponent<CollisionModifier>();
            Gate.Binding = this;
            Configure();
        }

        void Configure()
        {
            Modifier!.applyEffects = IsReady;
            Modifier.statusType = CharacterAfflictions.STATUSTYPE.Injury;
            Modifier.damage = Settings.InjuryAmount; Modifier.cooldown = Settings.CooldownSeconds;
            Modifier.knockback = Settings.Knockback; Modifier.ragdolledKnockback = Settings.RagdolledKnockback;
            Modifier.knockbackTowardsFwdVector = Settings.ForwardBias;
            Modifier.knockbackOnlyWhenStanding = Settings.KnockbackOnlyWhenStanding;
            Modifier.preventFallDamageIfKnockback = Settings.PreventFallDamage;
            Modifier.staminaBackOnBounce = 0f;
        }

        internal override void Publish()
        {
            if (Modifier) Modifier!.applyEffects = IsReady;
        }
        internal override void SetEnabled(bool Value)
        {
            if (Retired) return;
            Enabled = Value;
            if (Modifier) Modifier!.applyEffects = IsReady;
        }
        internal override void Suspend()
        {
            if (Retired || !Modifier) return;
            Modifier!.applyEffects = false;
            if (Gate.gameObject.activeInHierarchy) return;
            ReleaseModifier();
        }
        internal override void Resume()
        {
            if (Retired) return;
            if (!Modifier) Modifier = Gate.gameObject.AddComponent<CollisionModifier>();
            Configure();
        }
        internal override void Retire()
        {
            if (Retired) return;
            Retired = true; ReleaseModifier();
            if (Gate) Gate.Binding = null;
        }
        void ReleaseModifier()
        {
            var Owned = Modifier; Modifier = null;
            if (!Owned) return;
            Owned!.applyEffects = false; Owned.StopAllCoroutines();
            UnityEngine.Object.Destroy(Owned);
        }
        internal bool Owns(CollisionModifier Value) => ReferenceEquals(Modifier, Value);
    }

    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    static class ContactGatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(CollisionModifier __instance)
        {
            var Gate = __instance.GetComponent<PllHazardContact>();
            return !Gate || Gate.isActiveAndEnabled && Gate.Binding is ContactInstance Owner
                && Owner.Owns(__instance) && Owner.IsReady;
        }
    }
}
