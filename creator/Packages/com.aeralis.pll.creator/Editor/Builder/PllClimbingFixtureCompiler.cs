using System;
using System.Collections.Generic;
using Photon.Pun;
using PLL.Library;
using UnityEngine;
using static PLL.Creator.Editor.PllBuildArtifacts;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal static class PllClimbingFixtureCompiler
    {
        internal static void Validate(SourceIdentity Identity, FixtureAuthoring Fixtures)
        {
            RequireGraphComponents(Identity.Graph, Fixtures.RestPoints, Identity.Label, "rest points");
            RequireGraphComponents(Identity.Graph, Fixtures.HangingRopes, Identity.Label, "hanging ropes");
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            var Roots = new List<Transform>();
            foreach (var Source in Fixtures.RestPoints)
            {
                ValidateComponent(Source);
                RequireRoot(Source, Source.Id, Ids, Roots);
            }
            foreach (var Source in Fixtures.HangingRopes)
            {
                ValidateComponent(Source);
                RequireRoot(Source, Source.Id, Ids, Roots);
            }
        }

        internal static void ValidateComponent(Component Source)
        {
            var Failure = Source is PllRestPointAuthoring Rest
                ? PllClimbingFixtureShape.Failure(Rest)
                : PllClimbingFixtureShape.Failure((PllHangingRopeAuthoring)Source);
            if (Failure != null) throw new InvalidOperationException(Failure);
            if (Source is PllHangingRopeAuthoring Rope && Rope.RopeAppearance != PllRopeAppearance.Native
                && (!Rope.RopeMesh || Rope.RopeNativeRigFingerprint.Length == 0))
                throw new InvalidOperationException("Custom Rope has no prepared skin. Bake Automatic Rope Skin or bind a compatible captured skin.");
            if (Source is PllRestPointAuthoring Point && Point.HoldAnchor.GetComponents<Collider>().Length != 0)
                throw new InvalidOperationException("Rest Point hold anchor must not already own collision.");
            ValidateNewSource(Source.transform.root.gameObject, Source.gameObject,
                Source is PllRestPointAuthoring RestPoint ? RestPoint.Id : ((PllHangingRopeAuthoring)Source).Id, Source);
        }

        internal static void ValidateNewSource(GameObject Owner, GameObject Target, string Id, Component Source = null)
        {
            var Root = Target.transform;
            var Anchors = Owner.GetComponent<PllBiomePrefabAnchors>();
            if (!TransformOwnership.DiscoverableWhenActivated(Owner.transform, Root)
                || TransformOwnership.HasPropSpawnerInPath(Owner.transform, Root)
                || Anchors && (Target == Anchors.FloorRoot || Target == Anchors.CampfireRoot
                    || !TransformOwnership.Contains(Anchors.FloorRoot.transform, Root)
                        && !TransformOwnership.Contains(Anchors.CampfireRoot.transform, Root)))
                throw new InvalidOperationException("Climbing fixture must belong to an active stable floor/campfire branch or a reachable candidate.");
            foreach (var Existing in Owner.GetComponentsInChildren<PllRestPointAuthoring>(true))
                if (Existing != Source && (Existing.Id == Id || TransformOwnership.Overlaps(Existing.transform, Root)))
                    throw new InvalidOperationException("Climbing fixture IDs and roots must be distinct in the owning prefab.");
            foreach (var Existing in Owner.GetComponentsInChildren<PllHangingRopeAuthoring>(true))
                if (Existing != Source && (Existing.Id == Id || TransformOwnership.Overlaps(Existing.transform, Root)))
                    throw new InvalidOperationException("Climbing fixture IDs and roots must be distinct in the owning prefab.");
            if (Target.GetComponentInChildren<PhotonView>(true) || Target.GetComponentInChildren<Rigidbody>(true)
                || Target.GetComponentInChildren<ClimbHandle>(true) || Target.GetComponentInChildren<RopeAnchor>(true)
                || Target.GetComponentInChildren<RopeAnchorWithRope>(true))
                throw new InvalidOperationException("Climbing fixture source must not contain preinstalled native mechanics or moving bodies.");
        }

        static void RequireRoot(Component Source, string Id,
            HashSet<string> Ids, List<Transform> Roots)
        {
            if (!Ids.Add(Id)) throw new InvalidOperationException("Climbing fixture IDs must be unique in the biome.");
            var Root = Source.transform;
            foreach (var Existing in Roots)
                if (TransformOwnership.Overlaps(Existing, Root))
                    throw new InvalidOperationException("Climbing fixture roots must not overlap.");
            Roots.Add(Root);
        }

        internal static void TranslateRestPoints(GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllRestPointAuthoring> Sources)
        {
            foreach (var Source in Sources)
            {
                var Marker = MapComponent(SourceRoot, StagedRoot, Source, "Rest Point");
                var Root = Marker.HoldAnchor.gameObject;
                Root.layer = 0;
                Root.AddComponent<PhotonView>().OwnershipTransfer = OwnershipOption.Fixed;
                var Handle = Root.AddComponent<ClimbHandle>();
                Handle.isPickaxe = Marker.Profile == PllRestPointProfile.NativeFragileMountedTool;
                var Collider = Root.AddComponent<BoxCollider>();
                Collider.center = Marker.InteractionCenter; Collider.size = Marker.InteractionSize;
                Collider.enabled = false; Collider.isTrigger = false;
                if (Marker.Profile == PllRestPointProfile.NativeFragileMountedTool)
                    Root.AddComponent<ShittyPiton>().enabled = false;
            }
        }

        internal static void TranslateHangingRopes(GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllHangingRopeAuthoring> Sources)
        {
            foreach (var Source in Sources)
            {
                var Marker = MapComponent(SourceRoot, StagedRoot, Source, "Hanging Rope");
                Marker.gameObject.AddComponent<PhotonView>().OwnershipTransfer = OwnershipOption.Fixed;
                var Anchor = Marker.gameObject.AddComponent<RopeAnchor>();
                Anchor.normalPart = Marker.ModelRoot; Anchor.ghostPart = Marker.GhostRoot;
                Anchor.anchorPoint = Marker.AnchorPoint;
                var Provider = Marker.gameObject.AddComponent<RopeAnchorWithRope>();
                Provider.ropeSegmentLength = Marker.Segments; Provider.spoolOutTime = Marker.SpoolOutSeconds;
                Provider.enabled = false;
                if (Marker.AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor)
                {
                    var Breakable = Marker.gameObject.AddComponent<BreakableRopeAnchor>();
                    Breakable.enabled = false;
                    Breakable.breakableTimeMinMax = new Vector2(Marker.BreakTimeMinimum, Marker.BreakTimeMaximum);
                    Breakable.breakAnimTime = Marker.BreakAnimationSeconds; Breakable.dropSegments = Marker.DropSegments;
                }
            }
        }
    }
}
