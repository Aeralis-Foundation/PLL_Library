using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Photon.Pun;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.ExtrusionShapes;

namespace PLL.Library;

internal delegate RaycastHit NativeFlowSurfaceQuery(
    Transform Source, Vector3 From, Vector3 To, HelperFunctions.LayerType LayerType,
    float Radius, QueryTriggerInteraction TriggerInteraction);

internal sealed class NativeFlowProvider
{
    readonly byte[] Fingerprint;
    internal readonly PLLNativeResolverKind Kind;
    internal readonly Material Material;
    internal readonly Material? ZoneMaterial;
    internal readonly GameObject EndRock;
    internal readonly GameObject? Splash, AlongSpline, Light;
    internal readonly GameObject[] Details;
    internal readonly int EndRockNodes, SplashNodes, AlongSplineNodes, LightNodes;
    internal readonly int[] DetailNodes;
    internal readonly WaterfallPusher? Pusher;
    internal readonly CollisionModifier? Contact;

    NativeFlowProvider(PLLNativeResolverKind Kind, LavaRiver River, SplineExtrude Extrude,
        Material Material, Lava? LocalLava, WaterZone? LocalWater)
    {
        this.Kind = Kind; this.Material = Material;
        if (LocalWater)
        {
            if (!LocalWater.waterPlane || !LocalWater.waterPlane.sharedMaterial)
                throw new InvalidOperationException("PEAK's WaterZone provider has no surface material.");
            ZoneMaterial = LocalWater.waterPlane.sharedMaterial;
        }
        if (LocalLava)
        {
            var Renderer = LocalLava.GetComponentInChildren<MeshRenderer>(true);
            if (!Renderer || !Renderer.sharedMaterial)
                throw new InvalidOperationException("PEAK's local Lava provider has no surface material.");
            ZoneMaterial = Renderer.sharedMaterial;
        }
        EndRock = River.endRock; Splash = River.splash;
        AlongSpline = River.spawnAlongSpline; Details = (GameObject[])River.detailsAlongSpline.Clone();
        DetailNodes = new int[Details.Length];
        foreach (var SourceLight in River.GetComponentsInChildren<BakedVolumeLight>(true))
            if (SourceLight.transform.parent == River.transform)
            {
                if (Light) throw new InvalidOperationException("The native flow provider has multiple light templates.");
                Light = SourceLight.gameObject;
            }
        Pusher = Splash ? Splash.GetComponentInChildren<WaterfallPusher>(true) : null;
        Contact = Extrude.GetComponent<CollisionModifier>();
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write("PLL/NativeFlowProvider/v2"); Writer.Write((byte)Kind);
            WriteScalars(Writer, River.spawnVel, River.gravity, River.wallStick, River.drag,
                River.timeStep, River.maxLength, River.prefDistancePerFrame);
            Writer.Write(River.maxSteps); Writer.Write(River.spawnLights); Writer.Write(River.stepsAlongSpline);
            WriteScalars(Writer, Extrude.Radius, Extrude.SegmentsPerUnit); Writer.Write(Extrude.Sides);
            Writer.Write(Extrude.Capped); Writer.Write(Extrude.RebuildOnSplineChange);
            Writer.Write(VisualFingerprintWriter.HashSerializedObject(Material, "PLL/NativeFlowMaterial/v1"));
            EndRockNodes = WritePrefab(Writer, EndRock); SplashNodes = WritePrefab(Writer, Splash);
            AlongSplineNodes = WritePrefab(Writer, AlongSpline);
            Writer.Write(Details.Length);
            for (var i = 0; i < Details.Length; i++) DetailNodes[i] = WritePrefab(Writer, Details[i]);
            LightNodes = WritePrefab(Writer, Light);
            Writer.Write(Contact ? true : false);
            if (Contact) { Writer.Write(Contact.gameObject.layer); WriteContact(Writer, Contact); }
            Writer.Write(LocalLava ? true : false);
            if (LocalLava)
            {
                WriteScalars(Writer, LocalLava.heatRate, LocalLava.heat, LocalLava.height);
                Writer.Write(LocalLava.isKiln);
                Writer.Write(VisualFingerprintWriter.HashSerializedObject(ZoneMaterial!, "PLL/NativeLavaZoneMaterial/v1"));
            }
            Writer.Write(LocalWater ? true : false);
            if (LocalWater)
            {
                WriteScalars(Writer, LocalWater.movementModifierShallow, LocalWater.movementModifierDeep,
                    LocalWater.movementModifierJumping, LocalWater.jumpMultiplier, LocalWater.animModShallow, LocalWater.animModDeep);
                Writer.Write(LocalWater.disableSprintJump);
                Writer.Write(VisualFingerprintWriter.HashSerializedObject(ZoneMaterial!, "PLL/NativeWaterZoneMaterial/v1"));
            }
        }
        Fingerprint = Stream.ToArray();
    }

    internal static NativeFlowProvider Capture(NativeSegmentCatalog Catalog, PLLNativeResolverKind Kind)
    {
        var Segment = Catalog.RequireOrdinary(Kind == PLLNativeResolverKind.Water ? "Tropics" : "Caldera");
        var Pending = new Queue<GameObject>(); var Seen = new HashSet<GameObject>();
        var SeenRivers = new HashSet<LavaRiver>();
        Pending.Enqueue(Segment.SegmentParent); Seen.Add(Segment.SegmentParent);
        NativeFlowProvider? Result = null;
        Lava? LocalLava = null;
        WaterZone? LocalWater = null;
        if (Kind == PLLNativeResolverKind.Water)
            foreach (var Water in Catalog.RequireOrdinary("Beach").SegmentParent.GetComponentsInChildren<WaterZone>(true))
                if (Water.GetType() == typeof(WaterZone))
                {
                    if (LocalWater) throw new InvalidOperationException("PEAK's Beach source has multiple canonical WaterZone providers.");
                    LocalWater = Water;
                }
        if (Kind == PLLNativeResolverKind.Lava)
            foreach (var Lava in Segment.SegmentParent.GetComponentsInChildren<Lava>(true))
                if (Lava.GetType() == typeof(Lava))
                {
                    if (LocalLava) throw new InvalidOperationException("PEAK's Caldera source has multiple local Lava providers.");
                    LocalLava = Lava;
                }
        while (Pending.Count != 0)
        {
            var Root = Pending.Dequeue();
            foreach (var River in Root.scene.IsValid() ? Array.Empty<LavaRiver>() : Root.GetComponentsInChildren<LavaRiver>(true))
            {
                if (!SeenRivers.Add(River)) continue;
                var Extrudes = River.GetComponentsInChildren<SplineExtrude>(true);
                if (Extrudes.Length != 1) continue;
                var Extrude = Extrudes[0]; var Renderer = Extrude.GetComponent<MeshRenderer>();
                if (!Renderer || Renderer.sharedMaterials.Length != 1 || !Renderer.sharedMaterial) continue;
                var Material = Renderer.sharedMaterial;
                var Water = River.spawnAlongSpline && River.spawnAlongSpline.GetComponent<WaterfallTrigger>()
                    && River.spawnAlongSpline.GetComponent<StatusEmitter>();
                var Lava = Extrude.GetComponent<MeshCollider>() && Extrude.GetComponent<CollisionModifier>();
                if (Kind == PLLNativeResolverKind.Water ? !Water || Lava : !Lava || Water) continue;
                if (!River.endRock || !TransformOwnership.Contains(River.transform, River.endRock.transform)
                    || !Extrude.Container || Extrude.Container.transform != Extrude.transform
                    || River.GetComponentsInChildren<PhotonView>(true).Length != 0
                    || River.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                    throw new InvalidOperationException("PEAK's native flow provider has an incomplete or network-owned graph.");
                if (Result is not null)
                    throw new InvalidOperationException($"PEAK has multiple canonical {Kind} flow prefab recipes.");
                Result = new NativeFlowProvider(Kind, River, Extrude, Material, LocalLava, LocalWater);
            }
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
                if (NativePrefabContracts.EffectiveProps(Spawner) is { } Candidates)
                    foreach (var Candidate in Candidates)
                    {
                        if (!Candidate) throw new InvalidOperationException("PEAK's native flow candidate is missing.");
                        if (Seen.Add(Candidate)) Pending.Enqueue(Candidate);
                    }
        }
        return Result ?? throw new InvalidOperationException($"PEAK's canonical {Kind} flow prefab recipe is missing.");
    }

    internal bool Matches(NativeFlowProvider Other) => Fingerprint.AsSpan().SequenceEqual(Other.Fingerprint);
    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }

    internal static void WriteScalars(BinaryWriter Writer, params float[] Values)
    {
        foreach (var Value in Values)
        {
            if (!ManifestEncoding.CanEncode(Value)) throw new ArgumentException("A native flow scalar exceeds the synchronized numeric range.");
            Writer.Write(Value == 0f ? 0 : BitConverter.SingleToInt32Bits(Value));
        }
    }

    internal static void WriteContact(BinaryWriter Writer, CollisionModifier Contact)
    {
        Writer.Write(Contact.applyEffects); Writer.Write(Contact.applyPetrify); Writer.Write((int)Contact.statusType);
        WriteScalars(Writer, Contact.damage, Contact.cooldown, Contact.knockback,
            Contact.ragdolledKnockback, Contact.knockbackTowardsFwdVector);
        Writer.Write(Contact.knockbackOnlyWhenStanding); Writer.Write(Contact.standable);
        Writer.Write(Contact.preventFallDamageIfKnockback); Writer.Write(Contact.additionalMods.Count);
        foreach (var Mod in Contact.additionalMods)
        {
            Writer.Write((int)Mod.statusType); WriteScalars(Writer, Mod.amount, Mod.knockback);
        }
        Writer.Write(Contact.useBounceCoroutine);
        WriteScalars(Writer, Contact.superBouncePow, Contact.sinceGroundedOffset,
            Contact.bounceDotMinimum, Contact.staminaBackOnBounce);
    }

    internal static int WritePrefab(BinaryWriter Writer, GameObject? Prefab)
    {
        Writer.Write(Prefab ? true : false); if (!Prefab) return 0;
        var Nodes = PllNativeFlowSettings.ValidateOutput(Prefab);
        var Root = Prefab.transform;
        var Structural = DeterministicObjectIdentity.HashObjectHierarchy(Root, "PLL/NativeFlowOutputTemplate/v1");
        Writer.Write(Structural.Length); Writer.Write(Structural);
        var Particles = Prefab.GetComponentsInChildren<ParticleSystem>(true);
        var Visuals = VisualFingerprintWriter.HashParticleGraph(Root, Root, Particles, Particles);
        Writer.Write(Visuals.Length); Writer.Write(Visuals);
        foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
        {
            switch (Component)
            {
                case Transform or MeshFilter or MeshRenderer or ParticleSystem or ParticleSystemRenderer or IgnoreRenderersActive: break;
                case MeshCollider Collider:
                    WriteCollider(Writer, Collider);
                    Writer.Write(Collider.convex); Writer.Write((int)Collider.cookingOptions);
                    if (Collider.sharedMesh) Writer.Write(VisualFingerprintWriter.HashSerializedObject(Collider.sharedMesh, "PLL/FlowColliderMesh/v1"));
                    break;
                case CapsuleCollider Collider:
                    WriteCollider(Writer, Collider); Writer.Write(Collider.direction);
                    ManifestEncoding.WriteVector(Writer, Collider.center); WriteScalars(Writer, Collider.radius, Collider.height); break;
                case LODGroup Group:
                    ManifestEncoding.WriteVector(Writer, Group.localReferencePoint); WriteScalars(Writer, Group.size);
                    Writer.Write((int)Group.fadeMode); Writer.Write(Group.animateCrossFading);
                    var Lods = Group.GetLODs(); Writer.Write(Lods.Length);
                    foreach (var Lod in Lods)
                    {
                        WriteScalars(Writer, Lod.screenRelativeTransitionHeight, Lod.fadeTransitionWidth);
                        Writer.Write(Lod.renderers.Length);
                        foreach (var Renderer in Lod.renderers)
                        {
                            if (!Renderer || !TransformOwnership.Contains(Root, Renderer.transform))
                                throw new ArgumentException("A flow LOD renderer leaves its output prefab.");
                            Writer.Write(DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Renderer, Root));
                        }
                    }
                    break;
                case AudioSource Audio: WriteAudio(Writer, Audio); break;
                case AudioLoop Loop:
                    if (!Loop.loop || !TransformOwnership.Contains(Root, Loop.loop.transform))
                        throw new ArgumentException("A flow audio loop leaves its output prefab.");
                    WriteScalars(Writer, Loop.volume, Loop.pitch);
                    Writer.Write(DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Loop.loop, Root)); break;
                case WaterfallTrigger Trigger:
                    WriteScalars(Writer, Trigger.force, Trigger.drag, Trigger.ragdollLength); Writer.Write(Trigger.ragdoll); break;
                case StatusEmitter Emitter when Emitter.GetType() == typeof(StatusEmitter):
                    if (Emitter.statusType != CharacterAfflictions.STATUSTYPE.Hot || Emitter.amount > 0f
                        || Emitter.radius <= 0f || Emitter.tickTime <= 0f || Emitter.preventOverlap || Emitter.inZoneWarning)
                        throw new InvalidOperationException("Water flow status needs native local cooling without overlap ownership.");
                    Writer.Write((int)Emitter.statusType);
                    WriteScalars(Writer, Emitter.amount, Emitter.radius, Emitter.outerFade, Emitter.innerFade,
                        Emitter.minAmount, Emitter.tickTime, Emitter.extraWarningTime, Emitter.randomStart);
                    Writer.Write(Emitter.inZoneWarning); Writer.Write(Emitter.preventOverlap); Writer.Write(Emitter.overlapIdentifier); break;
                case WaterfallPusher Pusher:
                    if (!Pusher.sfx || !TransformOwnership.Contains(Root, Pusher.sfx.transform))
                        throw new ArgumentException("A waterfall SFX binding leaves its output prefab.");
                    WriteScalars(Writer, Pusher.fallTime, Pusher.knockback, Pusher.downwardKnockback, Pusher.cooldown); break;
                case SFX_PlayOneShot Sfx:
                    if (Sfx.sfxs is null || Sfx.sfxs.Length > 1)
                        throw new InvalidOperationException("Flow SFX needs one native sound handle.");
                    Writer.Write(Sfx.playOnStart); Writer.Write(Sfx.playOnEnable); Writer.Write(Sfx.followTransform); Writer.Write(Sfx.stopOnDisable);
                    WriteSfx(Writer, Sfx.sfx); Writer.Write(Sfx.sfxs.Length);
                    foreach (var Value in Sfx.sfxs) WriteSfx(Writer, Value); break;
                case BakedVolumeLight Light:
                    Writer.Write((int)Light.mode); ManifestEncoding.WriteColor(Writer, Light.color);
                    WriteScalars(Writer, Light.intensity, Light.radius, Light.falloff, Light.coneFalloff, Light.coneSize); break;
                case Light Light:
                    Writer.Write(Light.enabled); Writer.Write((int)Light.type);
                    var Baking = Light.bakingOutput;
                    Writer.Write((int)Baking.lightmapBakeType); Writer.Write((int)Baking.mixedLightingMode);
                    Writer.Write(Baking.isBaked); Writer.Write(Baking.probeOcclusionLightIndex); Writer.Write(Baking.occlusionMaskChannel);
                    ManifestEncoding.WriteColor(Writer, Light.color);
                    WriteScalars(Writer, Light.intensity, Light.range, Light.spotAngle, Light.innerSpotAngle,
                        Light.bounceIntensity, Light.shadowStrength, Light.shadowBias, Light.shadowNormalBias,
                        Light.shadowNearPlane, Light.cookieSize2D.x, Light.cookieSize2D.y, Light.colorTemperature);
                    Writer.Write(Light.useColorTemperature); Writer.Write((int)Light.shadows);
                    Writer.Write((int)Light.renderMode); Writer.Write(Light.cullingMask); Writer.Write(Light.renderingLayerMask);
                    Writer.Write(Light.cookie ? true : false);
                    if (Light.cookie) Writer.Write(VisualFingerprintWriter.HashSerializedObject(Light.cookie, "PLL/NativeFlowLightCookie/v1"));
                    break;
                default: throw new InvalidOperationException($"Native flow output component '{Component.GetType().Name}' is unsupported.");
            }
        }
        return Nodes;
    }

    static void WriteCollider(BinaryWriter Writer, Collider Collider)
    {
        Writer.Write(Collider.enabled); Writer.Write(Collider.isTrigger);
        WriteScalars(Writer, Collider.contactOffset);
        Writer.Write(Collider.includeLayers.value); Writer.Write(Collider.excludeLayers.value);
        Writer.Write(Collider.layerOverridePriority); Writer.Write(Collider.providesContacts);
        var Material = Collider.sharedMaterial; Writer.Write(Material ? true : false);
        if (!Material) return;
        WriteScalars(Writer, Material.dynamicFriction, Material.staticFriction, Material.bounciness);
        Writer.Write((int)Material.frictionCombine); Writer.Write((int)Material.bounceCombine);
    }

    internal static void WriteAudio(BinaryWriter Writer, AudioSource Audio)
    {
        if (!Audio.clip || Audio.resource != Audio.clip)
            throw new InvalidOperationException("Native flow audio needs a direct clip resource.");
        BiomeAuthoringContract.WriteClip(Writer, Audio.clip);
        Writer.Write(Audio.enabled); Writer.Write(Audio.loop); Writer.Write(Audio.playOnAwake); Writer.Write(Audio.priority);
        Writer.Write(Audio.mute); Writer.Write(Audio.bypassEffects); Writer.Write(Audio.bypassListenerEffects);
        Writer.Write(Audio.bypassReverbZones); Writer.Write(Audio.ignoreListenerPause); Writer.Write(Audio.ignoreListenerVolume);
        Writer.Write(Audio.spatialize); Writer.Write(Audio.spatializePostEffects); Writer.Write((int)Audio.velocityUpdateMode);
        Writer.Write((int)Audio.rolloffMode);
        WriteScalars(Writer, Audio.volume, Audio.pitch, Audio.panStereo, Audio.spatialBlend, Audio.reverbZoneMix,
            Audio.dopplerLevel, Audio.spread, Audio.minDistance, Audio.maxDistance);
        var Mixer = AudioMixerRouteFingerprint.Create(Audio.outputAudioMixerGroup, default, Audio.name, "native flow");
        Mixer.WriteManifest(Writer);
        NativeNetworkPrefabGraph.WriteCurve(Writer, Audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        NativeNetworkPrefabGraph.WriteCurve(Writer, Audio.GetCustomCurve(AudioSourceCurveType.SpatialBlend));
        NativeNetworkPrefabGraph.WriteCurve(Writer, Audio.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix));
        NativeNetworkPrefabGraph.WriteCurve(Writer, Audio.GetCustomCurve(AudioSourceCurveType.Spread));
    }

    internal static void WriteSfx(BinaryWriter Writer, SFX_Instance? Sfx)
    {
        Writer.Write(Sfx ? true : false); if (!Sfx) return;
        if (Sfx.clips is not { Length: > 0 } || Sfx.settings is null)
            throw new ArgumentException("Flow SFX has no native audio payload.");
        foreach (var Clip in Sfx.clips) if (!Clip) throw new ArgumentException("Flow SFX contains a missing clip.");
        BiomeAuthoringContract.WriteClips(Writer, Sfx.clips);
        var Settings = Sfx.settings;
        WriteScalars(Writer, Settings.volume, Settings.volume_Variation, Settings.pitch,
            Settings.pitch_Variation, Settings.spatialBlend, Settings.dopplerLevel, Settings.range, Settings.cooldown);
        Writer.Write(Settings.maxInstances_NOT_IMPLEMENTED);
    }
}

internal sealed class FluidDefinitionContract
{
    readonly byte[] Fingerprint;
    internal readonly PllFluidDefinition Source;
    internal readonly PllFluidBehavior Behavior;
    internal readonly float MovementShallow, MovementDeep, AnimationShallow, AnimationDeep;
    internal readonly float MovementJumping, JumpMultiplier, HeatRate, Heat;
    internal readonly bool DisableSprintJump, IsKiln;
    internal readonly PllFluidMaterialMode ZoneMaterialMode, FlowMaterialMode;
    internal readonly Material? ZoneMaterial, FlowMaterial;
    readonly PllFlowOutput Landing, Splash, Samples, Details;
    readonly GameObject? LandingPrefab, SplashPrefab, SamplesPrefab;
    readonly GameObject[] DetailsPrefabs;

    FluidDefinitionContract(PllFluidDefinition Source)
    {
        Source.Validate(); this.Source = Source; Behavior = Source.Behavior;
        MovementShallow = Source.MovementShallow; MovementDeep = Source.MovementDeep;
        AnimationShallow = Source.AnimationShallow; AnimationDeep = Source.AnimationDeep;
        MovementJumping = Source.MovementJumping; JumpMultiplier = Source.JumpMultiplier;
        DisableSprintJump = Source.DisableSprintJump; HeatRate = Source.HeatRate; Heat = Source.Heat; IsKiln = Source.IsKiln;
        ZoneMaterialMode = Source.ZoneMaterialMode; ZoneMaterial = Source.ZoneMaterial;
        FlowMaterialMode = Source.FlowMaterialMode; FlowMaterial = Source.FlowMaterial;
        Landing = Source.Landing; Splash = Source.Splash; Samples = Source.Samples; Details = Source.Details;
        LandingPrefab = Source.LandingPrefab; SplashPrefab = Source.SplashPrefab; SamplesPrefab = Source.SamplesPrefab;
        DetailsPrefabs = (GameObject[])Source.DetailsPrefabs.Clone();
        Fingerprint = Encode(Source);
    }

    internal static FluidDefinitionContract Capture(PllFluidDefinition Source,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions)
    {
        if (!Source) throw new ArgumentException("Custom Fluid needs a Fluid Definition.");
        if (Definitions.TryGetValue(Source, out var Existing)) return Existing;
        var Contract = new FluidDefinitionContract(Source); Definitions.Add(Source, Contract); return Contract;
    }

    internal void Validate()
    {
        if (!Source || !Fingerprint.AsSpan().SequenceEqual(Encode(Source)))
            throw new InvalidOperationException("Fluid Definition changed after preparation.");
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }

    static byte[] Encode(PllFluidDefinition Source)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write("PLL/FluidDefinition/v1"); Writer.Write(Source.DisplayName); Writer.Write((byte)Source.Behavior);
            NativeFlowProvider.WriteScalars(Writer, Source.MovementShallow, Source.MovementDeep,
                Source.AnimationShallow, Source.AnimationDeep, Source.MovementJumping, Source.JumpMultiplier,
                Source.HeatRate, Source.Heat);
            Writer.Write(Source.DisableSprintJump); Writer.Write(Source.IsKiln);
            WriteMaterial(Source.ZoneMaterialMode, Source.ZoneMaterial);
            WriteMaterial(Source.FlowMaterialMode, Source.FlowMaterial);
            WriteOutput(Source.Landing, Source.LandingPrefab); WriteOutput(Source.Splash, Source.SplashPrefab);
            WriteOutput(Source.Samples, Source.SamplesPrefab); Writer.Write((byte)Source.Details);
            Writer.Write(Source.DetailsPrefabs.Length);
            foreach (var Prefab in Source.DetailsPrefabs) NativeFlowProvider.WritePrefab(Writer, Prefab);

            void WriteMaterial(PllFluidMaterialMode Mode, Material? Material)
            {
                Writer.Write((byte)Mode); Writer.Write(Material ? true : false);
                if (Material) Writer.Write(VisualFingerprintWriter.HashSerializedObject(Material, "PLL/FluidMaterial/v1"));
            }
            void WriteOutput(PllFlowOutput Choice, GameObject? Prefab)
            {
                Writer.Write((byte)Choice); NativeFlowProvider.WritePrefab(Writer, Prefab);
            }
        }
        return Stream.ToArray();
    }

    internal PllNativeFlowSettings Resolve(PllNativeFlowSettings Settings)
    {
        if (Settings.Material) throw new ArgumentException("Custom Fluid material belongs to its Fluid Definition.");
        Settings.Material = FlowMaterial;
        Settings.LandingModel = ResolveChoice(Settings.LandingModel, Landing);
        if (Settings.LandingModel == PllFlowOutput.Custom && !Settings.EndRock) Settings.EndRock = LandingPrefab;
        Settings.SplashPresentation = ResolveChoice(Settings.SplashPresentation, Splash);
        if (Settings.SplashPresentation == PllFlowOutput.Custom && !Settings.Splash) Settings.Splash = SplashPrefab;
        Settings.SamplePresentation = ResolveChoice(Settings.SamplePresentation, Samples);
        if (Settings.SamplePresentation == PllFlowOutput.Custom && !Settings.SpawnAlongSpline) Settings.SpawnAlongSpline = SamplesPrefab;
        var HasDetails = Settings.DetailsAlongSpline is { Length: > 0 };
        Settings.DetailPresentation = ResolveChoice(Settings.DetailPresentation, Details);
        if (Settings.DetailPresentation == PllFlowOutput.Custom && !HasDetails) Settings.DetailsAlongSpline = DetailsPrefabs;
        Settings.GenerateSplash = Settings.SplashPresentation != PllFlowOutput.None;
        Settings.GenerateAlongSpline = Settings.SamplePresentation != PllFlowOutput.None;
        Settings.GenerateDetails = Settings.DetailPresentation != PllFlowOutput.None;
        return Settings;
    }

    static PllFlowOutput ResolveChoice(PllFlowOutput Choice, PllFlowOutput Default) =>
        Choice is PllFlowOutput.Legacy or PllFlowOutput.UseFluidDefault ? Default : Choice;
}

internal sealed class NativeFlowContract
{
    readonly ComponentBinding<PLLNativeResolver> Component;
    readonly PllNativeFlowSettings Settings;
    readonly byte[] Scalars;
    readonly TransformBinding? RayStart, RayEnd, Endpoint;
    readonly ComponentBinding<MeshRenderer>? Surface;
    readonly byte[]? SurfaceFingerprint;
    readonly FlowPrefab? EndRock, Splash, AlongSpline;
    readonly FlowPrefab[]? Details;
    readonly FluidDefinitionContract? Fluid;
    readonly ComponentBinding<MeshRenderer>[] AuthoredSurfaces;
    readonly int[] SurfaceSlots;
    readonly TransformBinding[] ContactFrames;
    readonly PllZoneVolume[] ContactRegions;
    readonly ComponentBinding<Collider>[] ContactProxies;
    readonly ObjectBinding? Emitter;
    internal readonly SurfaceTextureAuthoringContract? SurfaceTextures;

    NativeFlowContract(Transform Template, PLLNativeResolver Source, FluidDefinitionContract? Fluid,
        SurfaceTextureAuthoringContract? SurfaceTextures, BiomeId BiomeId)
    {
        this.Template = Template; CoverDelaySeconds = Source.Transition.CoverDelaySeconds;
        this.SurfaceTextures = SurfaceTextures; this.BiomeId = BiomeId;
        Component = ComponentBinding<PLLNativeResolver>.Create(Template, Source);
        for (var Node = Source.transform; Node != Template; Node = Node.parent)
            if (!Node.gameObject.activeSelf)
                throw new ArgumentException("A flow source must be active under its template.");
        this.Fluid = Fluid; Kind = Source.Kind; Form = Source.Form; Profile = Source.SourceProfile;
        InitiallyEnabled = Source.InitiallyEnabled; ContactMode = Source.ContactMode;
        Behavior = Fluid?.Behavior ?? (Kind == PLLNativeResolverKind.Water ? PllFluidBehavior.Water : PllFluidBehavior.Lava);
        Volume = Source.Volume; HeatRate = Fluid?.HeatRate ?? Source.LavaHeatRate;
        Heat = Fluid?.Heat ?? Source.LavaHeat; IsKiln = Fluid?.IsKiln ?? Source.LavaIsKiln;
        Settings = Form == PllNativeResolverForm.Source ? ResolveSettings(Source, Fluid)
            : new PllNativeFlowSettings { Material = Fluid is null ? Source.GeneratedSurfaceMaterial : Fluid.ZoneMaterial };
        if (SurfaceTextures is not null) Settings.Material = SurfaceTextures.Material;
        if (!Settings.GenerateSplash) Settings.Splash = null;
        if (!Settings.GenerateAlongSpline) Settings.SpawnAlongSpline = null;
        if (!Settings.GenerateDetails) Settings.DetailsAlongSpline = null;
        if (!Settings.EnablePusher) Settings.PusherSfx = null;
        if (Settings.GenerateDetails && Settings.DetailsAlongSpline is not null)
        {
            Settings.DetailsAlongSpline = (GameObject[])Settings.DetailsAlongSpline.Clone();
            Details = new FlowPrefab[Settings.DetailsAlongSpline.Length];
            for (var i = 0; i < Details.Length; i++) Details[i] = CaptureOutput(Settings.DetailsAlongSpline[i]);
        }
        if (Settings.EndRock) EndRock = CaptureOutput(Settings.EndRock);
        if (Settings.GenerateSplash && Settings.Splash) Splash = CaptureOutput(Settings.Splash);
        if (Settings.GenerateAlongSpline && Settings.SpawnAlongSpline)
        {
            AlongSpline = CaptureOutput(Settings.SpawnAlongSpline);
        }
        if (Settings.RayStart) RayStart = TransformBinding.Create(Template, Settings.RayStart);
        if (Settings.RayEnd) RayEnd = TransformBinding.Create(Template, Settings.RayEnd);
        if (Settings.Endpoint) Endpoint = TransformBinding.Create(Template, Settings.Endpoint);
        if (Source.SurfaceRenderer)
        {
            Surface = ComponentBinding<MeshRenderer>.Create(Template, Source.SurfaceRenderer);
            SurfaceFingerprint = VisualFingerprintWriter.HashParticleGraph(Source.SurfaceRenderer.transform, Template,
                Array.Empty<ParticleSystem>(), Array.Empty<ParticleSystem>());
        }
        Scalars = EncodeScalars(Source);
        AuthoredSurfaces = new ComponentBinding<MeshRenderer>[Source.SurfaceRenderers.Count];
        SurfaceSlots = new int[AuthoredSurfaces.Length];
        for (var i = 0; i < AuthoredSurfaces.Length; i++)
        {
            AuthoredSurfaces[i] = ComponentBinding<MeshRenderer>.Create(Template, Source.SurfaceRenderers[i]);
            SurfaceSlots[i] = Source.SurfaceSlots[i];
        }
        ContactFrames = new TransformBinding[Source.ContactRegionCount];
        ContactRegions = new PllZoneVolume[ContactFrames.Length];
        for (var i = 0; i < ContactFrames.Length; i++)
        {
            ContactFrames[i] = TransformBinding.Create(Template, Source.ContactFrames[i]);
            ContactRegions[i] = Source.ContactRegion(i);
        }
        ContactProxies = new ComponentBinding<Collider>[Source.ContactProxies.Count];
        for (var i = 0; i < ContactProxies.Length; i++)
            ContactProxies[i] = ComponentBinding<Collider>.Create(Template, Source.ContactProxies[i]);
        if (Source.EmitterModel) Emitter = ObjectBinding.Create(Template, Source.EmitterModel);

        FlowPrefab CaptureOutput(GameObject Prefab)
        {
            if (Behavior == PllFluidBehavior.VisualOnly
                && (Prefab.GetComponentsInChildren<Collider>(true).Length != 0
                    || Prefab.GetComponentsInChildren<StatusEmitter>(true).Length != 0
                    || Prefab.GetComponentsInChildren<WaterfallTrigger>(true).Length != 0
                    || Prefab.GetComponentsInChildren<WaterfallPusher>(true).Length != 0))
                throw new ArgumentException("Visual Only fluid decoration cannot contain contact or status components.");
            return FlowPrefab.Capture(Template, Prefab,
                Behavior == PllFluidBehavior.Water ? PLLNativeResolverKind.Water : PLLNativeResolverKind.Lava);
        }
    }

    internal PLLNativeResolverKind Kind { get; }
    internal PllFluidBehavior Behavior { get; }
    internal bool InitiallyEnabled { get; }
    internal PllFluidContactMode ContactMode { get; }
    internal int NativeSourceMask => Behavior switch
    {
        PllFluidBehavior.Water => 1 << (int)PLLNativeResolverKind.Water,
        PllFluidBehavior.Lava => 1 << (int)PLLNativeResolverKind.Lava,
        PllFluidBehavior.VisualOnly => 0,
        _ => throw new InvalidOperationException("The prepared fluid behavior is invalid.")
    };
    internal FluidDefinitionContract? Definition => Fluid;
    internal PllFluidMaterialMode MaterialMode => SurfaceTextures is not null ? PllFluidMaterialMode.Custom : Fluid is null
        ? Settings.Material ? PllFluidMaterialMode.NativeOverrides : PllFluidMaterialMode.NativeDefault
        : Form == PllNativeResolverForm.Zone ? Fluid.ZoneMaterialMode : Fluid.FlowMaterialMode;
    internal bool HasLandingModel => Settings.LandingModel != PllFlowOutput.None;
    internal bool HasWaterContacts => Behavior == PllFluidBehavior.Water && Form == PllNativeResolverForm.Source;
    internal PllNativeResolverForm Form { get; }
    internal PllNativeFlowProfile Profile { get; }
    internal PllZoneVolume Volume { get; }
    internal float HeatRate { get; }
    internal float Heat { get; }
    internal bool IsKiln { get; }
    internal PLLNativeResolver Source => Component.Source;
    internal Transform Template { get; }
    internal BiomeId BiomeId { get; }
    internal float CoverDelaySeconds { get; }
    internal ComponentBinding<PLLNativeResolver> SourceBinding => Component;
    internal int? EndRockNodes => EndRock?.Nodes;
    internal int? SplashNodes => Splash?.Nodes;
    internal int? AlongSplineNodes => AlongSpline?.Nodes;
    internal int? DetailNodes(int Index) => Details is null ? null : Details[Index].Nodes;

    internal static NativeFlowContract Capture(Transform Template, BiomeId Id, PLLNativeResolver Source,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions,
        IReadOnlyDictionary<PLLNativeResolver, SurfaceTextureAuthoringContract> SurfaceTextures)
    {
        Source.ValidateContactArrays();
        if (Source.Form == PllNativeResolverForm.Source) Source.SourceSettings.ValidateOutputSelections(Source.FluidDefinition);
        var Fluid = Source.Kind == PLLNativeResolverKind.CustomFluid
            ? FluidDefinitionContract.Capture(Source.FluidDefinition!, Definitions) : null;
        if (Fluid is null && Source.FluidDefinition)
            throw new ArgumentException($"Biome '{Id}' native resolver cannot carry a Custom Fluid definition.");
        if (Fluid is not null && (Source.GeneratedSurfaceMaterial || Source.SourceSettings.Material))
            throw new ArgumentException("Custom Fluid materials belong to its Fluid Definition.");
        if (Source.Form == PllNativeResolverForm.Source && Source.GeneratedSurfaceMaterial
            || Source.Form == PllNativeResolverForm.Source && Source.SourceProfile == PllNativeFlowProfile.SimulatedSpline && Source.SurfaceRenderer)
            throw new ArgumentException("Fluid Source cannot carry an unused Zone or authored surface material.");
        var Behavior = Fluid?.Behavior ?? (Source.Kind == PLLNativeResolverKind.Water ? PllFluidBehavior.Water : PllFluidBehavior.Lava);
        var Settings = Source.Form == PllNativeResolverForm.Source ? ResolveSettings(Source, Fluid) : Source.SourceSettings;
        if (Source.Form == PllNativeResolverForm.Source)
            Settings.Validate(Behavior == PllFluidBehavior.Water ? PLLNativeResolverKind.Water : PLLNativeResolverKind.Lava,
                Source.SourceProfile);
        else if (Behavior != PllFluidBehavior.VisualOnly && Source.Volume.Shape != PllZoneShape.Box)
            throw new ArgumentException($"Biome '{Id}' fluid Zone needs a Box volume.");
        ValidateStaticSource(Source.transform);
        if (Fluid is not null && Source.Form == PllNativeResolverForm.Zone && Behavior != PllFluidBehavior.VisualOnly)
            PllNativeFlowSettings.ValidateZoneTransform(Source.transform);
        if (Source.Form == PllNativeResolverForm.Source && Source.SourceProfile == PllNativeFlowProfile.RayWaterfall
            && (!TransformOwnership.Contains(Source.transform, Settings.RayStart!)
                || !TransformOwnership.Contains(Source.transform, Settings.RayEnd!)
                || Settings.Endpoint && !TransformOwnership.Contains(Source.transform, Settings.Endpoint)))
            throw new ArgumentException($"Biome '{Id}' waterfall anchors must belong to its source.");
        if (Source.Form == PllNativeResolverForm.Source && Settings.EnablePusher && Settings.PusherSfx
            && (Settings.PusherSfx.clips is not { Length: > 0 } || Settings.PusherSfx.settings is null))
            throw new ArgumentException($"Biome '{Id}' waterfall SFX has no native audio payload.");
        if (!Source.Root || Source.Root != Source.gameObject || !Source.enabled || Source.Presentation
            || PllAuthoringValueContract.TransitionFailure(Source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException($"Biome '{Id}' flow root or presentation is invalid.");
        if (Source.Transition.CoverDelaySeconds > 0f && Source.Form != PllNativeResolverForm.Source && Fluid is null)
            throw new ArgumentException($"Biome '{Id}' native Lava Zone requires zero Cover Delay.");
        if (Source.Transition.CoverDelaySeconds > 0f && Settings.SpawnLights)
            throw new ArgumentException($"Biome '{Id}' baked flow lighting cannot fade. Set Cover Delay to zero.");
        if (!Source.InitiallyEnabled && Settings.SpawnLights)
            throw new ArgumentException("Initially disabled fluid sources cannot use baked flow lights. Disable Spawn Lights.");
        if (Source.GetComponentsInChildren<Rigidbody>(true).Length != 0
            || Source.GetComponentInParent<Animator>(true) || Source.GetComponentInParent<Animation>(true)
            || Source.GetComponentsInChildren<Animator>(true).Length != 0
            || Source.GetComponentsInChildren<Animation>(true).Length != 0)
            throw new ArgumentException($"Biome '{Id}' native flow cannot use a moving or animated source.");
        if (Source.GetComponentsInChildren<CustomSpawnCondition>(true).Length != 0)
            throw new ArgumentException($"Biome '{Id}' native flow contains a prebuilt spawn condition.");
        if (Behavior == PllFluidBehavior.Lava && (!float.IsFinite(Fluid?.HeatRate ?? Source.LavaHeatRate)
            || (Fluid?.HeatRate ?? Source.LavaHeatRate) <= 0f
            || !float.IsFinite(Fluid?.Heat ?? Source.LavaHeat) || (Fluid?.Heat ?? Source.LavaHeat) < 0f))
            throw new ArgumentException($"Biome '{Id}' local lava heat settings are invalid.");
        if ((Source.Form == PllNativeResolverForm.Zone || Source.SourceProfile == PllNativeFlowProfile.RayWaterfall)
            && Source.SourceProfile != PllNativeFlowProfile.AuthoredSurface)
        {
            var Renderer = Source.SurfaceRenderer;
            if (!Renderer || !TransformOwnership.Contains(Source.transform, Renderer.transform)
                || !Renderer.enabled || Renderer.forceRenderingOff
                || !Renderer.GetComponent<MeshFilter>() || !Renderer.GetComponent<MeshFilter>().sharedMesh
                || Renderer.sharedMaterials.Length != 1 || !Renderer.sharedMaterial)
                throw new ArgumentException($"Biome '{Id}' flow needs an owned mesh surface and one material.");
            for (var Node = Renderer.transform; Node != Source.transform; Node = Node.parent)
                if (!Node.gameObject.activeSelf)
                    throw new ArgumentException($"Biome '{Id}' flow surface must be active under its source.");
        }
        Source.ValidateEmitterModel();
        if (Source.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
        {
            Source.ValidateAuthoredSurface(Behavior);
            if (Settings.GenerateSplash || Settings.GenerateAlongSpline || Settings.GenerateDetails
                || Settings.SpawnLights || Settings.EnablePusher || Settings.LandingModel != PllFlowOutput.None)
                throw new ArgumentException($"Biome '{Id}' Authored Surface does not generate landing models, spline decoration, lights or pushers.");
        }
        else if (Source.SurfaceRenderers.Count != 0 || Source.SurfaceSlots.Count != 0
            || Source.ContactRegionCount != 0 || Source.ContactProxies.Count != 0)
            throw new ArgumentException($"Biome '{Id}' fluid contact payload requires Authored Surface.");
        foreach (var Child in Source.GetComponentsInChildren<Transform>(true))
            if (Child != Source.transform && Child.name == "__PLL_NativeFlow")
                throw new ArgumentException($"Biome '{Id}' contains reserved native flow output.");
        SurfaceTextures.TryGetValue(Source, out var SurfaceProfile);
        if (SurfaceProfile is not null && (Source.Form != PllNativeResolverForm.Source
            || Source.SourceProfile != PllNativeFlowProfile.SimulatedSpline))
            throw new ArgumentException("Generated Surface Textures requires a Simulated Spline fluid source.");
        if (SurfaceProfile is not null && (Fluid is not null
                ? Fluid.FlowMaterialMode != PllFluidMaterialMode.Custom || Fluid.FlowMaterial != SurfaceProfile.Material
                : Settings.Material))
            throw new ArgumentException("Fluid and Surface Textures must use one Custom material. Remove the native material override.");
        return new NativeFlowContract(Template, Source, Fluid, SurfaceProfile, Id);
    }

    internal bool MatchesMetadata(PLLNativeResolver Source)
    {
        var Current = Form == PllNativeResolverForm.Source ? ResolveSettings(Source, Fluid) : Source.SourceSettings;
        if (SurfaceTextures is not null) Current.Material = SurfaceTextures.Material;
        if (Kind != Source.Kind || Form != Source.Form || Profile != Source.SourceProfile || !Volume.Equals(Source.Volume)
            || Fluid?.Source != Source.FluidDefinition || Surface?.Source != Source.SurfaceRenderer
            || !Scalars.AsSpan().SequenceEqual(EncodeScalars(Source))) return false;
        if (Form != PllNativeResolverForm.Source)
            return Settings.Material == (Fluid is null ? Source.GeneratedSurfaceMaterial : Fluid.ZoneMaterial);
        if (Settings.RayStart != Current.RayStart || Settings.RayEnd != Current.RayEnd
            || Settings.Endpoint != Current.Endpoint || Settings.EndRock != Current.EndRock
            || Settings.GenerateSplash && Settings.Splash != Current.Splash
            || Settings.GenerateAlongSpline && Settings.SpawnAlongSpline != Current.SpawnAlongSpline
            || Settings.Material != Current.Material
            || Settings.EnablePusher && Settings.PusherSfx != Current.PusherSfx) return false;
        if (!Settings.GenerateDetails) return true;
        if (Settings.DetailsAlongSpline is null || Current.DetailsAlongSpline is null)
            return Settings.DetailsAlongSpline is null && Current.DetailsAlongSpline is null;
        if (Settings.DetailsAlongSpline.Length != Current.DetailsAlongSpline.Length) return false;
        for (var i = 0; i < Settings.DetailsAlongSpline.Length; i++)
            if (Settings.DetailsAlongSpline[i] != Current.DetailsAlongSpline[i]) return false;
        return true;
    }

    internal void Validate(Transform Template, BiomeId Id)
    {
        Component.Validate(Template, Id, "native flow");
        if (!MatchesMetadata(Source)) throw new InvalidOperationException($"Biome '{Id}' changed native flow settings after sealing.");
        ValidateBindings(Template, Id);
    }

    internal void ValidateBindings(Transform Template, BiomeId Id)
    {
        Surface?.Validate(Template, Id, "native flow surface");
        if (SurfaceFingerprint is not null && !SurfaceFingerprint.AsSpan().SequenceEqual(
            VisualFingerprintWriter.HashParticleGraph(Surface!.Source.transform, Template,
                Array.Empty<ParticleSystem>(), Array.Empty<ParticleSystem>())))
            throw new InvalidOperationException($"Biome '{Id}' changed its native flow surface before sealing.");
        RayStart?.Validate(Template, Id.Value, "waterfall ray start");
        RayEnd?.Validate(Template, Id.Value, "waterfall ray end");
        Endpoint?.Validate(Template, Id.Value, "waterfall endpoint");
        EndRock?.Validate(Template, Id); Splash?.Validate(Template, Id); AlongSpline?.Validate(Template, Id);
        if (Details is not null) foreach (var Detail in Details) Detail.Validate(Template, Id);
        foreach (var Binding in AuthoredSurfaces) Binding.Validate(Template, Id, "authored fluid surface");
        foreach (var Binding in ContactFrames) Binding.Validate(Template, Id.Value, "authored water contact frame");
        foreach (var Binding in ContactProxies) Binding.Validate(Template, Id, "authored lava contact proxy");
        Emitter?.Validate(Template, Id, "fluid emitter model");
    }

    internal NativeFlowRuntime Resolve(Transform RuntimeRoot, BiomeId Id, GeneratedGeometryBudget Budget, Transform BiomeFrame)
    {
        var Source = Component.Resolve(RuntimeRoot, Id, "native flow");
        if (NativeFlowRuntime.TryGetSourceOwner(Source, out var Existing))
        {
            if (!ReferenceEquals(Existing.Contract, this))
                throw new InvalidOperationException("A native flow source changed its sealed owner.");
            return Existing;
        }
        var Resolved = Settings;
        Resolved.RayStart = RayStart?.Resolve(RuntimeRoot); Resolved.RayEnd = RayEnd?.Resolve(RuntimeRoot);
        Resolved.Endpoint = Endpoint?.Resolve(RuntimeRoot); Resolved.EndRock = EndRock?.Resolve(RuntimeRoot, Id);
        Resolved.Splash = Splash?.Resolve(RuntimeRoot, Id); Resolved.SpawnAlongSpline = AlongSpline?.Resolve(RuntimeRoot, Id);
        if (Details is not null)
        {
            Resolved.DetailsAlongSpline = new GameObject[Details.Length];
            for (var i = 0; i < Details.Length; i++) Resolved.DetailsAlongSpline[i] = Details[i].Resolve(RuntimeRoot, Id);
        }
        var Surfaces = new (MeshRenderer Renderer, int Slot)[AuthoredSurfaces.Length];
        for (var i = 0; i < Surfaces.Length; i++)
            Surfaces[i] = (AuthoredSurfaces[i].Resolve(RuntimeRoot, Id, "authored fluid surface"), SurfaceSlots[i]);
        var Regions = new (Transform Frame, PllZoneVolume Volume)[ContactFrames.Length];
        for (var i = 0; i < Regions.Length; i++) Regions[i] = (ContactFrames[i].Resolve(RuntimeRoot), ContactRegions[i]);
        var Proxies = new Collider[ContactProxies.Length];
        for (var i = 0; i < Proxies.Length; i++) Proxies[i] = ContactProxies[i].Resolve(RuntimeRoot, Id, "authored lava contact proxy");
        return NativeFlowRuntime.Create(this, Source, Resolved, Surface?.Resolve(RuntimeRoot, Id, "flow surface"), Budget,
            Surfaces, Regions, Proxies, Emitter?.Resolve(RuntimeRoot, Id, "fluid emitter model"), BiomeFrame);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer); Writer.Write(Scalars.Length); Writer.Write(Scalars);
        Surface?.WriteManifest(Writer); RayStart?.WriteManifest(Writer); RayEnd?.WriteManifest(Writer); Endpoint?.WriteManifest(Writer);
        Writer.Write(SurfaceFingerprint?.Length ?? 0); if (SurfaceFingerprint is not null) Writer.Write(SurfaceFingerprint);
        Write(EndRock); Write(Splash); Write(AlongSpline);
        Writer.Write(Details?.Length ?? -1);
        if (Details is not null) foreach (var Detail in Details) Detail.WriteManifest(Writer);
        Writer.Write(Fluid is not null); Fluid?.WriteManifest(Writer);
        Writer.Write(AuthoredSurfaces.Length);
        for (var i = 0; i < AuthoredSurfaces.Length; i++) { AuthoredSurfaces[i].WriteManifest(Writer); Writer.Write(SurfaceSlots[i]); }
        Writer.Write(ContactFrames.Length);
        for (var i = 0; i < ContactFrames.Length; i++) { ContactFrames[i].WriteManifest(Writer); ZoneVolumeContract.WriteManifest(Writer, ContactRegions[i]); }
        Writer.Write(ContactProxies.Length); foreach (var Proxy in ContactProxies) Proxy.WriteManifest(Writer);
        Writer.Write(Emitter is not null); Emitter?.WriteManifest(Writer);

        void Write(FlowPrefab? Prefab) { Writer.Write(Prefab is not null); Prefab?.WriteManifest(Writer); }
    }

    static byte[] EncodeScalars(PLLNativeResolver Source)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write((byte)Source.Kind); Writer.Write((byte)Source.Form); Writer.Write((byte)Source.SourceProfile);
            Writer.Write(Source.InitiallyEnabled); Writer.Write((byte)Source.ContactMode);
            ManifestEncoding.WriteScalar(Writer, Source.Transition.CoverDelaySeconds);
            NativeFlowProvider.WriteScalars(Writer, Source.LavaHeatRate, Source.LavaHeat); Writer.Write(Source.LavaIsKiln);
            if (Source.Form == PllNativeResolverForm.Zone)
            {
                Writer.Write(Source.GeneratedSurfaceMaterial ? true : false);
                if (Source.GeneratedSurfaceMaterial)
                    Writer.Write(VisualFingerprintWriter.HashSerializedObject(Source.GeneratedSurfaceMaterial, "PLL/FlowAuthoredMaterial/v1"));
            }
            if (Source.Form == PllNativeResolverForm.Source)
            {
                var Settings = Source.SourceSettings;
                NativeFlowProvider.WriteScalars(Writer, Settings.SpawnVelocity, Settings.Gravity, Settings.WallStick,
                    Settings.Drag, Settings.TimeStep, Settings.MaxLength, Settings.PreferredDistancePerFrame,
                    Settings.Radius, Settings.SegmentsPerUnit, Settings.WaterForce, Settings.WaterDrag,
                    Settings.WaterRagdollLength, Settings.PusherFallTime, Settings.PusherKnockback,
                    Settings.PusherDownwardKnockback, Settings.PusherCooldown);
                Writer.Write(Settings.MaxSteps); Writer.Write(Settings.SpawnLights); Writer.Write(Settings.StepsAlongSpline);
                Writer.Write(Settings.Sides); Writer.Write(Settings.Capped); Writer.Write(Settings.WaterRagdoll);
                Writer.Write(Settings.EnablePusher);
                Writer.Write(Settings.GenerateSplash); Writer.Write(Settings.GenerateAlongSpline); Writer.Write(Settings.GenerateDetails);
                Writer.Write((byte)Settings.LandingPolicy);
                Writer.Write((byte)Settings.LandingModel); Writer.Write((byte)Settings.SplashPresentation);
                Writer.Write((byte)Settings.SamplePresentation); Writer.Write((byte)Settings.DetailPresentation);
                Writer.Write(Settings.Material ? true : false);
                if (Settings.Material) Writer.Write(VisualFingerprintWriter.HashSerializedObject(Settings.Material, "PLL/FlowAuthoredMaterial/v1"));
                NativeFlowProvider.WriteSfx(Writer, Settings.EnablePusher ? Settings.PusherSfx : null);
            }
            Writer.Write(Source.SurfaceRenderers.Count);
            for (var i = 0; i < Source.SurfaceRenderers.Count; i++)
            {
                Writer.Write(Source.SurfaceSlots[i]);
                Writer.Write(VisualFingerprintWriter.HashParticleGraph(Source.SurfaceRenderers[i].transform, Source.transform,
                    Array.Empty<ParticleSystem>(), Array.Empty<ParticleSystem>()));
            }
            Writer.Write(Source.ContactRegionCount);
            for (var i = 0; i < Source.ContactRegionCount; i++)
            {
                ZoneVolumeContract.WriteManifest(Writer, Source.ContactRegion(i));
                var Frame = Source.ContactFrames[i];
                Writer.Write(DeterministicObjectIdentity.RelativeComponentPath(Frame, Source.transform, 0));
                LocalTransformFingerprint.Create(Frame).WriteManifest(Writer);
            }
            Writer.Write(Source.ContactProxies.Count);
            foreach (var Proxy in Source.ContactProxies)
            {
                Writer.Write(DeterministicObjectIdentity.RelativeComponentPath(Proxy, Source.transform, 0));
                LocalTransformFingerprint.Create(Proxy.transform).WriteManifest(Writer);
                Writer.Write(Proxy.enabled); Writer.Write(Proxy.isTrigger);
                switch (Proxy)
                {
                    case BoxCollider Box:
                        Writer.Write((byte)0); ManifestEncoding.WriteVector(Writer, Box.center); ManifestEncoding.WriteVector(Writer, Box.size); break;
                    case SphereCollider Sphere:
                        Writer.Write((byte)1); ManifestEncoding.WriteVector(Writer, Sphere.center); ManifestEncoding.WriteScalar(Writer, Sphere.radius); break;
                    case CapsuleCollider Capsule:
                        Writer.Write((byte)2); ManifestEncoding.WriteVector(Writer, Capsule.center);
                        NativeFlowProvider.WriteScalars(Writer, Capsule.radius, Capsule.height); Writer.Write(Capsule.direction); break;
                    case MeshCollider Mesh:
                        Writer.Write((byte)3); Writer.Write(Mesh.convex); Writer.Write((int)Mesh.cookingOptions);
                        Writer.Write(VisualFingerprintWriter.HashSerializedObject(Mesh.sharedMesh, "PLL/FluidContactMesh/v1")); break;
                    default: throw new InvalidOperationException("The sealed fluid contact proxy is unsupported.");
                }
            }
        }
        return Stream.ToArray();
    }

    static PllNativeFlowSettings ResolveSettings(PLLNativeResolver Source, FluidDefinitionContract? Fluid)
    {
        var Settings = Source.SourceSettings;
        if (Fluid is not null) Settings = Fluid.Resolve(Settings);
        Settings.LandingModel = Resolve(Settings.LandingModel, true, Settings.EndRock);
        Settings.SplashPresentation = Resolve(Settings.SplashPresentation, Settings.GenerateSplash, Settings.Splash);
        Settings.SamplePresentation = Resolve(Settings.SamplePresentation, Settings.GenerateAlongSpline, Settings.SpawnAlongSpline);
        Settings.DetailPresentation = Resolve(Settings.DetailPresentation, Settings.GenerateDetails,
            Settings.DetailsAlongSpline is { Length: > 0 });
        Settings.GenerateSplash = Settings.SplashPresentation != PllFlowOutput.None;
        Settings.GenerateAlongSpline = Settings.SamplePresentation != PllFlowOutput.None;
        Settings.GenerateDetails = Settings.DetailPresentation != PllFlowOutput.None;
        if (Settings.LandingModel == PllFlowOutput.None) Settings.EndRock = null;
        if (!Settings.GenerateSplash) Settings.Splash = null;
        if (!Settings.GenerateAlongSpline) Settings.SpawnAlongSpline = null;
        if (!Settings.GenerateDetails) Settings.DetailsAlongSpline = null;
        if (Fluid?.Behavior == PllFluidBehavior.VisualOnly
            && (Settings.LandingModel == PllFlowOutput.Native || Settings.SplashPresentation == PllFlowOutput.Native
                || Settings.SamplePresentation == PllFlowOutput.Native || Settings.DetailPresentation == PllFlowOutput.Native
                || Settings.SpawnLights || Settings.EnablePusher))
            throw new ArgumentException("Visual Only fluid requires Custom or None decoration and no native lights or pusher.");
        return Settings;

        static PllFlowOutput Resolve(PllFlowOutput Choice, bool Enabled, bool Payload) => Choice == PllFlowOutput.Legacy
            ? !Enabled ? PllFlowOutput.None : Payload ? PllFlowOutput.Custom : PllFlowOutput.Native : Choice;
    }

    internal static void ValidateWaterContact(GameObject Prefab)
    {
        var Capsule = Prefab.GetComponent<CapsuleCollider>();
        var Emitter = Prefab.GetComponent<StatusEmitter>();
        var Scale = Prefab.transform.localScale;
        if (!Prefab.GetComponent<WaterfallTrigger>() || !Capsule || !Capsule.isTrigger
            || !Emitter || Emitter.GetType() != typeof(StatusEmitter)
            || Prefab.GetComponentsInChildren<WaterfallTrigger>(true).Length != 1
            || Prefab.GetComponentsInChildren<StatusEmitter>(true).Length != 1
            || Prefab.GetComponentsInChildren<Collider>(true).Length != 1
            || !ManifestEncoding.CanEncode(Scale.x) || Scale.x <= 0f || Scale.x != Scale.y || Scale.x != Scale.z
            || Capsule.direction is < 0 or > 2 || Capsule.radius <= 0f || Capsule.height < Capsule.radius * 2f
            || Emitter.statusType != CharacterAfflictions.STATUSTYPE.Hot || Emitter.amount > 0f
            || Emitter.radius <= 0f || Emitter.tickTime <= 0f || Emitter.preventOverlap || Emitter.inZoneWarning)
            throw new ArgumentException("Water flow contact needs one root native trigger, cooling emitter and trigger capsule.");
    }

    internal static void ValidateStaticSource(Transform Source)
    {
        PllNativeFlowSettings.ValidateTransform(Source);
        for (var Owner = Source; Owner; Owner = Owner.parent)
            foreach (var Component in Owner.GetComponents<MonoBehaviour>())
                if (Component is MoveTransform or FollowTransform or FollowBodypart or MovingLava
                    || Component is Rotate Rotation && Rotation.tf
                        && TransformOwnership.Contains(Rotation.tf, Source))
                    throw new ArgumentException("Native flow sources cannot follow a moving native controller.");
    }

    sealed class FlowPrefab
    {
        readonly GameObject Source;
        readonly ObjectBinding? Binding;
        readonly byte[] Fingerprint;
        internal readonly int Nodes;

        FlowPrefab(Transform Template, GameObject Source, PLLNativeResolverKind Kind)
        {
            this.Source = Source;
            if (TransformOwnership.Contains(Template, Source.transform)) Binding = ObjectBinding.Create(Template, Source);
            else if (Source.scene.IsValid()) throw new ArgumentException("A flow output prefab belongs to a foreign scene.");
            if (Kind == PLLNativeResolverKind.Lava
                && (Source.GetComponentsInChildren<StatusEmitter>(true).Length != 0
                    || Source.GetComponentsInChildren<WaterfallTrigger>(true).Length != 0
                    || Source.GetComponentsInChildren<WaterfallPusher>(true).Length != 0))
                throw new ArgumentException("A lava flow output cannot contain water contact or cooling.");
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) Nodes = NativeFlowProvider.WritePrefab(Writer, Source);
            Fingerprint = Stream.ToArray();
        }

        internal static FlowPrefab Capture(Transform Template, GameObject Source, PLLNativeResolverKind Kind) => new(Template,
            Source ? Source : throw new ArgumentException("A flow output prefab is missing."), Kind);
        internal GameObject Resolve(Transform RuntimeRoot, BiomeId Id) => Binding?.Resolve(RuntimeRoot, Id, "flow output prefab") ?? Source;
        internal void Validate(Transform Template, BiomeId Id)
        {
            if (!Source)
                throw new InvalidOperationException($"Biome '{Id}' destroyed its flow output prefab before sealing.");
            Binding?.Validate(Template, Id, "flow output prefab");
            using var Stream = new MemoryStream();
            int CurrentNodes;
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
                CurrentNodes = NativeFlowProvider.WritePrefab(Writer, Source);
            if (CurrentNodes != Nodes
                || !Fingerprint.AsSpan().SequenceEqual(Stream.GetBuffer().AsSpan(0, checked((int)Stream.Length))))
                throw new InvalidOperationException($"Biome '{Id}' changed its flow output prefab before sealing.");
        }
        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(Binding is not null); Binding?.WriteManifest(Writer);
            Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
        }
    }
}

internal sealed class GeneratedNativeFlowContract
{
    const string CandidateFingerprintDomain = "PLL/NativeFlowCandidate/v1";
    readonly NativeFlowContract[] Sources;
    internal IReadOnlyList<NativeFlowContract> FlowSources => Sources;
    readonly byte[] CandidateFingerprint;
    internal GameObject CandidateTemplate { get; }

    internal GeneratedNativeFlowContract(GameObject CandidateTemplate, NativeFlowContract[] Sources)
    {
        this.CandidateTemplate = CandidateTemplate; this.Sources = Sources;
        CandidateFingerprint = DeterministicObjectIdentity.HashObjectHierarchy(
            CandidateTemplate.transform, CandidateFingerprintDomain);
    }

    internal int NativeSourceMask
    {
        get { var Mask = 0; foreach (var Source in Sources) Mask |= Source.NativeSourceMask; return Mask; }
    }

    internal NativeFlowRuntime[] Resolve(Transform Instance, BiomeId Id, GeneratedGeometryBudget Budget, Transform BiomeFrame)
    {
        var Result = new NativeFlowRuntime[Sources.Length]; var Created = 0;
        try
        {
            for (; Created < Result.Length; Created++) Result[Created] = Sources[Created].Resolve(Instance, Id, Budget, BiomeFrame);
            return Result;
        }
        catch
        {
            for (var i = Created - 1; i >= 0; i--) Result[i].Dispose();
            throw;
        }
    }

    internal void Validate(BiomeId Id, AuthoringCandidateGraph Graph)
    {
        if (!Graph.Contains(CandidateTemplate))
            throw new InvalidOperationException($"Biome '{Id}' generated flow candidate is no longer reachable.");
        if (!DeterministicObjectIdentity.SameHash(CandidateFingerprint,
                DeterministicObjectIdentity.HashObjectHierarchy(CandidateTemplate.transform, CandidateFingerprintDomain)))
            throw new InvalidOperationException($"Biome '{Id}' changed its generated flow candidate before sealing.");
        foreach (var Source in Sources) Source.Validate(CandidateTemplate.transform, Id);
    }
    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(CandidateFingerprint.Length); Writer.Write(CandidateFingerprint);
        Writer.Write(Sources.Length); foreach (var Source in Sources) Source.WriteManifest(Writer);
    }
}

internal sealed class NativeFlowLifetime : MonoBehaviour
{
    internal NativeFlowRuntime? Owner;
    void OnDestroy() => Owner?.Dispose();
}

internal abstract class NativeFlowRuntime : IDisposable
{
    static NativeFlowRuntime? Active;
    readonly GeneratedGeometryBudget Budget;
    readonly float CoverDelaySeconds;
    readonly NativeFlowProvider? NativeProvider;
    private protected readonly List<(MeshRenderer Renderer, Material[] Materials, bool Enabled,
        MaterialPropertyBlock? Properties, MaterialPropertyBlock[] Slots)> AuthoredSurfaces = new();
    readonly List<IDisposable> SurfaceBindings = new();
    GameObject? EmitterModel;
    private protected Transform BiomeFrame = null!;
    NativeFlowLifetime? Lifetime;
    MaterialPropertyBlock? InitialSurfaceProperties;
    MaterialPropertyBlock? InitialSurfaceSlotProperties;
    Material? InitialSurfaceMaterial;
    readonly List<Material> OwnedMaterials = new(4);
    readonly Dictionary<Material, (Material Owned, int Property)> BoundMaterials = new();
    readonly List<(Renderer Renderer, int Slot, MaterialPropertyBlock Block, int Property)> CoverageBlocks = new();
    readonly List<(Renderer Renderer, bool Hidden)> Renderers = new();
    readonly List<SFX_Player.SoundEffectHandle> SoundHandles = new();
    readonly List<ParticleSystem> Particles = new();
    readonly List<AudioSource> Audio = new();
    SFX_PlayOneShot[] Sfx = Array.Empty<SFX_PlayOneShot>();
    (StatusEmitter Emitter, bool Enabled)[] Emitters = Array.Empty<(StatusEmitter, bool)>();
    (WaterfallTrigger Trigger, bool Enabled)[] Triggers = Array.Empty<(WaterfallTrigger, bool)>();
    WaterfallPusher[] Pushers = Array.Empty<WaterfallPusher>();
    (Collider Collider, bool Enabled)[] ContactTriggers = Array.Empty<(Collider, bool)>();
    readonly List<AudioLoop> Loops = new();
    BakedVolumeLight[] Lights = Array.Empty<BakedVolumeLight>();
    (ParticleSystem Particle, float TimeRate, float DistanceRate)[] Emission = Array.Empty<(ParticleSystem, float, float)>();
    (AudioSource Source, float Volume)[] AudioGains = Array.Empty<(AudioSource, float)>();
    readonly List<(Light Light, float Intensity)> RealtimeLights = new();
    PllPresentationCoverage Transition;
    HookedPropGeneration.FlowPresentationStart? PublicationStart;
    long SourceCommandRevision;
    bool AudioStarted;
    bool Initialized, Released, ContactsActive;
    NativeFlowSurfaceQuery? Trace;
    Mesh? OwnedMesh;
    byte[]? RealizedMesh;
    byte[]? RealizedOutput;
    protected GameObject Graph = null!;
    protected readonly PLLNativeResolver Source;
    protected NativeFlowProvider Provider => NativeProvider
        ?? throw new InvalidOperationException("Visual Only fluid has no native interaction provider.");
    protected readonly PllNativeFlowSettings Settings;
    protected readonly MeshRenderer? Surface;
    protected MeshRenderer FluidSurface = null!;
    protected GameObject? LandingRock;
    protected RaycastHit LastHit;

    protected NativeFlowRuntime(NativeFlowContract Contract, PLLNativeResolver Source,
        PllNativeFlowSettings Settings, MeshRenderer? Surface, GeneratedGeometryBudget Budget)
    {
        this.Contract = Contract; this.Source = Source; this.Settings = Settings;
        this.Surface = Surface; this.Budget = Budget;
        FluidSurface = Surface!;
        CoverDelaySeconds = Contract.CoverDelaySeconds;
        if (Contract.Behavior != PllFluidBehavior.VisualOnly)
            NativeProvider = NativeResolverSourceCatalog.Flow(Contract.Behavior == PllFluidBehavior.Water
                ? PLLNativeResolverKind.Water : PLLNativeResolverKind.Lava);
        if (Surface && (!Surface.enabled || Surface.forceRenderingOff))
            throw new ArgumentException("A native flow surface cannot be disabled or hidden.");
        if (Contract.Form != PllNativeResolverForm.Source || Contract.Profile == PllNativeFlowProfile.AuthoredSurface) return;
        if (Contract.HasLandingModel && !Settings.EndRock && !Provider.EndRock
            || Settings.GenerateSplash && !Settings.Splash && !Provider.Splash
            || Settings.GenerateAlongSpline && !Settings.SpawnAlongSpline && !Provider.AlongSpline
            || Settings.SpawnLights && !Provider.Light)
            throw new ArgumentException("An enabled native flow output has no compatible prefab.");
        if (Settings.GenerateDetails && (Settings.DetailsAlongSpline ?? Provider.Details).Length == 0)
            throw new ArgumentException("Enabled flow details have no prefab.");
        if (Contract.HasWaterContacts)
            NativeFlowContract.ValidateWaterContact(Provider.AlongSpline!);
        if (Settings.EnablePusher)
        {
            var Pusher = Settings.GenerateSplash
                ? (Settings.Splash ? Settings.Splash : Provider.Splash)!.GetComponentInChildren<WaterfallPusher>(true) : null;
            if (!Pusher || !Pusher.sfx || !Settings.PusherSfx && !Pusher.sfx.sfx
                && !(Pusher.sfx.sfxs is { Length: 1 } && Pusher.sfx.sfxs[0]))
                throw new ArgumentException("Enabled waterfall pushing needs a splash with a native pusher and sound.");
        }
        if (Contract.Profile == PllNativeFlowProfile.RayWaterfall && Settings.SpawnLights)
            throw new ArgumentException("The ray waterfall profile has no native light sampling.");
    }

    internal NativeFlowContract Contract { get; }
    internal Transform SourceTransform => Source.transform;
    internal bool Ready => Initialized && !Released;
    bool GameplayActive => PublicationStart.HasValue && Transition.Target && Transition.Coverage == 1f && !Released;

    internal static NativeFlowRuntime Create(NativeFlowContract Contract, PLLNativeResolver Source,
        PllNativeFlowSettings Settings, MeshRenderer? Surface, GeneratedGeometryBudget Budget,
        (MeshRenderer Renderer, int Slot)[] Surfaces,
        (Transform Frame, PllZoneVolume Volume)[] Regions, Collider[] Proxies, GameObject? Emitter, Transform BiomeFrame)
    {
        NativeFlowRuntime Owner = Contract.Form switch
        {
            PllNativeResolverForm.Zone => Contract.Behavior switch
            {
                PllFluidBehavior.Lava => new LavaZoneFlow(Contract, Source, Settings, Surface!, Budget),
                PllFluidBehavior.Water => new WaterZoneFlow(Contract, Source, Settings, Surface!, Budget),
                PllFluidBehavior.VisualOnly => new VisualZoneFlow(Contract, Source, Settings, Surface!, Budget),
                _ => throw new InvalidOperationException("The sealed fluid behavior is invalid.")
            },
            PllNativeResolverForm.Source => Contract.Profile switch
            {
                PllNativeFlowProfile.RayWaterfall => new RayFlow(Contract, Source, Settings, Surface!, Budget),
                PllNativeFlowProfile.SimulatedSpline => new SplineFlow(Contract, Source, Settings, Budget),
                PllNativeFlowProfile.AuthoredSurface => new AuthoredSurfaceFlow(Contract, Source, Settings, Budget, Surfaces, Regions, Proxies),
                _ => throw new InvalidOperationException("The sealed native flow profile is invalid.")
            },
            _ => throw new InvalidOperationException("The sealed native flow form is invalid.")
        };
        try
        {
            Budget.Reserve(Owner, 0, 1, 0, 0);
            Owner.Graph = new GameObject("__PLL_NativeFlow"); Owner.Graph.SetActive(false);
            Owner.Graph.transform.SetParent(Source.transform, false);
            Owner.EmitterModel = Emitter;
            Owner.BiomeFrame = BiomeFrame;
            foreach (var (Renderer, _) in Surfaces)
            {
                var Found = false;
                foreach (var Previous in Owner.AuthoredSurfaces)
                    if (Previous.Renderer == Renderer) { Found = true; break; }
                if (Found) continue;
                MaterialPropertyBlock? Properties = null;
                var SlotProperties = Array.Empty<MaterialPropertyBlock>();
                if (Contract.CoverDelaySeconds > 0f)
                {
                    Properties = new MaterialPropertyBlock(); Renderer.GetPropertyBlock(Properties);
                    SlotProperties = new MaterialPropertyBlock[Renderer.sharedMaterials.Length];
                    for (var i = 0; i < SlotProperties.Length; i++)
                    {
                        SlotProperties[i] = new MaterialPropertyBlock(); Renderer.GetPropertyBlock(SlotProperties[i], i);
                    }
                }
                Owner.AuthoredSurfaces.Add((Renderer, Renderer.sharedMaterials, Renderer.enabled, Properties, SlotProperties));
                Renderer.enabled = false;
            }
            if (Surface)
            {
                Owner.InitialSurfaceProperties = new MaterialPropertyBlock(); Surface.GetPropertyBlock(Owner.InitialSurfaceProperties);
                Owner.InitialSurfaceSlotProperties = new MaterialPropertyBlock(); Surface.GetPropertyBlock(Owner.InitialSurfaceSlotProperties, 0);
                Owner.InitialSurfaceMaterial = Surface.sharedMaterial;
                Surface.enabled = false;
            }
            Owner.Lifetime = Source.GetComponent<NativeFlowLifetime>() ?? Source.gameObject.AddComponent<NativeFlowLifetime>();
            Owner.Lifetime.Owner = Owner;
            return Owner;
        }
        catch (Exception Failure)
        {
            try { Owner.Dispose(); }
            catch (Exception CleanupFailure) { throw new AggregateException("Native flow preparation and cleanup failed.", Failure, CleanupFailure); }
            throw;
        }
    }

    internal static bool TryGetSourceOwner(PLLNativeResolver Source, out NativeFlowRuntime Owner)
    {
        var Lifetime = Source.GetComponent<NativeFlowLifetime>();
        Owner = Lifetime ? Lifetime.Owner! : null!; return Owner is not null;
    }

    internal static bool TryGetOwner(Transform Value, out NativeFlowRuntime Owner)
    {
        var Lifetime = Value.GetComponentInParent<NativeFlowLifetime>(true);
        Owner = Lifetime ? Lifetime.Owner! : null!;
        return Owner is not null && Owner.Graph && TransformOwnership.Contains(Owner.Graph.transform, Value);
    }

    internal static bool IsOwnedComponent(Component Value) => Value is NativeFlowLifetime
        || TryGetOwner(Value.transform, out _);

    internal static bool OwnsRenderer(Renderer Renderer)
    {
        if (TryGetOwner(Renderer.transform, out _)) return true;
        var Lifetime = Renderer.GetComponentInParent<NativeFlowLifetime>(true);
        if (!Lifetime || Lifetime.Owner is not { } Owner) return false;
        if (Owner.Surface == Renderer) return true;
        foreach (var Surface in Owner.AuthoredSurfaces) if (Surface.Renderer == Renderer) return true;
        return Owner.EmitterModel && TransformOwnership.Contains(Owner.EmitterModel.transform, Renderer.transform);
    }

    internal static bool OwnsSurfaceSlot(Renderer Renderer, int Slot)
    {
        var Lifetime = Renderer.GetComponentInParent<NativeFlowLifetime>(true);
        if (!Lifetime || Lifetime.Owner is not { } Owner) return false;
        if (Owner.Surface == Renderer) return Slot == 0;
        for (var Index = 0; Index < Owner.Source.SurfaceRenderers.Count; Index++)
            if (Owner.Source.SurfaceRenderers[Index] == Renderer && Owner.Source.SurfaceSlots[Index] == Slot) return true;
        return false;
    }

    internal bool Initialize(PropSpawner.SpawnData Data, NativeFlowSurfaceQuery Trace)
    {
        if (Released) throw new InvalidOperationException("A retired native flow cannot initialize.");
        if (Initialized) return true;
        if (Active is not null) throw new InvalidOperationException("Native flow initialization cannot reenter.");
        this.Trace = Trace ?? throw new ArgumentNullException(nameof(Trace));
        Active = this;
        try
        {
            NativeFlowContract.ValidateStaticSource(SourceTransform);
            Generate(Data);
            CapturePresentation(); SetGameplay(false);
            foreach (var Particle in Particles) Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var AudioSource in Audio) AudioSource.Stop();
            ApplyCoverage();
            Graph.SetActive(true);
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) CaptureRealizedOutput(Writer);
            RealizedOutput = Stream.ToArray();
            if (CoverDelaySeconds > 0f)
            {
                BindFlowRenderer(FluidSurface);
                foreach (var Surface in AuthoredSurfaces)
                    if (Surface.Renderer != FluidSurface) BindFlowRenderer(Surface.Renderer);
                foreach (var (Renderer, Hidden) in Renderers)
                    if (Renderer != FluidSurface
                        && Renderer.enabled && !Hidden && ActiveOutput(Renderer.transform)
                        && (!LandingRock || !TransformOwnership.Contains(LandingRock.transform, Renderer.transform)))
                        BindFlowRenderer(Renderer);
                ApplyCoverage();
            }
            else
            {
                NativeFogRenderRuntime.BindRenderer(FluidSurface);
                foreach (var Surface in AuthoredSurfaces)
                    if (Surface.Renderer != FluidSurface) NativeFogRenderRuntime.BindRenderer(Surface.Renderer);
                foreach (var (Renderer, _) in Renderers)
                    if (Renderer != FluidSurface) NativeFogRenderRuntime.BindRenderer(Renderer);
            }
            Initialized = true; return true;
        }
        catch (FlowPlacementFailure)
        {
            Dispose(); return false;
        }
        catch (Exception Failure)
        {
            try { Dispose(); }
            catch (Exception CleanupFailure) { throw new AggregateException("Native flow initialization and cleanup failed.", Failure, CleanupFailure); }
            throw;
        }
        finally { Active = null; this.Trace = null; }
    }

    internal void AdoptPublication(HookedPropGeneration.FlowPresentationStart Start,
        HookedPropGeneration.NativeSourceCommand? Command, int Group, double Now)
    {
        if (!Ready) throw new InvalidOperationException("Native flow publication requires completed geometry.");
        var Published = PublicationStart.HasValue;
        if (PublicationStart is { } Previous)
        {
            if (!Previous.Equals(Start))
                throw new InvalidOperationException($"Native flow generation start changed from revision {Previous.Revision} to {Start.Revision}.");
            if (Command is null || Command.Revision <= SourceCommandRevision) return;
        }
        PublicationStart = Start;
        var PreviousTarget = Transition.Target;
        if (Command is null)
            Transition.Adopt(Contract.InitiallyEnabled, Start.RestoreEndpoint && Contract.InitiallyEnabled ? 1f : 0f,
                Start.StartClock, CoverDelaySeconds, Now);
        else
        {
            Command.Adopt(ref Transition, Start, Group, CoverDelaySeconds, Now);
            SourceCommandRevision = Command.Revision;
        }
        ApplyCoverage();
        if (Published && PreviousTarget == Transition.Target) return;
        if (Transition.Target)
        {
            foreach (var Particle in Particles) Particle.Play(false);
            foreach (var AudioSource in Audio)
                if (AudioStarted) AudioSource.UnPause(); else AudioSource.Play();
            AudioStarted = true;
        }
        else if (Transition.Coverage > 0f)
            foreach (var Particle in Particles) Particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    internal void Tick(double Now)
    {
        if (!Released && PublicationStart.HasValue && Transition.Tick(Now)) ApplyCoverage();
    }

    internal void Stop()
    {
        if (Released) return;
        Transition.Complete(false);
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, () => SetGameplay(false));
        CleanupOperations.Capture(ref Failures, ApplyCoverage);
        foreach (var Particle in Particles)
            if (Particle) CleanupOperations.Capture(ref Failures,
                () => Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear));
        foreach (var AudioSource in Audio)
            if (AudioSource) CleanupOperations.Capture(ref Failures, AudioSource.Stop);
        GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    void ApplyCoverage()
    {
        var Showing = Transition.Target || Transition.Coverage > 0f;
        foreach (var Entry in CoverageBlocks)
        {
            if (!Entry.Renderer) continue;
            Entry.Block.SetFloat(Entry.Property, Transition.Coverage);
            Entry.Renderer.SetPropertyBlock(Entry.Block, Entry.Slot);
        }
        foreach (var Value in RealtimeLights)
            if (Value.Light) Value.Light.intensity = Value.Intensity * Transition.Coverage;
        foreach (var Renderer in Renderers)
            if (Renderer.Renderer) Renderer.Renderer.forceRenderingOff = !Showing || Renderer.Hidden;
        if (Surface) Surface.enabled = Showing;
        foreach (var Surface in AuthoredSurfaces) if (Surface.Renderer) Surface.Renderer.enabled = Showing && Surface.Enabled;
        foreach (var Value in Emission)
        {
            if (!Value.Particle) continue;
            var Channel = Value.Particle.emission;
            Channel.rateOverTimeMultiplier = Value.TimeRate * Transition.Coverage;
            Channel.rateOverDistanceMultiplier = Value.DistanceRate * Transition.Coverage;
        }
        foreach (var Value in AudioGains) if (Value.Source) Value.Source.volume = Value.Volume * Transition.Coverage;
        if (ContactsActive != GameplayActive) SetGameplay(GameplayActive);
        if (!Showing)
        {
            foreach (var Particle in Particles) Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var Source in Audio) Source.Pause();
        }
    }

    void BindFlowRenderer(Renderer Renderer)
    {
        var Materials = Renderer.sharedMaterials;
        for (var i = 0; i < Materials.Length; i++)
        {
            var Original = Materials[i];
            if (!Original)
                throw new InvalidOperationException("Cover Delay requires a supported native flow material.");
            if (!BoundMaterials.TryGetValue(Original, out var Binding))
            {
                var NativeShader = NativeFogRenderRuntime.CanonicalShader(Original.shader);
                if (!NativeFogRenderRuntime.SupportsFlowMaterial(Original))
                    throw new InvalidOperationException("Cover Delay requires a supported native flow material.");
                var Owned = OwnedMaterials.Contains(Original) ? Original : CloneMaterial(Original);
                if (Owned.shader != NativeShader)
                {
                    var Keywords = Owned.shaderKeywords;
                    Owned.shader = NativeShader; Owned.shaderKeywords = Keywords;
                }
                Binding = (Owned, NativeFogRenderRuntime.AdaptFlowMaterial(Owned));
                BoundMaterials.Add(Original, Binding);
            }
            var Block = new MaterialPropertyBlock(); Renderer.GetPropertyBlock(Block, i);
            if (Block.isEmpty) Renderer.GetPropertyBlock(Block);
            CoverageBlocks.Add((Renderer, i, Block, Binding.Property));
            Materials[i] = Binding.Owned;
        }
        Renderer.sharedMaterials = Materials;
    }

    Material CloneMaterial(Material Original)
    {
        if (OwnedMaterials.Count == OwnedMaterials.Capacity)
            OwnedMaterials.Capacity = checked(OwnedMaterials.Capacity * 2);
        var Owned = new Material(Original); OwnedMaterials.Add(Owned); return Owned;
    }

    protected Material ResolveMaterial(Material? Native)
    {
        var Overrides = Settings.Material;
        if (Contract.MaterialMode == PllFluidMaterialMode.Custom)
        {
            if (!Overrides || !NativeFogRenderRuntime.SupportsMaterial(Overrides.shader))
                throw new ArgumentException($"Fluid material '{Overrides?.name}' has no admitted fog shader adapter. Choose a supported material or supply its adapter.");
            if (CoverDelaySeconds > 0f && !NativeFogRenderRuntime.SupportsFlowMaterial(Overrides))
                throw new ArgumentException($"Fluid material '{Overrides.name}' has no admitted coverage channel. Set Cover Delay to zero.");
            return Overrides;
        }
        if (!Native) throw new ArgumentException("The fluid material role has no admitted native recipe. Choose Custom Material.");
        if (!Overrides) return Native;
        PllNativeFlowSettings.ValidateMaterialOverridesPayload(Overrides);
        var Shader = NativeFogRenderRuntime.CanonicalShader(Native.shader);
        var SourceShader = Overrides.shader;
        if (Shader.GetPropertyCount() != SourceShader.GetPropertyCount())
            throw new ArgumentException("Material Overrides does not match the selected native recipe.");
        for (var i = 0; i < Shader.GetPropertyCount(); i++)
        {
            var Name = Shader.GetPropertyName(i); var Type = Shader.GetPropertyType(i);
            var SourceIndex = SourceShader.FindPropertyIndex(Name);
            if (SourceIndex < 0 || SourceShader.GetPropertyType(SourceIndex) != Type)
                throw new ArgumentException("Material Overrides does not match the selected native recipe.");
            if (Type == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                var Dimension = Shader.GetPropertyTextureDimension(i);
                var Texture = Overrides.GetTexture(Name);
                if (SourceShader.GetPropertyTextureDimension(SourceIndex) != Dimension
                    || Texture && Texture.dimension != Dimension)
                    throw new ArgumentException("Material Overrides has an incompatible texture dimension.");
            }
            if (Type == UnityEngine.Rendering.ShaderPropertyType.Range)
            {
                var Range = Shader.GetPropertyRangeLimits(i); var Value = Overrides.GetFloat(Name);
                if (Value < Range.x || Value > Range.y)
                    throw new ArgumentException("Material Overrides exceeds a native property range.");
            }
        }
        var Keywords = Overrides.shaderKeywords; var KeywordSpace = Shader.keywordSpace;
        foreach (var Keyword in Keywords)
            if (!KeywordSpace.FindKeyword(Keyword).isValid)
                throw new ArgumentException("Material Overrides contains a keyword outside the selected native recipe.");
        var Owned = CloneMaterial(Native);
        if (Owned.shader != Shader) Owned.shader = Shader;
        for (var i = 0; i < Shader.GetPropertyCount(); i++)
        {
            var Name = Shader.GetPropertyName(i);
            switch (Shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color: Owned.SetColor(Name, Overrides.GetColor(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector: Owned.SetVector(Name, Overrides.GetVector(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range: Owned.SetFloat(Name, Overrides.GetFloat(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Int: Owned.SetInteger(Name, Overrides.GetInteger(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Owned.SetTexture(Name, Overrides.GetTexture(Name));
                    Owned.SetTextureScale(Name, Overrides.GetTextureScale(Name));
                    Owned.SetTextureOffset(Name, Overrides.GetTextureOffset(Name)); break;
                default: throw new InvalidOperationException("The native material property type is unsupported.");
            }
        }
        Owned.shaderKeywords = Keywords;
        return Owned;
    }

    void CapturePresentation()
    {
        Pushers = Graph.GetComponentsInChildren<WaterfallPusher>(true);
        foreach (var Pusher in Pushers)
        {
            Pusher.fallTime = Settings.PusherFallTime; Pusher.knockback = Settings.PusherKnockback;
            Pusher.downwardKnockback = Settings.PusherDownwardKnockback; Pusher.cooldown = Settings.PusherCooldown;
            Pusher.gameObject.SetActive(Settings.EnablePusher);
            if (Settings.EnablePusher && Settings.PusherSfx) Pusher.sfx.sfx = Settings.PusherSfx;
        }
        foreach (var Renderer in Graph.GetComponentsInChildren<Renderer>(true)) Renderers.Add((Renderer, Renderer.forceRenderingOff));
        if (EmitterModel)
            foreach (var Renderer in EmitterModel.GetComponentsInChildren<Renderer>(true))
                Renderers.Add((Renderer, Renderer.forceRenderingOff));
        Graph.GetComponentsInChildren(true, Particles);
        if (EmitterModel)
            foreach (var Particle in EmitterModel.GetComponentsInChildren<ParticleSystem>(true)) Particles.Add(Particle);
        Particles.RemoveAll(Particle => !ActiveOutput(Particle.transform));
        foreach (var Particle in Particles)
        {
            var Main = Particle.main;
            if (Main.stopAction != ParticleSystemStopAction.None)
                throw new InvalidOperationException("Native flow particles have an unsupported destructive stop action.");
            Main.playOnAwake = false;
        }
        Lights = Graph.GetComponentsInChildren<BakedVolumeLight>(true);
        var Smooth = CoverDelaySeconds > 0f;
        if (Smooth)
        {
            foreach (var Light in Lights)
                if (Light.enabled && ActiveOutput(Light.transform))
                    throw new InvalidOperationException("Baked flow lighting cannot fade. Set Cover Delay to zero.");
            Emission = new (ParticleSystem, float, float)[Particles.Count];
            for (var i = 0; i < Particles.Count; i++)
            {
                if (PllAuthoringValueContract.ParticleTransitionFailure(Particles[i]) is { } Failure)
                    throw new InvalidOperationException(Failure);
                Emission[i] = (Particles[i], Particles[i].emission.rateOverTimeMultiplier,
                    Particles[i].emission.rateOverDistanceMultiplier);
            }
        }
        foreach (var Light in Graph.GetComponentsInChildren<Light>(true))
        {
            if (Light.bakingOutput.lightmapBakeType != LightmapBakeType.Realtime)
            {
                if (Smooth) throw new InvalidOperationException("Cover Delay requires realtime flow lights.");
                continue;
            }
            RealtimeLights.Add((Light, Light.intensity));
        }
        if (EmitterModel)
            foreach (var Light in EmitterModel.GetComponentsInChildren<Light>(true))
                RealtimeLights.Add((Light, Light.intensity));
        Graph.GetComponentsInChildren(true, Audio);
        if (EmitterModel)
            foreach (var Source in EmitterModel.GetComponentsInChildren<AudioSource>(true)) Audio.Add(Source);
        Audio.RemoveAll(Source => !Source.enabled || !ActiveOutput(Source.transform));
        foreach (var Source in Audio) Source.playOnAwake = false;
        Graph.GetComponentsInChildren(true, Loops);
        if (EmitterModel)
            foreach (var Loop in EmitterModel.GetComponentsInChildren<AudioLoop>(true)) Loops.Add(Loop);
        Loops.RemoveAll(Loop => !Loop.enabled || !ActiveOutput(Loop.transform));
        if (Smooth)
        {
            AudioGains = new (AudioSource, float)[Audio.Count];
            for (var i = 0; i < Audio.Count; i++)
            {
                if (!Audio[i].loop) throw new InvalidOperationException("Cover Delay requires looping flow audio.");
                AudioGains[i] = (Audio[i], Audio[i].volume);
            }
            foreach (var Loop in Loops)
            {
                Loop.enabled = false;
                for (var i = 0; i < AudioGains.Length; i++)
                    if (AudioGains[i].Source == Loop.loop)
                    {
                        AudioGains[i] = (Loop.loop, Loop.volume);
                        Loop.loop.pitch = Loop.pitch;
                        break;
                    }
            }
        }
        var Colliders = new List<(Collider, bool)>();
        foreach (var Collider in Graph.GetComponentsInChildren<Collider>(true))
            Colliders.Add((Collider, Collider.enabled));
        ContactTriggers = Colliders.ToArray();
        Sfx = Graph.GetComponentsInChildren<SFX_PlayOneShot>(true);
        foreach (var Value in Sfx)
        {
            if (Value.sfxs.Length == 1) { Value.sfx = Value.sfxs[0]; Value.sfxs = Array.Empty<SFX_Instance>(); }
            if (Value.sfxs.Length != 0)
                throw new InvalidOperationException("Native flow SFX needs one owned native sound handle.");
            Value.stopOnDisable = true; Value.playOnEnable = Value.playOnStart = false;
        }
        var Status = Graph.GetComponentsInChildren<StatusEmitter>(true);
        Emitters = new (StatusEmitter, bool)[Status.Length];
        for (var i = 0; i < Status.Length; i++) Emitters[i] = (Status[i], Status[i].enabled);
        var Water = Graph.GetComponentsInChildren<WaterfallTrigger>(true);
        Triggers = new (WaterfallTrigger, bool)[Water.Length];
        for (var i = 0; i < Water.Length; i++)
        {
            var Trigger = Water[i];
            Triggers[i] = (Trigger, Trigger.enabled);
            Trigger.force = Settings.WaterForce; Trigger.drag = Settings.WaterDrag;
            Trigger.ragdoll = Settings.WaterRagdoll; Trigger.ragdollLength = Settings.WaterRagdollLength;
        }
    }

    bool ActiveOutput(Transform Value)
    {
        for (; Value != Graph.transform && Value != SourceTransform; Value = Value.parent)
            if (!Value.gameObject.activeSelf) return false;
        return true;
    }

    void SetGameplay(bool Current)
    {
        ContactsActive = Current;
        foreach (var Value in Triggers) if (Value.Trigger) Value.Trigger.enabled = Current && Value.Enabled;
        foreach (var Value in ContactTriggers) if (Value.Collider) Value.Collider.enabled = Current && Value.Enabled;
        if (CoverDelaySeconds == 0f)
            foreach (var Loop in Loops) if (Loop) Loop.enabled = Current;
        foreach (var Pusher in Pushers) if (Pusher) Pusher.enabled = Current && Settings.EnablePusher;
        foreach (var Value in Emitters) if (Value.Emitter) Value.Emitter.enabled = Current && Value.Enabled;
        foreach (var Value in Sfx)
        {
            if (Value) Value.enabled = Current;
        }
        SetNativeGameplay(Current);
        if (!Current)
        {
            List<Exception>? Failures = null;
            foreach (var Handle in SoundHandles)
                CleanupOperations.Capture(ref Failures, () => StopSound(Handle));
            SoundHandles.Clear(); GeneratedGeometryBudget.ThrowFailures(Failures);
        }
    }

    protected abstract void Generate(PropSpawner.SpawnData Data);
    protected abstract void SetNativeGameplay(bool Current);
    protected abstract void WriteGeometry(BinaryWriter Writer);
    protected virtual void ReleaseNativeInputs() { }

    internal static void StopSound(SFX_Player.SoundEffectHandle Handle)
    {
        var Source = Handle.source;
        if (Source is null) return;
        if (Source.player) { Source.StopPlaying(); return; }
        Source.player!.OnStopped(Handle);
        if (Source.source) Source.source.Stop();
        Source.isPlaying = false; Source.handle = null; Handle.source = null;
    }

    protected GameObject Clone(GameObject Prefab, string Name)
    {
        var Clone = UnityEngine.Object.Instantiate(Prefab, Graph.transform, false);
        Clone.name = Name; return Clone;
    }

    protected GameObject WaterContact(string Name, bool Decoration)
    {
        var Prefab = Settings.SpawnAlongSpline;
        if (Decoration && (!Prefab || Prefab.GetComponent<WaterfallTrigger>()))
        {
            var Native = Prefab ? Prefab : Provider.AlongSpline!;
            NativeFlowContract.ValidateWaterContact(Native);
            return Clone(Native, Name);
        }
        var SourceContact = Provider.AlongSpline!;
        var Original = SourceContact.GetComponent<CapsuleCollider>();
        var Contact = new GameObject(Name); Contact.transform.SetParent(Graph.transform, false);
        Contact.layer = SourceContact.layer;
        Contact.transform.localScale = SourceContact.transform.localScale;
        var Capsule = Contact.AddComponent<CapsuleCollider>(); Capsule.isTrigger = true;
        Capsule.center = Original.center; Capsule.direction = Original.direction;
        Capsule.height = Original.height; Capsule.radius = Original.radius;
        ConfigureWaterContact(Contact);
        if (Decoration)
        {
            var Model = UnityEngine.Object.Instantiate(Prefab!, Contact.transform, false);
            Model.SetActive(true);
        }
        return Contact;
    }

    protected int ContactSampleNodes()
    {
        if (!Contract.HasWaterContacts)
            return Settings.GenerateAlongSpline ? Contract.AlongSplineNodes ?? Provider.AlongSplineNodes : 0;
        if (!Settings.GenerateAlongSpline) return 1;
        if (!Settings.SpawnAlongSpline) return Provider.AlongSplineNodes;
        return Settings.SpawnAlongSpline.GetComponent<WaterfallTrigger>()
            ? Contract.AlongSplineNodes!.Value : checked(1 + Contract.AlongSplineNodes!.Value);
    }

    protected void ConfigureWaterContact(GameObject Contact)
    {
        Contact.layer = Provider.AlongSpline!.layer;
        var Trigger = Contact.AddComponent<WaterfallTrigger>(); Trigger.enabled = false;
        Trigger.force = Settings.WaterForce; Trigger.drag = Settings.WaterDrag;
        Trigger.ragdoll = Settings.WaterRagdoll; Trigger.ragdollLength = Settings.WaterRagdollLength;
        var Original = Provider.AlongSpline!.GetComponent<StatusEmitter>();
        var Emitter = Contact.AddComponent<StatusEmitter>(); Emitter.enabled = false;
        Emitter.statusType = Original.statusType; Emitter.amount = Original.amount; Emitter.radius = Original.radius;
        Emitter.outerFade = Original.outerFade; Emitter.innerFade = Original.innerFade;
        Emitter.minAmount = Original.minAmount; Emitter.tickTime = Original.tickTime;
        Emitter.extraWarningTime = Original.extraWarningTime; Emitter.randomStart = Original.randomStart;
        Emitter.inZoneWarning = Original.inZoneWarning; Emitter.preventOverlap = false;
        Emitter.overlapIdentifier = string.Empty;
        Trigger.enabled = true; Emitter.enabled = true;
    }

    protected static void CopyContact(CollisionModifier Source, CollisionModifier Target)
    {
        Target.applyEffects = Source.applyEffects; Target.applyPetrify = Source.applyPetrify; Target.statusType = Source.statusType;
        Target.damage = Source.damage; Target.cooldown = Source.cooldown; Target.knockback = Source.knockback;
        Target.knockbackOnlyWhenStanding = Source.knockbackOnlyWhenStanding;
        Target.ragdolledKnockback = Source.ragdolledKnockback; Target.knockbackTowardsFwdVector = Source.knockbackTowardsFwdVector;
        Target.additionalMods = new List<CollisionMod>(Source.additionalMods.Count);
        foreach (var Mod in Source.additionalMods)
            Target.additionalMods.Add(new CollisionMod { statusType = Mod.statusType, amount = Mod.amount, knockback = Mod.knockback });
        Target.useBounceCoroutine = Source.useBounceCoroutine; Target.standable = Source.standable;
        Target.preventFallDamageIfKnockback = Source.preventFallDamageIfKnockback;
        Target.superBouncePow = Source.superBouncePow; Target.sinceGroundedOffset = Source.sinceGroundedOffset;
        Target.bounceDotMinimum = Source.bounceDotMinimum; Target.staminaBackOnBounce = Source.staminaBackOnBounce;
    }

    sealed class AuthoredSurfaceFlow : NativeFlowRuntime
    {
        readonly (MeshRenderer Renderer, int Slot)[] Surfaces;
        readonly (Transform Frame, PllZoneVolume Volume)[] Regions;
        readonly Collider[] Proxies;
        readonly List<CollisionModifier> Contacts = new();

        internal AuthoredSurfaceFlow(NativeFlowContract Contract, PLLNativeResolver Source,
            PllNativeFlowSettings Settings, GeneratedGeometryBudget Budget,
            (MeshRenderer Renderer, int Slot)[] Surfaces,
            (Transform Frame, PllZoneVolume Volume)[] Regions, Collider[] Proxies)
            : base(Contract, Source, Settings, null, Budget)
        {
            this.Surfaces = Surfaces; this.Regions = Regions; this.Proxies = Proxies;
            FluidSurface = Surfaces[0].Renderer;
        }

        protected override void Generate(PropSpawner.SpawnData Data)
        {
            var Material = ResolveMaterial(Contract.Behavior == PllFluidBehavior.VisualOnly ? null : Provider.Material);
            foreach (var (Renderer, Slot) in Surfaces)
            {
                var Materials = Renderer.sharedMaterials; Materials[Slot] = CloneMaterial(Material); Renderer.sharedMaterials = Materials;
            }
            switch (Contract.Behavior)
            {
                case PllFluidBehavior.VisualOnly: return;
                case PllFluidBehavior.Water:
                    if (Contract.ContactMode == PllFluidContactMode.Authored)
                    {
                        Reserve(0, checked(1 + Regions.Length), 0, 0);
                        foreach (var (Frame, Volume) in Regions) CreateWaterRegion(Frame, Volume);
                        return;
                    }
                    var Bounds = Source.AuthoredSurfaceBounds();
                    if (Bounds.size.x <= 0f || Bounds.size.y <= 0f || Bounds.size.z <= 0f)
                        throw new ArgumentException("Automatic fluid bounds need a three-dimensional mesh. Use authored contact regions.");
                    Reserve(0, 2, 0, 0);
                    CreateWaterRegion(SourceTransform, new PllZoneVolume(PllZoneShape.Box,
                        Bounds.center, Bounds.size, 0f));
                    return;
                case PllFluidBehavior.Lava:
                    if (Contract.ContactMode == PllFluidContactMode.Authored)
                    {
                        Reserve(0, checked(1 + Proxies.Length), 0, 0);
                        foreach (var Proxy in Proxies) CreateLavaContact(Proxy.transform, Proxy);
                        foreach (var Proxy in Proxies) Proxy.enabled = false;
                        return;
                    }
                    Reserve(0, checked(1 + AuthoredSurfaces.Count), 0, 0);
                    foreach (var Surface in AuthoredSurfaces)
                    {
                        var Renderer = Surface.Renderer;
                        var Contact = Child(Renderer.transform, "LavaContact");
                        var Collider = Contact.AddComponent<MeshCollider>();
                        Collider.sharedMesh = Renderer.GetComponent<MeshFilter>().sharedMesh;
                        AddLavaModifier(Contact);
                    }
                    return;
                default: throw new InvalidOperationException("The prepared fluid behavior is invalid.");
            }
        }

        void CreateWaterRegion(Transform Frame, PllZoneVolume Volume)
        {
            var Contact = Child(Frame, "WaterContact");
            Contact.transform.position = Frame.TransformPoint(Volume.Center);
            if (Volume.Shape == PllZoneShape.Box)
            {
                var Collider = Contact.AddComponent<BoxCollider>(); Collider.isTrigger = true;
                Collider.center = Vector3.zero; Collider.size = Volume.Size;
            }
            else
            {
                var Collider = Contact.AddComponent<SphereCollider>(); Collider.isTrigger = true;
                Collider.center = Vector3.zero; Collider.radius = Volume.Radius;
            }
            ConfigureWaterContact(Contact);
        }

        void CreateLavaContact(Transform Frame, Collider Proxy)
        {
            var Contact = Child(Frame, "LavaContact");
            switch (Proxy)
            {
                case BoxCollider Box:
                    var NewBox = Contact.AddComponent<BoxCollider>(); NewBox.center = Box.center; NewBox.size = Box.size; break;
                case SphereCollider Sphere:
                    var NewSphere = Contact.AddComponent<SphereCollider>(); NewSphere.center = Sphere.center; NewSphere.radius = Sphere.radius; break;
                case CapsuleCollider Capsule:
                    var NewCapsule = Contact.AddComponent<CapsuleCollider>(); NewCapsule.center = Capsule.center;
                    NewCapsule.radius = Capsule.radius; NewCapsule.height = Capsule.height; NewCapsule.direction = Capsule.direction; break;
                case MeshCollider Mesh:
                    var NewMesh = Contact.AddComponent<MeshCollider>(); NewMesh.sharedMesh = Mesh.sharedMesh;
                    NewMesh.convex = false; NewMesh.cookingOptions = Mesh.cookingOptions; break;
                default: throw new InvalidOperationException("The prepared lava contact proxy is unsupported.");
            }
            AddLavaModifier(Contact);
        }

        GameObject Child(Transform Frame, string Name)
        {
            var Child = new GameObject(Name); Child.transform.SetParent(Graph.transform, false);
            Child.transform.SetPositionAndRotation(Frame.position, Frame.rotation); return Child;
        }

        void AddLavaModifier(GameObject Contact)
        {
            Contact.layer = Provider.Contact!.gameObject.layer;
            var Modifier = Contact.AddComponent<CollisionModifier>(); CopyContact(Provider.Contact!, Modifier);
            Modifier.applyEffects = false; Contacts.Add(Modifier);
        }

        protected override void SetNativeGameplay(bool Current)
        {
            foreach (var Contact in Contacts) if (Contact) Contact.applyEffects = Current && Provider.Contact!.applyEffects;
        }

        protected override void WriteGeometry(BinaryWriter Writer)
        {
            Writer.Write(Surfaces.Length);
            foreach (var (Renderer, Slot) in Surfaces)
            {
                Writer.Write(Slot);
                Writer.Write(VisualFingerprintWriter.HashSerializedObject(Renderer.GetComponent<MeshFilter>().sharedMesh, "PLL/AuthoredFluidMesh/v1"));
                ManifestEncoding.WriteVector(Writer, Renderer.transform.position);
                ManifestEncoding.WriteVector(Writer, Renderer.transform.eulerAngles);
            }
            Writer.Write(Contacts.Count); foreach (var Contact in Contacts) NativeFlowProvider.WriteContact(Writer, Contact);
        }

        protected override void ReleaseNativeInputs()
        {
            foreach (var Proxy in Proxies) if (Proxy) Proxy.enabled = true;
        }
    }

    sealed class VisualZoneFlow : NativeFlowRuntime
    {
        internal VisualZoneFlow(NativeFlowContract Contract, PLLNativeResolver Source, PllNativeFlowSettings Settings,
            MeshRenderer Surface, GeneratedGeometryBudget Budget) : base(Contract, Source, Settings, Surface, Budget) { }
        protected override void Generate(PropSpawner.SpawnData Data) => Surface!.sharedMaterial = ResolveMaterial(null);
        protected override void SetNativeGameplay(bool Current) { }
        protected override void WriteGeometry(BinaryWriter Writer) { }
    }

    sealed class WaterZoneFlow : NativeFlowRuntime
    {
        WaterZone Zone = null!;
        internal WaterZoneFlow(NativeFlowContract Contract, PLLNativeResolver Source, PllNativeFlowSettings Settings,
            MeshRenderer Surface, GeneratedGeometryBudget Budget) : base(Contract, Source, Settings, Surface, Budget) { }

        protected override void Generate(PropSpawner.SpawnData Data)
        {
            Surface!.sharedMaterial = ResolveMaterial(Provider.ZoneMaterial);
            var Bounds = NativeResolverVolume.WorldBounds(SourceTransform, Contract.Volume);
            Graph.transform.position = Bounds.center;
            Zone = Graph.AddComponent<WaterZone>(); Zone.enabled = false; Zone.waterPlane = Surface;
            Zone.waterDepth = Bounds.size.y; Zone.zoneBounds = Bounds;
            var Fluid = Contract.Definition!;
            Zone.movementModifierShallow = Fluid.MovementShallow; Zone.movementModifierDeep = Fluid.MovementDeep;
            Zone.animModShallow = Fluid.AnimationShallow; Zone.animModDeep = Fluid.AnimationDeep;
            Zone.movementModifierJumping = Fluid.MovementJumping; Zone.jumpMultiplier = Fluid.JumpMultiplier;
            Zone.disableSprintJump = Fluid.DisableSprintJump;
        }

        protected override void SetNativeGameplay(bool Current)
        {
            if (!Zone) return;
            Zone.enabled = Current;
            if (Current || Zone.trackedCharacters.Count == 0) return;
            var Others = UnityEngine.Object.FindObjectsByType<WaterZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var Character in Zone.trackedCharacters.Keys)
            {
                if (!Character) continue;
                var Shared = false;
                foreach (var Other in Others)
                    if (Other != Zone && Other.isActiveAndEnabled
                        && Other.trackedCharacters.TryGetValue(Character, out var State) && (int)State != 0)
                    { Shared = true; break; }
                if (!Shared) Character.data.inWater = 0f;
            }
            Zone.trackedCharacters.Clear();
        }

        protected override void WriteGeometry(BinaryWriter Writer)
        {
            ManifestEncoding.WriteVector(Writer, Zone.zoneBounds.center); ManifestEncoding.WriteVector(Writer, Zone.zoneBounds.size);
            NativeFlowProvider.WriteScalars(Writer, Zone.movementModifierShallow, Zone.movementModifierDeep,
                Zone.animModShallow, Zone.animModDeep, Zone.movementModifierJumping, Zone.jumpMultiplier);
            Writer.Write(Zone.disableSprintJump);
        }
    }

    protected void Reserve(int Instances, int Nodes, int Meshes, long Bytes) => Budget.Reserve(this, Instances, Nodes, Meshes, Bytes);
    protected void AdoptMesh(Mesh Mesh)
    {
        OwnedMesh = Mesh;
        RealizedMesh = VisualFingerprintWriter.HashGeneratedPropMesh(Mesh, 64L * 1024 * 1024, out var Bytes);
        Budget.SetMeshBytes(this, Bytes);
    }

    internal void AttachGeneratedSurface(IDisposable Binding) => SurfaceBindings.Add(Binding);

    internal void WriteRealizedOutput(BinaryWriter Writer)
    {
        if (!Ready) throw new InvalidOperationException("Native flow output is not complete.");
        Writer.Write(RealizedOutput!.Length); Writer.Write(RealizedOutput);
    }

    void CaptureRealizedOutput(BinaryWriter Writer)
    {
        Writer.Write("PLL/NativeFlowRealized/v2"); Writer.Write((byte)Contract.Kind); Writer.Write((byte)Contract.Form);
        Writer.Write((byte)Contract.Profile);
        WriteGeometry(Writer);
        Writer.Write(Surface ? true : false);
        if (Surface)
        {
            Writer.Write(VisualFingerprintWriter.HashSerializedObject(Surface.GetComponent<MeshFilter>().sharedMesh, "PLL/FlowRealizedSurfaceMesh/v1"));
            Writer.Write(VisualFingerprintWriter.HashSerializedObject(Surface.sharedMaterial, "PLL/FlowRealizedSurfaceMaterial/v1"));
        }
        Writer.Write(RealizedMesh?.Length ?? 0); if (RealizedMesh is not null) Writer.Write(RealizedMesh);
        var Structural = DeterministicObjectIdentity.HashObjectHierarchy(Graph.transform, "PLL/NativeFlowRealizedGraph/v1");
        Writer.Write(Structural.Length); Writer.Write(Structural);
        foreach (var Light in Lights)
        {
            ManifestEncoding.WriteVector(Writer, Light.transform.position);
            Writer.Write((int)Light.mode); ManifestEncoding.WriteColor(Writer, Light.color);
            NativeFlowProvider.WriteScalars(Writer, Light.intensity, Light.radius, Light.falloff, Light.coneFalloff, Light.coneSize);
        }
        Writer.Write(Triggers.Length); Writer.Write(Emitters.Length); Writer.Write(Pushers.Length);
    }

    public void Dispose()
    {
        if (Released) return; Released = true;
        Transition.Complete(false);
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, () => SetGameplay(false));
        foreach (var Source in Audio) if (Source) CleanupOperations.Capture(ref Failures, Source.Stop);
        if (Graph) CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRoot(Graph));
        else foreach (var Entry in Renderers)
            CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRenderer(Entry.Renderer));
        if (Surface is not null)
            CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRenderer(Surface));
        if (EmitterModel) CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRoot(EmitterModel));
        foreach (var Material in OwnedMaterials)
            CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireMaterial(Material));
        foreach (var Surface in AuthoredSurfaces)
            if (Surface.Renderer)
            {
                CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRenderer(Surface.Renderer));
                CleanupOperations.Capture(ref Failures, () => Surface.Renderer.sharedMaterials = Surface.Materials);
                CleanupOperations.Capture(ref Failures, () => Surface.Renderer.enabled = Surface.Enabled);
                if (Surface.Properties is not null)
                {
                    CleanupOperations.Capture(ref Failures, () => Surface.Renderer.SetPropertyBlock(Surface.Properties));
                    for (var i = 0; i < Surface.Slots.Length; i++)
                    {
                        var Slot = i;
                        CleanupOperations.Capture(ref Failures, () => Surface.Renderer.SetPropertyBlock(Surface.Slots[Slot], Slot));
                    }
                }
            }
        if (EmitterModel)
            foreach (var (Renderer, Hidden) in Renderers)
                if (Renderer && TransformOwnership.Contains(EmitterModel.transform, Renderer.transform))
                    CleanupOperations.Capture(ref Failures, () => Renderer.forceRenderingOff = Hidden);
        foreach (var Binding in SurfaceBindings) CleanupOperations.Capture(ref Failures, Binding.Dispose);
        SurfaceBindings.Clear();
        if (Graph)
        {
            CleanupOperations.Capture(ref Failures, () => Graph.SetActive(false));
            CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.DestroyImmediate(Graph));
        }
        CleanupOperations.Capture(ref Failures, ReleaseNativeInputs);
        if (OwnedMesh) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.DestroyImmediate(OwnedMesh));
        if (Surface && InitialSurfaceProperties is not null)
        {
            CleanupOperations.Capture(ref Failures, () => Surface.SetPropertyBlock(InitialSurfaceProperties));
            CleanupOperations.Capture(ref Failures, () => Surface.SetPropertyBlock(InitialSurfaceSlotProperties, 0));
            CleanupOperations.Capture(ref Failures, () => Surface.sharedMaterial = InitialSurfaceMaterial);
            CleanupOperations.Capture(ref Failures, () => Surface.enabled = true);
        }
        foreach (var Material in OwnedMaterials)
            if (Material) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.DestroyImmediate(Material));
        OwnedMaterials.Clear(); BoundMaterials.Clear(); CoverageBlocks.Clear();
        if (Lifetime) Lifetime.Owner = null;
        Budget.Release(this); GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    protected sealed class FlowPlacementFailure : Exception { }

    [HarmonyPatch(typeof(HelperFunctions), nameof(HelperFunctions.LineCheck),
        new[] { typeof(Vector3), typeof(Vector3), typeof(HelperFunctions.LayerType), typeof(float), typeof(QueryTriggerInteraction) })]
    static class FlowSurfacePatch
    {
        static bool Prefix(Vector3 __0, Vector3 __1, HelperFunctions.LayerType __2,
            float __3, QueryTriggerInteraction __4, ref RaycastHit __result)
        {
            var Owner = Active; if (Owner is null) return true;
            __result = Owner.Trace!(Owner.SourceTransform, __0, __1, __2, __3, __4);
            Owner.LastHit = __result; return false;
        }
    }

    sealed class SplineFlow : NativeFlowRuntime
    {
        internal LavaRiver River = null!;
        SplineContainer Container = null!;
        SplineExtrude Extrude = null!;
        CollisionModifier? Contact;
        int InitialNodes;
        int InitialInstances;
        int AlongSplineNodes;
        int[] DetailNodes = Array.Empty<int>();

        internal SplineFlow(NativeFlowContract Contract, PLLNativeResolver Source,
            PllNativeFlowSettings Settings, GeneratedGeometryBudget Budget)
            : base(Contract, Source, Settings, null, Budget) { }

        protected override void Generate(PropSpawner.SpawnData Data)
        {
            var Material = ResolveMaterial(Contract.Behavior == PllFluidBehavior.VisualOnly ? null : Provider.Material);
            var Details = Settings.GenerateDetails ? Settings.DetailsAlongSpline ?? Provider.Details : Array.Empty<GameObject>();
            if (Settings.GenerateDetails && Details.Length == 0)
                throw new InvalidOperationException("Enabled flow details have no prefab.");
            AlongSplineNodes = ContactSampleNodes();
            InitialNodes = 5 + (Contract.HasLandingModel ? Contract.EndRockNodes ?? Provider.EndRockNodes : 1)
                + (Settings.GenerateSplash ? Contract.SplashNodes ?? Provider.SplashNodes : 0)
                + AlongSplineNodes + (Settings.SpawnLights ? Provider.LightNodes : 0);
            DetailNodes = new int[Details.Length];
            for (var i = 0; i < Details.Length; i++)
            {
                DetailNodes[i] = Contract.DetailNodes(i) ?? Provider.DetailNodes[i];
                InitialNodes = checked(InitialNodes + DetailNodes[i]);
            }
            InitialInstances = (Contract.HasLandingModel ? 1 : 0) + (Settings.GenerateSplash ? 1 : 0)
                + (Contract.HasWaterContacts || Settings.GenerateAlongSpline ? 1 : 0) + Details.Length + (Settings.SpawnLights ? 1 : 0);
            Reserve(InitialInstances, InitialNodes, 0, 0);
            River = Graph.AddComponent<LavaRiver>(); River.enabled = false;
            River.spawnVel = Settings.SpawnVelocity; River.gravity = Settings.Gravity; River.wallStick = Settings.WallStick;
            River.drag = Settings.Drag; River.timeStep = Settings.TimeStep; River.maxSteps = Settings.MaxSteps;
            River.maxLength = Settings.MaxLength; River.prefDistancePerFrame = Settings.PreferredDistancePerFrame;
            River.stepsAlongSpline = Settings.StepsAlongSpline; River.spawnLights = Settings.SpawnLights;
            River.endRock = Contract.HasLandingModel
                ? LandingRock = Clone(Settings.EndRock ? Settings.EndRock : Provider.EndRock, "EndRock")
                : Child("Endpoint");
            if (Settings.GenerateSplash)
                River.splash = Clone(Settings.Splash ? Settings.Splash : Provider.Splash
                    ? Provider.Splash : throw new InvalidOperationException("The flow profile has no splash prefab."), "Splash");
            if (Contract.HasWaterContacts) River.spawnAlongSpline = WaterContact("AlongSpline", Settings.GenerateAlongSpline);
            else if (Settings.GenerateAlongSpline)
                River.spawnAlongSpline = Clone(Settings.SpawnAlongSpline ? Settings.SpawnAlongSpline : Provider.AlongSpline!, "AlongSpline");
            River.detailsAlongSpline = new GameObject[Details.Length];
            for (var i = 0; i < Details.Length; i++) River.detailsAlongSpline[i] = Clone(Details[i], $"Detail{i}");
            if (Settings.SpawnLights)
                Clone(Provider.Light ? Provider.Light : throw new InvalidOperationException("The flow profile has no light prefab."), "BakedLight");
            Child("BakedLights"); Child("DetailObjects"); Child("SpawnedObjects");
            var SplineObject = Child("SplineMesh");
            SplineObject.AddComponent<MeshFilter>(); FluidSurface = SplineObject.AddComponent<MeshRenderer>();
            FluidSurface.sharedMaterial = Material;
            Container = SplineObject.AddComponent<SplineContainer>();
            Extrude = SplineObject.AddComponent<SplineExtrude>(); Extrude.enabled = false;
            Extrude.Container = Container; Extrude.Sides = Settings.Sides;
            Extrude.Radius = Settings.Radius; Extrude.SegmentsPerUnit = Settings.SegmentsPerUnit;
            Extrude.Range = new Vector2(0f, 1f); Extrude.Capped = Settings.Capped;
            Extrude.RebuildOnSplineChange = false;
            if (Contract.Behavior == PllFluidBehavior.Lava)
            {
                SplineObject.layer = Provider.Contact!.gameObject.layer;
                SplineObject.AddComponent<MeshCollider>(); Contact = SplineObject.AddComponent<CollisionModifier>();
                CopyContact(Provider.Contact!, Contact); Contact.applyEffects = false;
            }
            River.CheckCondition(Data);
        }

        GameObject Child(string Name)
        {
            var Child = new GameObject(Name); Child.transform.SetParent(Graph.transform, false); return Child;
        }

        internal void Apply()
        {
            if (River.frames.Count < 2) throw new FlowPlacementFailure();
            var Final = River.frames[River.frames.Count - 1];
            var Landing = Trace!(SourceTransform, Final.position + Final.up * 0.1f,
                Final.position - Final.up * 0.2f, HelperFunctions.LayerType.TerrainMap, 0f, QueryTriggerInteraction.Ignore);
            if (!Landing.collider) throw new FlowPlacementFailure();
            var Spline = new Spline();
            Container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Container.Spline = Spline;
            foreach (var Frame in River.frames)
            {
                if (!UnityValueContract.Finite(Frame.position)
                    || !UnityValueContract.Finite(Frame.forward) || !UnityValueContract.Finite(Frame.up)
                    || Frame.forward.sqrMagnitude < 0.000001f || Frame.up.sqrMagnitude < 0.000001f)
                    throw new FlowPlacementFailure();
                Spline.Add(new BezierKnot(Frame.position, float3.zero, float3.zero,
                    HelperFunctions.GetRotationWithUp(Frame.forward, Frame.up)), TangentMode.AutoSmooth);
            }
            for (var i = 0; i < Spline.Count; i++)
            {
                var Knot = Spline[i];
                Spline.SetKnot(i, new BezierKnot(Knot.Position, Knot.TangentIn, Knot.TangentOut,
                    Quaternion.Euler(Vector3.left)), BezierTangent.Out);
            }
            var Length = Spline.GetLength();
            if (!float.IsFinite(Length)) throw new FlowPlacementFailure();
            var SegmentCount = Math.Max(1d, Math.Ceiling(Length * Settings.SegmentsPerUnit));
            if (SegmentCount > int.MaxValue)
                throw new InvalidOperationException("Native flow extrusion exceeds its geometry budget.");
            var Extrusion = new ExtrudeSettings<Circle> { SegmentCount = (int)SegmentCount };
            SplineMesh.GetVertexAndIndexCount(Settings.Sides, Extrusion.SegmentCount, Settings.Capped, false,
                new Vector2(0f, 1f), out var Vertices, out var Indices);
            var MeshBytes = checked((long)Vertices * 32 + (long)Indices * (Vertices < ushort.MaxValue ? 2 : 4));
            var FrameCount = River.frames.Count;
            var Instances = InitialInstances; var Nodes = InitialNodes;
            if (Contract.HasWaterContacts || Settings.GenerateAlongSpline) AddOutput(AlongSplineNodes, FrameCount);
            foreach (var Detail in DetailNodes)
                AddOutput(Detail, (FrameCount + Settings.StepsAlongSpline - 1) / Settings.StepsAlongSpline);
            if (Settings.SpawnLights) AddOutput(Provider.LightNodes, (FrameCount + 2) / 3);
            Reserve(Instances, Nodes, 1, MeshBytes);
            var Mesh = new Mesh { name = "PLL Native Flow" };
            try
            {
                Extrude.targetMesh = Mesh;
                if (Contract.SurfaceTextures is { } SurfaceTextures)
                    AttachGeneratedSurface(SurfaceTextures.BindGenerated(FluidSurface, 0, Mesh, BiomeFrame));
                AdoptMesh(Mesh);
            }
            catch { UnityEngine.Object.DestroyImmediate(Mesh); throw; }
            if (Extrude.GetComponent<MeshFilter>().sharedMesh != Mesh
                || Contract.Behavior == PllFluidBehavior.Lava && Extrude.GetComponent<MeshCollider>().sharedMesh != Mesh)
                throw new InvalidOperationException("Native flow renderer and collider do not share the owned mesh.");
            if (River.splash) River.splash.transform.position = Final.position;
            River.endRock.transform.SetPositionAndRotation(Final.position, Quaternion.LookRotation(Final.up));

            void AddOutput(int PrefabNodes, int Count)
            {
                Instances = checked(Instances + Count);
                Nodes = checked(Nodes + Count * PrefabNodes);
            }
        }

        protected override void SetNativeGameplay(bool Current)
        {
            if (Contact) Contact.applyEffects = Current && Provider.Contact!.applyEffects;
        }

        protected override void WriteGeometry(BinaryWriter Writer)
        {
            Writer.Write(River.frames.Count);
            foreach (var Frame in River.frames)
            {
                ManifestEncoding.WriteVector(Writer, Frame.position); ManifestEncoding.WriteVector(Writer, Frame.forward);
                ManifestEncoding.WriteVector(Writer, Frame.up);
            }
            Writer.Write(Contact ? true : false); if (Contact) NativeFlowProvider.WriteContact(Writer, Contact);
        }


    }

    sealed class RayFlow : NativeFlowRuntime
    {
        WaterFallConfig Config = null!;
        internal RayFlow(NativeFlowContract Contract, PLLNativeResolver Source, PllNativeFlowSettings Settings,
            MeshRenderer Surface, GeneratedGeometryBudget Budget) : base(Contract, Source, Settings, Surface, Budget) { }

        protected override void Generate(PropSpawner.SpawnData Data)
        {
            var Material = ResolveMaterial(Provider.Material);
            if (!Material.HasProperty("_WorldPositionY"))
                throw new InvalidOperationException("The ray waterfall material has no native landing-height channel.");
            Surface!.sharedMaterial = Material;
            var Nodes = 1 + (Contract.HasLandingModel ? Contract.EndRockNodes ?? Provider.EndRockNodes : 1)
                + (Settings.GenerateSplash ? Contract.SplashNodes ?? Provider.SplashNodes : 0)
                + ContactSampleNodes();
            var Details = Settings.GenerateDetails ? Settings.DetailsAlongSpline ?? Provider.Details : Array.Empty<GameObject>();
            for (var i = 0; i < Details.Length; i++) Nodes = checked(Nodes + (Contract.DetailNodes(i) ?? Provider.DetailNodes[i]));
            var Instances = (Contract.HasLandingModel ? 1 : 0) + (Settings.GenerateSplash ? 1 : 0)
                + (Contract.HasWaterContacts || Settings.GenerateAlongSpline ? 1 : 0) + Details.Length;
            Reserve(Instances, Nodes, 0, 0);
            Config = Graph.AddComponent<WaterFallConfig>(); Config.enabled = false;
            Config.mesh = Surface; Config.rayStart = Settings.RayStart; Config.rayEnd = Settings.RayEnd;
            if (Contract.HasLandingModel)
                Config.endRock = (LandingRock = Clone(Settings.EndRock ? Settings.EndRock : Provider.EndRock, "EndRock")).transform;
            else
            {
                var Endpoint = new GameObject("Endpoint"); Endpoint.transform.SetParent(Graph.transform, false);
                Config.endRock = Endpoint.transform;
            }
            Config.CheckCondition(Data);
            if (!LastHit.collider)
            {
                if (Settings.LandingPolicy != PllNativeFlowLandingPolicy.AuthoredEndpoint) throw new FlowPlacementFailure();
                Config.endRock.position = Settings.Endpoint!.position;
                var Properties = new MaterialPropertyBlock(); Surface.GetPropertyBlock(Properties);
                Properties.SetFloat("_WorldPositionY", Config.endRock.position.y); Surface.SetPropertyBlock(Properties);
            }
            var SlotProperties = new MaterialPropertyBlock(); Surface.GetPropertyBlock(SlotProperties, 0);
            if (!SlotProperties.isEmpty)
            {
                SlotProperties.SetFloat("_WorldPositionY", Config.endRock.position.y);
                Surface.SetPropertyBlock(SlotProperties, 0);
            }
            if (Settings.GenerateSplash)
            {
                var Splash = Clone(Settings.Splash ? Settings.Splash : Provider.Splash!, "Splash");
                Splash.transform.position = Config.endRock.position;
                Splash.SetActive(true);
            }
            if (Contract.HasWaterContacts)
            {
                var Trigger = WaterContact("WaterContact", Settings.GenerateAlongSpline);
                var Start = Settings.RayStart!.position; var End = Config.endRock.position;
                var Capsule = Trigger.GetComponent<CapsuleCollider>();
                var Axis = Capsule.direction == 0 ? Vector3.right : Capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Trigger.transform.SetPositionAndRotation((Start + End) * 0.5f, Quaternion.FromToRotation(Axis, End - Start));
                Capsule.center = Vector3.zero;
                Capsule.height = Vector3.Distance(Start, End) / Trigger.transform.localScale.x + Capsule.radius * 2f;
                Trigger.SetActive(true);
            }
            if (Settings.GenerateDetails)
            {
                foreach (var Detail in Details)
                {
                    var Clone = this.Clone(Detail, "FlowAudio"); Clone.transform.position = SourceTransform.position;
                    Clone.SetActive(true);
                }
            }
        }

        protected override void SetNativeGameplay(bool Current) { }
        protected override void WriteGeometry(BinaryWriter Writer)
        {
            ManifestEncoding.WriteVector(Writer, Config.rayStart.position);
            ManifestEncoding.WriteVector(Writer, Config.rayEnd.position);
            ManifestEncoding.WriteVector(Writer, Config.endRock.position);
            Writer.Write(Config.mesh.GetComponent<MeshFilter>().sharedMesh ? true : false);
        }
    }

    sealed class LavaZoneFlow : NativeFlowRuntime
    {
        Lava Zone = null!;
        internal LavaZoneFlow(NativeFlowContract Contract, PLLNativeResolver Source, PllNativeFlowSettings Settings,
            MeshRenderer Surface, GeneratedGeometryBudget Budget) : base(Contract, Source, Settings, Surface, Budget) { }

        protected override void Generate(PropSpawner.SpawnData Data)
        {
            Surface!.sharedMaterial = ResolveMaterial(Provider.ZoneMaterial!);
            var Bounds = NativeResolverVolume.WorldBounds(SourceTransform, Contract.Volume);
            if (Vector3.Angle(Surface!.transform.up, Vector3.up) > 0.01f
                || Bounds.size.y <= 0f || Surface.bounds.size.x <= 0f || Surface.bounds.size.z <= 0f)
                throw new InvalidOperationException("Local Lava needs a horizontal surface with positive bounds.");
            Graph.transform.position = new Vector3(Bounds.center.x, Surface.transform.position.y, Bounds.center.z);
            Zone = Graph.AddComponent<Lava>(); Zone.enabled = false;
            Zone.heatRate = Contract.HeatRate; Zone.heat = Contract.Heat; Zone.isKiln = Contract.IsKiln;
            Zone.height = Bounds.size.y; Zone.bounds = Bounds;
        }

        internal void SetPlacedBounds() => Zone.bounds = NativeResolverVolume.WorldBounds(SourceTransform, Contract.Volume);

        protected override void SetNativeGameplay(bool Current)
        {
            if (!Zone) return;
            Zone.enabled = Current;
            if (Current) return;
            Zone.StopAllCoroutines(); Zone.hitPlayers.Clear();
            if (Zone.itemToCookTime.Count == 0) return;
            var Others = UnityEngine.Object.FindObjectsByType<Lava>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var Item in Zone.itemToCookTime.Keys)
            {
                if (!Item) continue;
                var Shared = false;
                foreach (var Other in Others)
                    if (Other != Zone && Other.isActiveAndEnabled && Other.itemToCookTime.ContainsKey(Item)) { Shared = true; break; }
                if (!Shared)
                    foreach (var Campfire in global::Campfire.ALL_CAMPFIRES)
                        if (Campfire && Campfire.currentlyCookingItem == Item) { Shared = true; break; }
                if (!Shared) Item.cooking.CancelCookingVisuals();
            }
            Zone.itemToCookTime.Clear(); Zone.itemToCookList.Clear(); Zone.itemToRemoveList.Clear();
        }

        protected override void WriteGeometry(BinaryWriter Writer)
        {
            var Bounds = Zone.bounds;
            ManifestEncoding.WriteVector(Writer, Bounds.center); ManifestEncoding.WriteVector(Writer, Bounds.size);
            ManifestEncoding.WriteVector(Writer, Zone.transform.position);
            NativeFlowProvider.WriteScalars(Writer, Zone.heatRate, Zone.heat, Zone.height); Writer.Write(Zone.isKiln);
        }
    }
    [HarmonyPatch(typeof(LavaRiver), nameof(LavaRiver.Apply))]
    static class FlowMeshPatch
    {
        static bool Prefix(LavaRiver __instance)
        {
            if (Active is not SplineFlow Flow || !ReferenceEquals(Flow.River, __instance)) return true;
            Flow.Apply(); return false;
        }
    }

    [HarmonyPatch(typeof(Lava), "Start")]
    static class LavaBoundsPatch
    {
        static bool Prefix(Lava __instance)
        {
            if (!TryGetOwner(__instance.transform, out var Owner) || Owner is not LavaZoneFlow Flow) return true;
            Flow.SetPlacedBounds(); return false;
        }
    }

    [HarmonyPatch(typeof(WaterfallTrigger), "OnTriggerStay")]
    static class WaterContactPatch
    {
        static bool Prefix(WaterfallTrigger __instance) =>
            !TryGetOwner(__instance.transform, out var Owner) || Owner.GameplayActive;
    }

    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    static class LavaContactPatch
    {
        static bool Prefix(CollisionModifier __instance) =>
            !TryGetOwner(__instance.transform, out var Owner) || Owner.GameplayActive;
    }

    [HarmonyPatch(typeof(WaterfallPusher), "OnTriggerEnter")]
    static class WaterPushPatch
    {
        static bool Prefix(WaterfallPusher __instance) =>
            !TryGetOwner(__instance.transform, out var Owner) || Owner.GameplayActive;
    }

    [HarmonyPatch(typeof(SFX_PlayOneShot), nameof(SFX_PlayOneShot.PlayOneShot))]
    static class FlowSoundPatch
    {
        static bool Prefix(SFX_PlayOneShot __instance) =>
            !TryGetOwner(__instance.transform, out var Owner) || Owner.GameplayActive;

        static void Postfix(SFX_PlayOneShot __instance)
        {
            if (!TryGetOwner(__instance.transform, out var Owner) || !Owner.GameplayActive
                || __instance.handle is not { source: not null } Handle) return;
            for (var i = Owner.SoundHandles.Count - 1; i >= 0; i--)
                if (Owner.SoundHandles[i].source is null) Owner.SoundHandles.RemoveAt(i);
            Owner.SoundHandles.Add(Handle);
        }
    }
}
