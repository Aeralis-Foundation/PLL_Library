using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class EffectAuthoringContract
{
    readonly AuthoringBindingOwner OwnerBinding;
    readonly ComponentBinding<PllEffectAuthoring> AuthoringBinding;
    readonly ObjectBinding? PresentationRootBinding;
    readonly ComponentBinding<ParticleSystem>[] TopParticleBindings;
    readonly ComponentBinding<ParticleSystem>[] AllParticleBindings;
    readonly float Duration;
    readonly float CoverDelaySeconds;
    readonly PllEffectMode Mode;
    readonly AudioClip? Ambience;
    readonly Color ColorValue;
    readonly AudioClip? Sound;
    readonly string EmitHook;
    readonly PllEffectParticleColorMode ParticleColorMode;
    readonly Color ParticleColor;
    readonly PllNativeEffectParticles NativeParticlesChoice;
    NativeEffectParticleSource? NativeParticleSource;
    readonly ComponentBinding<Renderer>? ColorRendererBinding;
    readonly Material? ColorMaterial;
    readonly Shader? ColorShader;
    readonly PllEffectMaterialColorChannel MaterialColorChannel;
    readonly int ColorMaterialSlot;
    readonly Color MaterialColor;

    EffectAuthoringContract(
        AuthoringBindingOwner OwnerBinding,
        ComponentBinding<PllEffectAuthoring> AuthoringBinding,
        ObjectBinding RootBinding,
        ObjectBinding? PresentationRootBinding,
        ComponentBinding<ParticleSystem>[] TopParticleBindings,
        ComponentBinding<ParticleSystem>[] AllParticleBindings,
        ComponentBinding<Renderer>? ColorRendererBinding,
        Material? ColorMaterial)
    {
        this.OwnerBinding = OwnerBinding;
        this.AuthoringBinding = AuthoringBinding;
        Root = RootBinding;
        this.PresentationRootBinding = PresentationRootBinding;
        this.TopParticleBindings = TopParticleBindings;
        this.AllParticleBindings = AllParticleBindings;
        var Source = AuthoringBinding.Source;
        Duration = Source.Duration;
        CoverDelaySeconds = Source.Transition.CoverDelaySeconds;
        Mode = Source.Mode;
        Ambience = Source.Ambience;
        ColorValue = Source.Color;
        Sound = Source.Sound;
        EmitHook = Source.EmitHook;
        ParticleColorMode = Source.ParticleColorMode;
        ParticleColor = Source.ParticleColor;
        NativeParticlesChoice = Source.NativeParticles;
        this.ColorRendererBinding = ColorRendererBinding;
        this.ColorMaterial = ColorMaterial;
        ColorShader = ColorMaterial ? ColorMaterial.shader : null;
        MaterialColorChannel = Source.MaterialColorChannel;
        ColorMaterialSlot = Source.ColorMaterialSlot;
        MaterialColor = Source.MaterialColor;
    }

    internal PllEffectAuthoring Source => AuthoringBinding.Source;
    internal AuthoringBindingOwner Owner => OwnerBinding;
    internal bool Generated => OwnerBinding.Generated;
    internal ObjectBinding Root { get; }
    internal ObjectBinding? PresentationRoot => PresentationRootBinding;
    internal int NativeSourceMask => NativeParticlesChoice == PllNativeEffectParticles.None
        ? 0 : 1 << (int)NativeParticlesChoice;

    internal void ResolveExternal(NativeVisualSources Sources)
    {
        if (NativeParticlesChoice == PllNativeEffectParticles.None) return;
        var Native = Sources.RequireEffectParticles(NativeParticlesChoice);
        if (CoverDelaySeconds > 0f && PllAuthoringValueContract.ParticleTransitionFailure(
                Native.Renderer.GetComponent<ParticleSystem>()) is { } Failure)
            throw new ArgumentException(Failure, "source");
        if (MaterialColorChannel != PllEffectMaterialColorChannel.None)
            PllEffectAppearanceValueContract.ValidateRenderer(Native.Renderer, ColorMaterialSlot, MaterialColorChannel);
        NativeParticleSource = Native;
    }

    internal void ClearExternal() => NativeParticleSource = null;

    internal static EffectAuthoringContract Create(
        Transform Template,
        AuthoringCandidateGraph Graph,
        BiomeId Biome,
        PllEffectAuthoring Source)
    {
        var OwnerBinding = AuthoringBindingOwner.Create(Template, Graph, Source.transform, Biome, "effect");
        var BindingRoot = OwnerBinding.BindingRoot;
        var EffectRoot = Source.Root;
        if (!Enum.IsDefined(typeof(PllEffectMode), Source.Mode))
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' has a reserved or invalid mode.", "source");
        if (!EffectRoot || !TransformOwnership.Contains(BindingRoot, Source.transform)
            || !TransformOwnership.Contains(BindingRoot, EffectRoot ? EffectRoot.transform : null)
            || EffectRoot && EffectRoot.transform == BindingRoot
            || EffectRoot && !TransformOwnership.DiscoverableWhenActivated(BindingRoot, EffectRoot.transform)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform)
            || EffectRoot && TransformOwnership.HasPropSpawnerInPath(BindingRoot, EffectRoot.transform))
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' must own a discoverable non-template child root outside every PropSpawner hierarchy.", "source");
        if (!ManifestEncoding.CanEncode(Source.Duration) || Source.Duration < 0f
            || Source.Mode == PllEffectMode.Timed && Source.Duration <= 0f)
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' has an invalid duration for its mode.", "source");
        if (PllAuthoringValueContract.TransitionFailure(Source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' has an invalid Cover Delay.", "source");
        if (Source.Mode == PllEffectMode.EmitHook) HookedPropRuntime.ValidateHook(Source.EmitHook, "source");
        else if (Source.EmitHook.Length != 0)
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' declares an emit hook outside EmitHook mode.", "source");
        if (PllAuthoringValueContract.DestroyedReference(Source.Ambience)
            || PllAuthoringValueContract.DestroyedReference(Source.Sound))
            throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' references a destroyed sound.", "source");
        BiomeAuthoringContract.ValidateColor(Source.Color, Biome, $"effect '{Source.name}' color");
        var ColorMaterial = PllEffectAppearanceValueContract.Validate(Source, BindingRoot);

        var Presentation = Source.PresentationRoot;
        if (Presentation)
        {
            if (Presentation == EffectRoot || !TransformOwnership.Contains(EffectRoot!.transform, Presentation.transform)
                || Presentation.activeSelf || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Presentation.transform))
                throw new ArgumentException($"Biome '{Biome}' effect '{Source.name}' presentation root must be a staged-inactive strict owned child outside every PropSpawner hierarchy.", "source");
            ValidatePresentationComponents(Presentation, Source, Biome, true);
        }
        ValidateParticleGraph(BindingRoot, EffectRoot!, Presentation, Source, Biome, true, out var TopSources, out var AllSources);
        var TopParticleBindings = new ComponentBinding<ParticleSystem>[TopSources.Length];
        for (var Index = 0; Index < TopParticleBindings.Length; Index++)
            TopParticleBindings[Index] = ComponentBinding<ParticleSystem>.Create(BindingRoot, TopSources[Index]);
        var AllParticleBindings = new ComponentBinding<ParticleSystem>[AllSources.Length];
        for (var Index = 0; Index < AllParticleBindings.Length; Index++)
            AllParticleBindings[Index] = ComponentBinding<ParticleSystem>.Create(BindingRoot, AllSources[Index]);
        return new EffectAuthoringContract(
            OwnerBinding,
            ComponentBinding<PllEffectAuthoring>.Create(BindingRoot, Source),
            ObjectBinding.Create(BindingRoot, EffectRoot!),
            Presentation ? ObjectBinding.Create(BindingRoot, Presentation) : null,
            TopParticleBindings, AllParticleBindings,
            Source.ColorRenderer ? ComponentBinding<Renderer>.Create(BindingRoot, Source.ColorRenderer) : null,
            ColorMaterial);
    }

    internal ResolvedEffect ResolveStable(Transform RuntimeRoot, BiomeId Biome)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a generated effect as stable authoring.");
        return Resolve(RuntimeRoot, Biome);
    }

    internal bool TryResolveGenerated(PllEffectAuthoring Source, BiomeId Biome, out ResolvedEffect Resolved)
    {
        Resolved = default;
        if (!TryMatchGenerated(Source, Biome, out var RuntimeRoot)) return false;
        Resolved = Resolve(RuntimeRoot, Biome);
        return true;
    }

    internal bool TryMatchGenerated(PllEffectAuthoring Source, BiomeId Biome, out Transform RuntimeRoot) =>
        AuthoringBinding.TryMatchGenerated(
            OwnerBinding, Source, Biome, "generated effect authoring", out RuntimeRoot);

    ResolvedEffect Resolve(Transform RuntimeRoot, BiomeId Biome)
    {
        var Value = AuthoringBinding.Resolve(RuntimeRoot, Biome, "effect authoring");
        var EffectRoot = Root.Resolve(RuntimeRoot, Biome, "effect root");
        var Presentation = PresentationRootBinding?.Resolve(RuntimeRoot, Biome, "effect presentation root");
        var ColorTarget = ColorRendererBinding?.Resolve(RuntimeRoot, Biome, "effect color renderer");
        var ResolvedTop = new ParticleSystem[TopParticleBindings.Length];
        for (var Index = 0; Index < ResolvedTop.Length; Index++)
            ResolvedTop[Index] = TopParticleBindings[Index].Resolve(RuntimeRoot, Biome, "effect top-level particle");
        var ResolvedAll = new ParticleSystem[AllParticleBindings.Length];
        for (var Index = 0; Index < ResolvedAll.Length; Index++)
            ResolvedAll[Index] = AllParticleBindings[Index].Resolve(RuntimeRoot, Biome, "effect particle");
        if (TransformOwnership.HasPropSpawnerInPath(RuntimeRoot, Value.transform)
            || TransformOwnership.HasPropSpawnerInPath(RuntimeRoot, EffectRoot.transform)
            || PllAuthoringValueContract.DestroyedReference(Ambience)
            || PllAuthoringValueContract.DestroyedReference(Sound)
            || Value.Root != EffectRoot || Value.PresentationRoot != Presentation
            || Value.Particles.Count != ResolvedTop.Length
            || !Value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds)
            || !Value.Duration.Equals(Duration) || Value.Mode != Mode || Value.Ambience != Ambience
            || !Value.Color.Equals(ColorValue) || Value.Sound != Sound || Value.EmitHook != EmitHook
            || Value.ParticleColorMode != ParticleColorMode || !Value.ParticleColor.Equals(ParticleColor)
            || Value.NativeParticles != NativeParticlesChoice
            || !SameAppearance(Value, ColorTarget)
            || Presentation && Presentation.activeSelf)
            throw new InvalidOperationException($"Biome '{Biome}' runtime effect metadata does not match its registered contract.");
        if (Presentation) ValidatePresentationComponents(Presentation, Value, Biome, false);
        ValidateParticleGraph(RuntimeRoot, EffectRoot, Presentation, Value, Biome, false,
            out var RuntimeTop, out var RuntimeAll, CheckActivePath: false);
        if (!Same(RuntimeTop, ResolvedTop) || !Same(RuntimeAll, ResolvedAll))
            throw new InvalidOperationException($"Biome '{Biome}' runtime effect particle graph does not match its registered contract.");
        var NativeParticles = NativeParticlesChoice == PllNativeEffectParticles.None ? null
            : NativeParticleSource ?? throw new InvalidOperationException($"Biome '{Biome}' native effect particles are not live-bound.");
        return new ResolvedEffect(Value, EffectRoot, Presentation, ResolvedTop, ResolvedAll, NativeParticles, ColorTarget);
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        OwnerBinding.Validate(Template, Graph, AuthoringBinding.Source.transform, Biome, "effect");
        var BindingRoot = OwnerBinding.BindingRoot;
        AuthoringBinding.Validate(BindingRoot, Biome, "effect authoring"); Root.Validate(BindingRoot, Biome, "effect root");
        PresentationRootBinding?.Validate(BindingRoot, Biome, "effect presentation root");
        ColorRendererBinding?.Validate(BindingRoot, Biome, "effect color renderer");
        foreach (var Particle in TopParticleBindings) Particle.Validate(BindingRoot, Biome, "effect top-level particle");
        foreach (var Particle in AllParticleBindings) Particle.Validate(BindingRoot, Biome, "effect particle");
        var Value = Source;
        var Presentation = PresentationRootBinding?.Source;
        if (!TransformOwnership.DiscoverableWhenActivated(BindingRoot, Root.Source.transform)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Value.transform)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Root.Source.transform)
            || PllAuthoringValueContract.DestroyedReference(Ambience)
            || PllAuthoringValueContract.DestroyedReference(Sound)
            || Value.Root != Root.Source || Value.PresentationRoot != Presentation
            || Value.Particles.Count != TopParticleBindings.Length
            || !Value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds)
            || !Value.Duration.Equals(Duration) || Value.Mode != Mode || Value.Ambience != Ambience
            || !Value.Color.Equals(ColorValue) || Value.Sound != Sound || Value.EmitHook != EmitHook
            || Value.ParticleColorMode != ParticleColorMode || !Value.ParticleColor.Equals(ParticleColor)
            || Value.NativeParticles != NativeParticlesChoice
            || !SameAppearance(Value, ColorRendererBinding?.Source)
            || Presentation && Presentation.activeSelf)
            throw new InvalidOperationException($"Biome '{Biome}' changed effect '{Value.name}' before sealing.");
        if (Presentation) ValidatePresentationComponents(Presentation, Value, Biome, false);
        ValidateParticleGraph(BindingRoot, Root.Source, Presentation, Value, Biome, false, out var SourceTop, out var SourceAll);
        for (var Index = 0; Index < SourceTop.Length; Index++)
            if (SourceTop[Index] != TopParticleBindings[Index].Source)
                throw new InvalidOperationException($"Biome '{Biome}' changed effect '{Value.name}' top-level particles before sealing.");
        for (var Index = 0; Index < SourceAll.Length; Index++)
            if (SourceAll[Index] != AllParticleBindings[Index].Source)
                throw new InvalidOperationException($"Biome '{Biome}' changed effect '{Value.name}' particle graph before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        OwnerBinding.WriteManifest(Writer);
        AuthoringBinding.WriteManifest(Writer); Root.WriteManifest(Writer);
        Writer.Write(PresentationRootBinding is not null);
        PresentationRootBinding?.WriteManifest(Writer);
        Writer.Write(TopParticleBindings.Length);
        foreach (var Particle in TopParticleBindings) Particle.WriteManifest(Writer);
        Writer.Write(AllParticleBindings.Length);
        foreach (var Particle in AllParticleBindings) Particle.WriteManifest(Writer);
        ManifestEncoding.WriteScalar(Writer, Duration); Writer.Write((int)Mode);
        BiomeAuthoringContract.WriteClip(Writer, Ambience); ManifestEncoding.WriteColor(Writer, ColorValue);
        BiomeAuthoringContract.WriteClip(Writer, Sound); Writer.Write(EmitHook);
        Writer.Write((byte)ParticleColorMode); ManifestEncoding.WriteColor(Writer, ParticleColor);
        Writer.Write((byte)NativeParticlesChoice);
        Writer.Write(ColorRendererBinding is not null); ColorRendererBinding?.WriteManifest(Writer);
        Writer.Write((byte)MaterialColorChannel); Writer.Write(ColorMaterialSlot);
        ManifestEncoding.WriteColor(Writer, MaterialColor);
        ManifestEncoding.WriteScalar(Writer, CoverDelaySeconds);
    }

    bool SameAppearance(PllEffectAuthoring Value, Renderer? Target)
    {
        if (Value.MaterialColorChannel != MaterialColorChannel || Value.ColorRenderer != Target
            || Value.ColorMaterialSlot != ColorMaterialSlot || !Value.MaterialColor.Equals(MaterialColor)) return false;
        if (!Target) return true;
        var Materials = Target.sharedMaterials;
        return ColorMaterialSlot < Materials.Length && Materials[ColorMaterialSlot] == ColorMaterial
            && ColorMaterial && ColorMaterial.shader == ColorShader;
    }

    static void ValidateParticleGraph(
        Transform BindingRoot,
        GameObject EffectRoot,
        GameObject? Presentation,
        PllEffectAuthoring Source,
        BiomeId Biome,
        bool Argument,
        out ParticleSystem[] Top,
        out ParticleSystem[] All,
        bool CheckActivePath = true)
    {
        Top = new ParticleSystem[Source.Particles.Count];
        var TopSet = new HashSet<ParticleSystem>();
        for (var Index = 0; Index < Top.Length; Index++)
        {
            var Particle = Source.Particles[Index];
            if (!Particle || !TopSet.Add(Particle))
                Fail($"Biome '{Biome}' effect '{Source.name}' contains a missing or duplicate top-level particle system.", Argument);
            Top[Index] = Particle!;
        }
        All = EffectRoot.GetComponentsInChildren<ParticleSystem>(true);
        var Owned = new HashSet<ParticleSystem>(All);
        var Visited = new HashSet<ParticleSystem>();
        foreach (var Particle in Top) Visit(Particle);
        if (Visited.Count != All.Length)
            Fail($"Biome '{Biome}' effect '{Source.name}' contains an orphan particle system outside its top-level/sub-emitter graph.", Argument);
        foreach (var Particle in All)
        {
            if (!Particle
                || CheckActivePath && !Discoverable(BindingRoot, Particle.transform, Presentation ? Presentation.transform : null)
                || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Particle.transform)
                || Particle.main.playOnAwake || !Particle.isStopped)
                Fail($"Biome '{Biome}' effect '{Source.name}' contains an inactive, unowned, PropSpawner-owned, or non-normalized particle system.", Argument);
            if (Source.Transition.CoverDelaySeconds > 0f
                && PllAuthoringValueContract.ParticleTransitionFailure(Particle!) is { } Failure)
                Fail(Failure, Argument);
        }

        void Visit(ParticleSystem Particle)
        {
            if (!Owned.Contains(Particle))
                Fail($"Biome '{Biome}' effect '{Source.name}' references a sub-emitter outside its owned root.", Argument);
            if (!Visited.Add(Particle))
                Fail($"Biome '{Biome}' effect '{Source.name}' contains a particle cycle or multiply owned sub-emitter.", Argument);
            var SubEmitters = Particle.subEmitters;
            for (var Index = 0; Index < SubEmitters.subEmittersCount; Index++)
            {
                var Child = SubEmitters.GetSubEmitterSystem(Index);
                if (!Child) Fail($"Biome '{Biome}' effect '{Source.name}' contains a missing sub-emitter.", Argument);
                Visit(Child!);
            }
        }
    }

    static void ValidatePresentationComponents(GameObject PresentationRoot, PllEffectAuthoring Source, BiomeId Biome, bool Argument)
    {
        foreach (var Value in PresentationRoot.GetComponentsInChildren<Component>(true))
        {
            if (!Value || Value is not Transform and not ParticleSystem and not ParticleSystemForceField
                and not Renderer and not MeshFilter and not Light and not Animator and not PllVisualAuthoring)
                Fail($"Biome '{Biome}' effect '{Source.name}' presentation contains unsupported component '{(Value ? Value.GetType().FullName : "missing script")}'.", Argument);
            if (Source.Transition.CoverDelaySeconds == 0f) continue;
            if (Value is Animator || Value is Renderer and not ParticleSystemRenderer)
                Fail($"Biome '{Biome}' effect '{Source.name}' presentation has no supported appearance channel for Cover Delay.", Argument);
            if (Value is Light LightValue
                && (!ManifestEncoding.CanEncode(LightValue.intensity) || LightValue.intensity < 0f))
                Fail($"Biome '{Biome}' effect '{Source.name}' light intensity is invalid for Cover Delay.", Argument);
        }
    }

    static bool Discoverable(Transform BindingRoot, Transform Value, Transform? StagedActivationRoot) =>
        StagedActivationRoot
            ? FinalForceAuthoringContract.ActiveBelow(Value, BindingRoot, StagedActivationRoot)
            : TransformOwnership.DiscoverableWhenActivated(BindingRoot, Value);

    static bool Same(ParticleSystem[] Left, ParticleSystem[] Right)
    {
        if (Left.Length != Right.Length) return false;
        for (var Index = 0; Index < Left.Length; Index++)
            if (Left[Index] != Right[Index]) return false;
        return true;
    }

    static void Fail(string Message, bool Argument)
    {
        if (Argument) throw new ArgumentException(Message, "source");
        throw new InvalidOperationException(Message);
    }

}

internal readonly struct ResolvedEffect
{
    internal ResolvedEffect(
        PllEffectAuthoring Authoring,
        GameObject Root,
        GameObject? PresentationRoot,
        ParticleSystem[] TopParticles,
        ParticleSystem[] AllParticles,
        NativeEffectParticleSource? NativeParticles = null,
        Renderer? ColorRenderer = null)
    {
        this.Authoring = Authoring; this.Root = Root; this.PresentationRoot = PresentationRoot;
        this.TopParticles = TopParticles; this.AllParticles = AllParticles;
        this.NativeParticles = NativeParticles;
        this.ColorRenderer = ColorRenderer;
    }
    internal PllEffectAuthoring Authoring { get; }
    internal GameObject Root { get; }
    internal GameObject? PresentationRoot { get; }
    internal ParticleSystem[] TopParticles { get; }
    internal ParticleSystem[] AllParticles { get; }
    internal NativeEffectParticleSource? NativeParticles { get; }
    internal Renderer? ColorRenderer { get; }
}
