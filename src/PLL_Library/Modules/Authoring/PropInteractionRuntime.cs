using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Library;

internal sealed class PropInteractionRuntime : IDisposable
{
    readonly PropInteractionContract Recipe;
    readonly Transform? Intact, Broken, Pivot;
    readonly Quaternion RestRotation;
    readonly Collider[] Supports;
    readonly ParticleSystem? Particles;
    readonly PllVineAuthoring? Definition;
    readonly PllPropInteractionDriver Driver;
    readonly Rigidbody? Body;
    readonly bool OwnsBody;
    readonly Action<PropInteractionState> Publish;
    readonly Func<bool> IsAuthority, IsReady;
    readonly Func<double> Clock;
    readonly Dictionary<Character, double> Contacts = new();
    readonly List<Character> ExpiredContacts = new();
    readonly List<CollisionModifier> Modifiers = new();
    readonly GameObject? NativeRoot;
    readonly JungleVine? Vine;
    readonly Material? NativeMaterial;
    readonly MeshRenderer? NativeRenderer;
    readonly PropBridgeResponse Response;
    readonly AudioSource? BreakAudio;
    readonly float WarningBaseVolume, WarningBasePitch;
    PropInteractionState State;
    enum Lifetime { Prepared, Admitted, Suspended, Retired }
    Lifetime StateLifetime;
    bool HasState, FallPresented, WarningStartPending, BreakProjected, PendingBreakEffects;
    GamefeelHandler? Feedback;
    double LocalTouch = double.NegativeInfinity, NextClimbingFeedback;

    internal PropInteractionRuntime(PropInteractionContract Recipe, Transform Root,
        Transform? Intact, Transform? Broken, Transform? Pivot, Collider[] Supports, ParticleSystem? Particles,
        Transform? Start, Transform? End, NativeVineRecipes.Recipe? Native,
        Action<PropInteractionState> Publish, Func<bool> IsAuthority, Func<bool> IsReady, Func<double> Clock,
        IReadOnlyList<TreePlatform>? Platforms, Transform? ConnectionScope)
    {
        this.Recipe = Recipe; this.Intact = Intact; this.Broken = Broken;
        this.Pivot = Pivot; this.Supports = Supports; this.Particles = Particles;
        this.Publish = Publish; this.IsAuthority = IsAuthority; this.IsReady = IsReady; this.Clock = Clock;
        if ((Recipe.Vine.HasValue || Recipe.HasMotion && Recipe.Motion == PllPropMotion.PhysicalPivot)
            && Root.lossyScale != Vector3.one)
            throw new InvalidOperationException("Placed native span or Physical Pivot must retain a unit-scaled source frame.");
        if (Recipe.Vine.HasValue)
        {
            if (Native is null) throw new InvalidOperationException("Vine recipe has not resolved its native provider.");
            if (!Root.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Native vine preparation needs an active owned runtime parent.");
            Definition = Root.GetComponent<PllVineAuthoring>();
            if (!Definition) throw new InvalidOperationException("Placed vine lost its authoring definition.");
            var From = Start ? Start.position : Root.position; var To = End ? End.position : Root.position;
            var Mid = Vector3.zero;
            if (Recipe.Endpoints == PllVineEndpoints.AuthoredAnchors)
            {
                var Distance = Vector3.Distance(From, To);
                if (Distance < Recipe.MinimumDistance || Distance > Recipe.MaximumDistance
                    || Mathf.Abs(To.y - From.y) > Recipe.MaximumHeightDifference
                    || !JungleVine.CheckVinePath(From, To, -Recipe.Hang, out Mid, 0f))
                    throw new InvalidOperationException("Vine endpoints do not admit a clear supported connection.");
            }
            else if (Platforms is null || Platforms.Count < 2 || !ConnectionScope)
                throw new InvalidOperationException("Native platform connection needs a prepared owned platform scope.");
            NativeRoot = new GameObject("PLL_Vine_" + Recipe.Id); NativeRoot.SetActive(false);
            Driver = NativeRoot.AddComponent<PllPropInteractionDriver>(); Driver.Owner = this;
            try
            {
                Vine = Native.Create(Root, Driver, Recipe.Hang, Recipe.ColliderOffset, Recipe.BoxShape,
                    Recipe.CustomSpan, Vector3.Distance(Recipe.ModelStart, Recipe.ModelEnd), out var Material);
                NativeMaterial = Material; Response = Recipe.BridgeResponse(Native);
                NativeRenderer = NativeRoot.GetComponentInChildren<MeshRenderer>(true);
                if (Driver.WarningAudio) Driver.WarningAudio.clip = Response.WarningClip;
                if (Driver.WarningAudio)
                {
                    WarningBaseVolume = Driver.WarningAudio.volume; WarningBasePitch = Driver.WarningAudio.pitch;
                }
                Vine.minDist = Recipe.MinimumDistance; Vine.maxDist = Recipe.MaximumDistance;
                Vine.maxHeightDifference = Recipe.MaximumHeightDifference;
                NativeRoot.SetActive(true);
                if (Recipe.Endpoints == PllVineEndpoints.AuthoredAnchors)
                {
                    NativeRoot.transform.position = From;
                    Vine.ForceBuildVine(From, To, -Recipe.Hang, Mid);
                }
                else NativeVinePlatformConnection.Build(Vine, Platforms!, ConnectionScope!);
                this.Supports = Vine.colliderRoot.GetComponentsInChildren<Collider>(true);
                if (Recipe.Vine == PllVineProfile.HangingBridge && (Recipe.HasBreak || Recipe.HasMotion))
                    foreach (var Collider in this.Supports)
                    {
                        var Modifier = Collider.gameObject.AddComponent<CollisionModifier>();
                        Modifier.applyEffects = false; Modifier.onCollide += NativeContact;
                        Modifiers.Add(Modifier);
                    }
                Definition!.Binding = this;
            }
            catch
            {
                if (NativeMaterial) Object.DestroyImmediate(NativeMaterial);
                Object.DestroyImmediate(NativeRoot);
                throw;
            }
        }
        else
        {
            var DriverRoot = Recipe.Motion == PllPropMotion.PhysicalPivot && Recipe.HasMotion ? Pivot! : Root;
            if (DriverRoot.GetComponent<PllPropInteractionDriver>())
                throw new InvalidOperationException("Prop interaction root already has an instance owner.");
            Driver = DriverRoot.gameObject.AddComponent<PllPropInteractionDriver>(); Driver.Owner = this;
            if (Recipe.HasBreak || Recipe.HasMotion && Recipe.Motion == PllPropMotion.PhysicalPivot)
            {
                Body = DriverRoot.GetComponent<Rigidbody>(); OwnsBody = !Body;
                if (OwnsBody) Body = DriverRoot.gameObject.AddComponent<Rigidbody>();
                Body!.isKinematic = true; Body.useGravity = false;
                Body.interpolation = RigidbodyInterpolation.Interpolate;
            }
            if (!Recipe.HasBreak && Recipe.HasMotion && Recipe.Motion == PllPropMotion.PhysicalPivot)
                this.Supports = DriverRoot.GetComponentsInChildren<Collider>(true);
            if (Pivot) RestRotation = Pivot.localRotation;
        }
        if (Recipe.BreakClip)
        {
            BreakAudio = Driver.gameObject.AddComponent<AudioSource>();
            BreakAudio.playOnAwake = false; BreakAudio.spatialBlend = 1f; BreakAudio.clip = Recipe.BreakClip;
        }
        SetSupport(false);
    }

    internal PropInteractionState Current => HasState ? State
        : throw new InvalidOperationException("Prop interaction has not adopted an authoritative state.");
    internal Photon.Pun.PhotonView? NativeView => Vine ? Vine.photonView : null;
    internal bool Owns(JungleVine Source) => Vine == Source;
    internal bool OwnsRuntimeComponent(Component Component)
    {
        if (Component == Driver || OwnsBody && Component == Body || Component == BreakAudio) return true;
        if (!NativeRoot || !TransformOwnership.Contains(NativeRoot.transform, Component.transform)) return false;
        return Component is Transform or MeshRenderer or MeshFilter or Collider or Photon.Pun.PhotonTransformView
            || Component == Vine || Component == NativeView || Component == Driver.Bridge
            || Component == Driver.WarningAudio || Component is CollisionModifier Modifier && Modifiers.Contains(Modifier);
    }
    internal bool TryNativeRenderer(Renderer Renderer, out Mesh Mesh, out Material Material)
    {
        Mesh = null!; Material = null!;
        if (Renderer != NativeRenderer) return false;
        Mesh = NativeRenderer!.GetComponent<MeshFilter>().sharedMesh; Material = NativeMaterial!; return true;
    }
    internal void WriteRealizedOutput(BinaryWriter Writer, Transform OccurrenceRoot)
    {
        Writer.Write("PLL/PropRealized/v1"); Writer.Write(Vine ? true : false);
        if (!Vine) return;
        ManifestEncoding.WritePose(Writer, new Pose(OccurrenceRoot.InverseTransformPoint(Vine.transform.position),
            Quaternion.Inverse(OccurrenceRoot.rotation) * Vine.transform.rotation));
        Writer.Write(Supports.Length);
        foreach (var Collider in Supports)
        {
            ManifestEncoding.WritePose(Writer, new Pose(OccurrenceRoot.InverseTransformPoint(Collider.transform.position),
                Quaternion.Inverse(OccurrenceRoot.rotation) * Collider.transform.rotation));
            if (Collider is BoxCollider Box) ManifestEncoding.WriteVector(Writer, Box.size);
            else if (Collider is CapsuleCollider Capsule)
            { ManifestEncoding.WriteScalar(Writer, Capsule.radius); ManifestEncoding.WriteScalar(Writer, Capsule.height); }
            else throw new InvalidOperationException("Native vine generated an unsupported collider recipe.");
        }
    }
    internal static bool OwnsNativeGeometry(Transform Source)
    {
        var Driver = Source.GetComponentInParent<PllPropInteractionDriver>(true);
        return Driver && Driver.Owner is { NativeRoot: { } Root } && Root
            && TransformOwnership.Contains(Root.transform, Source);
    }
    internal bool Admitted => StateLifetime == Lifetime.Admitted && IsReady();
    internal bool Interactible => Admitted && !State.IsBroken;
    internal bool RequiresFeedback => Recipe.HasMotion && Response.NeedsFeedback;
    internal void Admit(GamefeelHandler? Feedback = null)
    {
        if (StateLifetime == Lifetime.Retired || !HasState || !IsReady() || RequiresFeedback && !Feedback)
            throw new InvalidOperationException("Prop admission requires an adopted state and a ready output owner.");
        this.Feedback = Feedback; StateLifetime = Lifetime.Admitted;
        if (Pivot && Recipe.Motion == PllPropMotion.PhysicalPivot)
            Body!.rotation = Pivot.parent.rotation * RestRotation * Quaternion.AngleAxis(Angle(Clock()), Recipe.Axis);
        ApplyProjection();
    }
    internal (string BreakHook, string MotionHook) Adopt(PropInteractionState Value, bool Replay)
    {
        if (StateLifetime == Lifetime.Retired) return (string.Empty, string.Empty);
        if (HasState && State.IsBroken && !Value.IsBroken)
            throw new InvalidOperationException("A broken prop instance cannot become intact.");
        if (HasState && Value.IntentRevision < State.IntentRevision)
            throw new InvalidOperationException($"Prop intent receipt moved backwards from {State.IntentRevision} to {Value.IntentRevision}.");
        if (HasState && Value.BornAt != State.BornAt)
            throw new InvalidOperationException($"Prop birth changed from {State.BornAt:R} to {Value.BornAt:R}.");
        if (HasState && (State.MotionBaseline.HasValue && !Nullable.Equals(Value.MotionBaseline, State.MotionBaseline)
            || !State.MotionBaseline.HasValue && Value.MotionBaseline.HasValue && Value.IntentRevision == State.IntentRevision))
            throw new InvalidOperationException("Prop motion baseline changed."
                + (State.MotionBaseline is { } Expected
                    ? $" Expected active time {Expected.ActiveSeconds:R}, phase {Expected.Phase:R};" : " Expected no baseline;")
                + (Value.MotionBaseline is { } Actual
                    ? $" received active time {Actual.ActiveSeconds:R}, phase {Actual.Phase:R}." : " received no baseline."));
        var NewlyBroken = HasState && !State.IsBroken && Value.IsBroken;
        var WasAdmitted = Admitted;
        var MotionChanged = HasState && !Value.IsBroken && State.MotionRunning != Value.MotionRunning;
        WarningStartPending = !Replay && Recipe.HasMotion && Value.BreakPhase == PropBreakPhase.Warning
            && (!HasState || State.BreakPhase != PropBreakPhase.Warning);
        State = Value; HasState = true;
        if (Driver.Bridge) Driver.Bridge.holdsPeople = State.Capacity;
        PendingBreakEffects |= NewlyBroken && !Replay;
        ApplyProjection();
        return Replay || !WasAdmitted ? (string.Empty, string.Empty)
            : (NewlyBroken ? Recipe.BreakHook : string.Empty,
                MotionChanged ? State.MotionRunning ? Recipe.StartedHook : Recipe.PausedHook : string.Empty);
    }
    void ApplyProjection()
    {
        if (!Admitted) return;
        if (State.IsBroken && !BreakProjected)
        {
            BreakProjected = true;
            SetSupport(false); Contacts.Clear();
            if (Vine && Vine.colliderRoot) Object.DestroyImmediate(Vine.colliderRoot.gameObject);
            if (Intact) Intact.gameObject.SetActive(false);
            if (Broken) Broken.gameObject.SetActive(true);
            if (PendingBreakEffects)
            {
                PendingBreakEffects = false;
                if (Particles) Particles.Play(true);
                if (BreakAudio) BreakAudio.Play();
            }
        }
        else SetSupport(Interactible);
        ApplyWarning(); Present();
    }

    internal void RequestBreak()
    {
        if (Admitted && IsAuthority() && Recipe.HasBreak && State.BreakEnabled && !State.IsBroken)
            Publish(State.WithBreak(PropBreakPhase.Fallen, Clock()));
    }
    internal void Tick(bool Physics)
    {
        if (!Admitted) return;
        var Now = Clock();
        if (Physics && Pivot && Recipe.Motion == PllPropMotion.PhysicalPivot && !State.IsBroken)
        {
            var Local = RestRotation * Quaternion.AngleAxis(Angle(Now), Recipe.Axis);
            Body!.MoveRotation(Pivot.parent.rotation * Local);
        }
        if (Physics && IsAuthority() && !State.IsBroken
            && (Recipe.Vine == PllVineProfile.HangingBridge && (Recipe.HasBreak || Recipe.HasMotion)
                || Recipe.HasBreak && Recipe.Trigger == PllPropBreakTrigger.SupportLoad))
        {
            ExpiredContacts.Clear(); var People = 0;
            foreach (var Contact in Contacts)
                if (!Contact.Key || Now - Contact.Value >= 0.25) ExpiredContacts.Add(Contact.Key!);
                else People++;
            foreach (var Character in ExpiredContacts) Contacts.Remove(Character);
            if (Driver.Bridge) Driver.Bridge.peopleOnBridge = People;
            if (State.BreakPhase == PropBreakPhase.Intact && People > State.Capacity
                && (State.BreakEnabled && Recipe.HasBreak || Recipe.HasMotion && Recipe.Motion == PllPropMotion.NativeBridgeResponse))
                Publish(State.WithBreak(Recipe.Vine is null && State.BreakDelaySeconds == 0f
                    ? PropBreakPhase.Fallen : PropBreakPhase.OverloadDelay, Now));
            else if (State.BreakPhase == PropBreakPhase.OverloadDelay)
            {
                if (Recipe.Vine is null && People <= State.Capacity) Publish(State.WithBreak(PropBreakPhase.Intact, Now));
                else if (Now - State.BreakOrigin >= State.BreakDelaySeconds)
                    Publish(State.WithBreak(Recipe.Vine.HasValue ? PropBreakPhase.Warning : PropBreakPhase.Fallen, Now));
            }
            else if (State.BreakPhase == PropBreakPhase.Warning && Now - State.BreakOrigin >= Response.WarningSeconds)
                Publish(State.WithBreak(Recipe.HasBreak && State.BreakEnabled ? PropBreakPhase.Fallen : PropBreakPhase.Intact, Now));
        }
        if (!Physics && (State.MotionRunning || State.IsBroken && !FallPresented
            || State.BreakPhase == PropBreakPhase.Warning)) Present();
    }

    internal void Collision(Collision Collision)
    {
        if (!Admitted || !IsAuthority() || State.IsBroken) return;
        if (Recipe.HasBreak && State.BreakEnabled && Recipe.Trigger == PllPropBreakTrigger.Impact
            && Collision.relativeVelocity.sqrMagnitude >= Recipe.ImpactSpeed * Recipe.ImpactSpeed)
        { RequestBreak(); return; }
        if (!Recipe.HasBreak || Recipe.Trigger != PllPropBreakTrigger.SupportLoad) return;
        var Character = Collision.collider.GetComponentInParent<Character>();
        if (Character) Contacts[Character] = Clock();
    }
    void NativeContact(Character Character, CollisionModifier Modifier, Collision Collision, Bodypart Bodypart)
    {
        if (!Admitted || State.IsBroken || !Character) return;
        var Now = Clock();
        if (Character.IsLocal) LocalTouch = Now;
        if (IsAuthority()) Contacts[Character] = Now;
    }

    float Phase(double Now) => State.MotionRunning
        ? PropInteractionState.NormalizePhase(State.MotionPhase + (Now - State.MotionOrigin) / Recipe.Period)
        : State.MotionPhase;
    float Angle(double Now) => Recipe.Center + Recipe.Amplitude * Mathf.Sin(2f * Mathf.PI * Phase(Now));
    void Present()
    {
        if (!HasState || StateLifetime == Lifetime.Retired) return;
        var Now = Clock();
        if (Pivot && Recipe.Motion == PllPropMotion.VisualPivot && !State.IsBroken)
            Pivot.localRotation = RestRotation * Quaternion.AngleAxis(Angle(Now), Recipe.Axis);
        if (NativeMaterial && State.IsBroken && !FallPresented)
        {
            var Progress = Mathf.Clamp01((float)(Now - State.BreakOrigin) * 0.7f);
            NativeMaterial.SetFloat("_BreakAmount", Progress); FallPresented = Progress == 1f;
        }
        if (Admitted && Recipe.HasMotion && State.BreakPhase == PropBreakPhase.Warning
            && Now - LocalTouch < 0.2 && Now >= NextClimbingFeedback && Response.ClimbingAmount > 0f)
        {
            Feedback!.AddPerlinShake(Response.ClimbingAmount, 0.2f, 15f);
            NextClimbingFeedback = Now + Response.ClimbingInterval;
        }
        if (Admitted && State.BreakPhase == PropBreakPhase.Warning && Driver.WarningAudio)
        {
            var Gain = (float)(Now - State.BreakOrigin) * 0.1f;
            Driver.WarningAudio.volume = Mathf.Clamp01(WarningBaseVolume + Gain);
            Driver.WarningAudio.pitch = Mathf.Clamp(WarningBasePitch + Gain, -3f, 3f);
        }
    }
    void ApplyWarning()
    {
        if (!NativeMaterial) return;
        var Warning = State.BreakPhase == PropBreakPhase.Warning && Admitted;
        NativeMaterial.SetFloat("_JitterAmount", Warning && Recipe.HasMotion ? 1f : 0f);
        if (Driver.WarningAudio)
        {
            if (Warning && Driver.WarningAudio.clip && !Driver.WarningAudio.isPlaying)
                Driver.WarningAudio.Play();
            else if (!Warning && Driver.WarningAudio.isPlaying) Driver.WarningAudio.Stop();
        }
        if (Warning && WarningStartPending)
        {
            WarningStartPending = false;
            var LocalCharacter = Character.localCharacter;
            if (LocalCharacter && LocalCharacter.IsLocal && Response.StartAmount > 0f
                && Vector3.Distance(LocalCharacter.Center, Driver.transform.position) < Response.StartDistance)
                Feedback!.AddPerlinShake(Response.StartAmount, 0.2f, 15f);
        }
    }

    void SetSupport(bool Enabled)
    {
        foreach (var Support in Supports) if (Support) Support.enabled = Enabled;
    }
    internal void Suspend()
    {
        if (StateLifetime != Lifetime.Admitted) return;
        StateLifetime = Lifetime.Suspended; SetSupport(false); Contacts.Clear();
        ApplyWarning();
        if (Body) Body.detectCollisions = false;
    }
    internal void Resume()
    {
        if (StateLifetime != Lifetime.Suspended || !IsReady()) return;
        StateLifetime = Lifetime.Admitted; if (Body) Body.detectCollisions = true;
        ApplyProjection();
    }
    public void Dispose() => Release(false);
    internal void Destroyed() => Release(true);
    void Release(bool RootDestroying)
    {
        if (StateLifetime == Lifetime.Retired) return;
        StateLifetime = Lifetime.Retired;
        SetSupport(false);
        Contacts.Clear();
        foreach (var Modifier in Modifiers)
            if (Modifier) Modifier.onCollide -= NativeContact;
        Driver.Owner = null;
        if (!RootDestroying && NativeRoot)
        {
            NativeFogRenderRuntime.RetireRoot(NativeRoot);
            Object.DestroyImmediate(NativeRoot);
        }
        else if (RootDestroying && NativeRoot)
        {
            NativeFogRenderRuntime.RetireRoot(NativeRoot);
            Object.Destroy(NativeRoot);
        }
        else if (!RootDestroying)
        {
            if (BreakAudio) Object.DestroyImmediate(BreakAudio);
            if (OwnsBody && Body) Object.DestroyImmediate(Body);
            if (Driver) Object.DestroyImmediate(Driver);
        }
        else if (!NativeRoot)
        {
            if (BreakAudio) Object.Destroy(BreakAudio);
            if (OwnsBody && Body) Object.Destroy(Body);
        }
        if (NativeMaterial) Object.DestroyImmediate(NativeMaterial);
    }
}

internal sealed class PllPropInteractionDriver : MonoBehaviour
{
    internal PropInteractionRuntime? Owner;
    internal BreakableBridge? Bridge;
    internal AudioSource? WarningAudio;
    void Update() => Owner?.Tick(false);
    void FixedUpdate() => Owner?.Tick(true);
    void OnCollisionEnter(Collision Collision) => Owner?.Collision(Collision);
    void OnCollisionStay(Collision Collision) => Owner?.Collision(Collision);
    void OnDisable() => Owner?.Suspend();
    void OnEnable() => Owner?.Resume();
    void OnDestroy() => Owner?.Destroyed();
}

internal static class PropInteractionNativeGates
{
    [HarmonyPatch(typeof(BreakableBridge))]
    static class Bridge
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var Name in new[] { "Awake", "NetworkStart", "Update", "FixedUpdate", "OnBridgeCollision",
                "ShakeBridge_Rpc", "Fall_Rpc", "SyncHoldsPeopleRPC", "OnPlayerEnteredRoom" })
                yield return AccessTools.DeclaredMethod(typeof(BreakableBridge), Name)
                    ?? throw new MissingMethodException(typeof(BreakableBridge).FullName, Name);
        }
        [HarmonyPrefix]
        static bool Prefix(BreakableBridge __instance) => !__instance.GetComponent<PllPropInteractionDriver>()
            && !__instance.GetComponentInParent<PllVineAuthoring>(true);
    }
    [HarmonyPatch(typeof(JungleVine), nameof(JungleVine.IsInteractible))]
    static class Interaction
    {
        [HarmonyPrefix]
        static bool Prefix(JungleVine __instance, ref bool __result)
        {
            if (!__instance.TryGetComponent<PllPropInteractionDriver>(out var Driver))
            {
                var Definition = __instance.GetComponentInParent<PllVineAuthoring>(true);
                if (!Definition) return true;
                if (Definition.Binding is { Interactible: true } Owner && Owner.Owns(__instance)) return true;
                __result = false; return false;
            }
            if (Driver.Owner is { Interactible: true }) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(JungleVine), nameof(JungleVine.Interact))]
    static class Interact
    {
        [HarmonyPrefix]
        static bool Prefix(JungleVine __instance)
        {
            if (__instance.TryGetComponent<PllPropInteractionDriver>(out var Driver)) return Driver.Owner is { Interactible: true };
            var Definition = __instance.GetComponentInParent<PllVineAuthoring>(true);
            return !Definition || Definition.Binding is { Interactible: true } Owner && Owner.Owns(__instance);
        }
    }
    [HarmonyPatch(typeof(JungleVine), nameof(JungleVine.ForceBuildVine_RPC))]
    static class BuildRpc
    {
        [HarmonyPrefix]
        static bool Prefix(JungleVine __instance) => !__instance.GetComponent<PllPropInteractionDriver>()
            && !__instance.GetComponentInParent<PllVineAuthoring>(true);
    }
}
