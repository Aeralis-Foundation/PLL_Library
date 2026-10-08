using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Photon.Pun;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Library;

internal sealed class NativeVineRecipes
{
    internal const int VineMask = 1, BridgeMask = 2;
    readonly Recipe? Vine, Bridge;
    NativeVineRecipes(Recipe? Vine, Recipe? Bridge) { this.Vine = Vine; this.Bridge = Bridge; }
    internal static Seal BeginSeal(NativeSegmentCatalog Catalog) => new(Catalog);

    internal sealed class Seal
    {
        readonly Lazy<Recipe> Vine, Bridge;
        internal Seal(NativeSegmentCatalog Catalog)
        {
            var Sources = new Lazy<Inventory>(() => new(Catalog), LazyThreadSafetyMode.None);
            Vine = new(() => Sources.Value.Require(false), LazyThreadSafetyMode.None);
            Bridge = new(() => Sources.Value.Require(true), LazyThreadSafetyMode.None);
        }
        internal NativeVineRecipes Prepare(int Mask)
        {
            if ((Mask & ~(VineMask | BridgeMask)) != 0) throw new ArgumentException("Vine provider mask is unsupported.", nameof(Mask));
            return new((Mask & VineMask) != 0 ? Vine.Value : null, (Mask & BridgeMask) != 0 ? Bridge.Value : null);
        }
    }

    sealed class Inventory
    {
        readonly List<(JungleVine Source, BreakableBridge? Bridge)> Sources = new();
        internal Inventory(NativeSegmentCatalog Catalog)
        {
            var Pending = new Queue<GameObject>(); var Seen = new HashSet<GameObject>();
            var Components = new HashSet<Component>();
            Add(Catalog.RequireOrdinary("Beach").Container.gameObject);
            while (Pending.Count != 0)
                foreach (var Component in Pending.Dequeue().GetComponentsInChildren<Component>(true))
                {
                    if (!Components.Add(Component)) continue;
                    if (Component is PropSpawner Spawner)
                        foreach (var Candidate in NativePrefabContracts.EffectiveProps(Spawner) ?? Array.Empty<GameObject>())
                            Add(Candidate ? Candidate : throw new InvalidOperationException("Native vine source contains a missing candidate."));
                    if (Component is not JungleVine Source || Source.gameObject.scene.IsValid()) continue;
                    Sources.Add((Source, Source.GetComponent<BreakableBridge>()));
                }
            void Add(GameObject Root) { if (Seen.Add(Root)) Pending.Enqueue(Root); }
        }
        internal Recipe Require(bool IsBridge)
        {
            Recipe? Result = null;
            foreach (var (Source, Bridge) in Sources)
            {
                if ((bool)Bridge != IsBridge) continue;
                var Candidate = new Recipe(Source, Bridge);
                if (Result is null) Result = Candidate;
                else if (!Result.Fingerprint.AsSpan().SequenceEqual(Candidate.Fingerprint))
                    throw new InvalidOperationException("Native vine providers have different retained recipes.");
            }
            return Result ?? throw new InvalidOperationException("The requested native vine provider is unavailable.");
        }
    }

    internal Recipe Require(PllVineProfile Profile) => Profile switch
    {
        PllVineProfile.Vine => Vine!,
        PllVineProfile.HangingBridge => Bridge!,
        _ => throw new InvalidOperationException("Prepared vine profile is unsupported.")
    };

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Vine is not null); Vine?.WriteManifest(Writer);
        Writer.Write(Bridge is not null); Bridge?.WriteManifest(Writer);
    }

    internal sealed class Recipe
    {
        const int NativeSegments = 50;
        readonly GameObject Model;
        readonly float MeshLength;
        readonly int VineType;
        readonly string DisplayName;
        readonly bool IsBridge, ConnectsPlatforms;
        readonly JungleVine.ColliderType ColliderType;
        readonly ViewSynchronization Synchronization;
        readonly OwnershipOption Ownership;
        readonly PhotonView.ObservableSearch ObservableSearch;
        readonly (bool Position, bool Rotation, bool Scale, bool Local)? TransformSynchronization;
        readonly AudioClip? WarningClip;
        readonly PropBridgeResponse Response;
        readonly AudioSource? WarningAudio;
        readonly AnimationCurve? Rolloff, SpatialBlend, Reverb, Spread;
        internal readonly byte[] Fingerprint;
        internal int GeneratedTransforms { get; }
        internal int GeneratedColliders => ConnectsPlatforms ? NativeSegments - 2 : NativeSegments;
        internal Recipe(JungleVine Source, BreakableBridge? Bridge)
        {
            var MeshRoot = Source.transform.Find("Mesh");
            var View = Source.GetComponent<PhotonView>();
            if (!MeshRoot || !View || !PllAuthoringValueContract.CanEncode(Source.meshLength) || Source.meshLength <= 0f
                || Source.colliderType != (Bridge ? JungleVine.ColliderType.Box : JungleVine.ColliderType.Capsule))
                throw new InvalidOperationException("Native vine has an incomplete model, view or collider recipe.");
            var Renderers = MeshRoot.GetComponentsInChildren<MeshRenderer>(true);
            if (Renderers.Length != 1 || Renderers[0].sharedMaterials.Length != 1 || !Renderers[0].sharedMaterial
                || !Renderers[0].TryGetComponent<MeshFilter>(out var Filter) || !Filter.sharedMesh
                || Source.GetComponentInChildren<Renderer>(true) != Renderers[0])
                throw new InvalidOperationException("Native vine needs one first controlled mesh renderer.");
            foreach (var Component in MeshRoot.GetComponentsInChildren<Component>(true))
            {
                if (Component is not Transform and not MeshRenderer and not MeshFilter)
                    throw new InvalidOperationException("Native vine display model contains gameplay components.");
                if (Component is Transform) GeneratedTransforms++;
            }
            GeneratedTransforms += NativeSegments + 2;
            if (View.ObservedComponents.Count > 1)
                throw new InvalidOperationException("Native vine view has multiple observers.");
            if (View.ObservedComponents.Count == 1)
            {
                if (View.ObservedComponents[0] is not PhotonTransformView TransformView || TransformView.gameObject != Source.gameObject)
                    throw new InvalidOperationException("Native vine view has an unsupported observer.");
                TransformSynchronization = (TransformView.m_SynchronizePosition, TransformView.m_SynchronizeRotation,
                    TransformView.m_SynchronizeScale, TransformView.m_UseLocal);
            }
            Model = MeshRoot.gameObject; MeshLength = Source.meshLength; VineType = Source.vineType;
            DisplayName = Source.displayName; IsBridge = Bridge;
            ConnectsPlatforms = Source.connectsPlatforms; ColliderType = Source.colliderType;
            Synchronization = View.Synchronization; Ownership = View.OwnershipTransfer; ObservableSearch = View.observableSearch;
            if (Bridge is not null)
            {
                if (!Source.TryGetComponent<AudioSource>(out var Audio) || !Bridge.fullMesh
                    || !TransformOwnership.Contains(MeshRoot, Bridge.fullMesh)
                    || !PllAuthoringValueContract.CanEncode(Bridge.fallTime) || Bridge.fallTime <= 0f)
                    throw new InvalidOperationException("Native bridge has an incomplete warning presentation.");
                WarningClip = Audio.clip;
                Response = new(Bridge.fallTime, WarningClip, Bridge.startShakeDistance,
                    Bridge.startShakeAmount, Bridge.climbingScreenShake, Bridge.screenShakeTickTime);
                WarningAudio = Audio; Rolloff = Audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                SpatialBlend = Audio.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
                Reverb = Audio.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix); Spread = Audio.GetCustomCurve(AudioSourceCurveType.Spread);
            }
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
            {
                Writer.Write("PLL/NativeVine/v3"); Writer.Write(IsBridge); Writer.Write(VineType); Writer.Write(DisplayName);
                Writer.Write(ConnectsPlatforms); Writer.Write((int)ColliderType);
                Writer.Write((int)Synchronization); Writer.Write((int)Ownership); Writer.Write((int)ObservableSearch);
                Writer.Write(TransformSynchronization.HasValue);
                if (TransformSynchronization is { } Sync)
                {
                    Writer.Write(Sync.Position); Writer.Write(Sync.Rotation); Writer.Write(Sync.Scale); Writer.Write(Sync.Local);
                }
                ManifestEncoding.WriteScalar(Writer, MeshLength);
                Writer.Write(DeterministicObjectIdentity.HashObjectHierarchy(MeshRoot, "PLL/NativeVineModel/v1"));
                Writer.Write(VisualFingerprintWriter.HashSerializedObject(Filter.sharedMesh, "PLL/NativeVineMesh/v1"));
                Writer.Write(VisualFingerprintWriter.HashSerializedObject(Renderers[0].sharedMaterial, "PLL/NativeVineMaterial/v1"));
                if (IsBridge)
                {
                    Response.WriteManifest(Writer); NativeFlowProvider.WriteAudio(Writer, WarningAudio!);
                }
            }
            Fingerprint = Stream.ToArray();
        }
        internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
        internal PropBridgeResponse BridgeResponse => Response;

        internal JungleVine Create(Transform Parent, PllPropInteractionDriver Driver,
            float Hang, float Offset, Vector2 BoxShape, Mesh? CustomMesh, float CustomLength, out Material Material)
        {
            var Root = Driver.gameObject;
            Root.transform.SetParent(Parent, false);
            var ModelInstance = Object.Instantiate(Model, Root.transform); ModelInstance.name = "Mesh";
            var Renderer = ModelInstance.GetComponentInChildren<MeshRenderer>(true);
            if (CustomMesh)
            {
                var Filter = ModelInstance.GetComponentInChildren<MeshFilter>(true);
                Filter.sharedMesh = CustomMesh;
                if (Filter.transform != ModelInstance.transform)
                {
                    Filter.transform.localPosition = Vector3.zero; Filter.transform.localRotation = Quaternion.identity;
                    Filter.transform.localScale = Vector3.one;
                }
            }
            var View = Root.AddComponent<PhotonView>();
            View.Synchronization = Synchronization; View.OwnershipTransfer = Ownership; View.observableSearch = ObservableSearch;
            View.ObservedComponents = new List<Component>(TransformSynchronization.HasValue ? 1 : 0);
            if (TransformSynchronization is { } Sync)
            {
                var TransformView = Root.AddComponent<PhotonTransformView>();
                TransformView.m_SynchronizePosition = Sync.Position; TransformView.m_SynchronizeRotation = Sync.Rotation;
                TransformView.m_SynchronizeScale = Sync.Scale; TransformView.m_UseLocal = Sync.Local;
                View.ObservedComponents.Add(TransformView);
            }
            var Vine = Root.AddComponent<JungleVine>();
            Vine.photonView = View; Vine.meshLength = CustomMesh ? CustomLength : MeshLength;
            Vine.vineType = VineType; Vine.displayName = DisplayName; Vine.connectsPlatforms = ConnectsPlatforms;
            Vine.colliderType = ColliderType;
            Vine.minDown = Vine.maxDown = Hang; Vine.colliderOffset = Offset; Vine.boxShape = BoxShape;
            Vine.bannedMaterial = Array.Empty<Material>();
            if (IsBridge)
            {
                var Audio = Root.AddComponent<AudioSource>(); Audio.playOnAwake = false; Audio.loop = true; Audio.clip = WarningClip;
                var Source = WarningAudio!;
                Audio.priority = Source.priority; Audio.mute = Source.mute;
                Audio.bypassEffects = Source.bypassEffects; Audio.bypassListenerEffects = Source.bypassListenerEffects;
                Audio.bypassReverbZones = Source.bypassReverbZones; Audio.ignoreListenerPause = Source.ignoreListenerPause;
                Audio.ignoreListenerVolume = Source.ignoreListenerVolume; Audio.spatialize = Source.spatialize;
                Audio.spatializePostEffects = Source.spatializePostEffects; Audio.velocityUpdateMode = Source.velocityUpdateMode;
                Audio.rolloffMode = Source.rolloffMode; Audio.volume = Source.volume; Audio.pitch = Source.pitch;
                Audio.panStereo = Source.panStereo; Audio.spatialBlend = Source.spatialBlend;
                Audio.reverbZoneMix = Source.reverbZoneMix; Audio.dopplerLevel = Source.dopplerLevel;
                Audio.spread = Source.spread; Audio.minDistance = Source.minDistance; Audio.maxDistance = Source.maxDistance;
                Audio.outputAudioMixerGroup = Source.outputAudioMixerGroup;
                Audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff!);
                Audio.SetCustomCurve(AudioSourceCurveType.SpatialBlend, SpatialBlend!);
                Audio.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, Reverb!);
                Audio.SetCustomCurve(AudioSourceCurveType.Spread, Spread!);
                var Bridge = Root.AddComponent<BreakableBridge>();
                Bridge.fullMesh = ModelInstance.transform; Bridge.breakSfx = Array.Empty<SFX_Instance>();
                Bridge.fallTime = Response.WarningSeconds;
                Driver.Bridge = Bridge; Driver.WarningAudio = Audio;
            }
            Material = new Material(Renderer.sharedMaterial); Renderer.sharedMaterial = Material;
            if (IsBridge)
            {
                Material.SetFloat("_AlphaClip", 0.01f); Material.SetFloat("_JitterAmount", 0f);
                Material.SetFloat("_BreakAmount", 0f);
            }
            return Vine;
        }
    }
}

internal readonly struct PropBridgeResponse
{
    internal readonly float WarningSeconds, StartDistance, StartAmount, ClimbingAmount, ClimbingInterval;
    internal readonly AudioClip? WarningClip;
    internal PropBridgeResponse(float WarningSeconds, AudioClip? WarningClip,
        float StartDistance, float StartAmount, float ClimbingAmount, float ClimbingInterval)
    {
        this.WarningSeconds = WarningSeconds; this.WarningClip = WarningClip;
        this.StartDistance = StartDistance; this.StartAmount = StartAmount;
        this.ClimbingAmount = ClimbingAmount; this.ClimbingInterval = ClimbingInterval;
    }
    internal bool NeedsFeedback => StartAmount > 0f || ClimbingAmount > 0f;
    internal void WriteManifest(BinaryWriter Writer)
    {
        ManifestEncoding.WriteScalar(Writer, WarningSeconds); BiomeAuthoringContract.WriteClip(Writer, WarningClip);
        ManifestEncoding.WriteScalar(Writer, StartDistance); ManifestEncoding.WriteScalar(Writer, StartAmount);
        ManifestEncoding.WriteScalar(Writer, ClimbingAmount); ManifestEncoding.WriteScalar(Writer, ClimbingInterval);
    }
}
