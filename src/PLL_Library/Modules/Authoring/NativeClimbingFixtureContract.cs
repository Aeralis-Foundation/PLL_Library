using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal abstract class NativeClimbingFixtureContract
{
    protected readonly AuthoringBindingOwner Owner;
    protected readonly ObjectBinding Model;

    protected NativeClimbingFixtureContract(AuthoringBindingOwner Owner, GameObject Model)
    {
        this.Owner = Owner;
        this.Model = ObjectBinding.Create(Owner.BindingRoot, Model);
    }

    internal bool Generated => Owner.Generated;
    internal abstract bool RequiresRope { get; }
    internal virtual bool RequiresMountedTool => false;
    internal abstract Component Source { get; }
    internal abstract NativeClimbingFixtureBinding Bind(Transform Root, BiomeId Biome,
        NativeSpawnAdmission Admission, FeatureOccurrence Occurrence);
    internal abstract bool TryBindGenerated(Component Source, BiomeId Biome,
        NativeSpawnAdmission Admission, FeatureOccurrence Occurrence, out NativeClimbingFixtureBinding Binding);
    internal abstract void Validate(Transform Template, AuthoringCandidateGraph Graph, BiomeId Biome);
    internal abstract void WriteManifest(BinaryWriter Writer, bool External);
    internal virtual void ResolveExternal(NativeHangingRopeSource? Rope, NativeMountedToolSource? Tool = null) { }
    internal virtual void ClearExternal() { }

    internal static NativeClimbingFixtureContract[] CreateAll(
        Transform Template, Transform Floor, Transform Campfire, BiomeId Biome,
        AuthoringCandidateGraph Graph)
    {
        var RestPoints = AuthoringBindingOwner.GraphComponents<PllRestPointAuthoring>(Template, Biome, Graph, "Rest Point");
        var Ropes = AuthoringBindingOwner.GraphComponents<PllHangingRopeAuthoring>(Template, Biome, Graph, "Hanging Rope");
        var Result = new NativeClimbingFixtureContract[RestPoints.Count + Ropes.Count];
        var Ids = new HashSet<string>(StringComparer.Ordinal);
        var Roots = new List<Transform>();
        var Index = 0;
        foreach (var Entry in RestPoints)
        {
            RequireOwner(Entry.Owner, Entry.Value, Entry.Value.Id,
                Template, Floor, Campfire, Biome, Ids, Roots);
            if (PllClimbingFixtureShape.Failure(Entry.Value) is { } Failure)
                throw new ArgumentException(Failure, nameof(Template));
            Result[Index++] = new RestPoint(Entry.Owner, Entry.Value, Biome);
        }
        foreach (var Entry in Ropes)
        {
            RequireOwner(Entry.Owner, Entry.Value, Entry.Value.Id,
                Template, Floor, Campfire, Biome, Ids, Roots);
            if (PllClimbingFixtureShape.Failure(Entry.Value) is { } Failure)
                throw new ArgumentException(Failure, nameof(Template));
            Result[Index++] = new HangingRope(Entry.Owner, Entry.Value, Biome);
        }
        return Result;
    }

    static void RequireOwner(AuthoringBindingOwner Owner, Component Source, string Id,
        Transform Template, Transform Floor, Transform Campfire, BiomeId Biome,
        HashSet<string> Ids, List<Transform> Roots)
    {
        if (!Ids.Add(Id)) throw new ArgumentException($"Biome '{Biome}' contains duplicate climbing fixture ID '{Id}'.", nameof(Template));
        var Root = Source.transform;
        if (!TransformOwnership.DiscoverableWhenActivated(Owner.BindingRoot, Root)
            || TransformOwnership.HasPropSpawnerInPath(Owner.BindingRoot, Root)
            || !Owner.Generated && (Root == Floor || Root == Campfire
                || !TransformOwnership.Contains(Floor, Root) && !TransformOwnership.Contains(Campfire, Root)))
            throw new ArgumentException($"Biome '{Biome}' climbing fixture '{Id}' must belong to a stable floor/campfire branch or reachable candidate.", nameof(Template));
        foreach (var Existing in Roots)
            if (TransformOwnership.Overlaps(Existing, Root))
                throw new ArgumentException($"Biome '{Biome}' climbing fixture roots overlap.", nameof(Template));
        Roots.Add(Root);
        if (Source is PllRestPointAuthoring Rest && Rest.Profile == PllRestPointProfile.Permanent
            || Source is PllHangingRopeAuthoring Rope && Rope.AnchorBehavior == PllRopeAnchorBehavior.Permanent)
            RequirePermanent(Root, Biome);
    }

    protected static void RequirePermanent(Transform Root, BiomeId Biome)
    {
        if (Root.GetComponentInChildren<ShittyPiton>(true)
            || Root.GetComponentInChildren<BreakableRopeAnchor>(true)
            || Root.GetComponentInChildren<BrokenRopeCheck>(true))
            throw new InvalidOperationException($"Biome '{Biome}' permanent climbing fixture contains breakable mechanics.");
    }

    protected static PhotonView RequireView(GameObject Root, BiomeId Biome)
    {
        var Views = Root.GetComponents<PhotonView>();
        if (Views.Length != 1 || !Views[0].enabled || Views[0].ViewID != 0
            || Views[0].OwnershipTransfer != OwnershipOption.Fixed
            || Views[0].ObservedComponents.Count != 0)
            throw new ArgumentException($"Biome '{Biome}' climbing fixture must own one unassigned fixed PhotonView.");
        return Views[0];
    }

    sealed class RestPoint : NativeClimbingFixtureContract
    {
        readonly ComponentBinding<PllRestPointAuthoring> Authoring;
        readonly ComponentBinding<ClimbHandle> Handle;
        readonly ComponentBinding<PhotonView> View;
        readonly ComponentBinding<BoxCollider> Interaction;
        readonly string Id;
        readonly Vector3 Center;
        readonly Vector3 Size;
        readonly PllRestPointProfile Profile;
        readonly PllMountedToolModel MountedModel;
        readonly PllFixturePresentation CrackPresentation;
        readonly PllFixturePresentation BreakPresentation;
        readonly ObjectBinding? Crack;
        readonly ObjectBinding? BreakVfx;
        readonly string HangHook, WarningHook, BreakHook;
        readonly PllRestPointPlacement Placement;
        readonly float InsertionOffset;
        readonly ComponentBinding<ShittyPiton>? Fragile;
        NativeMountedToolSource? NativeTool;

        internal RestPoint(AuthoringBindingOwner Owner, PllRestPointAuthoring Source, BiomeId Biome)
            : base(Owner, Source.ModelRoot)
        {
            Authoring = ComponentBinding<PllRestPointAuthoring>.Create(Owner.BindingRoot, Source);
            Id = Source.Id; Center = Source.InteractionCenter; Size = Source.InteractionSize;
            Profile = Source.Profile; MountedModel = Source.MountedModel;
            CrackPresentation = Source.CrackPresentation; BreakPresentation = Source.BreakPresentation;
            HangHook = Source.HangHook; WarningHook = Source.WarningHook; BreakHook = Source.BreakHook;
            Placement = Source.Placement; InsertionOffset = Source.InsertionOffset;
            if (Placement == PllRestPointPlacement.GeneratedTerrain && (!Owner.Generated || Source.transform != Owner.BindingRoot))
                throw new ArgumentException("Terrain-mounted tools must own the complete generated candidate root.");
            Crack = Source.CrackRoot ? ObjectBinding.Create(Owner.BindingRoot, Source.CrackRoot) : null;
            BreakVfx = Source.BreakVfxRoot ? ObjectBinding.Create(Owner.BindingRoot, Source.BreakVfxRoot) : null;
            var Handles = Source.HoldAnchor.GetComponents<ClimbHandle>();
            var Colliders = Source.HoldAnchor.GetComponents<BoxCollider>();
            var Pitons = Source.HoldAnchor.GetComponents<ShittyPiton>();
            if (Profile == PllRestPointProfile.NativeFragileMountedTool)
            {
                if (Pitons.Length != 1 || Pitons[0].enabled
                    || Source.GetComponentsInChildren<ShittyPiton>(true).Length != 1
                    || Source.GetComponentInChildren<BreakableRopeAnchor>(true)
                    || Source.GetComponentInChildren<BrokenRopeCheck>(true))
                    throw new ArgumentException($"Biome '{Biome}' mounted tool '{Id}' needs one staged fragile tool.");
                Fragile = ComponentBinding<ShittyPiton>.Create(Owner.BindingRoot, Pitons[0]);
            }
            if (Handles.Length != 1 || !Handles[0].enabled
                || Handles[0].isPickaxe != (Profile == PllRestPointProfile.NativeFragileMountedTool) || Handles[0].hanger
                || Colliders.Length != 1 || Colliders[0].enabled || Colliders[0].isTrigger
                || Colliders[0].attachedRigidbody || Colliders[0].center != Center || Colliders[0].size != Size
                || Source.HoldAnchor.gameObject.layer != 0
                || Source.GetComponentsInChildren<ClimbHandle>(true).Length != 1
                || Source.GetComponentsInChildren<PhotonView>(true).Length != 1)
                throw new ArgumentException($"Biome '{Biome}' Rest Point '{Id}' has an incomplete staged hold graph.");
            Handle = ComponentBinding<ClimbHandle>.Create(Owner.BindingRoot, Handles[0]);
            Interaction = ComponentBinding<BoxCollider>.Create(Owner.BindingRoot, Colliders[0]);
            View = ComponentBinding<PhotonView>.Create(Owner.BindingRoot, RequireView(Source.HoldAnchor.gameObject, Biome));
        }

        internal override bool RequiresRope => false;
        internal override bool RequiresMountedTool => Profile == PllRestPointProfile.NativeFragileMountedTool;
        internal override Component Source => Authoring.Source;
        internal override void ResolveExternal(NativeHangingRopeSource? Rope, NativeMountedToolSource? Tool = null)
        {
            if (RequiresMountedTool) NativeTool = Tool ?? throw new InvalidOperationException("The native mounted-tool source was not captured.");
        }
        internal override void ClearExternal() => NativeTool = null;
        internal override NativeClimbingFixtureBinding Bind(Transform Root, BiomeId Biome,
            NativeSpawnAdmission Admission, FeatureOccurrence Occurrence)
        {
            var Marker = Authoring.Resolve(Root, Biome, "Rest Point");
            var Native = Handle.Resolve(Root, Biome, "Rest Point handle");
            var Collider = Interaction.Resolve(Root, Biome, "Rest Point interaction bounds");
            var NativeView = View.Resolve(Root, Biome, "Rest Point view");
            RequireGraph(Marker, Native, Collider, NativeView, Model.Resolve(Root, Biome, "Rest Point model"),
                Crack?.Resolve(Root, Biome, "Mounted crack"), BreakVfx?.Resolve(Root, Biome, "Mounted break effect"), Biome);
            var Piton = Fragile?.Resolve(Root, Biome, "Mounted Tool");
            GameObject? NativeModel = null;
            if (Piton)
            {
                var Tool = NativeTool ?? throw new InvalidOperationException("The mounted-tool recipe was not native-source sealed.");
                if (MountedModel == PllMountedToolModel.Native)
                {
                    NativeModel = Tool.CreateModel(Native.transform); Marker.ModelRoot.SetActive(false);
                }
                Piton!.crack = Tool.CreatePresentation(CrackPresentation, Marker.CrackRoot, Native.transform, true);
                Piton.vfx = Tool.CreatePresentation(BreakPresentation, Marker.BreakVfxRoot, Native.transform, false);
                Tool.BindSounds(Piton, CrackPresentation, BreakPresentation);
            }
            return new NativeClimbingFixtureBinding.RestPoint(Marker, Native, NativeView, Collider, Piton, NativeModel);
        }

        void RequireGraph(PllRestPointAuthoring Marker, ClimbHandle Native, BoxCollider Collider,
            PhotonView NativeView, GameObject ExpectedModel, GameObject? ExpectedCrack, GameObject? ExpectedBreak, BiomeId Biome)
        {
            if (!Marker.enabled || Marker.Id != Id || Marker.ModelRoot != ExpectedModel
                || Marker.HoldAnchor != Native.transform || Marker.InteractionCenter != Center
                || Marker.InteractionSize != Size || Marker.Profile != Profile || Marker.MountedModel != MountedModel
                || Marker.CrackPresentation != CrackPresentation || Marker.BreakPresentation != BreakPresentation
                || Marker.HangHook != HangHook || Marker.WarningHook != WarningHook || Marker.BreakHook != BreakHook
                || Marker.Placement != Placement || Marker.InsertionOffset != InsertionOffset
                || Marker.CrackRoot != ExpectedCrack || Marker.BreakVfxRoot != ExpectedBreak
                || !Native.enabled || Native.isPickaxe != (Profile == PllRestPointProfile.NativeFragileMountedTool)
                || NativeView.gameObject != Native.gameObject || Collider.gameObject != Native.gameObject
                || Collider.center != Center || Collider.size != Size || Collider.isTrigger || Collider.attachedRigidbody)
                throw new InvalidOperationException($"Biome '{Biome}' Rest Point '{Id}' changed its sealed graph.");
        }

        internal override bool TryBindGenerated(Component Source, BiomeId Biome,
            NativeSpawnAdmission Admission, FeatureOccurrence Occurrence, out NativeClimbingFixtureBinding Binding)
        {
            if (Source is PllRestPointAuthoring Marker && Marker.Id == Id
                && Authoring.TryMatchGenerated(Owner, Marker, Biome, "Rest Point", out var Root))
            {
                Binding = Bind(Root, Biome, Admission, Occurrence); return true;
            }
            Binding = null!; return false;
        }

        internal override void Validate(Transform Template, AuthoringCandidateGraph Graph, BiomeId Biome)
        {
            Owner.Validate(Template, Graph, Source.transform, Biome, "Rest Point");
            Authoring.Validate(Owner.BindingRoot, Biome, "Rest Point");
            Handle.Validate(Owner.BindingRoot, Biome, "Rest Point handle");
            View.Validate(Owner.BindingRoot, Biome, "Rest Point view");
            Interaction.Validate(Owner.BindingRoot, Biome, "Rest Point interaction bounds");
            Model.Validate(Owner.BindingRoot, Biome, "Rest Point model");
            if (Profile == PllRestPointProfile.Permanent) RequirePermanent(Source.transform, Biome);
            else
            {
                Fragile!.Validate(Owner.BindingRoot, Biome, "Mounted Tool");
                if (Fragile.Source.enabled || Source.GetComponentsInChildren<BreakableRopeAnchor>(true).Length != 0)
                    throw new InvalidOperationException("Mounted Tool has an active or competing break graph.");
                Crack?.Validate(Owner.BindingRoot, Biome, "Mounted crack");
                BreakVfx?.Validate(Owner.BindingRoot, Biome, "Mounted break effect");
            }
            if (PllClimbingFixtureShape.Failure(Authoring.Source) is not null
                || Interaction.Source.enabled || View.Source.ViewID != 0 || !View.Source.enabled
                || View.Source.OwnershipTransfer != OwnershipOption.Fixed || View.Source.ObservedComponents.Count != 0)
                throw new InvalidOperationException($"Biome '{Biome}' Rest Point '{Id}' changed before sealing.");
            RequireGraph(Authoring.Source, Handle.Source, Interaction.Source, View.Source, Model.Source,
                Crack?.Source, BreakVfx?.Source, Biome);
        }

        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)0); Owner.WriteManifest(Writer); Authoring.WriteManifest(Writer);
            Model.WriteManifest(Writer); Handle.WriteManifest(Writer); View.WriteManifest(Writer);
            Interaction.WriteManifest(Writer); Writer.Write(Id);
            ManifestEncoding.WriteVector(Writer, Center); ManifestEncoding.WriteVector(Writer, Size);
            Writer.Write((byte)Profile);
            if (RequiresMountedTool)
            {
                Writer.Write((byte)MountedModel); Writer.Write((byte)CrackPresentation); Writer.Write((byte)BreakPresentation);
                Writer.Write(HangHook); Writer.Write(WarningHook); Writer.Write(BreakHook);
                Writer.Write((byte)Placement); ManifestEncoding.WriteScalar(Writer, InsertionOffset);
                Fragile!.WriteManifest(Writer); Writer.Write(Crack is not null); Crack?.WriteManifest(Writer);
                Writer.Write(BreakVfx is not null); BreakVfx?.WriteManifest(Writer);
                if (External) (NativeTool ?? throw new InvalidOperationException("The mounted tool was not sealed.")).WriteManifest(Writer);
            }
        }
    }

    sealed class HangingRope : NativeClimbingFixtureContract
    {
        readonly ComponentBinding<PllHangingRopeAuthoring> Authoring;
        readonly ComponentBinding<RopeAnchorWithRope> Provider;
        readonly ComponentBinding<RopeAnchor> Anchor;
        readonly ComponentBinding<PhotonView> View;
        readonly TransformBinding Point;
        readonly ObjectBinding Ghost;
        readonly string Id;
        readonly float Segments;
        readonly float SpoolOutSeconds;
        readonly PllRopeAnchorBehavior AnchorBehavior;
        readonly ComponentBinding<BreakableRopeAnchor>? Breakable;
        readonly NativeRopeAppearance? Appearance;
        readonly float BreakMinimum, BreakMaximum, BreakSeconds, DropSegments;
        readonly string WarningHook, BreakHook;
        NativeHangingRopeSource? NativeSource;

        internal HangingRope(AuthoringBindingOwner Owner, PllHangingRopeAuthoring Source, BiomeId Biome)
            : base(Owner, Source.ModelRoot)
        {
            Authoring = ComponentBinding<PllHangingRopeAuthoring>.Create(Owner.BindingRoot, Source);
            Id = Source.Id; Segments = Source.Segments; SpoolOutSeconds = Source.SpoolOutSeconds;
            AnchorBehavior = Source.AnchorBehavior; Appearance = NativeRopeAppearance.Create(Source);
            BreakMinimum = Source.BreakTimeMinimum; BreakMaximum = Source.BreakTimeMaximum;
            BreakSeconds = Source.BreakAnimationSeconds; DropSegments = Source.DropSegments;
            WarningHook = Source.WarningHook; BreakHook = Source.BreakHook;
            if (AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor)
            {
                var Breakers = Source.GetComponents<BreakableRopeAnchor>();
                if (Breakers.Length != 1 || Breakers[0].enabled
                    || Source.GetComponentsInChildren<BreakableRopeAnchor>(true).Length != 1
                    || Source.GetComponentInChildren<ShittyPiton>(true)
                    || Source.GetComponentInChildren<BrokenRopeCheck>(true)
                    || Breakers[0].breakableTimeMinMax != new Vector2(BreakMinimum, BreakMaximum)
                    || Breakers[0].breakAnimTime != BreakSeconds || Breakers[0].dropSegments != DropSegments)
                    throw new ArgumentException("Breakable Rope has an incomplete staged anchor recipe.");
                Breakable = ComponentBinding<BreakableRopeAnchor>.Create(Owner.BindingRoot, Breakers[0]);
            }
            Point = TransformBinding.Create(Owner.BindingRoot, Source.AnchorPoint);
            Ghost = ObjectBinding.Create(Owner.BindingRoot, Source.GhostRoot);
            var Providers = Source.GetComponents<RopeAnchorWithRope>();
            var Anchors = Source.GetComponents<RopeAnchor>();
            if (Providers.Length != 1 || Providers[0].enabled || Providers[0].ropePrefab
                || Providers[0].ropeInstance || Providers[0].rope
                || !Providers[0].ropeSegmentLength.Equals(Segments) || !Providers[0].spoolOutTime.Equals(SpoolOutSeconds)
                || Anchors.Length != 1 || !Anchors[0].enabled || Anchors[0].anchorPoint != Source.AnchorPoint
                || Anchors[0].normalPart != Source.ModelRoot || Anchors[0].ghostPart != Source.GhostRoot
                || Source.GetComponentsInChildren<RopeAnchorWithRope>(true).Length != 1
                || Source.GetComponentsInChildren<PhotonView>(true).Length != 1)
                throw new ArgumentException($"Biome '{Biome}' Hanging Rope '{Id}' has an incomplete staged anchor graph.");
            Provider = ComponentBinding<RopeAnchorWithRope>.Create(Owner.BindingRoot, Providers[0]);
            Anchor = ComponentBinding<RopeAnchor>.Create(Owner.BindingRoot, Anchors[0]);
            View = ComponentBinding<PhotonView>.Create(Owner.BindingRoot, RequireView(Source.gameObject, Biome));
        }

        internal override bool RequiresRope => true;
        internal override Component Source => Authoring.Source;
        internal override void ResolveExternal(NativeHangingRopeSource? Rope, NativeMountedToolSource? Tool = null)
        {
            NativeSource = Rope ?? throw new InvalidOperationException("The native hanging rope source was not captured.");
            Appearance?.Seal(NativeSource);
        }
        internal override void ClearExternal() { NativeSource = null; Appearance?.Clear(); }

        internal override NativeClimbingFixtureBinding Bind(Transform Root, BiomeId Biome,
            NativeSpawnAdmission Admission, FeatureOccurrence Occurrence)
        {
            var Source = NativeSource ?? throw new InvalidOperationException($"Biome '{Biome}' Hanging Rope '{Id}' was not native-source sealed.");
            var Marker = Authoring.Resolve(Root, Biome, "Hanging Rope");
            var Native = Provider.Resolve(Root, Biome, "Hanging Rope provider");
            var NativeAnchor = Anchor.Resolve(Root, Biome, "Hanging Rope anchor");
            var NativeView = View.Resolve(Root, Biome, "Hanging Rope view");
            if (!Marker.enabled || Marker.Id != Id || Marker.ModelRoot != Model.Resolve(Root, Biome, "Hanging Rope model")
                || Marker.GhostRoot != Ghost.Resolve(Root, Biome, "Hanging Rope ghost")
                || Marker.AnchorPoint != Point.ResolveValidated(Root, Biome.Value, "Hanging Rope point")
                || !Marker.Segments.Equals(Segments) || !Marker.SpoolOutSeconds.Equals(SpoolOutSeconds)
                || Native.enabled || Native.gameObject != Marker.gameObject || NativeView.gameObject != Marker.gameObject
                || NativeAnchor.gameObject != Marker.gameObject || !NativeAnchor.enabled
                || NativeAnchor.normalPart != Marker.ModelRoot || NativeAnchor.ghostPart != Marker.GhostRoot
                || NativeAnchor.anchorPoint != Marker.AnchorPoint || !Native.ropeSegmentLength.Equals(Segments)
                || !Native.spoolOutTime.Equals(SpoolOutSeconds) || Marker.AnchorBehavior != AnchorBehavior
                || Marker.BreakTimeMinimum != BreakMinimum || Marker.BreakTimeMaximum != BreakMaximum
                || Marker.BreakAnimationSeconds != BreakSeconds || Marker.DropSegments != DropSegments
                || Marker.WarningHook != WarningHook || Marker.BreakHook != BreakHook
                || (Appearance is null ? Marker.RopeAppearance != PllRopeAppearance.Native : !Appearance.Matches(Marker)))
                throw new InvalidOperationException($"Biome '{Biome}' Hanging Rope '{Id}' changed its sealed graph.");
            Native.anchor = NativeAnchor; NativeAnchor.photonView = NativeView;
            Native.ropePrefab = Source.Prefab;
            return new NativeClimbingFixtureBinding.HangingRope(Marker, Native, NativeView,
                Source.Prefab, Admission, Occurrence, Breakable?.Resolve(Root, Biome, "Breakable Rope Anchor"), Appearance);
        }

        internal override bool TryBindGenerated(Component Source, BiomeId Biome,
            NativeSpawnAdmission Admission, FeatureOccurrence Occurrence, out NativeClimbingFixtureBinding Binding)
        {
            if (Source is PllHangingRopeAuthoring Marker && Marker.Id == Id
                && Authoring.TryMatchGenerated(Owner, Marker, Biome, "Hanging Rope", out var Root))
            {
                Binding = Bind(Root, Biome, Admission, Occurrence); return true;
            }
            Binding = null!; return false;
        }

        internal override void Validate(Transform Template, AuthoringCandidateGraph Graph, BiomeId Biome)
        {
            Owner.Validate(Template, Graph, Source.transform, Biome, "Hanging Rope");
            Authoring.Validate(Owner.BindingRoot, Biome, "Hanging Rope");
            Provider.Validate(Owner.BindingRoot, Biome, "Hanging Rope provider");
            Anchor.Validate(Owner.BindingRoot, Biome, "Hanging Rope anchor");
            View.Validate(Owner.BindingRoot, Biome, "Hanging Rope view");
            Point.Validate(Owner.BindingRoot, Biome.Value, "Hanging Rope point");
            Model.Validate(Owner.BindingRoot, Biome, "Hanging Rope model");
            Ghost.Validate(Owner.BindingRoot, Biome, "Hanging Rope ghost");
            if (AnchorBehavior == PllRopeAnchorBehavior.Permanent) RequirePermanent(Source.transform, Biome);
            else
            {
                Breakable!.Validate(Owner.BindingRoot, Biome, "Breakable Rope Anchor");
                if (Breakable.Source.enabled || Source.GetComponentInChildren<ShittyPiton>(true))
                    throw new InvalidOperationException("Breakable Rope has an active or competing break graph.");
            }
            var Marker = Authoring.Source;
            if (PllClimbingFixtureShape.Failure(Marker) is not null || Marker.Id != Id
                || !Marker.Segments.Equals(Segments) || !Marker.SpoolOutSeconds.Equals(SpoolOutSeconds)
                || Marker.AnchorBehavior != AnchorBehavior || Marker.BreakTimeMinimum != BreakMinimum
                || Marker.BreakTimeMaximum != BreakMaximum || Marker.BreakAnimationSeconds != BreakSeconds || Marker.DropSegments != DropSegments
                || Marker.WarningHook != WarningHook || Marker.BreakHook != BreakHook
                || (Appearance is null ? Marker.RopeAppearance != PllRopeAppearance.Native : !Appearance.Matches(Marker))
                || Provider.Source.enabled || Provider.Source.ropePrefab || Provider.Source.rope || Provider.Source.ropeInstance
                || View.Source.ViewID != 0 || !View.Source.enabled
                || View.Source.OwnershipTransfer != OwnershipOption.Fixed || View.Source.ObservedComponents.Count != 0
                || !Provider.Source.ropeSegmentLength.Equals(Segments) || !Provider.Source.spoolOutTime.Equals(SpoolOutSeconds)
                || Anchor.Source.anchorPoint != Point.Source || Anchor.Source.normalPart != Model.Source
                || Anchor.Source.ghostPart != Ghost.Source)
                throw new InvalidOperationException($"Biome '{Biome}' Hanging Rope '{Id}' changed before sealing.");
        }

        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)1); Owner.WriteManifest(Writer); Authoring.WriteManifest(Writer);
            Model.WriteManifest(Writer); Ghost.WriteManifest(Writer); Point.WriteManifest(Writer);
            Provider.WriteManifest(Writer); Anchor.WriteManifest(Writer); View.WriteManifest(Writer);
            Writer.Write(Id); ManifestEncoding.WriteScalar(Writer, Segments);
            ManifestEncoding.WriteScalar(Writer, SpoolOutSeconds);
            Writer.Write((byte)AnchorBehavior); Writer.Write(Appearance is not null); Appearance?.WriteManifest(Writer);
            if (AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor)
            {
                Breakable!.WriteManifest(Writer); ManifestEncoding.WriteScalar(Writer, BreakMinimum);
                ManifestEncoding.WriteScalar(Writer, BreakMaximum); ManifestEncoding.WriteScalar(Writer, BreakSeconds);
                ManifestEncoding.WriteScalar(Writer, DropSegments);
                Writer.Write(WarningHook); Writer.Write(BreakHook);
            }
            Writer.Write(NativeHangingRopeSource.ResourceAddress);
            if (External)
                (NativeSource ?? throw new InvalidOperationException("The hanging rope resource was not sealed.")).WriteManifest(Writer);
        }
    }
}
