using UnityEngine;

namespace PLL.Library;

public enum PLLNativeResolverKind : byte
{
    None = 0,
    Water = 1,
    Rain = 2,
    Wind = 3,
    Snow = 4,
    SporeCloud = 5,
    Lava = 6,
    CustomFluid = 7
}

public enum PllNativeResolverForm : byte { Zone = 0, Source = 1 }
public enum PllNativeFlowProfile : byte { RayWaterfall = 1, SimulatedSpline = 2, AuthoredSurface = 3 }
public enum PllNativeFlowLandingPolicy : byte { RequiredLanding = 0, AuthoredEndpoint = 1 }

[System.Serializable]
public struct PllNativeFlowSettings
{
    public float SpawnVelocity, Gravity, WallStick, Drag, TimeStep;
    public int MaxSteps;
    public float MaxLength, PreferredDistancePerFrame;
    public bool SpawnLights, GenerateSplash, GenerateAlongSpline, GenerateDetails;
    public GameObject? EndRock, Splash, SpawnAlongSpline;
    public GameObject[]? DetailsAlongSpline;
    public int StepsAlongSpline;
    public float Radius, SegmentsPerUnit;
    public int Sides;
    public bool Capped;
    public Transform? RayStart, RayEnd, Endpoint;
    public PllNativeFlowLandingPolicy LandingPolicy;
    public Material? Material;
    public float WaterForce, WaterDrag, WaterRagdollLength;
    public bool WaterRagdoll, EnablePusher;
    public float PusherFallTime, PusherKnockback, PusherDownwardKnockback, PusherCooldown;
    public SFX_Instance? PusherSfx;
    public PllFlowOutput LandingModel, SplashPresentation, SamplePresentation, DetailPresentation;

    public readonly void ValidateOutputSelections(PllFluidDefinition? Definition)
    {
        Validate(LandingModel, EndRock, "landing model"); Validate(SplashPresentation, Splash, "splash");
        Validate(SamplePresentation, SpawnAlongSpline, "samples");
        Validate(DetailPresentation, DetailsAlongSpline is { Length: > 0 }, "details");

        void Validate(PllFlowOutput Choice, bool Payload, string Name)
        {
            if (Choice == PllFlowOutput.Legacy)
            {
                if (Definition && Payload)
                    throw new System.ArgumentException($"Fluid default {Name} cannot carry a source-local prefab.");
                return;
            }
            if (Choice is not (PllFlowOutput.UseFluidDefault or PllFlowOutput.Native or PllFlowOutput.Custom or PllFlowOutput.None)
                || Choice == PllFlowOutput.UseFluidDefault && (!Definition || Payload)
                || Choice == PllFlowOutput.Custom && !Payload
                || Choice is PllFlowOutput.Native or PllFlowOutput.None && Payload)
                throw new System.ArgumentException($"Flow {Name} selection has a conflicting or missing payload.");
            if (Definition && Definition.Behavior == PllFluidBehavior.VisualOnly && Choice == PllFlowOutput.Native)
                throw new System.ArgumentException($"Visual Only fluid cannot use native {Name}.");
        }
    }

    public static PllNativeFlowSettings CreateDefault(PLLNativeResolverKind Kind)
    {
        if (Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava))
            throw new System.ArgumentOutOfRangeException(nameof(Kind));
        return new PllNativeFlowSettings
        {
            SpawnVelocity = Kind == PLLNativeResolverKind.Water ? 20f : 10f,
            Gravity = 10f, Drag = 0.95f, TimeStep = 0.05f,
            MaxSteps = 10000, MaxLength = 500f, PreferredDistancePerFrame = 7f,
            SpawnLights = Kind == PLLNativeResolverKind.Lava,
            GenerateSplash = Kind == PLLNativeResolverKind.Water,
            GenerateAlongSpline = Kind == PLLNativeResolverKind.Water,
            GenerateDetails = Kind == PLLNativeResolverKind.Water,
            StepsAlongSpline = Kind == PLLNativeResolverKind.Water ? 3 : 0,
            Radius = Kind == PLLNativeResolverKind.Water ? 4f : 1.25f,
            SegmentsPerUnit = 0.5f, Sides = 8, Capped = true,
            WaterForce = 300f, WaterDrag = 0.8f,
            WaterRagdoll = true, WaterRagdollLength = 0.5f,
            PusherFallTime = 0.5f, PusherKnockback = 500f,
            PusherDownwardKnockback = 50f, PusherCooldown = 1f
        };
    }

    public readonly void Validate(PLLNativeResolverKind Kind, PllNativeFlowProfile Profile)
    {
        if (Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava)
            || Profile is not (PllNativeFlowProfile.RayWaterfall or PllNativeFlowProfile.SimulatedSpline or PllNativeFlowProfile.AuthoredSurface)
            || Kind == PLLNativeResolverKind.Lava && Profile == PllNativeFlowProfile.RayWaterfall)
            throw new System.ArgumentException("The flow kind and profile are unsupported.");
        if (Profile == PllNativeFlowProfile.AuthoredSurface)
        {
            if (!Nonnegative(WaterForce) || !Nonnegative(WaterDrag) || !Nonnegative(WaterRagdollLength))
                throw new System.ArgumentException("The authored water contact settings are invalid.");
            return;
        }
        if (LandingPolicy is not (PllNativeFlowLandingPolicy.RequiredLanding or PllNativeFlowLandingPolicy.AuthoredEndpoint)
            || Profile == PllNativeFlowProfile.SimulatedSpline
                && LandingPolicy != PllNativeFlowLandingPolicy.RequiredLanding
            || LandingPolicy == PllNativeFlowLandingPolicy.AuthoredEndpoint && !Endpoint)
            throw new System.ArgumentException("The flow landing policy has no supported endpoint.");
        if (Profile == PllNativeFlowProfile.RayWaterfall)
        {
            if (!RayStart || !RayEnd || RayStart == RayEnd)
                throw new System.ArgumentException("The waterfall needs distinct ray anchors.");
        }
        else
        {
            if (!Positive(SpawnVelocity))
                throw new System.ArgumentOutOfRangeException(nameof(SpawnVelocity), SpawnVelocity, "Flow spawn velocity must be finite and positive.");
            if (!Nonnegative(Gravity))
                throw new System.ArgumentOutOfRangeException(nameof(Gravity), Gravity, "Flow gravity must be finite and nonnegative.");
            if (!Nonnegative(WallStick))
                throw new System.ArgumentOutOfRangeException(nameof(WallStick), WallStick, "Flow wall stick must be finite and nonnegative.");
            if (!Nonnegative(Drag) || Drag > 1f)
                throw new System.ArgumentOutOfRangeException(nameof(Drag), Drag, "Flow drag must be finite and between zero and one.");
            if (!Positive(TimeStep))
                throw new System.ArgumentOutOfRangeException(nameof(TimeStep), TimeStep, "Flow time step must be finite and positive.");
            if (MaxSteps is < 2 or > 65536)
                throw new System.ArgumentOutOfRangeException(nameof(MaxSteps), MaxSteps, "Flow maximum steps must be between 2 and 65536.");
            if (!Positive(MaxLength))
                throw new System.ArgumentOutOfRangeException(nameof(MaxLength), MaxLength, "Flow maximum length must be finite and positive.");
            if (!Positive(PreferredDistancePerFrame))
                throw new System.ArgumentOutOfRangeException(nameof(PreferredDistancePerFrame), PreferredDistancePerFrame,
                    "Flow frame spacing must be finite and positive.");
            if (!Positive(Radius) || Radius < 0.00001f)
                throw new System.ArgumentOutOfRangeException(nameof(Radius), Radius, "Flow radius must be finite and at least 0.00001.");
            if (!Positive(SegmentsPerUnit) || SegmentsPerUnit < 0.0001f)
                throw new System.ArgumentOutOfRangeException(nameof(SegmentsPerUnit), SegmentsPerUnit,
                    "Flow extrusion density must be finite and at least 0.0001.");
            if (Sides is < 3 or > 256)
                throw new System.ArgumentOutOfRangeException(nameof(Sides), Sides, "Flow extrusion sides must be between 3 and 256.");
            if (GenerateDetails && StepsAlongSpline <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(StepsAlongSpline), StepsAlongSpline,
                    "Enabled flow details require a positive step interval.");
            var MaximumSpeed = SpawnVelocity + (double)MaxSteps * (Gravity + (double)WallStick) * TimeStep;
            if (!PllAuthoringValueContract.CanEncode((float)MaximumSpeed)
                || !PllAuthoringValueContract.CanEncode((float)(MaxLength + MaximumSpeed * TimeStep)))
                throw new System.ArgumentException("The flow simulation exceeds its numeric range.");
        }
        if (!Nonnegative(WaterForce) || !Nonnegative(WaterDrag) || !Nonnegative(WaterRagdollLength)
            || !Nonnegative(PusherFallTime) || !Nonnegative(PusherKnockback)
            || !Nonnegative(PusherDownwardKnockback) || !Nonnegative(PusherCooldown))
            throw new System.ArgumentException("The waterfall contact settings are invalid.");
    }

    public static void ValidateMaterialOverridesPayload(Material Overrides)
    {
        if (!Overrides || !Overrides.shader)
            throw new System.ArgumentException("Material Overrides has no property schema.");
        var Shader = Overrides.shader;
        for (var i = 0; i < Shader.GetPropertyCount(); i++)
        {
            var Name = Shader.GetPropertyName(i);
            switch (Shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    var Vector = Overrides.GetVector(Name);
                    if (!PllAuthoringValueContract.CanEncode(Vector.x) || !PllAuthoringValueContract.CanEncode(Vector.y)
                        || !PllAuthoringValueContract.CanEncode(Vector.z) || !PllAuthoringValueContract.CanEncode(Vector.w))
                        throw new System.ArgumentException("Material Overrides contains an invalid color or vector.");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    if (!PllAuthoringValueContract.CanEncode(Overrides.GetFloat(Name)))
                        throw new System.ArgumentException("Material Overrides contains an invalid scalar.");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Int: break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    var Scale = Overrides.GetTextureScale(Name); var Offset = Overrides.GetTextureOffset(Name);
                    if (!PllAuthoringValueContract.CanEncode(Scale.x) || !PllAuthoringValueContract.CanEncode(Scale.y)
                        || !PllAuthoringValueContract.CanEncode(Offset.x) || !PllAuthoringValueContract.CanEncode(Offset.y))
                        throw new System.ArgumentException("Material Overrides contains an invalid texture transform.");
                    break;
                default: throw new System.ArgumentException("Material Overrides contains an unsupported property type.");
            }
        }
    }

    static bool Positive(float Value) => Value > 0f && PllAuthoringValueContract.CanEncode(Value);
    static bool Nonnegative(float Value) => Value >= 0f && PllAuthoringValueContract.CanEncode(Value);

    public static void ValidateTransform(Transform Root)
    {
        var X = Root.TransformVector(Vector3.right); var Y = Root.TransformVector(Vector3.up); var Z = Root.TransformVector(Vector3.forward);
        if (!Finite(X) || !Finite(Y) || !Finite(Z) || !Finite(Root.position)
            || Root.GetComponentInParent<Rigidbody>(true)
            || Mathf.Abs(X.sqrMagnitude - 1f) > 0.0001f || Mathf.Abs(Y.sqrMagnitude - 1f) > 0.0001f
            || Mathf.Abs(Z.sqrMagnitude - 1f) > 0.0001f || Mathf.Abs(Vector3.Dot(X, Y)) > 0.0001f
            || Mathf.Abs(Vector3.Dot(X, Z)) > 0.0001f || Mathf.Abs(Vector3.Dot(Y, Z)) > 0.0001f
            || (X - Root.right).sqrMagnitude > 0.0001f || (Y - Root.up).sqrMagnitude > 0.0001f
            || (Z - Root.forward).sqrMagnitude > 0.0001f)
            throw new System.ArgumentException("Native flow sources require a static unit world transform without shear or reflection.");
    }

    public static void ValidateZoneTransform(Transform Root)
    {
        ValidateTransform(Root);
        var Right = Root.right;
        if (Vector3.Angle(Root.up, Vector3.up) > 0.01f || Mathf.Max(Mathf.Abs(Right.x), Mathf.Abs(Right.z)) < 0.99999f)
            throw new System.ArgumentException("Fluid gameplay Box Zones require a horizontal, world-axis-aligned transform.");
    }

    static bool Finite(Vector3 Value) => !float.IsNaN(Value.x) && !float.IsInfinity(Value.x)
        && !float.IsNaN(Value.y) && !float.IsInfinity(Value.y) && !float.IsNaN(Value.z) && !float.IsInfinity(Value.z);

#if !PLL_CREATOR_SDK
    public static int ValidateOutput(GameObject Prefab)
    {
        if (!Prefab) throw new System.ArgumentException("A flow output prefab is missing.");
        var Components = Prefab.GetComponentsInChildren<Component>(true);
        var Nodes = 0;
        foreach (var Component in Components)
        {
            if (Component is Transform) Nodes++;
            if (Nodes > 65536) throw new System.ArgumentException("A flow output prefab exceeds the node budget.");
            if (Component is ParticleSystem Particle
                && PllAuthoringValueContract.ParticleCallbackFailure(Particle) is { } Failure)
                throw new System.ArgumentException(Failure);
            if (Component is BakedVolumeLight) continue;
            if (Component is not (Transform or MeshFilter or MeshRenderer or MeshCollider or CapsuleCollider
                or LODGroup or ParticleSystem or ParticleSystemRenderer or AudioSource or AudioLoop or Light
                or WaterfallTrigger or WaterfallPusher or StatusEmitter or SFX_PlayOneShot or IgnoreRenderersActive))
                throw new System.ArgumentException("A flow output prefab contains an unsupported component.");
            if (Component is StatusEmitter && Component.GetType() != typeof(StatusEmitter))
                throw new System.ArgumentException("A flow output prefab contains an unsupported status emitter.");
        }
        return Nodes;
    }
#endif
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Effects/Native Resolver")]
public sealed class PLLNativeResolver : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] PLLNativeResolverKind kind = PLLNativeResolverKind.None;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume volume = default;
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeField] MeshRenderer? surfaceRenderer = null;
    [SerializeField] Material? generatedSurfaceMaterial = null;
    [SerializeField] PllEffectAuthoring? presentation = null;
    [SerializeField, Min(0f)] float movementModifierShallow = PllWaterInteraction.DefaultMovementShallow;
    [SerializeField, Min(0f)] float movementModifierDeep = PllWaterInteraction.DefaultMovementDeep;
    [SerializeField, Min(0f)] float animModifierShallow = PllWaterInteraction.DefaultAnimationShallow;
    [SerializeField, Min(0f)] float animModifierDeep = PllWaterInteraction.DefaultAnimationDeep;
    [SerializeField] PllNativeResolverForm FormValue = PllNativeResolverForm.Zone;
    [SerializeField] PllNativeFlowProfile SourceProfileValue = PllNativeFlowProfile.SimulatedSpline;
#if PLL_CREATOR_SDK
    [SerializeField] PllNativeFlowSettings SourceSettingsValue = default;
#endif
    [SerializeField, HideInInspector] float BundleFlowSpawnVelocity = 0f, BundleFlowGravity = 0f;
    [SerializeField, HideInInspector] float BundleFlowWallStick = 0f, BundleFlowDrag = 0f, BundleFlowTimeStep = 0f;
    [SerializeField, HideInInspector] int BundleFlowMaxSteps = 0;
    [SerializeField, HideInInspector] float BundleFlowMaxLength = 0f, BundleFlowPreferredDistancePerFrame = 0f;
    [SerializeField, HideInInspector] bool BundleFlowSpawnLights = false, BundleFlowGenerateSplash = false;
    [SerializeField, HideInInspector] bool BundleFlowGenerateAlongSpline = false, BundleFlowGenerateDetails = false;
    [SerializeField, HideInInspector] GameObject? BundleFlowEndRock = null, BundleFlowSplash = null, BundleFlowSpawnAlongSpline = null;
    [SerializeField, HideInInspector] GameObject[]? BundleFlowDetailsAlongSpline = null;
    [SerializeField, HideInInspector] int BundleFlowStepsAlongSpline = 0;
    [SerializeField, HideInInspector] float BundleFlowRadius = 0f, BundleFlowSegmentsPerUnit = 0f;
    [SerializeField, HideInInspector] int BundleFlowSides = 0;
    [SerializeField, HideInInspector] bool BundleFlowCapped = false;
    [SerializeField, HideInInspector] Transform? BundleFlowRayStart = null, BundleFlowRayEnd = null, BundleFlowEndpoint = null;
    [SerializeField, HideInInspector] PllNativeFlowLandingPolicy BundleFlowLandingPolicy = default;
    [SerializeField, HideInInspector] Material? BundleFlowMaterial = null;
    [SerializeField, HideInInspector] float BundleFlowWaterForce = 0f, BundleFlowWaterDrag = 0f, BundleFlowWaterRagdollLength = 0f;
    [SerializeField, HideInInspector] bool BundleFlowWaterRagdoll = false, BundleFlowEnablePusher = false;
    [SerializeField, HideInInspector] float BundleFlowPusherFallTime = 0f, BundleFlowPusherKnockback = 0f;
    [SerializeField, HideInInspector] float BundleFlowPusherDownwardKnockback = 0f, BundleFlowPusherCooldown = 0f;
    [SerializeField, HideInInspector] SFX_Instance? BundleFlowPusherSfx = null;
    [SerializeField, HideInInspector] PllFlowOutput BundleLandingModel = default, BundleSplashPresentation = default;
    [SerializeField, HideInInspector] PllFlowOutput BundleSamplePresentation = default, BundleDetailPresentation = default;
    [SerializeField] PllFluidDefinition? FluidDefinitionValue = null;
    [SerializeField] GameObject? EmitterModelValue = null;
    [SerializeField] bool InitiallyDisabledValue = false;
    [SerializeField] MeshRenderer[] SurfaceRenderersValue = System.Array.Empty<MeshRenderer>();
    [SerializeField] int[] SurfaceSlotsValue = System.Array.Empty<int>();
    [SerializeField] PllFluidContactMode ContactModeValue = PllFluidContactMode.Automatic;
    [SerializeField] Transform[] ContactFramesValue = System.Array.Empty<Transform>();
    [SerializeField] PllZoneShape[] ContactShapesValue = System.Array.Empty<PllZoneShape>();
    [SerializeField] Vector3[] ContactCentersValue = System.Array.Empty<Vector3>();
    [SerializeField] Vector3[] ContactSizesValue = System.Array.Empty<Vector3>();
    [SerializeField] float[] ContactRadiiValue = System.Array.Empty<float>();
    [SerializeField] Collider[] ContactProxiesValue = System.Array.Empty<Collider>();
#if PLL_CREATOR_SDK
    [SerializeField] PllPresentationTransition TransitionValue = default;
#endif
    [SerializeField, HideInInspector] float BundleCoverDelaySeconds = 0f;
    [SerializeField, Min(0f)] float LavaHeatRateValue = PllLavaInteraction.DefaultHeatRate;
    [SerializeField, Min(0f)] float LavaHeatValue = PllLavaInteraction.DefaultHeat;
    [SerializeField] bool LavaIsKilnValue = false;

    public GameObject Root => root;
    public PLLNativeResolverKind Kind => kind;
    public PllNativeResolverForm Form => FormValue;
    public PllNativeFlowProfile SourceProfile => SourceProfileValue;
    public PllFluidDefinition? FluidDefinition => FluidDefinitionValue;
    public GameObject? EmitterModel => EmitterModelValue;
    public bool InitiallyEnabled => !InitiallyDisabledValue;
    public System.Collections.Generic.IReadOnlyList<MeshRenderer> SurfaceRenderers => SurfaceRenderersValue ?? System.Array.Empty<MeshRenderer>();
    public System.Collections.Generic.IReadOnlyList<int> SurfaceSlots => SurfaceSlotsValue ?? System.Array.Empty<int>();
    public PllFluidContactMode ContactMode => ContactModeValue;
    public System.Collections.Generic.IReadOnlyList<Transform> ContactFrames => ContactFramesValue ?? System.Array.Empty<Transform>();
    public System.Collections.Generic.IReadOnlyList<Collider> ContactProxies => ContactProxiesValue ?? System.Array.Empty<Collider>();
    public int ContactRegionCount => ContactFrames.Count;
    public PllZoneVolume ContactRegion(int Index) => new(ContactShapesValue[Index], ContactCentersValue[Index],
        ContactSizesValue[Index], ContactRadiiValue[Index]);

    public void ValidateContactArrays()
    {
        if (ContactRegionCount > 256 || ContactProxies.Count > 256
            || (ContactShapesValue?.Length ?? 0) != ContactRegionCount
            || (ContactCentersValue?.Length ?? 0) != ContactRegionCount
            || (ContactSizesValue?.Length ?? 0) != ContactRegionCount
            || (ContactRadiiValue?.Length ?? 0) != ContactRegionCount)
            throw new System.ArgumentException("Authored fluid contact arrays do not match their frames or exceed 256 regions.");
    }

    public Bounds AuthoredSurfaceBounds()
    {
        var Bounds = default(Bounds); var First = true;
        foreach (var Renderer in SurfaceRenderers)
        {
            var MeshBounds = Renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (var Corner = 0; Corner < 8; Corner++)
            {
                var Point = MeshBounds.center + Vector3.Scale(MeshBounds.extents,
                    new Vector3((Corner & 1) == 0 ? -1f : 1f, (Corner & 2) == 0 ? -1f : 1f, (Corner & 4) == 0 ? -1f : 1f));
                Point = transform.InverseTransformPoint(Renderer.transform.TransformPoint(Point));
                if (First) { Bounds = new Bounds(Point, Vector3.zero); First = false; }
                else Bounds.Encapsulate(Point);
            }
        }
        return Bounds;
    }

    public void ValidateEmitterModel()
    {
        if (!EmitterModel) return;
        if (EmitterModel == gameObject || !TransformOwnership.Contains(transform, EmitterModel.transform))
            throw new System.ArgumentException("Fluid emitter model must be an owned child.");
        if (SurfaceRenderer && TransformOwnership.Contains(EmitterModel.transform, SurfaceRenderer.transform))
            throw new System.ArgumentException("Fluid surface must be separate from its emitter model.");
        foreach (var Renderer in SurfaceRenderers)
            if (Renderer && TransformOwnership.Contains(EmitterModel.transform, Renderer.transform))
                throw new System.ArgumentException("Fluid surface must be separate from its emitter model.");
        foreach (var Component in EmitterModel.GetComponentsInChildren<Component>(true))
        {
            if (Component is not (Transform or MeshFilter or MeshRenderer or MeshCollider or BoxCollider or SphereCollider
                or CapsuleCollider or LODGroup or ParticleSystem or ParticleSystemRenderer or AudioSource or AudioLoop or Light))
                throw new System.ArgumentException("Fluid emitter model contains an unsupported component.");
            if (Component is ParticleSystem Particle)
            {
                if (PllAuthoringValueContract.ParticleCallbackFailure(Particle) is { } Failure)
                    throw new System.ArgumentException(Failure);
                if (Particle.main.playOnAwake)
                    throw new System.ArgumentException("Fluid emitter particles must disable Play On Awake.");
            }
            if (Component is AudioSource Audio && Audio.playOnAwake)
                throw new System.ArgumentException("Fluid emitter audio must disable Play On Awake.");
            if (Component is AudioLoop Loop && (!Loop.loop
                || !TransformOwnership.Contains(EmitterModel.transform, Loop.loop.transform)))
                throw new System.ArgumentException("Fluid emitter audio loop must own its AudioSource.");
            if (Component is Light EmitterLight && EmitterLight.bakingOutput.lightmapBakeType != LightmapBakeType.Realtime)
                throw new System.ArgumentException("Fluid emitter lights must use realtime lighting.");
        }
    }

    public void ValidateAuthoredSurface(PllFluidBehavior Behavior)
    {
        ValidateContactArrays();
        if (Form != PllNativeResolverForm.Source || SurfaceRenderer
            || SurfaceRenderers.Count is < 1 or > 256 || SurfaceSlots.Count != SurfaceRenderers.Count)
            throw new System.ArgumentException("Authored Surface needs one ordered renderer/slot list.");
        var Slots = new System.Collections.Generic.HashSet<(MeshRenderer, int)>();
        var Renderers = new System.Collections.Generic.Dictionary<MeshRenderer, (Material[] Materials, int Count)>();
        for (var i = 0; i < SurfaceRenderers.Count; i++)
        {
            var Renderer = SurfaceRenderers[i]; var Slot = SurfaceSlots[i];
            if (!Renderer) throw new System.ArgumentException("Authored Surface renderer is missing.");
            if (!Renderers.TryGetValue(Renderer, out var Entry))
            {
                var Filter = Renderer.GetComponent<MeshFilter>();
                if (!Renderer.enabled || Renderer.forceRenderingOff || !TransformOwnership.Contains(transform, Renderer.transform)
                    || !Filter || !Filter.sharedMesh)
                    throw new System.ArgumentException("Authored Surface has a hidden, unowned or missing static mesh.");
                PllNativeFlowSettings.ValidateTransform(Renderer.transform);
                if (Behavior == PllFluidBehavior.Lava && ContactMode == PllFluidContactMode.Automatic)
                    ValidateTriangles(Filter.sharedMesh);
                Entry = (Renderer.sharedMaterials, 0);
            }
            if (Slot < 0 || Slot >= Entry.Materials.Length || !Entry.Materials[Slot]
                || !Slots.Add((Renderer, Slot)))
                throw new System.ArgumentException("Authored Surface material slot is missing or duplicated.");
            Renderers[Renderer] = (Entry.Materials, Entry.Count + 1);
        }
        foreach (var Entry in Renderers.Values)
            if (Entry.Count != Entry.Materials.Length)
                throw new System.ArgumentException("Fluid renderer must declare every material slot. Separate non-fluid geometry.");
        if (ContactMode is not (PllFluidContactMode.Automatic or PllFluidContactMode.Authored)
            || Behavior == PllFluidBehavior.VisualOnly && (ContactRegionCount != 0 || ContactProxies.Count != 0)
            || Behavior == PllFluidBehavior.Water && ContactProxies.Count != 0
            || Behavior == PllFluidBehavior.Lava && ContactRegionCount != 0
            || ContactMode == PllFluidContactMode.Automatic && (ContactRegionCount != 0 || ContactProxies.Count != 0)
            || ContactMode == PllFluidContactMode.Authored && Behavior == PllFluidBehavior.Water && ContactRegionCount == 0
            || ContactMode == PllFluidContactMode.Authored && Behavior == PllFluidBehavior.Lava && ContactProxies.Count == 0)
            throw new System.ArgumentException("Authored Surface contact selection has a conflicting or missing payload.");
        for (var i = 0; i < ContactRegionCount; i++)
        {
            var Frame = ContactFrames[i];
            if (!Frame || !TransformOwnership.Contains(transform, Frame))
                throw new System.ArgumentException("Water contact frame is unowned.");
            PllNativeFlowSettings.ValidateTransform(Frame);
            if (PllAuthoringValueContract.ZoneVolumeFailure(ContactRegion(i)) is { } Failure)
                throw new System.ArgumentException(Failure.Message);
        }
        var Proxies = new System.Collections.Generic.HashSet<Collider>();
        foreach (var Proxy in ContactProxies)
        {
            if (!Proxy || Proxy.isTrigger || !Proxy.enabled || Proxy.attachedRigidbody
                || !TransformOwnership.Contains(transform, Proxy.transform) || !Proxies.Add(Proxy)
                || Proxy is not (BoxCollider or SphereCollider or CapsuleCollider or MeshCollider)
                || Proxy is MeshCollider MeshProxy && (MeshProxy.convex || !MeshProxy.sharedMesh))
                throw new System.ArgumentException("Lava contact proxy must be a unique owned static solid collider.");
            PllNativeFlowSettings.ValidateTransform(Proxy.transform);
            switch (Proxy)
            {
                case BoxCollider Box:
                    ValidateVolume(PllZoneShape.Box, Box.center, Box.size, 0f); break;
                case SphereCollider Sphere:
                    ValidateVolume(PllZoneShape.Sphere, Sphere.center, Vector3.zero, Sphere.radius); break;
                case CapsuleCollider Capsule:
                    ValidateVolume(PllZoneShape.Sphere, Capsule.center, Vector3.zero, Capsule.radius);
                    if (Capsule.direction is < 0 or > 2 || Capsule.height < Capsule.radius * 2f
                        || !PllAuthoringValueContract.CanEncode(Capsule.height))
                        throw new System.ArgumentException("Lava capsule height or direction is invalid.");
                    break;
                case MeshCollider Mesh: ValidateTriangles(Mesh.sharedMesh); break;
            }
        }

        static void ValidateVolume(PllZoneShape Shape, Vector3 Center, Vector3 Size, float Radius)
        {
            if (PllAuthoringValueContract.ZoneVolumeFailure(Shape, Center, Size, Radius) is { } Failure)
                throw new System.ArgumentException(Failure.Message);
        }
        static void ValidateTriangles(Mesh Mesh)
        {
            if (Mesh.vertexCount < 3 || Mesh.subMeshCount == 0)
                throw new System.ArgumentException("Lava contact mesh has no triangle surface.");
            for (var i = 0; i < Mesh.subMeshCount; i++)
                if (Mesh.GetTopology(i) != MeshTopology.Triangles || Mesh.GetIndexCount(i) == 0)
                    throw new System.ArgumentException("Lava contact mesh requires nonempty triangle submeshes.");
        }
    }

    public PllNativeFlowSettings SourceSettings
    {
        get
        {
#if PLL_CREATOR_SDK
            var Settings = SourceSettingsValue;
#else
            var Settings = ReadBundleSourceSettings();
#endif
            if (Settings.DetailsAlongSpline is { Length: 0 }) Settings.DetailsAlongSpline = null;
            return Settings;
        }
    }
#if PLL_CREATOR_SDK
    public PllPresentationTransition Transition => TransitionValue;
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public float LavaHeatRate => LavaHeatRateValue;
    public float LavaHeat => LavaHeatValue;
    public bool LavaIsKiln => LavaIsKilnValue;
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => volume;

    internal void SealBundleSerialization()
    {
        BundleShape = volume.Shape; BundleCenter = volume.Center;
        BundleSize = volume.Size; BundleRadius = volume.Radius;
        BundleCoverDelaySeconds = Transition.CoverDelaySeconds;
        var Settings = SourceSettings;
        BundleFlowSpawnVelocity = Settings.SpawnVelocity; BundleFlowGravity = Settings.Gravity;
        BundleFlowWallStick = Settings.WallStick; BundleFlowDrag = Settings.Drag; BundleFlowTimeStep = Settings.TimeStep;
        BundleFlowMaxSteps = Settings.MaxSteps; BundleFlowMaxLength = Settings.MaxLength;
        BundleFlowPreferredDistancePerFrame = Settings.PreferredDistancePerFrame;
        BundleFlowSpawnLights = Settings.SpawnLights; BundleFlowGenerateSplash = Settings.GenerateSplash;
        BundleFlowGenerateAlongSpline = Settings.GenerateAlongSpline; BundleFlowGenerateDetails = Settings.GenerateDetails;
        BundleFlowEndRock = Settings.EndRock; BundleFlowSplash = Settings.Splash; BundleFlowSpawnAlongSpline = Settings.SpawnAlongSpline;
        BundleFlowDetailsAlongSpline = Settings.DetailsAlongSpline; BundleFlowStepsAlongSpline = Settings.StepsAlongSpline;
        BundleFlowRadius = Settings.Radius; BundleFlowSegmentsPerUnit = Settings.SegmentsPerUnit;
        BundleFlowSides = Settings.Sides; BundleFlowCapped = Settings.Capped;
        BundleFlowRayStart = Settings.RayStart; BundleFlowRayEnd = Settings.RayEnd; BundleFlowEndpoint = Settings.Endpoint;
        BundleFlowLandingPolicy = Settings.LandingPolicy; BundleFlowMaterial = Settings.Material;
        BundleFlowWaterForce = Settings.WaterForce; BundleFlowWaterDrag = Settings.WaterDrag;
        BundleFlowWaterRagdollLength = Settings.WaterRagdollLength; BundleFlowWaterRagdoll = Settings.WaterRagdoll;
        BundleFlowEnablePusher = Settings.EnablePusher; BundleFlowPusherFallTime = Settings.PusherFallTime;
        BundleFlowPusherKnockback = Settings.PusherKnockback; BundleFlowPusherDownwardKnockback = Settings.PusherDownwardKnockback;
        BundleFlowPusherCooldown = Settings.PusherCooldown; BundleFlowPusherSfx = Settings.PusherSfx;
        BundleLandingModel = Settings.LandingModel; BundleSplashPresentation = Settings.SplashPresentation;
        BundleSamplePresentation = Settings.SamplePresentation; BundleDetailPresentation = Settings.DetailPresentation;
    }

    public void ValidateBundleSerialization()
    {
        var Compiled = ReadBundleSourceSettings(); var Authored = SourceSettings;
        if (Compiled.LandingModel != Authored.LandingModel || Compiled.SplashPresentation != Authored.SplashPresentation
            || Compiled.SamplePresentation != Authored.SamplePresentation || Compiled.DetailPresentation != Authored.DetailPresentation)
            throw new System.InvalidOperationException("Native flow compiled presentation changed.");
        var CompiledDetails = Compiled.DetailsAlongSpline; var AuthoredDetails = Authored.DetailsAlongSpline;
        Compiled.DetailsAlongSpline = null; Authored.DetailsAlongSpline = null;
        if (!Compiled.Equals(Authored) || !BundleCoverDelaySeconds.Equals(Transition.CoverDelaySeconds)
            || (CompiledDetails?.Length ?? 0) != (AuthoredDetails?.Length ?? 0))
            throw new System.InvalidOperationException("Native flow compiled settings changed.");
        for (var i = 0; i < (CompiledDetails?.Length ?? 0); i++)
            if (CompiledDetails![i] != AuthoredDetails![i])
                throw new System.InvalidOperationException("Native flow compiled detail input changed.");
    }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
#endif

    PllNativeFlowSettings ReadBundleSourceSettings() => new()
    {
        SpawnVelocity = BundleFlowSpawnVelocity, Gravity = BundleFlowGravity,
        WallStick = BundleFlowWallStick, Drag = BundleFlowDrag, TimeStep = BundleFlowTimeStep,
        MaxSteps = BundleFlowMaxSteps, MaxLength = BundleFlowMaxLength,
        PreferredDistancePerFrame = BundleFlowPreferredDistancePerFrame,
        SpawnLights = BundleFlowSpawnLights, GenerateSplash = BundleFlowGenerateSplash,
        GenerateAlongSpline = BundleFlowGenerateAlongSpline, GenerateDetails = BundleFlowGenerateDetails,
        EndRock = BundleFlowEndRock, Splash = BundleFlowSplash, SpawnAlongSpline = BundleFlowSpawnAlongSpline,
        DetailsAlongSpline = BundleFlowDetailsAlongSpline, StepsAlongSpline = BundleFlowStepsAlongSpline,
        Radius = BundleFlowRadius, SegmentsPerUnit = BundleFlowSegmentsPerUnit,
        Sides = BundleFlowSides, Capped = BundleFlowCapped,
        RayStart = BundleFlowRayStart, RayEnd = BundleFlowRayEnd, Endpoint = BundleFlowEndpoint,
        LandingPolicy = BundleFlowLandingPolicy, Material = BundleFlowMaterial,
        WaterForce = BundleFlowWaterForce, WaterDrag = BundleFlowWaterDrag,
        WaterRagdollLength = BundleFlowWaterRagdollLength, WaterRagdoll = BundleFlowWaterRagdoll,
        EnablePusher = BundleFlowEnablePusher, PusherFallTime = BundleFlowPusherFallTime,
        PusherKnockback = BundleFlowPusherKnockback, PusherDownwardKnockback = BundleFlowPusherDownwardKnockback,
        PusherCooldown = BundleFlowPusherCooldown, PusherSfx = BundleFlowPusherSfx,
        LandingModel = BundleLandingModel, SplashPresentation = BundleSplashPresentation,
        SamplePresentation = BundleSamplePresentation, DetailPresentation = BundleDetailPresentation
    };
    public MeshRenderer? SurfaceRenderer => surfaceRenderer;
    public Material? GeneratedSurfaceMaterial => generatedSurfaceMaterial;
    public PllEffectAuthoring? Presentation => presentation;
    public float MovementModifierShallow => movementModifierShallow;
    public float MovementModifierDeep => movementModifierDeep;
    public float AnimModifierShallow => animModifierShallow;
    public float AnimModifierDeep => animModifierDeep;
}
