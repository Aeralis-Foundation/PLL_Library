using System;
using UnityEngine;

namespace PLL.Library;

internal readonly struct LayoutTransition
{
    LayoutTransition(LayoutPort Exit, LayoutPort Entry, Quaternion Rotation)
    {
        Connector = Exit.Connector;
        this.Exit = Exit.Pose;
        this.Entry = Entry.Pose;
        this.Rotation = Rotation;
    }

    internal string Connector { get; }
    internal Pose Exit { get; }
    internal Pose Entry { get; }
    internal Quaternion Rotation { get; }

    internal static LayoutTransition Create(
        LayoutPort Exit, LayoutPort Entry, Quaternion Rotation) => new(Exit, Entry, Rotation);

    internal static LayoutTransition Resolve(LayoutSegmentSource Previous, LayoutSegmentSource Next)
    {
        if (Previous is null) throw new ArgumentNullException(nameof(Previous));
        if (Next is null) throw new ArgumentNullException(nameof(Next));
        if (TryResolve(Previous, Next, out var Transition)) return Transition;
        throw new InvalidOperationException(
            $"No compatible segment connector exists from '{Previous.Id}' to '{Next.Id}'.");
    }

    internal static bool TryResolve(
        LayoutSegmentSource Previous,
        LayoutSegmentSource Next,
        out LayoutTransition Transition)
    {
        if (Previous is null) throw new ArgumentNullException(nameof(Previous));
        if (Next is null) throw new ArgumentNullException(nameof(Next));
        foreach (var Exit in Previous.ExitPorts)
            foreach (var Entry in Next.EntryPorts)
                if (TryCreate(Previous, Next, Exit, Entry, out Transition)) return true;
        Transition = default;
        return false;
    }

    static bool TryCreate(
        LayoutSegmentSource Previous,
        LayoutSegmentSource Next,
        LayoutPort Exit,
        LayoutPort Entry,
        out LayoutTransition Transition)
    {
        var PeakToNadir = Previous is PeakAttachmentSource && Next is NadirLayoutSegmentSource;
        if (PeakToNadir) Entry = ((NadirLayoutSegmentSource)Next).PeakSuccessorEntry;
        var ExitRotation = Previous.AuthoredRotation(Exit.Pose);
        var EntryRotation = Next.AuthoredRotation(Entry.Pose);
        if (PllConnectorContract.Compatible(
                Exit.Connector, ExitRotation, Entry.Connector, EntryRotation))
        {
            Transition = new LayoutTransition(Exit, Entry, Quaternion.identity);
            return true;
        }
        if (!PeakToNadir || !string.Equals(Exit.Connector, Entry.Connector, StringComparison.Ordinal))
        {
            Transition = default;
            return false;
        }

        var Rotation = ExitRotation * Quaternion.Inverse(EntryRotation);
        if (!UnityValueContract.Finite(Rotation))
            throw new InvalidOperationException(
                "PEAK's native Peak-to-Nadir connector rotation is invalid.");
        Rotation = Rotation.normalized;
        if (Vector3.Dot(Rotation * Vector3.up, Vector3.up) < 0.999999f)
            throw new InvalidOperationException(
                "PEAK's native Peak-to-Nadir connector rotation is not horizontal.");
        Transition = new LayoutTransition(Exit, Entry, Rotation);
        return true;
    }

}
