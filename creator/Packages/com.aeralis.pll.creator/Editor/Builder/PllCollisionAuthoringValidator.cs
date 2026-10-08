using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEngine;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal static class PllCollisionAuthoringValidator
    {
        internal static CollisionGeometry[] Capture(SourceIdentity Identity, PresentationAuthoring Presentation)
        {
            var Result = new List<CollisionGeometry>();
            var Markers = DiscoverGraphComponents<PllCollisionAuthoring>(Identity.Graph);
            RequireGraphComponents(Identity.Graph, Markers, Identity.Label, "collision");
            var PendingMarkers = new HashSet<PllCollisionAuthoring>(Markers);
            var ExcludedRoots = new HashSet<Transform>();
            var CollidersByOwner = new Dictionary<Transform, Collider[]>();
            var ValidatedMeshes = new HashSet<Mesh>();
            Collider[] CollidersFor(Transform Owner)
            {
                if (!CollidersByOwner.TryGetValue(Owner, out var Colliders))
                {
                    Colliders = Owner.GetComponents<Collider>();
                    CollidersByOwner.Add(Owner, Colliders);
                }
                return Colliders;
            }
            void ValidateColliders(IReadOnlyList<Collider> Colliders, string Context, bool Dynamic)
            {
                foreach (var Collider in Colliders)
                {
                    if (!PllClimbSurfaceAuthoringValidator.IsSupportedColliderType(Collider)
                        || Collider.enabled && !Collider.isTrigger
                            && !PllClimbSurfaceAuthoringValidator.HasRequiredColliderData(Collider))
                        throw new InvalidOperationException(Context + " has unsupported or incomplete authored collision. Supply a supported solid shape.");
                    if (!Dynamic || !Collider.enabled || Collider.isTrigger
                        || Collider is not MeshCollider AuthoredMesh || AuthoredMesh.convex) continue;
                    var BodyOwner = Collider.attachedRigidbody;
                    if (Collider.attachedArticulationBody || BodyOwner && !BodyOwner.isKinematic)
                        throw new InvalidOperationException(Context + " has concave collision on a dynamic body. Supply convex authored collision.");
                }
            }
            foreach (var Visual in Presentation.Visuals)
                if (Visual.Root) ExcludedRoots.Add(Visual.Root.transform);
            foreach (var Effect in Presentation.Effects)
            {
                if (Effect.Root) ExcludedRoots.Add(Effect.Root.transform);
                if (Effect.PresentationRoot) ExcludedRoots.Add(Effect.PresentationRoot.transform);
            }
            foreach (var Resolver in Presentation.NativeResolvers)
            {
                if (Resolver.Root) ExcludedRoots.Add(Resolver.Root.transform);
                if (Resolver.SurfaceRenderer) ExcludedRoots.Add(Resolver.SurfaceRenderer.transform);
            }
            foreach (var Node in Identity.Graph.Nodes)
            {
                var Pending = new Stack<(Transform Target, Transform Owner,
                    PllCollisionAuthoring Marker, bool Dynamic)>();
                void AddScope(Transform Scope)
                {
                    if (ReservedSubtree(Node.SourceRoot.transform, Scope)) return;
                    var Owner = Scope;
                    var Dynamic = false;
                    for (var Current = Scope.parent; Current; Current = Current.parent)
                    {
                        if (ExcludedRoots.Contains(Current) || FocusedOwner(Current)) return;
                        var Body = Current.GetComponent<Rigidbody>() || Current.GetComponent<ArticulationBody>();
                        if (Body && Owner == Scope) Owner = Current;
                        Dynamic |= Body || Current.GetComponent<Animator>() || Current.GetComponent<Animation>();
                        if (Current == Node.SourceRoot.transform) break;
                    }
                    Pending.Push((Scope, Owner, null, Dynamic));
                }
                if (Node.IsRoot)
                {
                    AddScope(Identity.Campfire);
                    AddScope(Identity.Floor);
                }
                else AddScope(Node.SourceRoot.transform);
                while (Pending.Count != 0)
                {
                    var Branch = Pending.Pop();
                    var Target = Branch.Target;
                    if ((Target.GetComponent<PllRestPointAuthoring>() || Target.GetComponent<PllHangingRopeAuthoring>())
                        && Target.TryGetComponent<PllCollisionAuthoring>(out var FixtureCollision))
                    {
                        var FixtureContext = "Climbing fixture collision '" + PllHierarchy.NamePath(Node.SourceRoot.transform, Target) + "'";
                        if (Target.GetComponents<PllCollisionAuthoring>().Length != 1 || !FixtureCollision.enabled
                            || FixtureCollision.Mode is not (PllCollisionMode.Automatic or PllCollisionMode.Authored or PllCollisionMode.None))
                            throw new InvalidOperationException(FixtureContext + " requires one enabled Collision with a valid mode.");
                        var FixtureColliders = CollidersFor(Target);
                        if (FixtureCollision.Mode != PllCollisionMode.None && !HasSolid(FixtureColliders))
                            throw new InvalidOperationException(FixtureContext + " needs its authored solid shape. Climbing fixtures do not infer model collision.");
                        ValidateColliders(FixtureColliders, FixtureContext, Branch.Dynamic);
                        ValidateTransformPath(Node.SourceRoot.transform, Target, FixtureContext);
                        PendingMarkers.Remove(FixtureCollision);
                        Result.Add(new CollisionGeometry(Node, Target, Target, FixtureCollision,
                            FixtureCollision.Mode, null, FixtureColliders, FixtureColliders));
                    }
                    if (ExcludedRoots.Contains(Target) || FocusedOwner(Target)
                        || Target != Node.SourceRoot.transform
                            && Target.name is "RuntimeMarkers" or "Presentation") continue;
                    var Components = Target.GetComponents<PllCollisionAuthoring>();
                    var Marker = Components.Length == 0 ? Branch.Marker : Components[0];
                    var Context = "Collision geometry '" + PllHierarchy.NamePath(
                        Node.SourceRoot.transform, Target) + "'";
                    if (Components.Length != 0)
                    {
                        if (Components.Length != 1 || !Marker.enabled
                            || Marker.Mode is not (PllCollisionMode.Automatic
                                or PllCollisionMode.Authored or PllCollisionMode.None))
                            throw new InvalidOperationException(Context
                                + " requires one enabled Collision with a valid mode.");
                    }
                    var Body = Target.GetComponent<Rigidbody>() || Target.GetComponent<ArticulationBody>();
                    var Owner = Components.Length == 0 && !Body ? Branch.Owner : Target;
                    var Dynamic = Branch.Dynamic || Body
                        || Target.GetComponent<Animator>() || Target.GetComponent<Animation>();
                    var Renderer = Target.GetComponent<MeshRenderer>();
                    var Skinned = Target.GetComponent<SkinnedMeshRenderer>();
                    if (Renderer || Skinned)
                    {
                        if (Marker) PendingMarkers.Remove(Marker);
                        var Mode = Marker ? Marker.Mode : PllCollisionMode.Automatic;
                        var CollisionOwner = Target;
                        var TargetColliders = CollidersFor(Target);
                        var Colliders = TargetColliders;
                        var Solid = HasSolid(Colliders);
                        if (!Solid && Owner != Target)
                        {
                            var Proxy = CollidersFor(Owner);
                            if (HasSolid(Proxy))
                            {
                                CollisionOwner = Owner;
                                Colliders = Proxy;
                                Solid = true;
                            }
                        }
                        Mesh Mesh = null;
                        if (Mode != PllCollisionMode.None && !Solid)
                        {
                            if (Mode == PllCollisionMode.Authored)
                                throw new InvalidOperationException(Context
                                    + " has no authored solid collision. Add an enabled nontrigger Collider.");
                            if (Colliders.Length != 0)
                                throw new InvalidOperationException(Context
                                    + " has no enabled solid collision. Add a solid shape or select None.");
                            if (Dynamic || Skinned)
                                throw new InvalidOperationException(Context
                                    + " uses dynamic, animated, or skinned geometry. Supply its authored body collision.");
                            if (!Renderer.enabled)
                                throw new InvalidOperationException(Context
                                    + " has a disabled MeshRenderer. Supply authored collision or select None.");
                            var Filters = Target.GetComponents<MeshFilter>();
                            if (Target.GetComponents<MeshRenderer>().Length != 1 || Filters.Length != 1)
                                throw new InvalidOperationException(Context
                                    + " requires one MeshRenderer and one MeshFilter for automatic collision.");
                            Mesh = Filters[0].sharedMesh;
                            if (!Mesh || ValidatedMeshes.Add(Mesh))
                                PllClimbSurfaceAuthoringValidator.ValidateColliderMesh(Mesh, Context);
                            ValidateTransformPath(Node.SourceRoot.transform, Target, Context);
                        }
                        ValidateColliders(Colliders, Context, Dynamic);
                        Result.Add(new CollisionGeometry(Node, Target,
                            CollisionOwner, Marker, Mode, Mesh, Colliders, TargetColliders));
                    }
                    for (var Index = Target.childCount - 1; Index >= 0; Index--)
                        Pending.Push((Target.GetChild(Index), Owner, Marker, Dynamic));
                }
            }
            foreach (var Marker in PendingMarkers)
                throw new InvalidOperationException("Collision '" + Marker.name
                    + "' is outside ordinary geometry. Move it to its geometry or candidate owner.");
            return Result.ToArray();
        }

        static bool HasSolid(IReadOnlyList<Collider> Values)
        {
            foreach (var Value in Values)
                if (Value.enabled && !Value.isTrigger) return true;
            return false;
        }

        static bool FocusedOwner(Transform Target) =>
            Target.GetComponent<PropSpawner>()
            || Target.GetComponent<PllHazardAuthoring>()
            || Target.GetComponent<PllEntityAuthoring>()
            || Target.GetComponent<PllBreakablePropAuthoring>()
            || Target.GetComponent<PllSwingingPropAuthoring>()
            || Target.GetComponent<PllVineAuthoring>()
            || Target.GetComponent<PllRestPointAuthoring>()
            || Target.GetComponent<PllHangingRopeAuthoring>()
            || Target.GetComponent<PllCampfireAuthoring>()
            || Target.GetComponent<PllLuggageAuthoring>()
            || Target.GetComponent<PllStatueAuthoring>()
            || Target.GetComponent<PllItemSpawnerAuthoring>()
            || Target.GetComponent<Campfire>() || Target.GetComponent<Luggage>()
            || Target.GetComponent<RespawnChest>() || Target.GetComponent<Item>()
            || Target.GetComponent<Photon.Pun.PhotonView>()
            || Target.GetComponent<PLLNativeResolver>()
            || Target.GetComponent<PllCustomVisualAuthoring>()
            || Target.GetComponent<PllVisualAuthoring>()
            || Target.GetComponent<PllEffectAuthoring>()
            || Target.GetComponent<PllEffectZoneAuthoring>()
            || Target.GetComponent<PllFogAuthoring>()
            || Target.GetComponent<PllSoundEmitterAuthoring>()
            || Target.GetComponent<PllBiomeStingerAuthoring>()
            || Target.GetComponent<ParticleSystem>()
            || Target.GetComponent<AudioSource>() || Target.GetComponent<WaterZone>();
    }

    internal sealed class CollisionGeometry
    {
        internal CollisionGeometry(PrefabNode Node, Transform Target,
            Transform CollisionOwner, PllCollisionAuthoring Marker,
            PllCollisionMode Mode, Mesh GeneratedMesh, Collider[] Colliders,
            Collider[] TargetColliders)
        {
            this.Node = Node;
            this.Target = Target;
            this.CollisionOwner = CollisionOwner;
            this.Marker = Marker;
            this.Mode = Mode;
            this.GeneratedMesh = GeneratedMesh;
            this.Colliders = Colliders;
            this.TargetColliders = TargetColliders;
        }

        internal PrefabNode Node { get; }
        internal Transform Target { get; }
        internal Transform CollisionOwner { get; }
        internal PllCollisionAuthoring Marker { get; }
        internal PllCollisionMode Mode { get; }
        internal Mesh GeneratedMesh { get; }
        internal Collider[] Colliders { get; }
        internal Collider[] TargetColliders { get; }
        internal string Outcome => Mode == PllCollisionMode.None
            ? "Decorative; retained authored shapes"
            : GeneratedMesh ? "Automatic collision; generated static MeshCollider and climbable layer"
            : "Authored collision; preserved collision owner and shapes";
    }
}
