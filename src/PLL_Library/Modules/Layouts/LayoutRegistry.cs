using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace PLL.Library;

internal static class LayoutRegistry
{
    internal const string Protocol = PLLLibrary.PluginGuid + "/layout-registry/v3";
    static readonly object Gate = new();
    static readonly Dictionary<string, LayoutDefinition> PendingLayouts = new(StringComparer.Ordinal);
    static readonly Dictionary<string, LayoutDefinition> PendingDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, LayoutModification> PendingModifications = new(StringComparer.Ordinal);
    static volatile RegisteredState? Registered;

    internal static string ManifestRevision => State.ManifestRevision;

    internal static void Register(LayoutDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        lock (Gate)
        {
            if (Registered is not null) throw new InvalidOperationException("The layout registry is sealed.");
            if (PendingLayouts.ContainsKey(definition.Id.Value))
                throw new InvalidOperationException($"Layout '{definition.Id}' is already registered.");
            if (PendingDisplayNames.TryGetValue(definition.DisplayName, out var named))
                throw new InvalidOperationException(
                    $"Layout display name '{definition.DisplayName}' is already owned by layout '{named.Id}'.");
            PendingLayouts.Add(definition.Id.Value, definition);
            PendingDisplayNames.Add(definition.DisplayName, definition);
        }
    }

    internal static void Register(LayoutModification modification)
    {
        if (modification is null) throw new ArgumentNullException(nameof(modification));
        lock (Gate)
        {
            if (Registered is not null) throw new InvalidOperationException("The layout registry is sealed.");
            if (PendingModifications.ContainsKey(modification.Id))
                throw new InvalidOperationException($"Layout modification '{modification.Id}' is already registered.");
            PendingModifications.Add(modification.Id, modification);
        }
    }

    internal static void Seal()
    {
        lock (Gate)
        {
            if (Registered is not null) return;
            var Layouts = new LayoutDefinition[PendingLayouts.Count];
            PendingLayouts.Values.CopyTo(Layouts, 0);
            Array.Sort(Layouts, (Left, Right) => Left.Id.CompareTo(Right.Id));
            var Modifications = new LayoutModification[PendingModifications.Count];
            PendingModifications.Values.CopyTo(Modifications, 0);
            Array.Sort(Modifications, (Left, Right) => StringComparer.Ordinal.Compare(Left.Id, Right.Id));

            var LayoutsById = new Dictionary<LayoutId, LayoutDefinition>(Layouts.Length);
            foreach (var Layout in Layouts) LayoutsById.Add(Layout.Id, Layout);
            foreach (var Modification in Modifications)
                if (!Modification.TargetLayoutId.IsDefault
                    && !LayoutsById.ContainsKey(Modification.TargetLayoutId))
                    throw new InvalidOperationException(
                        $"Layout modification '{Modification.Id}' targets unknown layout '{Modification.TargetLayoutId}'.");

            var LayoutSnapshot = new ReadOnlyCollection<LayoutDefinition>(Layouts);
            var ModificationSnapshot = new ReadOnlyCollection<LayoutModification>(Modifications);
            Registered = new RegisteredState(
                LayoutSnapshot,
                ModificationSnapshot,
                LayoutsById,
                ManifestEncoding.CreateRevision(Writer => WriteManifest(Writer, LayoutSnapshot, ModificationSnapshot)));
        }
    }

    internal static IReadOnlyList<LayoutDefinition> Snapshot() => State.Layouts;

    internal static bool HasDefaultModification()
    {
        foreach (var Modification in State.Modifications)
            if (Modification.TargetLayoutId.IsDefault) return true;
        return false;
    }

    internal static bool TryGet(LayoutId id, out LayoutDefinition definition)
    {
        if (!id.IsValid || id.IsDefault)
        {
            definition = null!;
            return false;
        }
        return State.LayoutsById.TryGetValue(id, out definition!);
    }

    internal static BiomeMetadataLayoutSolver.Sequence ResolveEffectiveOrder(
        LayoutSegmentCatalog Catalog,
        string? selection,
        Func<BiomeMetadataLayoutSolver.Sequence> defaultResolver,
        bool applyDefaultModifications = true)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (defaultResolver is null) throw new ArgumentNullException(nameof(defaultResolver));
        var Registry = State;
        if (TrySelect(Registry, selection, out var Selected))
        {
            ValidateLayoutId(Catalog, Selected.Id);
            var Description = $"Named layout '{Selected.Id}'";
            var resolved = BiomeMetadataLayoutSolver.CreateStrictSequence(
                Catalog, Selected.SegmentTokens, Description);
            return ApplyModifications(Registry, Catalog, Selected.Id, resolved, Description);
        }
        if (!applyDefaultModifications) return defaultResolver();
        return ApplyModifications(
            Registry,
            Catalog,
            LayoutId.Default,
            defaultResolver(),
            "Default layout");
    }

    static RegisteredState State => Registered
        ?? throw new InvalidOperationException("The layout registry has not been sealed.");

    static void ValidateLayoutId(LayoutSegmentCatalog Catalog, LayoutId Id)
    {
        if (!BiomeMetadataLayoutSolver.TryParseToken(
                Catalog, Id.Value, $"Named layout ID '{Id}'", out _)) return;
        throw new InvalidOperationException(
            $"Named layout ID '{Id}' conflicts with a segment token in the live catalog.");
    }

    static bool TrySelect(RegisteredState state, string? selection, out LayoutDefinition definition)
    {
        if (selection is not null)
        {
            var value = selection.Trim();
            if (LayoutId.TryParse(value, out var id) && !id.IsDefault
                && state.LayoutsById.TryGetValue(id, out definition!)) return true;
        }
        definition = null!;
        return false;
    }

    static BiomeMetadataLayoutSolver.Sequence ApplyModifications(
        RegisteredState state,
        LayoutSegmentCatalog Catalog,
        LayoutId target,
        BiomeMetadataLayoutSolver.Sequence baseLayout,
        string description)
    {
        var ModificationIndex = 0;
        while (ModificationIndex < state.Modifications.Count
            && state.Modifications[ModificationIndex].TargetLayoutId != target) ModificationIndex++;
        if (ModificationIndex == state.Modifications.Count) return baseLayout;

        var baseTokens = baseLayout.Tokens;
        var insertions = new List<BiomeMetadataLayoutSolver.Token>?[baseTokens.Count + 1];
        var removed = new bool[baseTokens.Count];
        for (; ModificationIndex < state.Modifications.Count; ModificationIndex++)
        {
            var Modification = state.Modifications[ModificationIndex];
            if (Modification.TargetLayoutId != target) continue;
            for (var editIndex = 0; editIndex < Modification.Edits.Count; editIndex++)
            {
                var edit = Modification.Edits[editIndex];
                var AnchorIndex = ResolveAnchor(Catalog, baseTokens, Modification, edit, editIndex);
                if (edit.Operation == LayoutEditOperation.Remove)
                {
                    removed[AnchorIndex] = true;
                    continue;
                }

                var boundary = edit.Operation == LayoutEditOperation.InsertBefore
                    ? AnchorIndex
                    : AnchorIndex + 1;
                var values = insertions[boundary] ??=
                    new List<BiomeMetadataLayoutSolver.Token>(edit.SegmentTokens.Count);
                for (var tokenIndex = 0; tokenIndex < edit.SegmentTokens.Count; tokenIndex++)
                    values.Add(BiomeMetadataLayoutSolver.CreateStrictToken(
                        Catalog,
                        edit.SegmentTokens[tokenIndex],
                        $"Layout modification '{Modification.Id}' edit {editIndex} insertion {tokenIndex}"));
            }
        }

        var result = new List<BiomeMetadataLayoutSolver.Token>(baseTokens.Count);
        for (var i = 0; i <= baseTokens.Count; i++)
        {
            if (insertions[i] is { } boundary) result.AddRange(boundary);
            if (i < baseTokens.Count && !removed[i]) result.Add(baseTokens[i]);
        }
        return new BiomeMetadataLayoutSolver.Sequence(
            result,
            $"{description} after registered modifications");
    }

    static int ResolveAnchor(
        LayoutSegmentCatalog Catalog,
        IReadOnlyList<BiomeMetadataLayoutSolver.Token> baseLayout,
        LayoutModification modification,
        LayoutEdit edit,
        int editIndex)
    {
        var anchor = BiomeMetadataLayoutSolver.CreateStrictToken(
            Catalog,
            edit.Anchor,
            $"Layout modification '{modification.Id}' edit {editIndex} anchor");
        var matches = 0;
        for (var i = 0; i < baseLayout.Count; i++)
        {
            if (!baseLayout[i].MatchesAnchor(anchor)) continue;
            if (matches++ == edit.Occurrence) return i;
        }
        throw new InvalidOperationException(
            $"Layout modification '{modification.Id}' edit {editIndex} anchor '{edit.Anchor}' occurrence {edit.Occurrence} was not found in unmodified layout '{modification.TargetLayoutId}'. Found {matches} matching ordinary segment occurrence(s).");
    }

    static void WriteManifest(
        BinaryWriter writer,
        IReadOnlyList<LayoutDefinition> layouts,
        IReadOnlyList<LayoutModification> Modifications)
    {
        writer.Write(Protocol);
        writer.Write(layouts.Count);
        foreach (var layout in layouts)
        {
            writer.Write(layout.Id.Value);
            writer.Write(layout.DisplayName);
            writer.Write(layout.ContentRevision);
            writer.Write(layout.SegmentTokens.Count);
            foreach (var token in layout.SegmentTokens) writer.Write(token);
        }
        writer.Write(Modifications.Count);
        foreach (var Modification in Modifications)
        {
            writer.Write(Modification.Id);
            writer.Write(Modification.ContentRevision);
            writer.Write(Modification.TargetLayoutId.Value);
            writer.Write(Modification.Edits.Count);
            foreach (var Edit in Modification.Edits)
            {
                writer.Write((byte)Edit.Operation);
                writer.Write(Edit.Anchor);
                writer.Write(Edit.Occurrence);
                writer.Write(Edit.SegmentTokens.Count);
                foreach (var Token in Edit.SegmentTokens) writer.Write(Token);
            }
        }
    }

    sealed class RegisteredState
    {
        internal RegisteredState(
            IReadOnlyList<LayoutDefinition> Layouts,
            IReadOnlyList<LayoutModification> Modifications,
            Dictionary<LayoutId, LayoutDefinition> LayoutsById,
            string ManifestRevision)
        {
            this.Layouts = Layouts;
            this.Modifications = Modifications;
            this.LayoutsById = LayoutsById;
            this.ManifestRevision = ManifestRevision;
        }

        internal IReadOnlyList<LayoutDefinition> Layouts { get; }
        internal IReadOnlyList<LayoutModification> Modifications { get; }
        internal IReadOnlyDictionary<LayoutId, LayoutDefinition> LayoutsById { get; }
        internal string ManifestRevision { get; }
    }
}
