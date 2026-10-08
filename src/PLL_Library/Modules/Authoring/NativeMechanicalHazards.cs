using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DG.Tweening;
using HarmonyLib;
using Photon.Pun;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace PLL.Library;

internal sealed class NativeMechanicalHazards
{
    internal const int ArrowMask = 1, SawMask = 2, SpikeMask = 4;
    readonly ArrowProvider? Arrow;
    readonly SawProvider? Saw;
    readonly SpikeProvider? Spike;

    NativeMechanicalHazards(ArrowProvider? Arrow, SawProvider? Saw, SpikeProvider? Spike)
    {
        this.Arrow = Arrow; this.Saw = Saw; this.Spike = Spike;
    }

    internal static Seal BeginSeal(NativeSegmentCatalog Catalog) => new(Catalog);

    internal sealed class Seal
    {
        readonly Lazy<ArrowProvider> Arrow;
        readonly Lazy<SawProvider> Saw;
        readonly Lazy<SpikeProvider> Spike;
        internal Seal(NativeSegmentCatalog Catalog)
        {
            var Sources = new Lazy<Inventory>(() => new(Catalog), LazyThreadSafetyMode.None);
            Arrow = new(() => Capture(Sources.Value.Arrows, Source => new ArrowProvider(Source)), LazyThreadSafetyMode.None);
            Saw = new(() => Capture(Sources.Value.Saws, Source => new SawProvider(Source)), LazyThreadSafetyMode.None);
            Spike = new(() => Capture(Sources.Value.Spikes, Source => new SpikeProvider(Source)), LazyThreadSafetyMode.None);
        }
        internal NativeMechanicalHazards Prepare(int Mask)
        {
            if ((Mask & ~(ArrowMask | SawMask | SpikeMask)) != 0)
                throw new ArgumentException("Mechanical hazard provider mask is unsupported.", nameof(Mask));
            return new((Mask & ArrowMask) != 0 ? Arrow.Value : null,
                (Mask & SawMask) != 0 ? Saw.Value : null, (Mask & SpikeMask) != 0 ? Spike.Value : null);
        }
        static TProvider Capture<TSource, TProvider>(IReadOnlyList<TSource> Sources, Func<TSource, TProvider> Create)
            where TProvider : Provider
        {
            TProvider? Result = null;
            foreach (var Source in Sources)
            {
                var Candidate = Create(Source);
                if (Result is null) { Result = Candidate; continue; }
                if (!Result.Fingerprint.AsSpan().SequenceEqual(Candidate.Fingerprint))
                    throw new InvalidOperationException($"Native {typeof(TSource).Name} providers have different retained recipes.");
            }
            return Result ?? throw new InvalidOperationException("The requested native mechanical hazard provider is unavailable.");
        }
    }

    sealed class Inventory
    {
        internal readonly List<ArrowShooter> Arrows = new();
        internal readonly List<Peak.MovingSawBlade> Saws = new();
        internal readonly List<Peak.SpikeTrap> Spikes = new();
        internal Inventory(NativeSegmentCatalog Catalog)
        {
            var Pending = new Queue<GameObject>(); var Seen = new HashSet<GameObject>();
            var SeenComponents = new HashSet<Component>();
            Add(Catalog.RequireOrdinary("Gloom").Container.gameObject);
            Add(Catalog.RequireOrdinary("Citadel").Container.gameObject);
            while (Pending.Count != 0)
            {
                var Root = Pending.Dequeue();
                foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                {
                    if (!SeenComponents.Add(Component)) continue;
                    switch (Component)
                    {
                        case ArrowShooter Arrow when !Root.scene.IsValid(): Arrows.Add(Arrow); break;
                        case Peak.MovingSawBlade Saw when Saw.hitSound: Saws.Add(Saw); break;
                        case Peak.SpikeTrap Spike when !Root.scene.IsValid(): Spikes.Add(Spike); break;
                        case PropSpawner Spawner:
                            foreach (var Candidate in NativePrefabContracts.EffectiveProps(Spawner) ?? Array.Empty<GameObject>())
                            {
                                if (!Candidate) throw new InvalidOperationException("Native mechanical source contains a missing candidate.");
                                Add(Candidate);
                            }
                            break;
                    }
                }
            }
            void Add(GameObject Root) { if (Seen.Add(Root)) Pending.Enqueue(Root); }
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Arrow is not null); Arrow?.WriteManifest(Writer);
        Writer.Write(Saw is not null); Saw?.WriteManifest(Writer);
        Writer.Write(Spike is not null); Spike?.WriteManifest(Writer);
    }

    internal static HazardBehaviorContract Prepare(PllHazardAuthoring Source)
    {
        if (PllMechanicalHazardValueContract.Failure(Source) is { } Failure)
            throw new ArgumentException(Failure, nameof(Source));
        return Source.Behavior switch
        {
            PllArrowShooterBehavior Arrow => new ArrowRecipe(Source, Arrow),
            PllMovingSawBehavior Saw => new SawRecipe(Source, Saw),
            PllSpikeTrapBehavior Spike => new SpikeRecipe(Source, Spike),
            _ => throw new InvalidOperationException("The validated mechanical hazard profile is unsupported.")
        };
    }

    abstract class Provider
    {
        internal byte[] Fingerprint = null!;
        protected void Seal(Action<BinaryWriter> Write)
        {
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) Write(Writer);
            Fingerprint = Stream.ToArray();
        }
        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
        }
        protected static void Asset(BinaryWriter Writer, Object Asset) =>
            Writer.Write(VisualFingerprintWriter.HashSerializedObject(Asset, "PLL/MechanicalAsset/v1"));
        protected static void Sounds(BinaryWriter Writer, SFX_Instance[] Sounds)
        {
            Writer.Write(Sounds.Length);
            foreach (var Sound in Sounds)
            {
                if (!Sound || Sound.clips is null || Sound.clips.Length == 0 || Sound.settings is null)
                    throw new InvalidOperationException("Native mechanical sound has no clips or settings.");
                NativeFlowProvider.WriteSfx(Writer, Sound);
            }
        }
        internal static int Presentation(BinaryWriter Writer, GameObject Prefab, bool Particles)
        {
            if (!Prefab) throw new InvalidOperationException("Native mechanical presentation is missing.");
            var Nodes = 0;
            foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
            {
                if (Component is Transform) Nodes++;
                if (Component is not Transform and not MeshFilter and not MeshRenderer
                    and not ParticleSystem and not ParticleSystemRenderer and not RemoveAfterSeconds)
                    throw new InvalidOperationException("Native arrow presentation contains an unsupported component.");
            }
            var Hash = DeterministicObjectIdentity.HashObjectHierarchy(Prefab.transform, "PLL/MechanicalPresentation/v1");
            Writer.Write(Hash.Length); Writer.Write(Hash);
            if (Particles)
            {
                var Values = Prefab.GetComponentsInChildren<ParticleSystem>(true);
                Writer.Write(VisualFingerprintWriter.HashParticleGraph(Prefab.transform, Prefab.transform, Values, Values));
            }
            foreach (var Lifetime in Prefab.GetComponentsInChildren<RemoveAfterSeconds>(true))
            {
                if (Lifetime.photonRemove) throw new InvalidOperationException("Native arrow presentation cannot remove Photon entities.");
                ManifestEncoding.WriteScalar(Writer, Lifetime.seconds); Writer.Write(Lifetime.shrink);
            }
            return Nodes;
        }
    }

    sealed class ArrowProvider : Provider
    {
        internal readonly GameObject Arrow;
        internal readonly ParticleSystem Trail, Fired, Empty;
        internal readonly LineRenderer Line;
        internal readonly Vector3 LinePosition, LineScale;
        internal readonly Quaternion LineRotation;
        internal readonly Material ColorblindMaterial;
        internal readonly SFX_Instance[] ShotSounds;
        internal readonly ViewSynchronization Synchronization;
        internal readonly OwnershipOption Ownership;
        internal readonly RunSettings.SETTINGTYPE Setting;
        internal int ArrowNodes { get; private set; }
        internal int TrailNodes { get; private set; }
        internal ArrowProvider(ArrowShooter Source)
        {
            var View = Source.GetComponent<PhotonView>();
            var Gate = Source.GetComponent<DisableBasedOnRunSettings>();
            if (!View || !Gate || View.ObservedComponents.Count != 1
                || View.ObservedComponents[0] != Source || !Source.shooter
                || !Source.lineRenderer || Source.lineRenderer.useWorldSpace
                || !Source.lineRenderer.sharedMaterial || !Source.colorblindLineMaterial
                || !Source.trailParticles || Source.trailParticles.main.loop || !Source.trailParticles.main.playOnAwake
                || !Source.firedParticles || !Source.emptyParticles)
                throw new InvalidOperationException("Native Arrow Shooter has an incomplete view, settings or presentation graph.");
            Arrow = Source.arrowPrefab; Trail = Source.trailParticles; Fired = Source.firedParticles;
            Empty = Source.emptyParticles; Line = Source.lineRenderer; ColorblindMaterial = Source.colorblindLineMaterial;
            foreach (var Component in Line.GetComponentsInChildren<Component>(true))
                if (Component is not Transform and not LineRenderer)
                    throw new InvalidOperationException("Native Arrow warning line contains an unsupported component.");
            LinePosition = Source.shooter.InverseTransformPoint(Line.transform.position);
            LineRotation = Quaternion.Inverse(Source.shooter.rotation) * Line.transform.rotation;
            LineScale = Line.transform.localScale;
            ShotSounds = (SFX_Instance[])Source.shotSFX.Clone();
            Synchronization = View.Synchronization; Ownership = View.OwnershipTransfer;
            Setting = Gate.disableIfSettingDisabled;
            Seal(Writer =>
            {
                Writer.Write("PLL/NativeArrowShooter/v1"); Writer.Write((int)Synchronization);
                Writer.Write((int)Ownership); Writer.Write((int)Setting);
                ArrowNodes = Presentation(Writer, Arrow, false); TrailNodes = Presentation(Writer, Trail.gameObject, true);
                Presentation(Writer, Fired.gameObject, true); Presentation(Writer, Empty.gameObject, true);
                ManifestEncoding.WriteVector(Writer, LinePosition); ManifestEncoding.WriteVector(Writer, LineScale);
                ManifestEncoding.WritePose(Writer, new Pose(Vector3.zero, LineRotation));
                Writer.Write((int)Line.alignment); Writer.Write((int)Line.textureMode);
                Writer.Write(Line.numCapVertices); Writer.Write(Line.numCornerVertices);
                ManifestEncoding.WriteScalar(Writer, Line.widthMultiplier);
                NativeNetworkPrefabGraph.WriteCurve(Writer, Line.widthCurve);
                var Colors = Line.colorGradient;
                var ColorKeys = Colors.colorKeys; var AlphaKeys = Colors.alphaKeys;
                Writer.Write((int)Colors.mode); Writer.Write(ColorKeys.Length);
                foreach (var Key in ColorKeys) { ManifestEncoding.WriteColor(Writer, Key.color); ManifestEncoding.WriteScalar(Writer, Key.time); }
                Writer.Write(AlphaKeys.Length);
                foreach (var Key in AlphaKeys) { ManifestEncoding.WriteScalar(Writer, Key.alpha); ManifestEncoding.WriteScalar(Writer, Key.time); }
                Asset(Writer, Line.sharedMaterial); Asset(Writer, ColorblindMaterial);
                Sounds(Writer, ShotSounds);
            });
        }
    }

    sealed class SawProvider : Provider
    {
        internal readonly SFX_Instance HitSound;
        internal readonly RunSettings.SETTINGTYPE? Setting;
        internal SawProvider(Peak.MovingSawBlade Source)
        {
            HitSound = Source.hitSound;
            var Gate = Source.GetComponentInParent<DisableBasedOnRunSettings>(true);
            Setting = Gate ? Gate.disableIfSettingDisabled : null;
            Seal(Writer =>
            {
                Writer.Write("PLL/NativeMovingSaw/v1"); Writer.Write(Setting.HasValue);
                if (Setting.HasValue) Writer.Write((int)Setting.Value);
                Sounds(Writer, new[] { HitSound });
            });
        }
    }

    sealed class SpikeProvider : Provider
    {
        internal const int HitboxLayer = 0;
        internal readonly GameObject Hitbox;
        internal readonly Vector3 HitboxPosition, HitboxScale;
        internal readonly Quaternion HitboxRotation;
        internal readonly SFX_Instance[] AttackSounds, RestSounds;
        internal readonly ViewSynchronization Synchronization;
        internal readonly OwnershipOption Ownership;
        internal readonly RunSettings.SETTINGTYPE? Setting;
        internal readonly int SupportLayer;
        internal SpikeProvider(Peak.SpikeTrap Source)
        {
            if (!Source.view || !Source.spikes || !Source.activeSpikeTF || !Source.readySpikeTF
                || !Source.restSpikeTF || !Source.hitbox || !Source.selfCollider
                || Source.hitbox.activeSelf || Source.hitbox.transform.parent != Source.transform
                || Source.view.ObservedComponents.Count != 0 || Source.view.transform != Source.transform
                || !TransformOwnership.Contains(Source.transform, Source.spikes)
                || !TransformOwnership.Contains(Source.transform, Source.activeSpikeTF)
                || !TransformOwnership.Contains(Source.transform, Source.readySpikeTF)
                || !TransformOwnership.Contains(Source.transform, Source.restSpikeTF)
                || !TransformOwnership.Contains(Source.transform, Source.selfCollider.transform))
                throw new InvalidOperationException("Native Spike Trap has an incomplete or externally owned phase/contact graph.");
            Hitbox = Source.hitbox; HitboxPosition = Hitbox.transform.localPosition;
            HitboxRotation = Hitbox.transform.localRotation; HitboxScale = Hitbox.transform.localScale;
            AttackSounds = (SFX_Instance[])Source.attackSFX.Clone(); RestSounds = (SFX_Instance[])Source.restSFX.Clone();
            Synchronization = Source.view.Synchronization; Ownership = Source.view.OwnershipTransfer;
            SupportLayer = Source.selfCollider.gameObject.layer;
            if ((HelperFunctions.terrainMapMask.value & 1 << SupportLayer) == 0)
                throw new InvalidOperationException("Native Spike support is outside the terrain/map collision mask.");
            var Gate = Source.GetComponentInParent<DisableBasedOnRunSettings>(true);
            Setting = Gate ? Gate.disableIfSettingDisabled : null;
            Seal(Writer =>
            {
                Writer.Write("PLL/NativeSpikeTrap/v1"); Writer.Write((int)Synchronization); Writer.Write((int)Ownership);
                Writer.Write(SupportLayer); Writer.Write(HelperFunctions.terrainMapMask.value);
                Writer.Write(Setting.HasValue); if (Setting.HasValue) Writer.Write((int)Setting.Value);
                var Graph = DeterministicObjectIdentity.HashObjectHierarchy(Hitbox.transform, "PLL/NativeSpikeHitbox/v1", HitboxLayer);
                Writer.Write(Graph.Length); Writer.Write(Graph);
                Writer.Write(Hitbox.tag);
                var Area = Hitbox.GetComponent<AOE>();
                if (!Area || Hitbox.GetComponentsInChildren<AOE>(true).Length != 1 || Area.auto || !Area.onEnable
                    || !Area.useSingleDirection || !Area.singleDirectionForwardTF
                    || !TransformOwnership.Contains(Hitbox.transform, Area.singleDirectionForwardTF)
                    || Area.hasAffliction || Area.affliction is not null || Area.procCollisionEvents || Area.cooksItems
                    || Area.canLaunchItems || Area.worksOnCarriedScout || Area.illegalStatus != string.Empty
                    || Area.addtlStatus.Length != 0 || Area.addlStatusAmountOverrides.Count != 0)
                    throw new InvalidOperationException("Native Spike Trap has no isolated activation-driven AOE graph.");
                foreach (var Component in Hitbox.GetComponentsInChildren<Component>(true))
                    if (Component is not Transform && Component != Area)
                        throw new InvalidOperationException("Native Spike Trap hitbox contains an unsupported component.");
                Writer.Write((int)Area.mask); Writer.Write((int)Area.statusType);
                NativeFlowProvider.WriteScalars(Writer, Area.range, Area.fallTime, Area.knockback,
                    Area.additionalVerticalKnockback, Area.fallDamageCap, Area.fallDamageCapTime,
                    Area.minFactor, Area.factorPow, Area.itemKnockbackMultiplier, Area.statusAmount);
                Writer.Write(Area.capFallDamage); Writer.Write(Area.requireLineOfSigh); Writer.Write(Area.ignoreFactor);
                TransformBinding.Create(Hitbox.transform, Area.singleDirectionForwardTF).WriteManifest(Writer);
                Sounds(Writer, AttackSounds); Sounds(Writer, RestSounds);
            });
        }
    }

    abstract class Recipe : HazardBehaviorContract
    {
        readonly Transform SourceRoot;
        readonly byte[] Profile;
        protected NativeMechanicalHazards? Providers;
        protected Recipe(PllHazardAuthoring Source)
        {
            SourceRoot = Source.transform; Profile = Snapshot(SourceRoot, Source.Behavior);
        }
        internal override void ValidateMetadata(PllHazardAuthoring Source)
        {
            if (!Profile.AsSpan().SequenceEqual(Snapshot(Source.transform, Source.Behavior)))
                throw new InvalidOperationException($"Mechanical hazard '{Source.Id}' changed its sealed profile.");
        }
        internal override void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(Profile.Length); Writer.Write(Profile);
        }
        internal override void ResolveExternal(NativeMechanicalHazards? Providers) =>
            this.Providers = Providers ?? throw new InvalidOperationException("Mechanical hazard providers were not sealed.");
        internal override void ClearExternal() => Providers = null;
        protected TransformBinding Bind(Transform Value) => TransformBinding.Create(SourceRoot, Value);
        protected static byte[] Snapshot(Transform Root, PllHazardBehavior Behavior)
        {
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
            {
                switch (Behavior)
                {
                    case PllArrowShooterBehavior Arrow:
                        Writer.Write((byte)0); Transform(Arrow.Muzzle);
                        Scalars(Arrow.Range, Arrow.ReloadSeconds, Arrow.Knockback); Writer.Write(Arrow.InitialAmmo);
                        Asset(Arrow.ArrowPrefab); Asset(Arrow.TrailParticles); Asset(Arrow.FiredParticles); Asset(Arrow.EmptyParticles);
                        Asset(Arrow.WarningLineMaterial); Asset(Arrow.ColorblindLineMaterial); Clips(Arrow.ShotClips); break;
                    case PllMovingSawBehavior Saw:
                        Writer.Write((byte)1); Transform(Saw.Rail); Transform(Saw.RailStart); Transform(Saw.RailEnd);
                        Transform(Saw.Blade); Transform(Saw.BladeVisual);
                        ComponentBinding<Collider>.Create(Root, Saw.Hitbox).WriteManifest(Writer);
                        Scalars(Saw.TimeoutSeconds, Saw.OscillateSpeed, Saw.TraceSpeed, Saw.ConstantInjury,
                            Saw.InitialInjury, Saw.InitialInjuryCooldownSeconds, Saw.Buffer); Asset(Saw.HitClip); break;
                    case PllSpikeTrapBehavior Spike:
                        Writer.Write((byte)2); Transform(Spike.Spikes); Transform(Spike.ActivePose);
                        Transform(Spike.ReadyPose); Transform(Spike.RestPose);
                        ComponentBinding<Collider>.Create(Root, Spike.SupportCollider).WriteManifest(Writer);
                        ComponentBinding<Collider>.Create(Root, Spike.DetectionTrigger).WriteManifest(Writer);
                        Scalars(Spike.SpikeWaitSeconds, Spike.AttackWaitSeconds, Spike.ReadyTweenSeconds,
                            Spike.AttackTweenSeconds, Spike.ReturnTweenSeconds, Spike.HitboxActiveSeconds);
                        Clips(Spike.AttackClips); Clips(Spike.RestClips); break;
                    default: throw new InvalidOperationException("Mechanical hazard profile changed its supported type.");
                }
                void Transform(Transform Value) => TransformBinding.Create(Root, Value).WriteManifest(Writer);
                void Scalars(params float[] Values) { foreach (var Value in Values) ManifestEncoding.WriteScalar(Writer, Value); }
                void Asset(Object? Value)
                {
                    if (PllAuthoringValueContract.DestroyedReference(Value))
                        throw new InvalidOperationException("Mechanical profile contains a destroyed asset.");
                    Writer.Write(Value ? true : false);
                    if (!Value) return;
                    switch (Value)
                    {
                        case GameObject Prefab: Provider.Presentation(Writer, Prefab, false); break;
                        case ParticleSystem Particle: Provider.Presentation(Writer, Particle.gameObject, true); break;
                        case AudioClip Clip: BiomeAuthoringContract.WriteClip(Writer, Clip); break;
                        case Material Material: Writer.Write(VisualFingerprintWriter.HashSerializedObject(Material, "PLL/MechanicalProfileMaterial/v1")); break;
                        default: throw new InvalidOperationException("Mechanical profile asset type is unsupported.");
                    }
                }
                void Clips(AudioClip[] Values) { Writer.Write(Values.Length); foreach (var Value in Values) Asset(Value); }
            }
            return Stream.ToArray();
        }
    }

    sealed class ArrowRecipe : Recipe
    {
        readonly TransformBinding Muzzle;
        readonly PllArrowShooterBehavior Settings;
        readonly int ArrowNodes, TrailNodes;
        internal ArrowRecipe(PllHazardAuthoring Source, PllArrowShooterBehavior Settings) : base(Source)
        {
            Muzzle = Bind(Settings.Muzzle);
            this.Settings = new()
            {
                Range = Settings.Range, ReloadSeconds = Settings.ReloadSeconds, Knockback = Settings.Knockback,
                InitialAmmo = Settings.InitialAmmo,
                ArrowPrefab = Settings.ArrowPrefab ? Settings.ArrowPrefab : null,
                TrailParticles = Settings.TrailParticles ? Settings.TrailParticles : null,
                FiredParticles = Settings.FiredParticles ? Settings.FiredParticles : null,
                EmptyParticles = Settings.EmptyParticles ? Settings.EmptyParticles : null,
                WarningLineMaterial = Settings.WarningLineMaterial ? Settings.WarningLineMaterial : null,
                ColorblindLineMaterial = Settings.ColorblindLineMaterial ? Settings.ColorblindLineMaterial : null,
                ShotClips = (AudioClip[])Settings.ShotClips.Clone()
            };
            ArrowNodes = this.Settings.ArrowPrefab is { } Arrow ? Arrow.GetComponentsInChildren<Transform>(true).Length : 0;
            TrailNodes = this.Settings.TrailParticles is { } Trail ? Trail.GetComponentsInChildren<Transform>(true).Length : 0;
        }
        internal override int RequiredMechanicalMask => ArrowMask;
        internal override HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal) =>
            new ArrowInstance(Source, Occurrence, Providers!.Arrow!, Settings, Muzzle.Resolve(Source.transform), ArrowNodes, TrailNodes);
    }

    sealed class SawRecipe : Recipe
    {
        readonly TransformBinding Rail, Start, End, Blade, Visual;
        readonly ComponentBinding<Collider> Hitbox;
        readonly PllMovingSawBehavior Settings;
        internal SawRecipe(PllHazardAuthoring Source, PllMovingSawBehavior Settings) : base(Source)
        {
            Rail = Bind(Settings.Rail); Start = Bind(Settings.RailStart); End = Bind(Settings.RailEnd);
            Blade = Bind(Settings.Blade); Visual = Bind(Settings.BladeVisual);
            Hitbox = ComponentBinding<Collider>.Create(Source.transform, Settings.Hitbox);
            this.Settings = new()
            {
                TimeoutSeconds = Settings.TimeoutSeconds, OscillateSpeed = Settings.OscillateSpeed,
                TraceSpeed = Settings.TraceSpeed, ConstantInjury = Settings.ConstantInjury,
                InitialInjury = Settings.InitialInjury, InitialInjuryCooldownSeconds = Settings.InitialInjuryCooldownSeconds,
                Buffer = Settings.Buffer, HitClip = Settings.HitClip ? Settings.HitClip : null
            };
        }
        internal override int RequiredMechanicalMask => SawMask;
        internal override HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal)
        {
            var Root = Source.transform;
            return new SawInstance(Source, Occurrence, Providers!.Saw!, Settings, Rail.Resolve(Root),
                Start.Resolve(Root), End.Resolve(Root), Blade.Resolve(Root), Visual.Resolve(Root),
                Hitbox.Resolve(Root, default, "Saw hitbox"));
        }
    }

    sealed class SpikeRecipe : Recipe
    {
        readonly TransformBinding Spikes, Active, Ready, Rest;
        readonly ComponentBinding<Collider> Support, Detection;
        readonly PllSpikeTrapBehavior Settings;
        internal SpikeRecipe(PllHazardAuthoring Source, PllSpikeTrapBehavior Settings) : base(Source)
        {
            Spikes = Bind(Settings.Spikes); Active = Bind(Settings.ActivePose);
            Ready = Bind(Settings.ReadyPose); Rest = Bind(Settings.RestPose);
            Support = ComponentBinding<Collider>.Create(Source.transform, Settings.SupportCollider);
            Detection = ComponentBinding<Collider>.Create(Source.transform, Settings.DetectionTrigger);
            this.Settings = new()
            {
                SpikeWaitSeconds = Settings.SpikeWaitSeconds, AttackWaitSeconds = Settings.AttackWaitSeconds,
                ReadyTweenSeconds = Settings.ReadyTweenSeconds, AttackTweenSeconds = Settings.AttackTweenSeconds,
                ReturnTweenSeconds = Settings.ReturnTweenSeconds, HitboxActiveSeconds = Settings.HitboxActiveSeconds,
                AttackClips = (AudioClip[])Settings.AttackClips.Clone(), RestClips = (AudioClip[])Settings.RestClips.Clone()
            };
        }
        internal override int RequiredMechanicalMask => SpikeMask;
        internal override HazardInstance Bind(PllHazardAuthoring Source, FeatureOccurrence Occurrence, int Ordinal)
        {
            var Root = Source.transform;
            if (Vector3.Dot(Root.up, Vector3.up) < 0.9999f)
                throw new InvalidOperationException("Native Spike Trap placement must remain upright.");
            return new SpikeInstance(Source, Occurrence, Providers!.Spike!, Settings, Spikes.Resolve(Root),
                Active.Resolve(Root), Ready.Resolve(Root), Rest.Resolve(Root),
                Support.Resolve(Root, default, "Spike support"), Detection.Resolve(Root, default, "Spike detection"));
        }
    }

    static readonly Dictionary<SFX_Instance, MechanicalInstance> SoundOwners = new();
    static ArrowInstance? ArrowOutputOwner;

    abstract class MechanicalInstance : HazardInstance, IDisposable
    {
        enum Admission : byte { Prepared, Published, Activated }
        readonly GeneratedGeometryBudget Budget;
        readonly List<GameObject> Objects = new();
        readonly List<Component> Components = new();
        readonly List<Object> Assets = new();
        readonly List<SFX_Player.SoundEffectHandle> Sounds = new();
        readonly List<(Collider Collider, bool Enabled)> Contacts = new();
        Admission State;
        int FixedInstances, FixedNodes, SecondaryInstances, SecondaryNodes;

        protected MechanicalInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence) : base(Source, Occurrence)
        {
            Budget = Occurrence.GeometryBudget;
        }

        internal bool GameplayReady => State == Admission.Activated && IsReady && NativeActive && NativeInitialized;
        internal bool ActivationComplete => State == Admission.Activated;
        protected void Construct(Action Build)
        {
            try { Build(); }
            catch (Exception Failure)
            {
                try { Retire(); }
                catch (Exception Cleanup) { throw new AggregateException("Mechanical hazard construction and cleanup failed.", Failure, Cleanup); }
                throw;
            }
        }
        protected GameObject Child(string Name, Transform Parent)
        {
            ReserveObject(1);
            var Value = new GameObject(Name); Objects.Add(Value);
            Value.SetActive(false); Value.transform.SetParent(Parent, false); return Value;
        }
        protected void GateLifetime(GameObject Gate) => Gate.AddComponent<NativeMechanicalGate>().Owner = this;
        protected T Add<T>(GameObject Target) where T : Component
        {
            var Value = Target.AddComponent<T>(); Components.Add(Value); return Value;
        }
        protected T Own<T>(T Asset) where T : Object { Assets.Add(Asset); return Asset; }
        protected GameObject Clone(GameObject Template, Transform Parent)
        {
            ReserveObject(Template.GetComponentsInChildren<Transform>(true).Length);
            var Value = Object.Instantiate(Template, Parent, false); Objects.Add(Value); return Value;
        }
        void ReserveObject(int Nodes)
        {
            var Count = checked(FixedInstances + 1); var Total = checked(FixedNodes + Nodes);
            Budget.ReserveNativeOutput(this, checked(Count + SecondaryInstances), checked(Total + SecondaryNodes), 0, 0);
            FixedInstances = Count; FixedNodes = Total;
        }
        protected void ReserveSecondary(int Instances, int Nodes)
        {
            Budget.ReserveNativeOutput(this, checked(FixedInstances + Instances), checked(FixedNodes + Nodes), 0, 0);
            SecondaryInstances = Instances; SecondaryNodes = Nodes;
        }
        protected void ExtendSecondary(int Nodes) => ReserveSecondary(checked(SecondaryInstances + 1), checked(SecondaryNodes + Nodes));
        protected void Contact(Collider Value)
        {
            Contacts.Add((Value, Value.enabled)); Value.enabled = false;
        }
        protected SFX_Instance Sound(SFX_Instance Template, AudioClip[] Clips)
        {
            var Value = Own(ScriptableObject.CreateInstance<SFX_Instance>());
            Value.settings = Template.settings;
            Value.clips = (AudioClip[])(Clips.Length != 0 ? Clips : Template.clips).Clone();
            SoundOwners.Add(Value, this); return Value;
        }
        protected SFX_Instance[] SoundSet(SFX_Instance[] Templates, AudioClip[] Clips)
        {
            if (Clips.Length != 0)
            {
                if (Templates.Length == 0) throw new InvalidOperationException("Native mechanical sound recipe has no template for custom clips.");
                return new[] { Sound(Templates[0], Clips) };
            }
            var Result = new SFX_Instance[Templates.Length];
            for (var i = 0; i < Result.Length; i++) Result[i] = Sound(Templates[i], Array.Empty<AudioClip>());
            return Result;
        }
        internal void CaptureSound(SFX_Player.SoundEffectHandle Handle)
        {
            for (var i = Sounds.Count - 1; i >= 0; i--) if (Sounds[i].source is null) Sounds.RemoveAt(i);
            Sounds.Add(Handle);
        }
        internal override void Publish()
        {
            if (Retired) throw new InvalidOperationException("A retired mechanical hazard cannot publish.");
            if (State != Admission.Prepared || !AdmissionReady) return;
            State = Admission.Published; Resume();
        }
        internal override void SetEnabled(bool Value)
        {
            if (Retired) return;
            Enabled = Value;
            if (Value) Resume(); else Suspend();
        }
        internal override void Resume()
        {
            if (State == Admission.Prepared || !IsReady) return;
            if (State == Admission.Published)
            {
                StartNative(); State = Admission.Activated;
            }
            if (!NativeActive) return;
            foreach (var (Collider, Enabled) in Contacts) if (Collider) Collider.enabled = Enabled;
            EnableNative();
        }
        internal override void Suspend()
        {
            List<Exception>? Failures = null;
            AuthoringCleanup.Capture(ref Failures, StopNative);
            foreach (var (Collider, _) in Contacts)
                if (Collider) AuthoringCleanup.Capture(ref Failures, () => Collider.enabled = false);
            AuthoringCleanup.Capture(ref Failures, StopSounds);
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }
        void StopSounds()
        {
            List<Exception>? Failures = null;
            foreach (var Handle in Sounds)
                AuthoringCleanup.Capture(ref Failures, () => NativeFlowRuntime.StopSound(Handle));
            Sounds.Clear(); GeneratedGeometryBudget.ThrowFailures(Failures);
        }
        internal override void Retire()
        {
            if (Retired) return;
            Retired = true;
            List<Exception>? Failures = null;
            AuthoringCleanup.Capture(ref Failures, Suspend);
            for (var i = Objects.Count - 1; i >= 0; i--)
                if (Objects[i])
                {
                    var Value = Objects[i];
                    AuthoringCleanup.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRoot(Value));
                    AuthoringCleanup.Capture(ref Failures, () => { Value.SetActive(false); Object.DestroyImmediate(Value); });
                }
            Objects.Clear();
            for (var i = Components.Count - 1; i >= 0; i--)
                if (Components[i]) { var Value = Components[i]; AuthoringCleanup.Capture(ref Failures, () => Object.DestroyImmediate(Value)); }
            Components.Clear();
            foreach (var Asset in Assets)
            {
                if (Asset is SFX_Instance Sound) SoundOwners.Remove(Sound);
                if (Asset) AuthoringCleanup.Capture(ref Failures, () => Object.DestroyImmediate(Asset));
            }
            Assets.Clear();
            foreach (var (Collider, Enabled) in Contacts) if (Collider) Collider.enabled = Enabled;
            Contacts.Clear();
            AuthoringCleanup.Capture(ref Failures, RestoreAuthoringPose);
            if (Source && ReferenceEquals(Source.Binding, this)) Source.Binding = null;
            Budget.ReleaseNativeOutput(this);
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }
        protected abstract bool NativeActive { get; }
        protected abstract bool NativeInitialized { get; }
        protected virtual void RestoreAuthoringPose() { }
        protected abstract void StartNative();
        protected abstract void EnableNative();
        protected abstract void StopNative();
        public void Dispose() => Retire();
    }

    sealed class ArrowInstance : MechanicalInstance
    {
        GameObject Graph = null!;
        ArrowShooter Shooter = null!;
        readonly List<ParticleSystem> ShotOutputs = new();
        readonly int ArrowNodes, TrailNodes, PlannedWarnings, PlannedTrails;
        int Warnings, Trails;
        internal bool Awakened;
        internal ArrowInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence,
            ArrowProvider Provider, PllArrowShooterBehavior Settings, Transform Muzzle,
            int ArrowNodes, int TrailNodes) : base(Source, Occurrence)
        {
            this.ArrowNodes = ArrowNodes != 0 ? ArrowNodes : Provider.ArrowNodes;
            this.TrailNodes = TrailNodes != 0 ? TrailNodes : Provider.TrailNodes;
            PlannedWarnings = checked(Settings.InitialAmmo + 2); PlannedTrails = Settings.InitialAmmo;
            Construct(() =>
            {
                ReserveSecondary(checked(PlannedWarnings + PlannedTrails),
                    checked(PlannedWarnings * this.ArrowNodes + PlannedTrails * this.TrailNodes));
                Graph = Child("__PLL_ArrowShooter", Source.transform);
                Graph.transform.SetPositionAndRotation(Muzzle.position, Muzzle.rotation);
                var View = Graph.AddComponent<PhotonView>();
                View.observableSearch = PhotonView.ObservableSearch.Manual;
                View.Synchronization = Provider.Synchronization; View.OwnershipTransfer = Provider.Ownership;
                Shooter = Graph.AddComponent<ArrowShooter>(); Shooter.enabled = false;
                View.ObservedComponents = new List<Component> { Shooter };
                Shooter.shooter = Muzzle; Shooter.pos = Muzzle.position; Shooter.forward = Muzzle.forward;
                Shooter.range = Settings.Range; Shooter.reloadTime = Settings.ReloadSeconds; Shooter.knockback = Settings.Knockback;
                Shooter.arrowPrefab = Settings.ArrowPrefab ?? Provider.Arrow;
                Shooter.trailParticles = Settings.TrailParticles ?? Provider.Trail;
                Shooter.firedParticles = Clone(Settings.FiredParticles?.gameObject
                    ?? Provider.Fired.gameObject, Graph.transform).GetComponent<ParticleSystem>();
                Shooter.emptyParticles = Clone(Settings.EmptyParticles?.gameObject
                    ?? Provider.Empty.gameObject, Graph.transform).GetComponent<ParticleSystem>();
                Shooter.firedParticles.transform.SetPositionAndRotation(Muzzle.position, Muzzle.rotation);
                Shooter.emptyParticles.transform.SetPositionAndRotation(Muzzle.position, Muzzle.rotation);
                var Line = Clone(Provider.Line.gameObject, Graph.transform);
                Line.transform.SetPositionAndRotation(Muzzle.TransformPoint(Provider.LinePosition), Muzzle.rotation * Provider.LineRotation);
                Line.transform.localScale = Provider.LineScale;
                Shooter.lineRenderer = Line.GetComponent<LineRenderer>();
                var NativeLine = Provider.Line; var Warning = Shooter.lineRenderer;
                Warning.positionCount = 2;
                Warning.sharedMaterial = Own(new Material(Settings.WarningLineMaterial ?? NativeLine.sharedMaterial));
                Shooter.colorblindLineMaterial = Own(new Material(Settings.ColorblindLineMaterial ?? Provider.ColorblindMaterial));
                Shooter.shotSFX = SoundSet(Provider.ShotSounds, Settings.ShotClips);
                Shooter.ammo = Settings.InitialAmmo;
                GateLifetime(Graph);
                Graph.AddComponent<DisableBasedOnRunSettings>().disableIfSettingDisabled = Provider.Setting;
                foreach (var Particle in Graph.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var Main = Particle.main; Main.playOnAwake = false;
                    Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            });
        }
        internal ArrowShooter Native => Shooter;
        protected override bool NativeActive => Graph && Graph.activeInHierarchy;
        protected override bool NativeInitialized => Awakened;
        internal bool OwnsOutputTemplate(Object Template) => Template == Shooter.arrowPrefab || Template == Shooter.trailParticles;
        internal Object InstantiateOutput(Object Template, Vector3 Position, Quaternion Rotation)
        {
            if (Template == Shooter.arrowPrefab)
            {
                if (Warnings >= PlannedWarnings) ExtendSecondary(ArrowNodes);
                Warnings = checked(Warnings + 1);
            }
            else
            {
                if (Trails >= PlannedTrails) ExtendSecondary(TrailNodes);
                Trails = checked(Trails + 1);
            }
            var Value = Object.Instantiate(Template, Position, Rotation, Graph.transform);
            if (Value is ParticleSystem Particle)
            {
                var Main = Particle.main; Main.stopAction = ParticleSystemStopAction.Destroy;
                for (var i = ShotOutputs.Count - 1; i >= 0; i--) if (!ShotOutputs[i]) ShotOutputs.RemoveAt(i);
                ShotOutputs.Add(Particle);
            }
            return Value;
        }
        protected override void StartNative() { Physics.SyncTransforms(); Graph.SetActive(true); }
        protected override void EnableNative() { if (Graph.activeInHierarchy) Shooter.enabled = true; }
        protected override void StopNative()
        {
            if (Shooter)
            {
                Shooter.enabled = false; Shooter.StopAllCoroutines(); Shooter.reloading = false;
                if (Shooter.firedParticles) Shooter.firedParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (Shooter.emptyParticles) Shooter.emptyParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var Particle in ShotOutputs) if (Particle) Object.DestroyImmediate(Particle.gameObject);
            ShotOutputs.Clear();
        }
    }

    sealed class SawInstance : MechanicalInstance
    {
        Peak.MovingSawBlade Saw = null!;
        GameObject Path = null!;
        readonly Transform Blade;
        readonly Transform Visual;
        readonly Vector3 OriginalPosition;
        readonly Quaternion OriginalVisualRotation;
        internal bool Started;
        internal SawInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence,
            SawProvider Provider, PllMovingSawBehavior Settings, Transform Rail, Transform Start,
            Transform End, Transform Blade, Transform Visual, Collider Hitbox) : base(Source, Occurrence)
        {
            this.Blade = Blade; this.Visual = Visual; OriginalPosition = Blade.localPosition;
            OriginalVisualRotation = Visual.localRotation;
            Construct(() =>
            {
                Contact(Hitbox);
                Path = Child("__PLL_SawPath", Rail);
                var Container = Path.AddComponent<SplineContainer>(); var Spline = new Spline();
                var First = Start.localPosition; var Last = End.localPosition;
                Path.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Path.transform.localPosition = new Vector3(First.x, Blade.localPosition.z, First.z);
                Spline.Add(new BezierKnot(new float3(0f, 0f, First.y - Blade.localPosition.z)), TangentMode.Linear);
                Spline.Add(new BezierKnot(new float3(0f, 0f, Last.y - Blade.localPosition.z)), TangentMode.Linear);
                Container.Spline = Spline;
                Saw = Add<Peak.MovingSawBlade>(Blade.gameObject); Saw.enabled = false;
                Saw.path = Container; Saw.Animate = null!; Saw.sawBlade = Blade.gameObject; Saw.sawBladeVisual = Visual.gameObject;
                Saw.timeout = Settings.TimeoutSeconds; Saw.sawOscillateSpeed = Settings.OscillateSpeed;
                Saw.traceSpeed = Settings.TraceSpeed; Saw.constantInjury = Settings.ConstantInjury;
                Saw.initialInjury = Settings.InitialInjury; Saw.initialInjuryCooldown = Settings.InitialInjuryCooldownSeconds;
                Saw.buffer = Settings.Buffer;
                Saw.hitSound = Sound(Provider.HitSound, Settings.HitClip is { } Clip ? new[] { Clip } : Array.Empty<AudioClip>());
                GateLifetime(Path);
                if (Provider.Setting.HasValue)
                    Add<DisableBasedOnRunSettings>(Path).disableIfSettingDisabled = Provider.Setting.Value;
            });
        }
        internal Peak.MovingSawBlade Native => Saw;
        protected override bool NativeActive => Path && Path.activeInHierarchy;
        protected override bool NativeInitialized => Started;
        protected override void StartNative() => Path.SetActive(true);
        protected override void EnableNative() { if (Path.activeInHierarchy) Saw.enabled = true; }
        protected override void StopNative()
        {
            if (Saw) Saw.enabled = false;
            if (Blade) Blade.localPosition = OriginalPosition;
            if (Visual) Visual.localRotation = OriginalVisualRotation;
        }
    }

    sealed class SpikeInstance : MechanicalInstance
    {
        Peak.SpikeTrap Trap = null!;
        readonly Transform Spikes;
        readonly Transform Rest;
        readonly Vector3 OriginalPosition;
        readonly Quaternion OriginalRotation;
        readonly bool OriginalActive;
        readonly GameObject SupportObject;
        readonly int OriginalSupportLayer;
        GameObject Hitbox = null!;
        GameObject Gate = null!;
        internal bool Started;
        internal SpikeInstance(PllHazardAuthoring Source, FeatureOccurrence Occurrence,
            SpikeProvider Provider, PllSpikeTrapBehavior Settings, Transform Spikes,
            Transform Active, Transform Ready, Transform Rest, Collider Support, Collider Detection) : base(Source, Occurrence)
        {
            this.Spikes = Spikes; this.Rest = Rest;
            OriginalPosition = Spikes.localPosition; OriginalRotation = Spikes.localRotation;
            OriginalActive = Spikes.gameObject.activeSelf;
            SupportObject = Support.gameObject; OriginalSupportLayer = SupportObject.layer;
            Construct(() =>
            {
                Contact(Detection);
                Gate = Child("__PLL_SpikeGate", Source.transform);
                GateLifetime(Gate);
                if (Provider.Setting.HasValue)
                    Gate.AddComponent<DisableBasedOnRunSettings>().disableIfSettingDisabled = Provider.Setting.Value;
                Hitbox = Clone(Provider.Hitbox, Gate.transform);
                foreach (var Node in Hitbox.GetComponentsInChildren<Transform>(true))
                    Node.gameObject.layer = SpikeProvider.HitboxLayer;
                Hitbox.SetActive(false); Hitbox.transform.SetParent(Source.transform, false);
                Hitbox.transform.localPosition = Provider.HitboxPosition;
                Hitbox.transform.localRotation = Provider.HitboxRotation; Hitbox.transform.localScale = Provider.HitboxScale;
                var View = Add<PhotonView>(Detection.gameObject);
                View.observableSearch = PhotonView.ObservableSearch.Manual;
                View.Synchronization = Provider.Synchronization; View.OwnershipTransfer = Provider.Ownership;
                View.ObservedComponents = new List<Component>();
                Trap = Add<Peak.SpikeTrap>(Detection.gameObject); Trap.enabled = false;
                Trap.view = View; Trap.spikes = Spikes; Trap.activeSpikeTF = Active;
                Trap.readySpikeTF = Ready; Trap.restSpikeTF = Rest; Trap.selfCollider = Support; Trap.hitbox = Hitbox;
                Trap.spikeWaitTime = Settings.SpikeWaitSeconds; Trap.spikeAttackWaitTime = Settings.AttackWaitSeconds;
                Trap.readyTweenTime = Settings.ReadyTweenSeconds; Trap.attackTweenTime = Settings.AttackTweenSeconds;
                Trap.returnToRestTweenTime = Settings.ReturnTweenSeconds; Trap.hitboxActiveTime = Settings.HitboxActiveSeconds;
                Trap.attackSFX = SoundSet(Provider.AttackSounds, Settings.AttackClips);
                Trap.restSFX = SoundSet(Provider.RestSounds, Settings.RestClips);
                SupportObject.layer = Provider.SupportLayer;
                Spikes.position = Rest.position; Spikes.gameObject.SetActive(false);
            });
        }
        internal Peak.SpikeTrap Native => Trap;
        internal bool OwnsHitbox(Component Value) => Hitbox && TransformOwnership.Contains(Hitbox.transform, Value.transform);
        protected override bool NativeActive => Gate && Gate.activeInHierarchy;
        protected override bool NativeInitialized => Started;
        protected override void StartNative() => Gate.SetActive(true);
        protected override void EnableNative() { if (Gate.activeInHierarchy) Trap.enabled = true; }
        protected override void StopNative()
        {
            List<Exception>? Failures = null;
            if (Trap)
                AuthoringCleanup.Capture(ref Failures, () => { Trap.enabled = false; Trap.StopAllCoroutines(); Trap._isAttacking = false; });
            if (Spikes)
            {
                AuthoringCleanup.Capture(ref Failures, () => Spikes.DOKill(false));
                AuthoringCleanup.Capture(ref Failures, () => { if (Rest) Spikes.position = Rest.position; Spikes.gameObject.SetActive(false); });
            }
            if (Hitbox) AuthoringCleanup.Capture(ref Failures, () => Hitbox.SetActive(false));
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }
        protected override void RestoreAuthoringPose()
        {
            if (SupportObject) SupportObject.layer = OriginalSupportLayer;
            if (!Spikes) return;
            Spikes.localPosition = OriginalPosition; Spikes.localRotation = OriginalRotation;
            Spikes.gameObject.SetActive(OriginalActive);
        }
    }

    sealed class NativeMechanicalGate : MonoBehaviour
    {
        internal MechanicalInstance Owner = null!;
        void OnDisable() => Owner?.Suspend();
        void OnEnable() { if (Owner?.ActivationComplete == true) Owner.Resume(); }
    }

    static MechanicalInstance? Owner(Component Value) =>
        Value.GetComponentInParent<PllHazardAuthoring>(true)?.Binding as MechanicalInstance;

    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.Awake))]
    static class ArrowAwakePatch
    {
        static void Prefix(ArrowShooter __instance, out ArrowInstance? __state)
        {
            __state = ArrowOutputOwner;
            if (Owner(__instance) is not ArrowInstance Instance) return;
            __instance.pos = __instance.shooter.position; __instance.forward = __instance.shooter.forward;
            ArrowOutputOwner = Instance;
        }
        static Exception? Finalizer(ArrowInstance? __state, Exception? __exception)
        {
            ArrowOutputOwner = __state; return __exception;
        }
        static void Postfix(ArrowShooter __instance)
        {
            if (Owner(__instance) is ArrowInstance Instance) Instance.Awakened = true;
        }
    }

    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FireArrowVisuals_RPC))]
    static class ArrowShotPatch
    {
        static bool Prefix(ArrowShooter __instance, out ArrowInstance? __state)
        {
            __state = ArrowOutputOwner;
            if (Owner(__instance) is not ArrowInstance Instance) return true;
            if (!Instance.GameplayReady || !__instance.enabled) return false;
            ArrowOutputOwner = Instance; return true;
        }
        static Exception? Finalizer(ArrowInstance? __state, Exception? __exception)
        {
            ArrowOutputOwner = __state; return __exception;
        }
    }

    [HarmonyPatch(typeof(Object), nameof(Object.Internal_InstantiateSingle), typeof(Object), typeof(Vector3), typeof(Quaternion))]
    static class ArrowOutputPatch
    {
        static bool Prefix(Object __0, Vector3 __1, Quaternion __2, ref Object __result)
        {
            if (ArrowOutputOwner is not { } Owner || !Owner.OwnsOutputTemplate(__0)) return true;
            __result = Owner.InstantiateOutput(__0, __1, __2); return false;
        }
    }

    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.SpawnWarningArrow))]
    static class ArrowWarningPatch
    {
        static void Prefix(ArrowShooter __instance, out ArrowInstance? __state)
        {
            __state = ArrowOutputOwner;
            if (Owner(__instance) is ArrowInstance Instance) ArrowOutputOwner = Instance;
        }
        static Exception? Finalizer(ArrowInstance? __state, Exception? __exception)
        {
            ArrowOutputOwner = __state; return __exception;
        }
    }

    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FixedUpdate))]
    static class ArrowUpdatePatch
    {
        static bool Prefix(ArrowShooter __instance) => Owner(__instance) is not { } Instance || Instance.GameplayReady;
    }

    [HarmonyPatch(typeof(Peak.MovingSawBlade), nameof(Peak.MovingSawBlade.Update))]
    static class SawUpdatePatch
    {
        static bool Prefix(Peak.MovingSawBlade __instance) => Owner(__instance) is not { } Instance || Instance.GameplayReady;
    }

    [HarmonyPatch(typeof(Peak.MovingSawBlade), nameof(Peak.MovingSawBlade.Start))]
    static class SawStartPatch
    {
        static void Postfix(Peak.MovingSawBlade __instance)
        {
            if (Owner(__instance) is SawInstance Instance && Instance.Native == __instance) Instance.Started = true;
        }
    }

    [HarmonyPatch(typeof(Peak.MovingSawBlade), nameof(Peak.MovingSawBlade.OnTriggerStay))]
    static class SawContactPatch
    {
        static bool Prefix(Peak.MovingSawBlade __instance) => Owner(__instance) is not { } Instance
            || Instance.GameplayReady && __instance.enabled;
    }

    [HarmonyPatch(typeof(Peak.SpikeTrap), nameof(Peak.SpikeTrap.OnTriggerEnter))]
    static class SpikeContactPatch
    {
        static bool Prefix(Peak.SpikeTrap __instance) => Owner(__instance) is not { } Instance
            || Instance.GameplayReady && __instance.enabled;
    }

    [HarmonyPatch(typeof(Peak.SpikeTrap), nameof(Peak.SpikeTrap.Start))]
    static class SpikeStartPatch
    {
        static void Postfix(Peak.SpikeTrap __instance)
        {
            if (Owner(__instance) is SpikeInstance Instance && Instance.Native == __instance) Instance.Started = true;
        }
    }

    [HarmonyPatch(typeof(Peak.SpikeTrap), nameof(Peak.SpikeTrap.SpikeTrapRPC))]
    static class SpikeRpcPatch
    {
        static bool Prefix(Peak.SpikeTrap __instance) => Owner(__instance) is not { } Instance
            || Instance.GameplayReady && __instance.enabled && !__instance._isAttacking;
    }

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    static class SpikeAreaPatch
    {
        static bool Prefix(AOE __instance) => Owner(__instance) is not SpikeInstance Instance
            || !Instance.OwnsHitbox(__instance) || Instance.GameplayReady && Instance.Native.enabled;
    }

    [HarmonyPatch(typeof(SFX_Player), nameof(SFX_Player.PlaySFX))]
    static class MechanicalSoundPatch
    {
        static bool Prefix(SFX_Instance __0) => !__0 || !SoundOwners.TryGetValue(__0, out var Instance) || Instance.GameplayReady;
        static void Postfix(SFX_Instance __0, SFX_Player.SoundEffectHandle? __result)
        {
            if (__0 && __result is { source: not null } Handle && SoundOwners.TryGetValue(__0, out var Instance))
                Instance.CaptureSound(Handle);
        }
    }
}
