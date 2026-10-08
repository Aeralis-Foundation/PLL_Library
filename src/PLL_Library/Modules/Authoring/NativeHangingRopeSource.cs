using System;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeHangingRopeSource
{
    internal const string ResourceAddress = "RopeDynamic";
    readonly byte[] Fingerprint;

    NativeHangingRopeSource(GameObject Prefab, Rope Rope, RopeBoneVisualizer Visual)
    {
        this.Prefab = Prefab;
        SkinDescriptor = NativeEntitySkinDescriptor.Capture(Prefab);
        RopeMaterial = Visual.ropeMaterial; GhostMaterial = Visual.ghostMaterial;
        VisualBounds = Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true).localBounds;
        Fingerprint = NativeNetworkPrefabGraph.Fingerprint(Prefab, "PLL/NativeHangingRope/v1", Writer =>
        {
            Writer.Write(ResourceAddress); Writer.Write(global::Rope.MaxSegments);
            ManifestEncoding.WriteVector(Writer, VisualBounds.center);
            ManifestEncoding.WriteVector(Writer, VisualBounds.extents);
            SkinDescriptor.WriteManifest(Writer);
            ManifestEncoding.WriteScalar(Writer, Rope.spacing);
            ManifestEncoding.WriteScalar(Writer, Rope.climberGravity);
            ManifestEncoding.WriteScalar(Writer, Rope.slurpTime);
            Writer.Write(Rope.antigrav); Writer.Write(Rope.isHelicopterRope);
            ManifestEncoding.WriteScalar(Writer, Visual.segmentMod);
            Writer.Write(Visual.withRotOfStartPos);
            Writer.Write(DeterministicObjectIdentity.RelativeComponentStructuralPath(Visual.boneRoot.transform, Prefab.transform));
            var GhostMaterial = VisualFingerprintWriter.HashSerializedObject(Visual.ghostMaterial, "PLL/RopeMaterial/v1");
            var RopeMaterial = VisualFingerprintWriter.HashSerializedObject(Visual.ropeMaterial, "PLL/RopeMaterial/v1");
            Writer.Write(GhostMaterial.Length); Writer.Write(GhostMaterial);
            Writer.Write(RopeMaterial.Length); Writer.Write(RopeMaterial);
            WriteSegment(Writer, Rope.ropeSegmentPrefab);
            WriteSegment(Writer, Rope.remoteSegmentPrefab);
        });
    }

    internal GameObject Prefab { get; }
    internal NativeEntitySkinDescriptor SkinDescriptor { get; }
    internal Material RopeMaterial { get; }
    internal Material GhostMaterial { get; }
    internal Bounds VisualBounds { get; }

    internal static NativeHangingRopeSource Capture()
    {
        var Prefab = NativeNetworkPrefabGraph.Resource(ResourceAddress);
        var Rope = Prefab.GetComponent<Rope>();
        var View = Prefab.GetComponent<PhotonView>();
        var Sync = Prefab.GetComponent<RopeSyncer>();
        var Visual = Prefab.GetComponent<RopeBoneVisualizer>();
        if (!Rope || !Rope.enabled || Rope.isHelicopterRope || Rope.antigrav || Rope.creatorLeft
            || Rope.attachmenState != global::Rope.ATTACHMENT.unattached
            || global::Rope.MaxSegments != PllHangingRopeAuthoring.MaximumSegments
            || !View || !View.enabled || View.OwnershipTransfer != OwnershipOption.Fixed
            || View.ObservedComponents.Count != 1 || View.ObservedComponents[0] != Sync
            || Prefab.GetComponentsInChildren<PhotonView>(true).Length != 1
            || !Sync || !Sync.enabled || !Prefab.GetComponent<RopeClimbingAPI>()
            || !Visual || !Visual.enabled || !Visual.boneRoot || !Visual.ghostMaterial || !Visual.ropeMaterial
            || !TransformOwnership.Contains(Prefab.transform, Visual.boneRoot.transform)
            || !Rope.ropeSegmentPrefab || !Rope.remoteSegmentPrefab
            || !Rope.ropeSegmentPrefab.GetComponent<RopeSegment>()
            || !Rope.ropeSegmentPrefab.GetComponent<Rigidbody>()
            || !Rope.ropeSegmentPrefab.GetComponent<ConfigurableJoint>()
            || !Rope.ropeSegmentPrefab.GetComponent<Collider>()
            || !Rope.remoteSegmentPrefab.GetComponent<Collider>()
            || !Rope.remoteSegmentPrefab.GetComponent<RopeSegment>()
            || Rope.spacing <= 0f || Rope.slurpTime <= 0f)
            throw new InvalidOperationException("PEAK's hanging rope resource has an incomplete native simulation, sync or visual graph.");
        NativeNetworkPrefabGraph.RequireSkin(Prefab, PllHangingRopeAuthoring.MaximumSegments);
        return new NativeHangingRopeSource(Prefab, Rope, Visual);
    }

    static void WriteSegment(BinaryWriter Writer, GameObject Prefab)
    {
        var Graph = DeterministicObjectIdentity.HashObjectHierarchy(Prefab.transform, "PLL/NativeRopeSegment/v1");
        Writer.Write(Graph.Length); Writer.Write(Graph);
        var Collider = Prefab.GetComponent<Collider>();
        Writer.Write(Collider.includeLayers.value); Writer.Write(Collider.excludeLayers.value);
        Writer.Write(Collider.layerOverridePriority); Writer.Write(Collider.providesContacts);
        var Segment = Prefab.GetComponent<RopeSegment>();
        Writer.Write(Segment.angle); Writer.Write(Segment.displayName);
        if (Prefab.TryGetComponent<Rigidbody>(out var Body))
        {
            Writer.Write(Body.linearDamping); Writer.Write(Body.angularDamping);
            Writer.Write(Body.automaticCenterOfMass); ManifestEncoding.WriteVector(Writer, Body.centerOfMass);
            Writer.Write(Body.automaticInertiaTensor); ManifestEncoding.WriteVector(Writer, Body.inertiaTensor);
            ManifestEncoding.WritePose(Writer, new Pose(Vector3.zero, Body.inertiaTensorRotation));
            Writer.Write(Body.includeLayers.value); Writer.Write(Body.excludeLayers.value);
            Writer.Write((int)Body.constraints); Writer.Write((int)Body.collisionDetectionMode);
            Writer.Write((int)Body.interpolation);
            Writer.Write(Body.solverIterations); Writer.Write(Body.solverVelocityIterations);
        }
        if (!Prefab.TryGetComponent<ConfigurableJoint>(out var Joint)) return;
        ManifestEncoding.WriteVector(Writer, Joint.anchor); ManifestEncoding.WriteVector(Writer, Joint.connectedAnchor);
        Writer.Write(Joint.autoConfigureConnectedAnchor);
        ManifestEncoding.WriteVector(Writer, Joint.axis); ManifestEncoding.WriteVector(Writer, Joint.secondaryAxis);
        Writer.Write((int)Joint.xMotion); Writer.Write((int)Joint.yMotion); Writer.Write((int)Joint.zMotion);
        Writer.Write((int)Joint.angularXMotion); Writer.Write((int)Joint.angularYMotion); Writer.Write((int)Joint.angularZMotion);
        WriteLimit(Writer, Joint.linearLimit); WriteLimit(Writer, Joint.lowAngularXLimit);
        WriteLimit(Writer, Joint.highAngularXLimit); WriteLimit(Writer, Joint.angularYLimit); WriteLimit(Writer, Joint.angularZLimit);
        Writer.Write(Joint.linearLimitSpring.spring); Writer.Write(Joint.linearLimitSpring.damper);
        Writer.Write(Joint.angularXLimitSpring.spring); Writer.Write(Joint.angularXLimitSpring.damper);
        Writer.Write(Joint.angularYZLimitSpring.spring); Writer.Write(Joint.angularYZLimitSpring.damper);
        ManifestEncoding.WriteVector(Writer, Joint.targetPosition); ManifestEncoding.WriteVector(Writer, Joint.targetVelocity);
        ManifestEncoding.WritePose(Writer, new Pose(Vector3.zero, Joint.targetRotation));
        ManifestEncoding.WriteVector(Writer, Joint.targetAngularVelocity);
        Writer.Write((int)Joint.rotationDriveMode);
        WriteDrive(Writer, Joint.xDrive); WriteDrive(Writer, Joint.yDrive); WriteDrive(Writer, Joint.zDrive);
        WriteDrive(Writer, Joint.angularXDrive); WriteDrive(Writer, Joint.angularYZDrive); WriteDrive(Writer, Joint.slerpDrive);
        Writer.Write((int)Joint.projectionMode); Writer.Write(Joint.projectionDistance); Writer.Write(Joint.projectionAngle);
        Writer.Write(Joint.configuredInWorldSpace); Writer.Write(Joint.swapBodies);
        Writer.Write(Joint.breakForce); Writer.Write(Joint.breakTorque);
        Writer.Write(Joint.enableCollision); Writer.Write(Joint.enablePreprocessing);
        ManifestEncoding.WriteScalar(Writer, Joint.massScale); ManifestEncoding.WriteScalar(Writer, Joint.connectedMassScale);
    }

    static void WriteLimit(BinaryWriter Writer, SoftJointLimit Limit)
    {
        Writer.Write(Limit.limit); Writer.Write(Limit.bounciness); Writer.Write(Limit.contactDistance);
    }

    static void WriteDrive(BinaryWriter Writer, JointDrive Drive)
    {
        Writer.Write(Drive.positionSpring); Writer.Write(Drive.positionDamper);
        Writer.Write(Drive.maximumForce); Writer.Write(Drive.useAcceleration);
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}
