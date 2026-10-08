using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class PropInteractionContract
{
    readonly TransformBinding Root;
    readonly AuthoringBindingOwner BindingOwner;
    readonly TransformBinding? Intact, Broken, Pivot, StartAnchor, EndAnchor;
    readonly ComponentBinding<Collider>[] Supports;
    readonly ComponentBinding<ParticleSystem>? Particles;
    NativeVineRecipes.Recipe? Native;
    internal readonly bool HasBreak, InitiallyEnabled, HasMotion, InitiallyRunning;
    internal readonly PllPropBreakTrigger Trigger;
    internal readonly PllPropMotion Motion;
    internal readonly float ImpactSpeed, OverloadSeconds, Center, Amplitude, Period, InitialPhase;
    internal readonly Vector3 Axis;
    internal readonly int Capacity, NativeMaximumPeople;
    internal readonly PllBridgeCapacity CapacityPolicy;
    internal readonly AudioClip? BreakClip;
    internal readonly PllVineProfile? Vine;
    internal readonly PllVineEndpoints Endpoints;
    internal readonly float MinimumDistance, MaximumDistance, MaximumHeightDifference, Hang, ColliderOffset;
    internal readonly Vector2 BoxShape;
    internal readonly Mesh? CustomSpan;
    internal readonly Vector3 ModelStart, ModelEnd, ModelUp;
    internal readonly string BreakHook, StartedHook, PausedHook;
    readonly int SpanVersion;
    readonly PropBridgeResponse? CustomResponse;

    PropInteractionContract(AuthoringBindingOwner BindingOwner, Transform Owner, PllBreakablePropAuthoring? Break,
        PllSwingingPropAuthoring? Swing, PllVineAuthoring? Span)
    {
        this.BindingOwner = BindingOwner;
        var Template = BindingOwner.BindingRoot;
        Root = TransformBinding.Create(Template, Owner);
        SourceComponent = Span ? Span : Break ? Break : (Component)Swing!;
        Id = Span ? Span.Id : Break ? Break.Id : Swing!.Id;
        StablePath = DeterministicObjectIdentity.RelativeComponentPath(
            Span ? Span : Break ? Break : (Component)Swing!, Template, 0);
        HasBreak = Break; HasMotion = Swing; InitiallyEnabled = !Break || Break.InitiallyEnabled;
        BreakHook = Break ? Break.EmitHook : string.Empty;
        StartedHook = Swing ? Swing.StartedHook : string.Empty; PausedHook = Swing ? Swing!.PausedHook : string.Empty;
        InitiallyRunning = Swing && !Span && Swing.InitiallyRunning;
        Supports = new ComponentBinding<Collider>[Break && !Span ? Break.SupportColliders.Length : 0];
        if (Break)
        {
            Trigger = Break.Trigger; ImpactSpeed = Break.ImpactSpeed; Capacity = Break.Capacity;
            OverloadSeconds = Break.OverloadSeconds; NativeMaximumPeople = Break.NativeMaximumPeople;
            CapacityPolicy = Break.BridgeCapacity; BreakClip = Break.BreakClip ? Break.BreakClip : null;
            if (!Span) Intact = TransformBinding.Create(Template, Break.IntactModel.transform);
            if (Break.BrokenModel) Broken = TransformBinding.Create(Template, Break.BrokenModel.transform);
            if (Break.BreakParticles) Particles = ComponentBinding<ParticleSystem>.Create(Template, Break.BreakParticles);
            for (var i = 0; i < Supports.Length; i++) Supports[i] = ComponentBinding<Collider>.Create(Template, Break.SupportColliders[i]);
        }
        else { Capacity = 1; NativeMaximumPeople = 5; }
        if (Swing && !Span)
        {
            Pivot = TransformBinding.Create(Template, Swing.MotionRoot); Motion = Swing.Profile;
            Axis = Swing.Axis.normalized; Center = Swing.CenterDegrees; Amplitude = Swing.AmplitudeDegrees;
            Period = Swing.PeriodSeconds; InitialPhase = Swing.InitialPhase % 1f;
        }
        if (!Span) return;
        Vine = Span.Profile; Endpoints = Span.Endpoints;
        if (Endpoints == PllVineEndpoints.AuthoredAnchors)
        {
            StartAnchor = TransformBinding.Create(Template, Span.StartAnchor);
            EndAnchor = TransformBinding.Create(Template, Span.EndAnchor);
        }
        MinimumDistance = Span.MinimumDistance; MaximumDistance = Span.MaximumDistance;
        MaximumHeightDifference = Span.MaximumHeightDifference; Hang = Span.HangAmount;
        ColliderOffset = Span.ColliderOffset; BoxShape = Span.BoxShape;
        CustomSpan = Span.Appearance == PllVineAppearance.CustomSpan ? Span.SpanMesh : null;
        ModelStart = Span.ModelStart; ModelEnd = Span.ModelEnd; ModelUp = Span.ModelUp;
        SpanVersion = Span.BundleSpanVersion;
        Motion = PllPropMotion.NativeBridgeResponse;
        if (Swing && Swing.BridgeResponse == PllBridgeResponseProfile.Custom)
            CustomResponse = new PropBridgeResponse(Swing.WarningSeconds, Swing.WarningClip ? Swing.WarningClip : null,
                Swing.StartShakeDistance, Swing.StartShakeAmount, Swing.ClimbingShakeAmount, Swing.ClimbingShakeInterval);
    }

    internal string Id { get; }
    internal string StablePath { get; }
    internal bool Generated => BindingOwner.Generated;
    internal int CandidateIndex => BindingOwner.CandidateIndex;
    internal GameObject SourceRoot => BindingOwner.SourceRoot;
    internal Transform Source => Root.Source;
    internal Component SourceComponent { get; }
    internal Component ResolveSource(Transform RuntimeRoot)
    {
        var Owner = Root.Resolve(RuntimeRoot);
        return SourceComponent switch
        {
            PllVineAuthoring => Owner.GetComponent<PllVineAuthoring>(),
            PllBreakablePropAuthoring => Owner.GetComponent<PllBreakablePropAuthoring>(),
            PllSwingingPropAuthoring => Owner.GetComponent<PllSwingingPropAuthoring>(),
            _ => throw new InvalidOperationException("Prop interaction source kind is unsupported.")
        };
    }
    internal bool TryMatchGenerated(Component Source, out Transform RuntimeRoot)
    {
        RuntimeRoot = null!;
        if (!Generated || !BindingOwner.TryGetAdmittedGeneratedRoot(Source.transform, out RuntimeRoot)) return false;
        return Root.Resolve(RuntimeRoot) == Source.transform;
    }
    internal int GeneratedTransformBudget => Native?.GeneratedTransforms ?? 0;
    internal int GeneratedColliderBudget => Native?.GeneratedColliders ?? 0;
    internal int RequiresVineMask => Vine switch
    {
        PllVineProfile.Vine => NativeVineRecipes.VineMask,
        PllVineProfile.HangingBridge => NativeVineRecipes.BridgeMask,
        null => 0,
        _ => throw new InvalidOperationException("Prepared vine profile is unsupported.")
    };
    internal static PropInteractionContract[] CreateAll(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        var Sources = AuthoringBindingOwner.GraphComponents<MonoBehaviour>(Template, Biome, Graph, "prop interaction");
        var Roots = new HashSet<Transform>(); var Ids = new HashSet<string>(StringComparer.Ordinal);
        var Result = new List<PropInteractionContract>();
        foreach (var Item in Sources)
        {
            if (Item.Value is not PllBreakablePropAuthoring and not PllSwingingPropAuthoring and not PllVineAuthoring
                || !Roots.Add(Item.Value.transform)) continue;
            var Owner = Item.Value.transform;
            var Break = Owner.GetComponent<PllBreakablePropAuthoring>();
            var Motion = Owner.GetComponent<PllSwingingPropAuthoring>();
            var Vine = Owner.GetComponent<PllVineAuthoring>();
            if (PllPropInteractionValueContract.Failure(Owner, Break, Motion, Vine) is { } Failure)
                throw new ArgumentException(Failure, nameof(Template));
            var Recipe = new PropInteractionContract(Item.Owner, Owner, Break, Motion, Vine);
            if (!Ids.Add(Recipe.Id)) throw new ArgumentException("Prop interactions must have unique stable IDs.", nameof(Template));
            Result.Add(Recipe);
        }
        return Result.ToArray();
    }
    internal void Validate(Transform Template, AuthoringCandidateGraph Graph, BiomeId Biome) =>
        BindingOwner.Validate(Template, Graph, Root.Source, Biome, "prop interaction");
    internal void ResolveNative(NativeVineRecipes Recipes)
    {
        if (Vine is { } Profile) Native = Recipes.Require(Profile)
            ?? throw new InvalidOperationException("The requested vine provider was not prepared.");
    }
    internal PropBridgeResponse BridgeResponse(NativeVineRecipes.Recipe Native) => CustomResponse ?? Native.BridgeResponse;

    internal PropInteractionState InitialState(int Seed, double Clock)
    {
        var Random = new System.Random(Seed);
        var ResolvedCapacity = Vine == PllVineProfile.HangingBridge && CapacityPolicy == PllBridgeCapacity.NativeRandom
            ? Random.Next(1, NativeMaximumPeople) : Capacity;
        var Delay = Vine == PllVineProfile.HangingBridge ? (float)(2.5 + Random.NextDouble() * 5) : OverloadSeconds;
        return new(InitiallyEnabled, PropBreakPhase.Intact, Clock, Delay, ResolvedCapacity,
            HasMotion && Motion != PllPropMotion.NativeBridgeResponse && InitiallyRunning, InitialPhase, Clock);
    }

    internal PropInteractionRuntime Bind(Transform RuntimeRoot, BiomeId Biome,
        Action<PropInteractionState> Publish, Func<bool> IsAuthority, Func<bool> IsReady, Func<double> Clock,
        IReadOnlyList<TreePlatform>? Platforms = null, Transform? ConnectionScope = null)
    {
        var Owner = Root.Resolve(RuntimeRoot);
        var Colliders = new Collider[Supports.Length];
        for (var i = 0; i < Colliders.Length; i++) Colliders[i] = Supports[i].Resolve(RuntimeRoot, Biome, "prop support");
        return new(this, Owner, Intact?.Resolve(RuntimeRoot), Broken?.Resolve(RuntimeRoot),
            Pivot?.Resolve(RuntimeRoot), Colliders, Particles?.Resolve(RuntimeRoot, Biome, "break particles"),
            StartAnchor?.Resolve(RuntimeRoot), EndAnchor?.Resolve(RuntimeRoot), Native,
            Publish, IsAuthority, IsReady, Clock, Platforms, ConnectionScope);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write("PLL/PropInteraction/v2"); Writer.Write(Id); Root.WriteManifest(Writer);
        Writer.Write(HasBreak); Writer.Write(InitiallyEnabled); Writer.Write((int)Trigger);
        Writer.Write(BreakHook); Writer.Write(StartedHook); Writer.Write(PausedHook);
        ManifestEncoding.WriteScalar(Writer, ImpactSpeed); Writer.Write(Capacity);
        ManifestEncoding.WriteScalar(Writer, OverloadSeconds); Writer.Write((int)CapacityPolicy); Writer.Write(NativeMaximumPeople);
        Binding(Intact); Binding(Broken); Writer.Write(Supports.Length);
        foreach (var Support in Supports) Support.WriteManifest(Writer);
        Writer.Write(Particles is not null); Particles?.WriteManifest(Writer);
        BiomeAuthoringContract.WriteClip(Writer, BreakClip);
        Writer.Write(HasMotion); Writer.Write((int)Motion); Writer.Write(InitiallyRunning); Binding(Pivot);
        ManifestEncoding.WriteVector(Writer, Axis); ManifestEncoding.WriteScalar(Writer, Center);
        ManifestEncoding.WriteScalar(Writer, Amplitude); ManifestEncoding.WriteScalar(Writer, Period);
        ManifestEncoding.WriteScalar(Writer, InitialPhase); Writer.Write(Vine.HasValue);
        if (Vine is { } Profile)
        {
            Writer.Write((int)Profile); Writer.Write((int)Endpoints); Binding(StartAnchor); Binding(EndAnchor);
            Writer.Write(CustomResponse.HasValue); CustomResponse?.WriteManifest(Writer);
            ManifestEncoding.WriteScalar(Writer, MinimumDistance); ManifestEncoding.WriteScalar(Writer, MaximumDistance);
            ManifestEncoding.WriteScalar(Writer, MaximumHeightDifference); ManifestEncoding.WriteScalar(Writer, Hang);
            ManifestEncoding.WriteScalar(Writer, ColliderOffset); ManifestEncoding.WriteScalar(Writer, BoxShape.x);
            ManifestEncoding.WriteScalar(Writer, BoxShape.y);
            Writer.Write((bool)CustomSpan);
            Writer.Write(SpanVersion);
            if (CustomSpan) Writer.Write(VisualFingerprintWriter.HashSerializedObject(CustomSpan, "PLL/CustomSpan/v1"));
            ManifestEncoding.WriteVector(Writer, ModelStart); ManifestEncoding.WriteVector(Writer, ModelEnd); ManifestEncoding.WriteVector(Writer, ModelUp);
        }
        void Binding(TransformBinding? Value) { Writer.Write(Value is not null); Value?.WriteManifest(Writer); }
    }
}
