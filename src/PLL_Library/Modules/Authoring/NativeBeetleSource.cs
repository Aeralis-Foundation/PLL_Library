using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeBeetleSource
{
    readonly byte[] Fingerprint;
    readonly int ViewCount;
    readonly int ViewCapacity;

    NativeBeetleSource(GameObject Prefab)
    {
        this.Prefab = Prefab;
        SkinDescriptor = NativeEntitySkinDescriptor.Capture(Prefab);
        ViewCount = Prefab.GetComponentsInChildren<PhotonView>(true).Length;
        ViewCapacity = PhotonNetwork.MAX_VIEW_IDS - 1;
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            var Graph = DeterministicObjectIdentity.HashObjectHierarchy(Prefab.transform, "PLL/NativeBeetle/v1");
            Writer.Write(Graph.Length); Writer.Write(Graph);
            Writer.Write(ViewCount); Writer.Write(ViewCapacity);
            var Beetle = Prefab.GetComponent<Beetle>();
            ManifestEncoding.WriteScalar(Writer, Beetle.bonkForce);
            ManifestEncoding.WriteScalar(Writer, Beetle.bonkForceUp);
            ManifestEncoding.WriteScalar(Writer, Beetle.bonkRange);
            ManifestEncoding.WriteScalar(Writer, Beetle.ragdollTime);
            ManifestEncoding.WriteScalar(Writer, Beetle.allBeetlesIgnoreAfterAttackTime.x);
            ManifestEncoding.WriteScalar(Writer, Beetle.allBeetlesIgnoreAfterAttackTime.y);
            ManifestEncoding.WriteScalar(Writer, Beetle.movementSpeed);
            ManifestEncoding.WriteScalar(Writer, Beetle.turnRate);
            ManifestEncoding.WriteScalar(Writer, Beetle.aggroDistance);
            ManifestEncoding.WriteScalar(Writer, Beetle.targetSwitchCooldown);
            ManifestEncoding.WriteScalar(Writer, Beetle.targetCheckCooldown);
            ManifestEncoding.WriteScalar(Writer, Beetle.closeToTargetFactor);
            ManifestEncoding.WriteScalar(Writer, Beetle.overShootFactor);
            ManifestEncoding.WriteScalar(Writer, Beetle.attackStartDistance);
            ManifestEncoding.WriteScalar(Writer, Beetle.attackDistance);
            ManifestEncoding.WriteScalar(Writer, Beetle.attackCooldown);
            ManifestEncoding.WriteScalar(Writer, Beetle.postAttackRest);
            ManifestEncoding.WriteScalar(Writer, Beetle.whiffRefund);
            ManifestEncoding.WriteScalar(Writer, Beetle.maxVelocityToStartFlipping);
            ManifestEncoding.WriteScalar(Writer, Beetle.minTimeToStartFlipping);
            ManifestEncoding.WriteScalar(Writer, Beetle.maxStandingAngle);
            ManifestEncoding.WriteScalar(Writer, Beetle.moveTimeBeforeAttack);
            ManifestEncoding.WriteScalar(Writer, Beetle.spring);
            ManifestEncoding.WriteScalar(Writer, Beetle.drag);
            ManifestEncoding.WriteScalar(Writer, Beetle.attackTime);
            ManifestEncoding.WriteScalar(Writer, Beetle.attackAngle);
            ManifestEncoding.WriteScalar(Writer, Beetle.sleepDistance);
            Writer.Write(Beetle.sleeping); Writer.Write((int)Beetle._mobState);
            Writer.Write(Beetle.attacking); Writer.Write(Beetle.hasBrain);
            Writer.Write(Beetle.forceNoMovement);
            ManifestEncoding.WriteScalar(Writer, Beetle.inRangeForAttackTime);
            var Physics = Prefab.GetComponent<PhysicsSyncer>();
            Writer.Write(Physics.shouldSync); Writer.Write(Physics.overrideTargetRigTransform);
            ManifestEncoding.WriteScalar(Writer, Physics.maxAngleChangePerSecond);
        }
        Fingerprint = Stream.ToArray();
    }

    internal GameObject Prefab { get; }
    internal NativeEntitySkinDescriptor SkinDescriptor { get; }

    internal void AdmitInitialCount(int Count)
    {
        if (Count > ViewCapacity / ViewCount)
            throw new InvalidOperationException(
                $"Beetle initial count {Count} exceeds the native PhotonView capacity {ViewCapacity / ViewCount}.");
    }

    internal static NativeBeetleSource Capture(NativeSegmentCatalog Catalog)
    {
        var Pending = new Queue<GameObject>();
        var Seen = new HashSet<GameObject>();
        var Root = Catalog.RequireOrdinary("Roots").SegmentParent;
        GameObject? Provider = null;
        Pending.Enqueue(Root); Seen.Add(Root);
        while (Pending.Count != 0)
        {
            var Current = Pending.Dequeue();
            foreach (var Spawner in Current.GetComponentsInChildren<PropSpawner>(true))
            {
                var Candidates = NativePrefabContracts.EffectiveProps(Spawner);
                if (Candidates is null) continue;
                foreach (var Candidate in Candidates)
                {
                    if (!Candidate)
                        throw new InvalidOperationException("PEAK's Roots provider has a missing native candidate.");
                    if (!Seen.Add(Candidate)) continue;
                    if (Candidate.GetComponent<Beetle>())
                    {
                        if (Provider)
                            throw new InvalidOperationException("PEAK's Roots provider has distinct native Beetle candidates.");
                        Validate(Candidate); Provider = Candidate;
                    }
                    Pending.Enqueue(Candidate);
                }
            }
        }
        return Provider ? new NativeBeetleSource(Provider)
            : throw new InvalidOperationException("PEAK's Roots provider has no native Beetle candidate.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
    }

    static void Validate(GameObject Prefab)
    {
        var Beetle = Prefab.GetComponent<Beetle>();
        var Physics = Prefab.GetComponent<PhysicsSyncer>();
        var View = Prefab.GetComponent<PhotonView>();
        var Root = Prefab.transform;
        if (Prefab.GetComponents<Beetle>().Length != 1 || !Beetle.enabled
            || Prefab.GetComponentsInChildren<LevelGenStep>(true).Length != 0
            || Prefab.GetComponentsInChildren<ILevelGenFinalizer>(true).Length != 0
            || !Physics || !Physics.enabled
            || Physics.overrideTargetRigTransform && (!Physics.targetRigTransformOverride
                || !TransformOwnership.Contains(Root, Physics.targetRigTransformOverride))
            || !Prefab.GetComponent<Rigidbody>() || !View || !View.enabled || View.ViewID != 0
            || !Beetle.mesh || !Beetle.visuals || !Beetle.anim || !Beetle.bonkPoint
            || !Beetle.anim.runtimeAnimatorController
            || !TransformOwnership.Contains(Root, Beetle.mesh)
            || !TransformOwnership.Contains(Root, Beetle.visuals)
            || !TransformOwnership.Contains(Root, Beetle.anim.transform)
            || !TransformOwnership.Contains(Root, Beetle.bonkPoint))
            throw new InvalidOperationException("PEAK's native Beetle candidate has an incomplete Mob, physics, animation, or Photon graph.");
        foreach (var Collider in Prefab.GetComponentsInChildren<Collider>(true))
            if (Collider.enabled && !Collider.isTrigger) return;
        throw new InvalidOperationException("PEAK's native Beetle candidate has no solid collision body.");
    }
}
