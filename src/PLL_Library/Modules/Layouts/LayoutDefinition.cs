using System;
using System.Collections.Generic;

namespace PLL.Library;

public sealed class LayoutDefinition
{
    public LayoutDefinition(
        LayoutId id,
        string displayName,
        string contentRevision,
        IReadOnlyList<string> segmentTokens)
    {
        if (!id.IsValid || id.IsDefault)
            throw new ArgumentException("A named layout requires a non-default valid layout ID.", nameof(id));
        Id = id;
        DisplayName = LayoutContract.DisplayName(displayName, nameof(displayName));
        ContentRevision = LayoutContract.Revision(contentRevision, nameof(contentRevision));
        SegmentTokens = LayoutContract.Tokens(segmentTokens, nameof(segmentTokens));
    }

    public LayoutId Id { get; }
    public string DisplayName { get; }
    public string ContentRevision { get; }
    public IReadOnlyList<string> SegmentTokens { get; }
}

public enum LayoutEditOperation : byte
{
    InsertBefore = 0,
    InsertAfter = 1,
    Remove = 2
}

public sealed class LayoutEdit
{
    LayoutEdit(
        LayoutEditOperation operation,
        string anchor,
        int occurrence,
        IReadOnlyList<string>? segmentTokens)
    {
        Operation = operation;
        Anchor = LayoutContract.SegmentToken(anchor, nameof(anchor));
        if (occurrence < 0) throw new ArgumentOutOfRangeException(nameof(occurrence));
        Occurrence = occurrence;
        SegmentTokens = operation == LayoutEditOperation.Remove
            ? Array.Empty<string>()
            : LayoutContract.Tokens(segmentTokens!, nameof(segmentTokens));
    }

    public LayoutEditOperation Operation { get; }
    public string Anchor { get; }
    public int Occurrence { get; }
    public IReadOnlyList<string> SegmentTokens { get; }

    public static LayoutEdit InsertBefore(
        string anchor,
        int occurrence,
        IReadOnlyList<string> segmentTokens) =>
        new(LayoutEditOperation.InsertBefore, anchor, occurrence, segmentTokens);

    public static LayoutEdit InsertAfter(
        string anchor,
        int occurrence,
        IReadOnlyList<string> segmentTokens) =>
        new(LayoutEditOperation.InsertAfter, anchor, occurrence, segmentTokens);

    public static LayoutEdit Remove(string anchor, int occurrence) =>
        new(LayoutEditOperation.Remove, anchor, occurrence, null);
}

public sealed class LayoutModification
{
    public LayoutModification(
        string id,
        string contentRevision,
        LayoutId targetLayoutId,
        IReadOnlyList<LayoutEdit> edits)
    {
        Id = LayoutContract.QualifiedId(id, nameof(id));
        ContentRevision = LayoutContract.Revision(contentRevision, nameof(contentRevision));
        if (!targetLayoutId.IsValid)
            throw new ArgumentException("A layout modification requires a valid target layout ID.", nameof(targetLayoutId));
        TargetLayoutId = targetLayoutId;
        if (edits is null) throw new ArgumentNullException(nameof(edits));
        if (edits.Count == 0) throw new ArgumentException("A layout modification must contain an edit.", nameof(edits));
        var copy = new LayoutEdit[edits.Count];
        for (var i = 0; i < copy.Length; i++)
            copy[i] = edits[i] ?? throw new ArgumentException("A layout modification cannot contain a null edit.", nameof(edits));
        Edits = Array.AsReadOnly(copy);
    }

    public string Id { get; }
    public string ContentRevision { get; }
    public LayoutId TargetLayoutId { get; }
    public IReadOnlyList<LayoutEdit> Edits { get; }
}

static class LayoutContract
{
    const int MaximumTextLength = 128;

    internal static string QualifiedId(string value, string parameter)
    {
        if (value is null) throw new ArgumentNullException(parameter);
        if (!LayoutId.TryParse(value, out var id) || id.IsDefault)
            throw new ArgumentException("A stable ID must be a canonical lowercase qualified ID.", parameter);
        return value;
    }

    internal static string Revision(string value, string parameter)
    {
        ValidateText(value, parameter, false, false);
        return value;
    }

    internal static string DisplayName(string value, string parameter)
    {
        ValidateText(value, parameter, true, true);
        return value;
    }

    internal static string SegmentToken(string value, string parameter)
    {
        ValidateText(value, parameter, true, true);
        if (value.IndexOf('>') >= 0)
            throw new ArgumentException("A segment token cannot contain the layout separator '>'.", parameter);
        if (IsPeakToken(value))
            throw new ArgumentException("Peak is a structural ending, not a layout segment token.", parameter);
        if (IsCalderaToken(value))
            throw new ArgumentException("Caldera cannot be selected on its own. Use Volcano.", parameter);
        if (IsGloomToken(value))
            throw new ArgumentException("Gloom cannot be selected on its own. Use Citadel.", parameter);
        return value;
    }

    internal static bool IsPeakToken(string value) =>
        value.Equals("Peak", StringComparison.OrdinalIgnoreCase)
        || value.Equals(SegmentId.Peak.Value, StringComparison.OrdinalIgnoreCase)
        || value.Equals(NativeBiomeIds.Peak.Value, StringComparison.OrdinalIgnoreCase);

    internal static bool IsCalderaToken(string value) =>
        value.Equals("Caldera", StringComparison.OrdinalIgnoreCase)
        || value.Equals(SegmentId.Caldera.Value, StringComparison.OrdinalIgnoreCase)
        || value.Equals(NativeBiomeIds.Caldera.Value, StringComparison.OrdinalIgnoreCase);

    internal static bool IsGloomToken(string value) =>
        value.Equals("Gloom", StringComparison.OrdinalIgnoreCase)
        || value.Equals(SegmentId.Gloom.Value, StringComparison.OrdinalIgnoreCase)
        || value.Equals(NativeBiomeIds.Gloom.Value, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> Tokens(IReadOnlyList<string> values, string parameter)
    {
        if (values is null) throw new ArgumentNullException(parameter);
        if (values.Count == 0)
            throw new ArgumentException("A segment-token sequence cannot be empty.", parameter);
        var copy = new string[values.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = SegmentToken(values[i], parameter);
        return Array.AsReadOnly(copy);
    }

    static void ValidateText(string value, string parameter, bool allowWhitespace, bool requireTrimmed)
    {
        if (value is null) throw new ArgumentNullException(parameter);
        if (value.Length == 0 || value.Length > MaximumTextLength)
            throw new ArgumentException($"The value must contain 1 to {MaximumTextLength} characters.", parameter);
        if (requireTrimmed && !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("The value cannot have leading or trailing whitespace.", parameter);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (char.IsControl(character) || !allowWhitespace && char.IsWhiteSpace(character))
                throw new ArgumentException("The value contains an unsupported control or whitespace character.", parameter);
            if (char.IsHighSurrogate(character))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                    throw new ArgumentException("The value contains malformed UTF-16.", parameter);
            }
            else if (char.IsLowSurrogate(character))
                throw new ArgumentException("The value contains malformed UTF-16.", parameter);
        }
    }
}
