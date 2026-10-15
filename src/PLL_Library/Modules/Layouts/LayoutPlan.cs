using System;
using System.Collections.Generic;

namespace PLL.Library;

internal static class LayoutPlan
{
    internal static void AttachTerminalPeak(
        List<LayoutSegmentSource> Segments,
        PeakAttachmentSource PeakPrototype,
        bool ForceAttachment,
        bool GemlessNadir,
        bool ValidNadir,
        string Description)
    {
        if (Segments is null) throw new ArgumentNullException(nameof(Segments));
        if (PeakPrototype is null) throw new ArgumentNullException(nameof(PeakPrototype));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        if (Segments.Count == 0) return;
        var Owner = Segments[Segments.Count - 1];
        if (Owner is PeakAttachmentSource)
            throw new InvalidOperationException($"{Description} already ends with a structural attachment.");
        if (Owner is NadirLayoutSegmentSource)
        {
            var Predecessor = Segments.Count >= 2 ? Segments[Segments.Count - 2] : null;
            var ContextualPeak = (Predecessor as NativeOrdinarySegmentSource)?.ContextualPeak;
            var Peak = ContextualPeak ?? PeakPrototype;
            if (Predecessor is null || !CanAttachPeak(Predecessor, Peak))
            {
                if (ForceAttachment)
                    throw new InvalidOperationException(
                        $"{Description} cannot attach Peak before terminal Nadir.");
                Validate(Segments, Description);
                return;
            }
            if (ContextualPeak is null
                && !RequiresScoutsHonor(Segments, Segments.Count - 1, GemlessNadir, ValidNadir)
                && !ForceAttachment)
            {
                Validate(Segments, Description);
                return;
            }
            Segments.Insert(Segments.Count - 1, Peak);
            Validate(Segments, Description);
            return;
        }

        PeakAttachmentSource? Attachment = Owner is NativeOrdinarySegmentSource Native
            ? Native.ContextualPeak
            : null;
        if (Attachment is null && ForceAttachment) Attachment = PeakPrototype;
        if (Attachment is not null)
        {
            if (!CanAttachPeak(Owner, Attachment))
                throw new InvalidOperationException(
                    $"{Description} cannot attach Peak after terminal segment '{Owner.Id}'.");
            Segments.Add(Attachment);
        }
        Validate(Segments, Description);
    }

    internal static bool[] ResolveNadirScoutsHonor(
        IReadOnlyList<LayoutSegmentSource> Segments,
        bool GemlessNadir,
        bool ValidNadir,
        string Description)
    {
        if (Segments is null) throw new ArgumentNullException(nameof(Segments));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var SlotCount = 0;
        foreach (var Source in Segments)
            if (Source is not PeakAttachmentSource) SlotCount++;
        var Result = new bool[SlotCount];
        var SlotIndex = 0;
        for (var i = 0; i < Segments.Count; i++)
        {
            if (Segments[i] is PeakAttachmentSource) continue;
            if (Segments[i] is NadirLayoutSegmentSource
                && RequiresScoutsHonor(Segments, i, GemlessNadir, ValidNadir))
            {
                if (!HasImmediatePeakBridge(Segments, i))
                    throw new InvalidOperationException(
                        $"{Description} places an honor-required Nadir at logical position {SlotIndex + 1} without an immediate usable Peak bridge checkpoint.");
                Result[SlotIndex] = true;
            }
            SlotIndex++;
        }
        return Result;
    }

    internal static bool CanAttachPeak(LayoutSegmentSource Owner, LayoutSegmentSource Peak) =>
        Owner is OrdinaryLayoutSegmentSource
        && Peak is PeakAttachmentSource PeakAttachment
        && (Owner is NativeOrdinarySegmentSource { ContextualPeak: { } Contextual }
            ? ReferenceEquals(Contextual, PeakAttachment)
            : true);

    internal static bool IsValidPeakPosition(IReadOnlyList<LayoutSegmentSource> Segments, int Index) =>
        Index > 0 && (Index == Segments.Count - 1
            || Index == Segments.Count - 2 && Segments[Segments.Count - 1] is NadirLayoutSegmentSource);

    internal static void Validate(IReadOnlyList<LayoutSegmentSource> Segments, string Description)
    {
        if (Segments is null) throw new ArgumentNullException(nameof(Segments));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var OrdinarySegments = 0;
        var PeakIndex = -1;
        for (var i = 0; i < Segments.Count; i++)
        {
            var Current = Segments[i];
            if (Current is PeakAttachmentSource)
            {
                if (PeakIndex >= 0)
                    throw new InvalidOperationException(
                        $"{Description} places Peak more than once, at indexes {PeakIndex} and {i}.");
                PeakIndex = i;
                if (!IsValidPeakPosition(Segments, i) || !CanAttachPeak(Segments[i - 1], Current))
                    throw new InvalidOperationException(
                        $"{Description} places Peak at index {i}; Peak must follow an eligible ordinary segment.");
                continue;
            }

            OrdinarySegments++;
            if (OrdinarySegments > NavigationRoles.MaximumOrdinarySegments)
                throw new InvalidOperationException(
                    $"{Description} contains more than {NavigationRoles.MaximumOrdinarySegments} ordinary segments.");
            if (i == 0) continue;
            try { _ = LayoutTransition.Resolve(Segments[i - 1], Current); }
            catch (InvalidOperationException Exception)
            {
                throw new InvalidOperationException(
                    $"{Description} has an incompatible transition at indexes {i - 1} and {i}. {Exception.Message}",
                    Exception);
            }
        }
        if (OrdinarySegments == 0)
            throw new InvalidOperationException($"{Description} contains no ordinary segments.");
    }

    static bool HasImmediatePeakBridge(IReadOnlyList<LayoutSegmentSource> Segments, int NadirIndex)
    {
        if (NadirIndex <= 0) return false;
        var Previous = Segments[NadirIndex - 1];
        if (Previous is PeakAttachmentSource Peak)
            return NadirIndex >= 2 && CanAttachPeak(Segments[NadirIndex - 2], Peak);
        return Previous is NativeOrdinarySegmentSource { ContextualPeak: { } Contextual }
            && Contextual.BridgeRoot is not null;
    }

    static bool RequiresScoutsHonor(
        IReadOnlyList<LayoutSegmentSource> Segments,
        int NadirIndex,
        bool GemlessNadir,
        bool ValidNadir)
    {
        if (GemlessNadir || NadirIndex <= 0 || NadirIndex >= Segments.Count
            || Segments[NadirIndex] is not NadirLayoutSegmentSource) return false;
        var Prerequisites = 0;
        for (var i = 0; i < NadirIndex; i++)
            Prerequisites |= Segments[i].AmuletOpportunityCoverage;
        return !ValidNadir || Prerequisites == 15;
    }
}
