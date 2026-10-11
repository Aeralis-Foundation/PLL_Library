using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllAmuletOpportunityCategory : byte
{
    InheritBiome = 0,
    First = 1,
    Second = 2,
    Third = 3,
    Fourth = 4
}

[Serializable]
public abstract class PllAmuletPresentation
{
    private protected PllAmuletPresentation() { }
}

[Serializable]
public sealed class PllNativeAmuletPedestal : PllAmuletPresentation { }

[Serializable]
public sealed class PllCustomAmuletPedestal : PllAmuletPresentation
{
    [SerializeField] GameObject Model = null!;
    [SerializeField] Transform Anchor = null!;
    [SerializeField] Material? SurfaceMatchMaterial;

    public PllCustomAmuletPedestal(GameObject Model, Transform Anchor, Material? SurfaceMatchMaterial = null)
    {
        Validate(Model, Anchor, SurfaceMatchMaterial);
        this.Model = Model; this.Anchor = Anchor;
        this.SurfaceMatchMaterial = SurfaceMatchMaterial;
    }

    public GameObject ModelPrefab => Model;
    public Transform AmuletAnchor => Anchor;
    public Material? NativeSurfaceMatchMaterial => SurfaceMatchMaterial;
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    void RequireValid() => Validate(Model, Anchor, SurfaceMatchMaterial);

    static void Validate(GameObject Model, Transform Anchor, Material? Match)
    {
        if (!Model || !Model.activeSelf || !Anchor
            || Anchor != Model.transform && !Anchor.IsChildOf(Model.transform)
            || Model.transform.localPosition != Vector3.zero
            || Model.transform.localRotation != Quaternion.identity || Model.transform.localScale != Vector3.one)
            throw new ArgumentException("The custom pedestal requires an active unit-transform model and its own amulet anchor.", nameof(Model));
        var Solid = false;
        var Visible = false;
        var MatchSlot = !Match;
        foreach (var Component in Model.GetComponentsInChildren<Component>(true))
        {
            if (!Component) throw new ArgumentException("The custom pedestal contains a missing component.", nameof(Model));
            if (Component is ParticleSystem Particle)
            {
                if (PllAuthoringValueContract.ParticleCallbackFailure(Particle) is { } Failure)
                    throw new ArgumentException(Failure, nameof(Model));
                continue;
            }
            if (Component is not Transform and not MeshFilter and not MeshRenderer
                and not ParticleSystemRenderer and not ParticleSystemForceField and not Light
                and not BoxCollider and not SphereCollider and not CapsuleCollider and not MeshCollider)
                throw new ArgumentException("The custom pedestal contains an unsupported presentation or lifecycle component.", nameof(Model));
            if (Component is Collider Collider && Collider.enabled && !Collider.isTrigger)
            {
                var Active = true;
                for (var Node = Collider.transform; Node != Model.transform; Node = Node.parent)
                    if (!Node.gameObject.activeSelf) Active = false;
                Solid |= Active;
            }
            if (Component is Renderer Renderer)
            {
                var Materials = Renderer.sharedMaterials;
                if (Materials.Length == 0)
                    throw new ArgumentException("The pedestal renderer has no materials.", nameof(Model));
                foreach (var Material in Materials)
                {
                    if (!Material || !Material.shader)
                        throw new ArgumentException("The pedestal renderer has a missing material or shader.", nameof(Model));
                    MatchSlot |= Material == Match;
                }
                if (Renderer is MeshRenderer && Renderer.enabled)
                {
                    var Geometry = Renderer.GetComponent<MeshFilter>();
                    var Mesh = Geometry ? Geometry.sharedMesh : null;
                    var Drawable = false;
                    if (Mesh && Mesh.vertexCount != 0)
                        for (var Index = 0; Index < Mesh.subMeshCount; Index++)
                            if (Mesh.GetSubMesh(Index).indexCount != 0)
                            {
                                Drawable = true;
                                break;
                            }
                    if (!Drawable)
                        throw new ArgumentException("The pedestal renderer has no drawable mesh.", nameof(Model));
                    var Active = true;
                    for (var Node = Renderer.transform; Node != Model.transform; Node = Node.parent)
                        if (!Node.gameObject.activeSelf) Active = false;
                    Visible |= Active;
                }
            }
            if (Component is MeshFilter Filter && (!Filter.sharedMesh || !Filter.GetComponent<MeshRenderer>()))
                throw new ArgumentException("The pedestal mesh has no mesh renderer dependency.", nameof(Model));
        }
        if (!Solid) throw new ArgumentException("The custom pedestal requires active solid collision.", nameof(Model));
        if (!Visible) throw new ArgumentException("The custom pedestal requires an active mesh renderer.", nameof(Model));
        if (!MatchSlot) throw new ArgumentException("The custom pedestal does not contain its declared surface-match material.", nameof(Match));
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Amulet Spawner")]
public sealed class PllAmuletSpawnerAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
    [SerializeField] PllAmuletOpportunityCategory CategoryValue = PllAmuletOpportunityCategory.InheritBiome;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume VolumeValue = default;
    [SerializeReference] PllAmuletPresentation PresentationValue = new PllNativeAmuletPedestal();
#else
    PllAmuletPresentation? PreparedPresentation;
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeReference, HideInInspector] object[]? BundlePresentation = null;

    public PllAmuletOpportunityCategory Category => CategoryValue;
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static int ResolveCategory(PllAmuletOpportunityCategory Category, PllBiomeBehavior Behavior) => Category switch
    {
        PllAmuletOpportunityCategory.First => 0,
        PllAmuletOpportunityCategory.Second => 1,
        PllAmuletOpportunityCategory.Third => 2,
        PllAmuletOpportunityCategory.Fourth => 3,
        PllAmuletOpportunityCategory.InheritBiome => PllBiomeBehaviorCompatibility.Resolve(Behavior) is { } Native
            ? Math.Min((int)Native.Role, 3)
            : throw new ArgumentException("An Authored biome requires an explicit amulet opportunity category.", nameof(Category)),
        _ => throw new ArgumentException("The amulet opportunity category is undefined.", nameof(Category))
    };
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static (int Coverage, int Maximum) CapturePotential<T>(Transform Root,
        IReadOnlyList<PllVariationAuthoring> Groups, IReadOnlyList<T> Sources,
        Func<T, (Transform Root, int Category)> ReadSource)
    {
        var Coverage = 0;
        var Maximum = 0;
        foreach (var Value in Sources)
        {
            var Source = ReadSource(Value);
            if (!TransformOwnership.Contains(Root, Source.Root)) continue;
            var InAlternative = false;
            foreach (var Group in Groups)
                foreach (var Entry in Group.Entries)
                    if (Entry.Root.transform != Root && TransformOwnership.Contains(Root, Entry.Root.transform)
                        && TransformOwnership.Contains(Entry.Root.transform, Source.Root)) InAlternative = true;
            if (InAlternative || !PotentiallyActive(Source.Root, Root)) continue;
            Coverage |= 1 << Source.Category;
            Maximum++;
        }
        foreach (var Group in Groups)
        {
            if (Group.Entries.Count == 0 || !TransformOwnership.Contains(Root, Group.Entries[0].Root.transform)
                || Group.Entries[0].Root.transform == Root) continue;
            if (!TransformOwnership.Contains(Root, Group.transform)
                || !PotentiallyActive(Group.transform, Root)) continue;
            var Nested = false;
            foreach (var Other in Groups)
            {
                if (ReferenceEquals(Group, Other)) continue;
                foreach (var Entry in Other.Entries)
                    if (Entry.Root.transform != Root && TransformOwnership.Contains(Root, Entry.Root.transform)
                        && TransformOwnership.Contains(Entry.Root.transform, Group.Entries[0].Root.transform)) Nested = true;
            }
            if (Nested) continue;
            var Common = 15;
            var AlternativeMaximum = 0;
            foreach (var Entry in Group.Entries)
            {
                var Alternative = CapturePotential(Entry.Root.transform, Groups, Sources, ReadSource);
                Common &= Alternative.Coverage;
                AlternativeMaximum = Math.Max(AlternativeMaximum, Alternative.Maximum);
            }
            Coverage |= Common;
            Maximum += AlternativeMaximum;
        }
        return (Coverage, Maximum);
    }

    static bool PotentiallyActive(Transform Source, Transform Root)
    {
        for (var Node = Source; Node != Root; Node = Node.parent)
            if (!Node.gameObject.activeSelf && !Node.GetComponent<PLLFinalGeneration>()) return false;
        return true;
    }
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => VolumeValue;
    public PllAmuletPresentation Presentation => PresentationValue;

    internal void SealBundleSerialization()
    {
        BundleShape = VolumeValue.Shape; BundleCenter = VolumeValue.Center;
        BundleSize = VolumeValue.Size; BundleRadius = VolumeValue.Radius;
        BundlePresentation = new object[] { PresentationValue };
    }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
    public PllAmuletPresentation Presentation => PreparedPresentation ??= ReadPresentation();

    PllAmuletPresentation ReadPresentation() => BundlePresentation is { Length: 1 }
        && BundlePresentation[0] is PllAmuletPresentation Value
        ? Value : throw PllBundleSerialization.Missing(nameof(BundlePresentation));

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize() => PreparedPresentation = null;
#endif
}
