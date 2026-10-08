using UnityEngine;

namespace PLL.Library;

internal sealed class ResolvedSegment
{
    internal ResolvedSegment(
        GameObject Root,
        CustomSegmentRuntime Runtime,
        GameObject SegmentParent,
        GameObject SegmentCampfire,
        RespawnChest? CampfireRespawnChest,
        RespawnChest? SegmentRespawnChest,
        PllBiomePrefabAnchors? AuthoringAnchors,
        FinalForceOccurrence FinalForce,
        Transform Entry,
        Transform Exit,
        Transform Start,
        SegmentPort EntryPort,
        SegmentPort ExitPort,
        SegmentPort StartPort)
    {
        this.Root = new TransformState(Root.transform);
        this.Runtime = Runtime;
        this.SegmentParent = SegmentParent;
        this.SegmentCampfire = SegmentCampfire;
        this.CampfireRespawnChest = CampfireRespawnChest;
        this.SegmentRespawnChest = SegmentRespawnChest;
        this.AuthoringAnchors = AuthoringAnchors;
        this.FinalForce = FinalForce;
        this.Entry = Entry;
        this.Exit = Exit;
        this.Start = Start;
        SegmentParentState = new TransformState(SegmentParent.transform);
        SegmentCampfireState = new TransformState(SegmentCampfire.transform);
        EntryState = new TransformState(Entry);
        ExitState = new TransformState(Exit);
        StartState = new TransformState(Start);
        this.EntryPort = EntryPort;
        this.ExitPort = ExitPort;
        this.StartPort = StartPort;
    }

    internal TransformState Root { get; }
    internal CustomSegmentRuntime Runtime { get; }
    internal GameObject SegmentParent { get; }
    internal GameObject SegmentCampfire { get; }
    internal RespawnChest? CampfireRespawnChest { get; }
    internal RespawnChest? SegmentRespawnChest { get; }
    internal PllBiomePrefabAnchors? AuthoringAnchors { get; }
    internal FinalForceOccurrence FinalForce { get; }
    internal Transform Entry { get; }
    internal Transform Exit { get; }
    internal Transform Start { get; }
    internal TransformState SegmentParentState { get; }
    internal TransformState SegmentCampfireState { get; }
    internal TransformState EntryState { get; }
    internal TransformState ExitState { get; }
    internal TransformState StartState { get; }
    internal SegmentPort EntryPort { get; }
    internal SegmentPort ExitPort { get; }
    internal SegmentPort StartPort { get; }

    internal void FreezePorts()
    {
        EntryPort.Freeze(); ExitPort.Freeze(); StartPort.Freeze();
    }
}

internal sealed class TransformState
{
    internal TransformState(Transform Value)
    {
        this.Value = Value;
        Parent = Value.parent;
        Pose = new Pose(Value.position, Value.rotation);
        LocalScale = Value.localScale;
        LossyScale = Value.lossyScale;
    }

    internal Transform Value { get; }
    internal Transform? Parent { get; }
    internal Pose Pose { get; }
    internal Vector3 LocalScale { get; }
    internal Vector3 LossyScale { get; }
}
