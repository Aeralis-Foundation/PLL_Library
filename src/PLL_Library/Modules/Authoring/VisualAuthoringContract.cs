using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class VisualAuthoringContract
{
    readonly AuthoringBindingOwner OwnerBinding;
    readonly ComponentBinding<PllVisualAuthoring> AuthoringBinding;
    readonly ComponentBinding<Renderer>[] RendererBindings;
    readonly bool[] RendererEnabledStates;
    readonly ComponentBinding<Renderer>? PropertyRendererBinding;
    readonly ObjectBinding? StagedActivationRootBinding;
    readonly Material? TargetMaterial;
    readonly int MaterialIndexValue;
    readonly string PropertyName;
    readonly float TriggerValue;
    readonly float CoverDelaySeconds;
    readonly bool EnabledByDefault;
    readonly Shader? ShaderValue;
    readonly Material? MaterialValue;

    VisualAuthoringContract(
        AuthoringBindingOwner OwnerBinding,
        ComponentBinding<PllVisualAuthoring> AuthoringBinding,
        ObjectBinding RootBinding,
        ComponentBinding<Renderer>[] RendererBindings,
        bool[] RendererEnabledStates,
        ComponentBinding<Renderer>? PropertyRendererBinding,
        ObjectBinding? StagedActivationRootBinding,
        Material? TargetMaterial,
        int MaterialIndexValue,
        string PropertyName,
        float TriggerValue,
        bool EnabledByDefault,
        Shader? ShaderValue,
        Material? MaterialValue,
        int EffectIndexValue)
    {
        this.OwnerBinding = OwnerBinding;
        this.AuthoringBinding = AuthoringBinding;
        Root = RootBinding;
        this.RendererBindings = RendererBindings;
        this.RendererEnabledStates = RendererEnabledStates;
        this.PropertyRendererBinding = PropertyRendererBinding;
        this.StagedActivationRootBinding = StagedActivationRootBinding;
        this.TargetMaterial = TargetMaterial;
        this.MaterialIndexValue = MaterialIndexValue;
        this.PropertyName = PropertyName;
        this.TriggerValue = TriggerValue;
        CoverDelaySeconds = AuthoringBinding.Source.Transition.CoverDelaySeconds;
        this.EnabledByDefault = EnabledByDefault;
        this.ShaderValue = ShaderValue;
        this.MaterialValue = MaterialValue;
        EffectIndex = EffectIndexValue;
    }

    internal PllVisualAuthoring Source => AuthoringBinding.Source;
    internal AuthoringBindingOwner Owner => OwnerBinding;
    internal bool Generated => OwnerBinding.Generated;
    internal ObjectBinding Root { get; }
    internal int EffectIndex { get; }

    internal static VisualAuthoringContract Create(
        Transform Template,
        AuthoringCandidateGraph Graph,
        BiomeId Biome,
        PllVisualAuthoring Source,
        GameObject? StagedActivationRoot,
        int EffectIndexValue)
    {
        var OwnerBinding = AuthoringBindingOwner.Create(Template, Graph, Source.transform, Biome, "visual");
        var BindingRoot = OwnerBinding.BindingRoot;
        var VisualRoot = Source.Root;
        if (!VisualRoot || !TransformOwnership.Contains(BindingRoot, Source.transform)
            || !TransformOwnership.Contains(BindingRoot, VisualRoot ? VisualRoot.transform : null)
            || VisualRoot && VisualRoot.transform == BindingRoot
            || VisualRoot && !Discoverable(BindingRoot, VisualRoot.transform, StagedActivationRoot ? StagedActivationRoot.transform : null)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform)
            || VisualRoot && TransformOwnership.HasPropSpawnerInPath(BindingRoot, VisualRoot.transform))
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' must own a discoverable non-template child root outside every PropSpawner hierarchy.", "source");
        if (PllAuthoringValueContract.TransitionFailure(Source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' has an invalid Cover Delay.", "source");
        if (Source.Transition.CoverDelaySeconds > 0f && Source.PropertyName.Length == 0)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' needs a scalar shader channel for Cover Delay.", "source");
        ValidateComponents(VisualRoot!, Source, Biome, true);
        var SourceRenderers = VisualRoot!.GetComponentsInChildren<Renderer>(true);
        if (SourceRenderers.Length == 0)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' root contains no Renderer.", "source");
        var RendererBindings = new ComponentBinding<Renderer>[SourceRenderers.Length];
        var RendererEnabledStates = new bool[SourceRenderers.Length];
        for (var Index = 0; Index < RendererBindings.Length; Index++)
        {
            if (!Discoverable(BindingRoot, SourceRenderers[Index].transform, StagedActivationRoot ? StagedActivationRoot.transform : null)
                || TransformOwnership.HasPropSpawnerInPath(BindingRoot, SourceRenderers[Index].transform))
                throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' renderer is inactive or belongs to a PropSpawner hierarchy.", "source");
            RendererBindings[Index] = ComponentBinding<Renderer>.Create(BindingRoot, SourceRenderers[Index]);
            RendererEnabledStates[Index] = SourceRenderers[Index].enabled;
        }
        if (!ManifestEncoding.CanEncode(Source.TriggerValue))
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' trigger exceeds the synchronized numeric range.", "source");
        if (PllAuthoringValueContract.DestroyedReference(Source.Shader)
            || PllAuthoringValueContract.DestroyedReference(Source.Material))
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' references a destroyed shader or material.", "source");
        if (Source.Shader && Source.Material && Source.Material.shader != Source.Shader)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' material does not use its declared shader.", "source");
        if ((Source.Shader || Source.Material) && Source.PropertyName.Length == 0)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' needs a shader property name.", "source");
        if (!Source.Shader && !Source.Material && Source.PropertyName.Length != 0)
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' declares a shader property without a shader or material.", "source");
        Renderer? TargetRenderer = null;
        Material? TargetMaterial = null;
        var MaterialIndexValue = -1;
        if (Source.PropertyName.Length != 0)
            foreach (var RendererValue in SourceRenderers)
            {
                var Materials = RendererValue.sharedMaterials;
                for (var Index = 0; Index < Materials.Length; Index++)
                {
                    var Candidate = Materials[Index];
                    if (!Candidate || Source.Material && Candidate != Source.Material
                        || !Source.Material && Source.Shader && Candidate.shader != Source.Shader) continue;
                    if (TargetRenderer)
                        throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' shader-property target is ambiguous across Renderer material slots.", "source");
                    TargetRenderer = RendererValue; TargetMaterial = Candidate; MaterialIndexValue = Index;
                }
            }
        if (Source.PropertyName.Length != 0 && (!TargetRenderer || !TargetMaterial))
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' cannot resolve its shader-property target under the visual root.", "source");
        if (TargetMaterial && !TargetMaterial.HasProperty(Source.PropertyName))
            throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' material has no '{Source.PropertyName}' property.", "source");
        var TargetShader = TargetMaterial ? TargetMaterial.shader : null;
        if (TargetShader)
        {
            var PropertyIndex = TargetShader.FindPropertyIndex(Source.PropertyName);
            if (PropertyIndex < 0)
                throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' shader has no '{Source.PropertyName}' property.", "source");
            var PropertyType = TargetShader.GetPropertyType(PropertyIndex);
            if (PropertyType is not ShaderPropertyType.Float and not ShaderPropertyType.Range)
                throw new ArgumentException($"Biome '{Biome}' visual '{Source.name}' property '{Source.PropertyName}' is not a float or range.", "source");
        }
        BiomeAuthoringContract.RequireToken(Source.PropertyName, Biome, "visual property name", true);
        return new VisualAuthoringContract(
            OwnerBinding,
            ComponentBinding<PllVisualAuthoring>.Create(BindingRoot, Source),
            ObjectBinding.Create(BindingRoot, VisualRoot), RendererBindings, RendererEnabledStates,
            TargetRenderer ? ComponentBinding<Renderer>.Create(BindingRoot, TargetRenderer) : null,
            StagedActivationRoot ? ObjectBinding.Create(BindingRoot, StagedActivationRoot) : null,
            TargetMaterial, MaterialIndexValue, Source.PropertyName, Source.TriggerValue,
            Source.EnabledByDefault, Source.Shader, Source.Material, EffectIndexValue);
    }

    internal ResolvedVisual ResolveStable(Transform RuntimeRoot, BiomeId Biome)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a generated visual as stable authoring.");
        return Resolve(RuntimeRoot, Biome);
    }

    internal bool TryResolveGenerated(PllVisualAuthoring Source, BiomeId Biome, out ResolvedVisual Resolved)
    {
        Resolved = default;
        if (!TryMatchGenerated(Source, Biome, out var RuntimeRoot)) return false;
        Resolved = Resolve(RuntimeRoot, Biome);
        return true;
    }

    internal bool TryMatchGenerated(PllVisualAuthoring Source, BiomeId Biome, out Transform RuntimeRoot) =>
        AuthoringBinding.TryMatchGenerated(
            OwnerBinding, Source, Biome, "generated visual authoring", out RuntimeRoot);

    ResolvedVisual Resolve(Transform RuntimeRoot, BiomeId Biome)
    {
        var Value = AuthoringBinding.Resolve(RuntimeRoot, Biome, "visual authoring");
        var VisualRoot = Root.Resolve(RuntimeRoot, Biome, "visual root");
        StagedActivationRootBinding?.Resolve(RuntimeRoot, Biome, "effect presentation root");
        ValidateComponents(VisualRoot, Value, Biome, false);
        var ResolvedRenderers = new Renderer[RendererBindings.Length];
        for (var Index = 0; Index < ResolvedRenderers.Length; Index++)
        {
            ResolvedRenderers[Index] = RendererBindings[Index].Resolve(RuntimeRoot, Biome, "visual renderer");
            if (ResolvedRenderers[Index].enabled != RendererEnabledStates[Index])
                throw new InvalidOperationException($"Biome '{Biome}' runtime visual renderer state does not match its registered contract.");
        }
        var ResolvedTarget = PropertyRendererBinding?.Resolve(RuntimeRoot, Biome, "visual property renderer");
        if (TransformOwnership.HasPropSpawnerInPath(RuntimeRoot, Value.transform)
            || TransformOwnership.HasPropSpawnerInPath(RuntimeRoot, VisualRoot.transform)
            || Array.Exists(ResolvedRenderers, RendererValue => TransformOwnership.HasPropSpawnerInPath(RuntimeRoot, RendererValue.transform))
            || PllAuthoringValueContract.DestroyedReference(ShaderValue)
            || PllAuthoringValueContract.DestroyedReference(MaterialValue)
            || PllAuthoringValueContract.DestroyedReference(TargetMaterial)
            || Value.Root != VisualRoot || Value.PropertyName != PropertyName || !Value.TriggerValue.Equals(TriggerValue)
            || !Value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds)
            || Value.EnabledByDefault != EnabledByDefault || Value.Shader != ShaderValue || Value.Material != MaterialValue)
            throw new InvalidOperationException($"Biome '{Biome}' runtime visual metadata does not match its registered contract.");
        if (ResolvedTarget)
        {
            var Materials = ResolvedTarget.sharedMaterials;
            if (MaterialIndexValue < 0 || MaterialIndexValue >= Materials.Length || Materials[MaterialIndexValue] != TargetMaterial)
                throw new InvalidOperationException($"Biome '{Biome}' runtime visual property target changed its material slot.");
        }
        return new ResolvedVisual(Value, VisualRoot, ResolvedRenderers, ResolvedTarget, MaterialIndexValue, EffectIndex);
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        OwnerBinding.Validate(Template, Graph, AuthoringBinding.Source.transform, Biome, "visual");
        var BindingRoot = OwnerBinding.BindingRoot;
        AuthoringBinding.Validate(BindingRoot, Biome, "visual authoring"); Root.Validate(BindingRoot, Biome, "visual root");
        StagedActivationRootBinding?.Validate(BindingRoot, Biome, "effect presentation root");
        for (var Index = 0; Index < RendererBindings.Length; Index++)
        {
            RendererBindings[Index].Validate(BindingRoot, Biome, "visual renderer");
            if (RendererBindings[Index].Source.enabled != RendererEnabledStates[Index])
                throw new InvalidOperationException($"Biome '{Biome}' changed a visual renderer state before sealing.");
        }
        PropertyRendererBinding?.Validate(BindingRoot, Biome, "visual property renderer");
        var Value = Source;
        ValidateComponents(Root.Source, Value, Biome, false);
        if (!Discoverable(BindingRoot, Root.Source.transform, StagedActivationRootBinding?.Source.transform)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Value.transform)
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Root.Source.transform)
            || Array.Exists(RendererBindings, RendererValue => !Discoverable(BindingRoot, RendererValue.Source.transform, StagedActivationRootBinding?.Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(BindingRoot, RendererValue.Source.transform))
            || PllAuthoringValueContract.DestroyedReference(ShaderValue)
            || PllAuthoringValueContract.DestroyedReference(MaterialValue)
            || PllAuthoringValueContract.DestroyedReference(TargetMaterial)
            || Value.Root != Root.Source || Value.PropertyName != PropertyName || !Value.TriggerValue.Equals(TriggerValue)
            || !Value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds)
            || Value.EnabledByDefault != EnabledByDefault || Value.Shader != ShaderValue || Value.Material != MaterialValue)
            throw new InvalidOperationException($"Biome '{Biome}' changed visual '{Value.name}' before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        OwnerBinding.WriteManifest(Writer);
        AuthoringBinding.WriteManifest(Writer); Root.WriteManifest(Writer);
        Writer.Write(StagedActivationRootBinding is not null);
        StagedActivationRootBinding?.WriteManifest(Writer);
        Writer.Write(RendererBindings.Length);
        for (var Index = 0; Index < RendererBindings.Length; Index++)
        {
            RendererBindings[Index].WriteManifest(Writer);
            Writer.Write(RendererEnabledStates[Index]);
        }
        Writer.Write(PropertyRendererBinding is not null);
        PropertyRendererBinding?.WriteManifest(Writer); Writer.Write(MaterialIndexValue);
        Writer.Write(PropertyName); ManifestEncoding.WriteScalar(Writer, TriggerValue); Writer.Write(EnabledByDefault);
        Writer.Write(ShaderValue ? ShaderValue.name : ""); Writer.Write(MaterialValue ? MaterialValue.name : "");
        Writer.Write(TargetMaterial ? TargetMaterial.name : ""); Writer.Write(EffectIndex);
        ManifestEncoding.WriteScalar(Writer, CoverDelaySeconds);
    }

    static bool Discoverable(Transform BindingRoot, Transform Value, Transform? StagedActivationRoot) =>
        StagedActivationRoot
            ? FinalForceAuthoringContract.ActiveBelow(Value, BindingRoot, StagedActivationRoot)
            : TransformOwnership.DiscoverableWhenActivated(BindingRoot, Value);

    static void ValidateComponents(GameObject VisualRoot, PllVisualAuthoring Source, BiomeId Biome, bool Argument)
    {
        foreach (var Value in VisualRoot.GetComponentsInChildren<Component>(true))
            if (!Value || Value is not Transform and not Renderer and not MeshFilter and not ParticleSystem
                and not PllVisualAuthoring and not PllEffectAuthoring)
            {
                var Message = $"Biome '{Biome}' visual '{Source.name}' root contains unsupported component '{(Value ? Value.GetType().FullName : "missing script")}'. Visual roots may contain rendering components and PLL authoring metadata only.";
                if (Argument) throw new ArgumentException(Message, "source");
                throw new InvalidOperationException(Message);
            }
    }
}

internal readonly struct ResolvedVisual
{
    internal ResolvedVisual(
        PllVisualAuthoring Authoring,
        GameObject Root,
        Renderer[] Renderers,
        Renderer? PropertyRenderer,
        int MaterialIndex,
        int EffectIndex)
    {
        this.Authoring = Authoring; this.Root = Root; this.Renderers = Renderers;
        this.PropertyRenderer = PropertyRenderer; this.MaterialIndex = MaterialIndex; this.EffectIndex = EffectIndex;
    }
    internal PllVisualAuthoring Authoring { get; }
    internal GameObject Root { get; }
    internal Renderer[] Renderers { get; }
    internal Renderer? PropertyRenderer { get; }
    internal int MaterialIndex { get; }
    internal int EffectIndex { get; }
    internal ResolvedVisual WithEffectIndex(int EffectIndex) => new(
        Authoring, Root, Renderers, PropertyRenderer, MaterialIndex, EffectIndex);
}
