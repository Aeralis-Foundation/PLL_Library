using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class ProgressProjection
{
    static readonly MethodInfo CharacterSpawnProgressMoveNext = MapTransitionAdapter.BindIterator(
        AccessTools.DeclaredMethod(typeof(CharacterSpawner), nameof(CharacterSpawner.SpawnDeadAtBaseCamp), [typeof(bool)])
            ?? throw new MissingMethodException(typeof(CharacterSpawner).FullName, nameof(CharacterSpawner.SpawnDeadAtBaseCamp)),
        typeof(CharacterSpawner), out _);
    static readonly MethodInfo SetRichPresenceMethod = AccessTools.DeclaredMethod(
        typeof(MountainProgressHandler), "SetRichPresence",
        new[] { typeof(MountainProgressHandler.ProgressPoint) })
        ?? throw new MissingMethodException(
            typeof(MountainProgressHandler).FullName, "SetRichPresence");

    internal ProgressState State { get; } = new();
    internal void Release() => State.Clear();
    internal sealed class ProgressState
    {
        internal MountainProgressHandler? Owner;
        internal MountainProgressHandler.ProgressPoint[] Templates = Array.Empty<MountainProgressHandler.ProgressPoint>();
        internal List<Biome.BiomeType>? Skipped;
        internal int Max;
        internal bool WasInitialized;
        internal GameObject? Root;
        internal readonly Dictionary<MountainProgressHandler.ProgressPoint, int> Slots = new();
        internal readonly Dictionary<MountainProgressHandler.ProgressPoint, string> Titles = new();
        internal PeakProgress? Peak;
        internal int[] PointIndices = Array.Empty<int>();

        internal void Clear()
        {
            Owner = null;
            Templates = Array.Empty<MountainProgressHandler.ProgressPoint>();
            Skipped = null;
            Max = 0;
            WasInitialized = false;
            Root = null;
            Slots.Clear();
            Titles.Clear();
            Peak = null;
            PointIndices = Array.Empty<int>();
        }
    }

    internal sealed class PeakProgress
    {
        internal PeakProgress(MountainProgressHandler.ProgressPoint Point, int SlotIndex, Vector3 Forward)
        {
            this.Point = Point; this.SlotIndex = SlotIndex; this.Forward = Forward;
        }

        internal MountainProgressHandler.ProgressPoint Point { get; }
        internal int SlotIndex { get; }
        internal Vector3 Forward { get; }
    }

    static ProgressState Progress => Map.CurrentRun!.Progress.State;
    internal static bool InitializeProgress(MountainProgressHandler Handler)
    {
        if (Map.CurrentRun is null) return true;
        if (!Map.HasPublishedLayout)
        {
            CaptureProgressTemplates(Handler);
            return true;
        }
        var PointCount = Map.Slots.Length;
        var PointIndices = new int[Map.Slots.Length];
        var progressIndex = 0;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            PointIndices[i] = progressIndex++;
            if (Map.Slots[i].PeakAttachment)
            {
                PointCount++;
                progressIndex++;
            }
        }
        var IndicesMatch = Progress.PointIndices.Length == PointIndices.Length;
        for (var i = 0; IndicesMatch && i < PointIndices.Length; i++)
            IndicesMatch = Progress.PointIndices[i] == PointIndices[i];
        if (Progress.Owner == Handler && Progress.Root && Handler.progressPoints.Length == PointCount
            && IndicesMatch) return false;

        if (Progress.Owner != Handler || Progress.Templates.Length == 0)
        {
            if (!CaptureProgressTemplates(Handler))
            {
                Progress.Slots.Clear();
                Progress.Peak = null;
                Progress.Titles.Clear();
                Progress.PointIndices = Array.Empty<int>();
                return true;
            }
        }
        if (Progress.Root) UnityEngine.Object.DestroyImmediate(Progress.Root);
        Progress.Slots.Clear();
        Progress.Peak = null;
        Progress.Titles.Clear();
        Progress.PointIndices = PointIndices;

        Progress.Root = new GameObject("PLL_ProgressPoints");
        Progress.Root.transform.SetParent(Handler.transform, false);
        var Points = new MountainProgressHandler.ProgressPoint[PointCount];
        var pointIndex = 0;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            var Source = Map.Slots[i].Source;
            MountainProgressHandler.ProgressPoint Template;
            Biome.BiomeType Biome;
            bool OccurrencePosition;
            string Title;
            ACHIEVEMENTTYPE Achievement;
            switch (Source)
            {
                case CustomLayoutSegmentSource Custom:
                    Template = Custom.Registration.ProgressPoint;
                    Biome = LayoutSegmentBehavior.CarrierBiome(Custom);
                    OccurrencePosition = true;
                    Title = Template.title;
                    Achievement = Template.achievement;
                    break;
                case NativeOrdinarySegmentSource Native:
                    Template = Native.NativeProgressPoint;
                    Biome = Native.NativeBiome;
                    OccurrencePosition = false;
                    Title = Template.title;
                    Achievement = Template.achievement;
                    break;
                case PeakAttachmentSource Peak:
                    Template = Peak.NativeProgressPoint;
                    Biome = Peak.NativeBiome;
                    OccurrencePosition = false;
                    Title = Template.title;
                    Achievement = Template.achievement;
                    break;
                case NadirLayoutSegmentSource Nadir:
                    Template = Progress.Templates[Progress.Templates.Length - 1];
                    Biome = Nadir.NativeBiome;
                    OccurrencePosition = true;
                    Title = Nadir.HeroTitle.textIndex;
                    Achievement = default;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Layout segment source type '{Source.GetType().FullName}' is outside the closed source model.");
            }
            var point = CreateProgressPoint(
                Template,
                Title,
                Biome,
                Achievement,
                OccurrencePosition
                    ? Map.Slots[i].PlacedEntry
                    : Template.transform
                        ? Map.Slots[i].Frame.Point(Template.transform.position)
                        : Map.Slots[i].Segment.segmentParent.transform.position,
                pointIndex);
            Points[pointIndex++] = point;
            Progress.Slots.Add(point, i);
            if (BiomeAuthoringCatalog.TryGet(Source.Id, out var authoring))
            {
                if (authoring.DisplayTitle.Length != 0) Progress.Titles.Add(point, authoring.DisplayTitle);
                if (authoring.EnterSounds.Count != 0)
                    point.clip = authoring.EnterSounds[i % authoring.EnterSounds.Count];
            }
            if (!Map.Slots[i].PeakAttachment) continue;
            if (Progress.Peak is not null)
                throw new InvalidOperationException("The published layout contains more than one Peak progress point.");
            var PeakSource = Map.Slots[i].PeakSource
                ?? throw new InvalidOperationException(
                    $"Segment '{Source.Id}' lost its Peak attachment source.");
            var PeakTemplate = (PeakSource as PeakAttachmentSource)?.NativeProgressPoint
                ?? throw new InvalidOperationException("PEAK's native summit progress template is unavailable.");
            var PeakExit = PlacementPlan.PlacedPeakExitPose(Map.Slots[i]);
            var PeakPoint = CreateProgressPoint(
                PeakTemplate,
                PeakTemplate.title,
                PeakTemplate.biome,
                PeakTemplate.achievement,
                PeakExit.position,
                pointIndex);
            Points[pointIndex++] = PeakPoint;
            Progress.Slots.Add(PeakPoint, i);
            Progress.Peak = new PeakProgress(PeakPoint, i, PeakExit.rotation * Vector3.forward);
        }

        Handler.progressPoints = Points;
        Handler._skippedBiomes = new List<Biome.BiomeType>();
        Handler.maxProgressPointReached = -1;
        Handler._isInitialized = true;
        return false;
    }
    internal static int ExpandedProgressIndex(int SlotIndex)
    {
        if (!Map.HasPublishedLayout) return SlotIndex;
        if (Progress.PointIndices.Length != Map.Slots.Length)
            throw new InvalidOperationException("The published map layout has no expanded progress index map.");
        if (SlotIndex >= 0 && SlotIndex < Progress.PointIndices.Length) return Progress.PointIndices[SlotIndex];
        if (SlotIndex == Map.Slots.Length && Progress.Owner && Progress.Owner.progressPoints.Length != 0)
            return Progress.Owner.progressPoints.Length - 1;
        throw new InvalidOperationException($"Map slot {SlotIndex} has no expanded progress point.");
    }
    internal static int ExpandedNavigationProgressIndex(int RoleValue)
    {
        if (!Map.HasPublishedLayout) return RoleValue;
        var Role = (Segment)RoleValue;
        if (NavigationRoles.TryDecode(Role, out var index) && index < Map.Slots.Length)
            return ExpandedProgressIndex(index);
        if (Role == Segment.Void) return ExpandedProgressIndex(Map.Slots.Length);
        if (Role == Segment.Peak) return ExpandedProgressIndex(PeakLifecycle.PeakSlot()) + 1;
        throw new InvalidOperationException($"Map segment role {Role} has no expanded progress point.");
    }
    internal static bool PeakProgressReached(int SlotIndex) =>
        Progress.Peak is { } Peak && SlotIndex == Peak.SlotIndex && Peak.Point.Reached;

    static global::Campfire RequiredSlotCampfire(int Index)
    {
        var Campfire = SpawnTrackerTransaction.SlotCampfire(Index);
        return Campfire
            ? Campfire
            : throw new InvalidOperationException($"Map slot {Index} has no required campfire.");
    }

    static bool LastSeenCampfireIsSafe()
    {
        if (Map.Index >= 0 && Map.Index < Map.Slots.Length
            && Map.Slots[Map.Index].ProgressionBoundary != NativeProgressionBoundaryKind.None)
            return !OrbFogHandler.IsFoggingCurrentSegment || NativeProgressionBoundaryReached(Map.Slots[Map.Index]);
        var Handler = Map.Owner;
        if (Handler && NadirRuntime.CurrentSegmentIsNadir(Handler)) return !LavaRising.LavaActive(Segment.TheKiln);
        if (Map.Index >= 0 && Map.Index < Map.Slots.Length && LayoutSegmentBehavior.Resolve(Map.Slots[Map.Index].Source)?.Role == Segment.TheKiln)
            return !LavaRising.LavaActive(Segment.TheKiln);
        return !OrbFogHandler.IsFoggingCurrentSegment || RequiredSlotCampfire(Map.Index).EveryoneInRange();
    }

    internal static bool PreviousSegmentIsStillBaseCamp()
    {
        if (Map.Index == 0) return false;
        if (Map.Index >= 0 && Map.Index < Map.Slots.Length
            && Map.Slots[Map.Index].ProgressionBoundary != NativeProgressionBoundaryKind.None)
            return !NativeProgressionBoundaryReached(Map.Slots[Map.Index]);
        var Handler = Map.Owner;
        if (Handler && NadirRuntime.CurrentSegmentIsNadir(Handler)) return false;
        if (Map.Index >= 0 && Map.Index < Map.Slots.Length && LayoutSegmentBehavior.Resolve(Map.Slots[Map.Index].Source)?.Role == Segment.TheKiln)
            return true;
        return !RequiredSlotCampfire(Map.Index).EveryoneInRange();
    }

    internal static bool EveryoneAtCheckpoint(Vector3 Position, float Radius)
    {
        foreach (var Character in PlayerHandler.GetAllPlayerCharacters())
            if (!Character.data.dead && Vector3.Distance(Position, Character.Center) > Radius) return false;
        return true;
    }

    static bool NativeProgressionBoundaryReached(MapSlot Slot) => Slot.ProgressionBoundary switch
    {
        NativeProgressionBoundaryKind.PeakBridge => Map.CurrentRun!.Peak.Checkpoints.AdmissionSatisfied(Slot.Index),
        NativeProgressionBoundaryKind.NadirGate => NadirRuntime.EveryoneAtNadirGate(Slot),
        _ => throw new InvalidOperationException(
            $"Segment '{Slot.Source.Id}' has no native progression boundary.")
    };

    internal static bool CanAuthorizeDirectAdvance(int Requested)
    {
        if (Requested != Map.Index || !ReleasedSlotRetirement.CanDirectAdvance(Requested)) return false;
        var Checkpoints = Map.CurrentRun!.Peak.Checkpoints;
        if (Checkpoints.HasCheckpoint(Requested)) return Checkpoints.AdmissionSatisfied(Requested);
        return Map.Slots[Requested].ProgressionBoundary == NativeProgressionBoundaryKind.None
            || NativeProgressionBoundaryReached(Map.Slots[Requested]);
    }

    internal static string CheckpointInteractionText(Vector3 Position, float Radius)
    {
        StringBuilder? Missing = null;
        foreach (var Character in PlayerHandler.GetAllPlayerCharacters())
        {
            if (Character.data.dead) continue;
            var Distance = Vector3.Distance(Position, Character.Center);
            if (Distance <= Radius) continue;
            Missing ??= new StringBuilder(LocalizedText.GetText("CANTLIGHT")).Append('\n');
            Missing.Append('\n').Append(Character.photonView.Owner.NickName).Append(' ')
                .Append(Mathf.RoundToInt(Distance * CharacterStats.unitsToMeters)).Append('m');
        }
        return Missing?.ToString() ?? LocalizedText.GetText("LIGHT");
    }

    [HarmonyPatch]
    static class TransitionProgressPatch
    {
        static MethodBase TargetMethod() => MapTransitionAdapter.Instance.TransitionTitleMoveNext;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Expand = ((Func<int, int>)ExpandedProgressIndex).Method;
            var Found = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (!Codes[i].Calls(MapTransitionAdapter.Instance.SetSegmentComplete)) continue;
                if (Found)
                    throw new AmbiguousMatchException("MapHandler.GoToSegment's progress completion was ambiguous.");
                Codes.Insert(i++, new CodeInstruction(OpCodes.Call, Expand));
                Found = true;
            }
            if (!Found)
                throw new MissingMethodException("MapHandler.GoToSegment's progress completion was not found.");
            return Codes;
        }
    }

    [HarmonyPatch]
    static class CharacterSpawnProgressPatch
    {
        static MethodBase TargetMethod() => CharacterSpawnProgressMoveNext;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            NavigationRoles.ReplaceBaseCampSegmentQuery(Codes, "CharacterSpawner dead-base-camp iterator");
            var Previous = ((Func<Segment, Segment>)NavigationRoles.Previous).Method;
            var Expand = ((Func<int, int>)ExpandedNavigationProgressIndex).Method;
            var PreviousPatched = false;
            var ProgressPatched = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (Codes[i].opcode == OpCodes.Sub && i >= 2
                    && MapTransitionAdapter.LoadsInt(Codes[i - 1], 1))
                {
                    if (PreviousPatched)
                        throw new AmbiguousMatchException("CharacterSpawner's reconnect previous slot was ambiguous.");
                    Codes[i - 1].opcode = OpCodes.Call;
                    Codes[i - 1].operand = Previous;
                    Codes[i - 1].labels.AddRange(Codes[i].labels);
                    Codes[i].labels.Clear();
                    Codes[i - 1].blocks.AddRange(Codes[i].blocks);
                    Codes[i].blocks.Clear();
                    Codes.RemoveAt(i--);
                    PreviousPatched = true;
                    continue;
                }
                if (!PreviousPatched || !Codes[i].Calls(MapTransitionAdapter.Instance.JumpToProgressSegment)) continue;
                if (ProgressPatched || i == 0 || Codes[i - 1].opcode != OpCodes.Ldc_R4)
                    throw new AmbiguousMatchException("CharacterSpawner's reconnect progress index was ambiguous.");
                Codes.Insert(i - 1, new CodeInstruction(OpCodes.Call, Expand));
                i++;
                ProgressPatched = true;
            }
            if (!PreviousPatched || !ProgressPatched)
                throw new MissingMethodException("CharacterSpawner's reconnect progress adapters were not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.LastSeenCampfireIsSafe), MethodType.Getter)]
    static class CampfireSafetyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (!Map.Active) return true;
            __result = LastSeenCampfireIsSafe();
            return false;
        }
    }

    [HarmonyPatch(typeof(MapHandler), "PreviousSegmentIsStillBaseCamp", MethodType.Getter)]
    static class PreviousBaseCampPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (!Map.Active) return true;
            __result = PreviousSegmentIsStillBaseCamp();
            return false;
        }
    }
    static bool BlocksPeakHelicopter(bool NativeNadirRequirement)
    {
        if (!Map.Active) return NativeNadirRequirement;
        var Index = Map.Index;
        var Terminal = MapTransitionCoordinator.RunTerminalIndex();
        return NativeNadirRequirement || !Map.CurrentRun!.Configuration.Terminal.Winnable
            || Index < 0 || Index > Terminal || !Map.Slots[Index].PeakAttachment
            || Index != Terminal && (Index != Terminal - 1
                || Map.Slots[Terminal].Source is not NadirLayoutSegmentSource);
    }

    static bool CaptureProgressTemplates(MountainProgressHandler handler)
    {
        if (Progress.Owner == handler && Progress.Templates.Length != 0) return true;
        var templates = handler.progressPoints;
        if (templates.Length == 0)
        {
            if (Progress.Owner == handler)
            {
                Progress.Owner = null;
                Progress.Templates = Array.Empty<MountainProgressHandler.ProgressPoint>();
                Progress.Skipped = null;
            }
            return false;
        }
        Progress.Owner = handler;
        Progress.Templates = templates;
        Progress.Skipped = handler._skippedBiomes;
        Progress.Max = handler.maxProgressPointReached;
        Progress.WasInitialized = handler._isInitialized;
        return true;
    }

    internal static IReadOnlyList<MountainProgressHandler.ProgressPoint> NativeProgressCatalog(
        MountainProgressHandler Handler) => Map.CurrentRun is { } run
            && ReferenceEquals(run.Progress.State.Owner, Handler)
            && run.Progress.State.Templates.Length != 0
            ? run.Progress.State.Templates
            : Handler.progressPoints;

    static MountainProgressHandler.ProgressPoint CreateProgressPoint(
        MountainProgressHandler.ProgressPoint template,
        string title,
        Biome.BiomeType biome,
        ACHIEVEMENTTYPE achievement,
        Vector3 position,
        int index)
    {
        var value = new GameObject($"PLL_Progress_{index:D3}");
        value.transform.SetParent(Progress.Root!.transform, false);
        value.transform.position = position;
        value.transform.rotation = template.transform ? template.transform.rotation : Quaternion.identity;
        return new MountainProgressHandler.ProgressPoint
        {
            transform = value.transform,
            title = title,
            clip = template.clip,
            achievement = achievement,
            biome = biome,
            Reached = false
        };
    }

    static Biome.BiomeType? NativeProgressBiome(
        MountainProgressHandler.ProgressPoint Point)
    {
        if (!Map.Active || ReferenceEquals(Point, Progress.Peak?.Point)
            || !Progress.Slots.TryGetValue(Point, out var Index)) return Point.biome;
        return LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Biome;
    }

    static bool ProgressBiomeMatches(
        MountainProgressHandler.ProgressPoint Point, Biome.BiomeType Expected) =>
        NativeProgressBiome(Point) == Expected;

    static bool SkippedBiomesContain(
        List<Biome.BiomeType> Skipped,
        MountainProgressHandler.ProgressPoint Point) =>
        NativeProgressBiome(Point) is { } Biome && Skipped.Contains(Biome);

    static void MarkProgressBiomeSkipped(
        MountainProgressHandler.ProgressPoint Point)
    {
        if (!Map.Active || ReferenceEquals(Point, Progress.Peak?.Point)
            || !Progress.Slots.TryGetValue(Point, out var Index))
        {
            MountainProgressHandler.MarkBiomeSkipped(Point.biome);
            return;
        }
        if (LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source) is { } Compatibility)
            MountainProgressHandler.MarkBiomeSkipped(Compatibility.Biome);
    }

    static RichPresenceState RichPresenceFor(PllBiomeBehavior Behavior) => Behavior switch
    {
        PllBiomeBehavior.Beach => RichPresenceState.Status_Shore,
        PllBiomeBehavior.Tropics => RichPresenceState.Status_Tropics,
        PllBiomeBehavior.Roots => RichPresenceState.Status_Roots,
        PllBiomeBehavior.Alpine => RichPresenceState.Status_Alpine,
        PllBiomeBehavior.Mesa => RichPresenceState.Status_Mesa,
        PllBiomeBehavior.Caldera => RichPresenceState.Status_Caldera,
        PllBiomeBehavior.Gloom => RichPresenceState.Status_Gloom,
        PllBiomeBehavior.Volcano => RichPresenceState.Status_Kiln,
        PllBiomeBehavior.Citadel => RichPresenceState.Status_Citadel,
        _ => throw new InvalidOperationException(
            $"Biome behavior '{Behavior}' has no native rich-presence state.")
    };

    static void SetProgressRichPresence(
        MountainProgressHandler.ProgressPoint Point)
    {
        if (!Map.Active || ReferenceEquals(Point, Progress.Peak?.Point)
            || !Progress.Slots.TryGetValue(Point, out var Index)
            || Map.Slots[Index].Source is not CustomLayoutSegmentSource Custom)
        {
            MountainProgressHandler.SetRichPresence(Point);
            return;
        }
        if (Custom.Behavior == PllBiomeBehavior.Authored) return;
        GameHandler.GetService<RichPresenceService>().SetState(
            RichPresenceFor(Custom.Behavior));
    }

    static void SetProgressHeroTitle(
        GUIManager manager,
        string title,
        AudioClip clip,
        bool mysteryTime,
        MountainProgressHandler.ProgressPoint point)
    {
        if (Map.Active && Progress.Slots.TryGetValue(point, out var index)
            && Map.Slots[index] is { Source: NadirLayoutSegmentSource, NadirHeroTitle: not null }) return;
        manager.SetHeroTitle(title, clip, mysteryTime);
    }
    internal static void RestoreProgress()
    {
        if (Progress.Owner)
        {
            Progress.Owner.progressPoints = Progress.Templates;
            Progress.Owner._skippedBiomes = Progress.Skipped ?? new List<Biome.BiomeType>();
            Progress.Owner.maxProgressPointReached = Progress.Max;
            Progress.Owner._isInitialized = Progress.WasInitialized;
        }
        if (Progress.Root) UnityEngine.Object.DestroyImmediate(Progress.Root);
        Progress.Clear();
    }

    [HarmonyPatch(typeof(MountainProgressHandler.ProgressPoint), nameof(MountainProgressHandler.ProgressPoint.localizedTitle), MethodType.Getter)]
    static class ProgressTitlePatch
    {
        [HarmonyPostfix]
        static void Postfix(MountainProgressHandler.ProgressPoint __instance, ref string __result)
        {
            if (Map.Active && Progress.Titles.TryGetValue(__instance, out var title)) __result = title;
        }
    }

    [HarmonyPatch(typeof(MountainProgressHandler), "InitProgressPoints")]
    static class ProgressPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MountainProgressHandler __instance) => InitializeProgress(__instance);
    }

    [HarmonyPatch(typeof(MountainProgressHandler), "CheckAreaAchievement")]
    static class ProgressAchievementPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var BiomeField = AccessTools.DeclaredField(
                typeof(MountainProgressHandler.ProgressPoint),
                nameof(MountainProgressHandler.ProgressPoint.biome))
                ?? throw new MissingFieldException(
                    typeof(MountainProgressHandler.ProgressPoint).FullName,
                    nameof(MountainProgressHandler.ProgressPoint.biome));
            var Contains = AccessTools.DeclaredMethod(
                typeof(List<Biome.BiomeType>), nameof(List<Biome.BiomeType>.Contains),
                new[] { typeof(Biome.BiomeType) })
                ?? throw new MissingMethodException(
                    typeof(List<Biome.BiomeType>).FullName,
                    nameof(List<Biome.BiomeType>.Contains));
            var Match = ((Func<MountainProgressHandler.ProgressPoint, Biome.BiomeType, bool>)ProgressBiomeMatches).Method;
            var Skipped = ((Func<List<Biome.BiomeType>, MountainProgressHandler.ProgressPoint, bool>)SkippedBiomesContain).Method;
            var Comparisons = 0;
            var Memberships = 0;
            for (var Index = 0; Index < Codes.Count; Index++)
            {
                if (!Codes[Index].LoadsField(BiomeField)) continue;
                if (Index + 1 < Codes.Count && Codes[Index + 1].Calls(Contains))
                {
                    Codes[Index].opcode = OpCodes.Nop;
                    Codes[Index].operand = null;
                    Codes[Index + 1].opcode = OpCodes.Call;
                    Codes[Index + 1].operand = Skipped;
                    Memberships++;
                    Index++;
                    continue;
                }
                if (Index + 2 >= Codes.Count
                    || !MapTransitionAdapter.LoadsInt(Codes[Index + 1], (int)Biome.BiomeType.Mesa)
                        && !MapTransitionAdapter.LoadsInt(Codes[Index + 1], (int)Biome.BiomeType.Alpine)
                        && !MapTransitionAdapter.LoadsInt(Codes[Index + 1], (int)Biome.BiomeType.Roots)
                        && !MapTransitionAdapter.LoadsInt(Codes[Index + 1], (int)Biome.BiomeType.Peak)
                    || !MapTransitionAdapter.IsEqualBranch(Codes[Index + 2].opcode)
                        && !MapTransitionAdapter.IsNotEqualBranch(Codes[Index + 2].opcode))
                    throw new MissingMethodException(
                        "MountainProgressHandler.CheckAreaAchievement has an unknown biome consumer.");
                Codes[Index].opcode = OpCodes.Nop;
                Codes[Index].operand = null;
                var Branch = Codes[Index + 2];
                Codes.Insert(Index + 2, new CodeInstruction(OpCodes.Call, Match));
                if (MapTransitionAdapter.IsEqualBranch(Branch.opcode))
                    Branch.opcode = Branch.opcode == OpCodes.Beq_S ? OpCodes.Brtrue_S : OpCodes.Brtrue;
                else Branch.opcode = Branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
                Comparisons++;
                Index += 2;
            }
            if (Comparisons != 4 || Memberships != 1)
                throw new MissingMethodException(
                    $"MountainProgressHandler.CheckAreaAchievement projected {Comparisons} biome comparisons "
                    + $"and {Memberships} skipped-biome lookups; expected 4 and 1.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(MountainProgressHandler), nameof(MountainProgressHandler.JumpToSegment))]
    static class ProgressSkipPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var BiomeField = AccessTools.DeclaredField(
                typeof(MountainProgressHandler.ProgressPoint),
                nameof(MountainProgressHandler.ProgressPoint.biome))
                ?? throw new MissingFieldException(
                    typeof(MountainProgressHandler.ProgressPoint).FullName,
                    nameof(MountainProgressHandler.ProgressPoint.biome));
            var MarkSkipped = AccessTools.DeclaredMethod(
                typeof(MountainProgressHandler), nameof(MountainProgressHandler.MarkBiomeSkipped),
                new[] { typeof(Biome.BiomeType) })
                ?? throw new MissingMethodException(
                    typeof(MountainProgressHandler).FullName,
                    nameof(MountainProgressHandler.MarkBiomeSkipped));
            var Replacement = ((Action<MountainProgressHandler.ProgressPoint>)MarkProgressBiomeSkipped).Method;
            var PresenceReplacement = ((Action<MountainProgressHandler.ProgressPoint>)SetProgressRichPresence).Method;
            var Replaced = false;
            var PresenceReplaced = false;
            for (var Index = 1; Index < Codes.Count; Index++)
            {
                if (Codes[Index].Calls(SetRichPresenceMethod))
                {
                    if (PresenceReplaced)
                        throw new AmbiguousMatchException(
                            "MountainProgressHandler.JumpToSegment rich-presence call was ambiguous.");
                    Codes[Index].opcode = OpCodes.Call;
                    Codes[Index].operand = PresenceReplacement;
                    PresenceReplaced = true;
                    continue;
                }
                if (!Codes[Index - 1].LoadsField(BiomeField)
                    || !Codes[Index].Calls(MarkSkipped)) continue;
                if (Replaced)
                    throw new AmbiguousMatchException(
                        "MountainProgressHandler.JumpToSegment biome skip was ambiguous.");
                Codes[Index - 1].opcode = OpCodes.Nop;
                Codes[Index - 1].operand = null;
                Codes[Index].opcode = OpCodes.Call;
                Codes[Index].operand = Replacement;
                Replaced = true;
            }
            if (!Replaced || !PresenceReplaced)
                throw new MissingMethodException(
                    "MountainProgressHandler.JumpToSegment biome skip or rich-presence call was not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(MountainProgressHandler), "CheckReached")]
    static class ProgressOrderPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MountainProgressHandler.ProgressPoint __0, ref bool __result)
        {
            if (!Map.Active || !Progress.Slots.TryGetValue(__0, out var index)) return true;
            var Slot = Map.Slots[index];
            var Axis = Progress.Peak is { } Peak && ReferenceEquals(__0, Peak.Point)
                ? Peak.Forward
                : Slot.PlacedProgressionAxis;
            var character = Character.localCharacter;
            __result = index <= Map.Index && character && !character.data.dead && __0.transform
                && Vector3.Dot(character.Center - __0.transform.position, Axis) > 0f;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(MountainProgressHandler),
        "TriggerReached",
        typeof(MountainProgressHandler.ProgressPoint),
        typeof(bool))]
    static class ProgressHeroTitlePatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setHeroTitle = AccessTools.DeclaredMethod(
                typeof(GUIManager), nameof(GUIManager.SetHeroTitle),
                new[] { typeof(string), typeof(AudioClip), typeof(bool) })
                ?? throw new MissingMethodException(typeof(GUIManager).FullName, nameof(GUIManager.SetHeroTitle));
            var HeroReplacement = ((Action<GUIManager, string, AudioClip, bool, MountainProgressHandler.ProgressPoint>)SetProgressHeroTitle).Method;

            var PresenceReplacement = ((Action<MountainProgressHandler.ProgressPoint>)SetProgressRichPresence).Method;
            var HeroReplaced = false;
            var PresenceReplaced = false;
            foreach (var Instruction in instructions)
            {
                if (Instruction.Calls(setHeroTitle))
                {
                    if (HeroReplaced)
                        throw new AmbiguousMatchException("MountainProgressHandler.TriggerReached's hero-title call is ambiguous.");
                    if (Instruction.labels.Count != 0 || Instruction.blocks.Count != 0)
                        throw new InvalidOperationException(
                            "MountainProgressHandler.TriggerReached's hero-title call control flow changed.");
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = HeroReplacement;
                    HeroReplaced = true;
                }
                else if (Instruction.Calls(SetRichPresenceMethod))
                {
                    if (PresenceReplaced)
                        throw new AmbiguousMatchException(
                            "MountainProgressHandler.TriggerReached's rich-presence call is ambiguous.");
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = PresenceReplacement;
                    PresenceReplaced = true;
                }
                yield return Instruction;
            }
            if (!HeroReplaced || !PresenceReplaced)
                throw new MissingMethodException(
                    "MountainProgressHandler.TriggerReached's hero-title or rich-presence call was not found.");
        }
    }

    [HarmonyPatch(typeof(MountainProgressHandler), "CheckProgress")]
    static class ProgressCompletionPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            if (!Map.Active) return;
            var Index = Map.Index;
            if (Map.CurrentRun!.Configuration.Terminal.Winnable && !Ascents.shouldSpawnFlare
                && PeakProgressReached(Index) && ReleasedSlotRetirement.IsRunTerminal(Index))
                LazySlotGenerator.RequestFinishRun();
        }
    }

    [HarmonyPatch(typeof(MountainProgressHandler), nameof(MountainProgressHandler.DisplaySegmentTitleAfterDelay))]
    static class ProgressTitleIndexPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref int __0) => __0 = ExpandedNavigationProgressIndex(__0);
    }

    [HarmonyPatch(typeof(Flare), nameof(Flare.TriggerHelicopter))]
    static class HelicopterPatch
    {
        [HarmonyPrefix]
        static bool Prefix() => !Map.Active || !BlocksPeakHelicopter(Ascents.shouldRequireNadir);
    }

    [HarmonyPatch(typeof(MountainProgressHandler), nameof(MountainProgressHandler.IsAtPeak), typeof(Vector3))]
    static class PeakPositionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Vector3 __0, ref bool __result)
        {
            if (!Map.Active) return true;
            __result = Progress.Peak is { } Peak && Map.Index == Peak.SlotIndex
                && Vector3.Dot(__0 - Peak.Point.transform.position, Peak.Forward) > 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Flare), "Update")]
    static class FlareUpdatePatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var requirement = AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.shouldRequireNadir))
                ?? throw new MissingMethodException(typeof(Ascents).FullName, nameof(Ascents.shouldRequireNadir));
            var Replacement = ((Func<bool, bool>)BlocksPeakHelicopter).Method;
            var replaced = false;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(requirement)) continue;
                if (replaced)
                    throw new AmbiguousMatchException("Flare's Nadir requirement is ambiguous.");
                yield return new CodeInstruction(OpCodes.Call, Replacement);
                replaced = true;
            }
            if (!replaced)
                throw new MissingMethodException("Flare's Nadir requirement was not found.");
        }
    }
}
