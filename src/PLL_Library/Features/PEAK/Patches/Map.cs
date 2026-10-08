using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using Zorro.Core;
namespace PLL.Library;

internal static class Map
{
    internal const string LayoutQuicksaveMode = "layout";
    internal const string GenerativeQuicksaveMode = "generative";
    internal const string InfiniteQuicksaveMode = "infinite";
    internal const string InactiveQuicksaveMode = "inactive";
    internal static readonly FieldInfo CurrentSegmentField =
        AccessTools.DeclaredField(typeof(MapHandler), "currentSegment")
        ?? throw new MissingFieldException(typeof(MapHandler).FullName, "currentSegment");
    static MapRunIngress Preflight = new();
    internal static MapRun? CurrentRun;
    internal static MapRunIngress Ingress => CurrentRun?.Ingress ?? Preflight;
    internal const string AdvanceRequestKey = PhotonPropertyProtocol.StatePrefix + "advance/request";
    internal const string PeakBestowKey = PhotonPropertyProtocol.StatePrefix + "peak/bestow";
    internal const string PeakCompletionKey = PhotonPropertyProtocol.StatePrefix + "peak/completion/v3";
    internal const string WinRequestKey = PhotonPropertyProtocol.StatePrefix + "win/request";
    internal const string ReleasedKey = PhotonPropertyProtocol.StatePrefix + "released";
    internal const string GenerationReadyPrefix = PhotonPropertyProtocol.StatePrefix + "generation/ready/";
    internal const string GenerationGoKey = PhotonPropertyProtocol.StatePrefix + "generation/go";
    internal static int LayoutRevision => CurrentRun?.LayoutRevision ?? 0;
    internal static string GenerationEpoch => CurrentRun?.GenerationEpoch ?? string.Empty;
    internal static PreparedMap? Pending => CurrentRun?.RollbackOwner;
    internal static MapHandler? Owner => CurrentRun?.Handler;
    internal static MapSlot[] Slots => CurrentRun?.Slots ?? Array.Empty<MapSlot>();
    internal static bool Active => CurrentRun?.Active == true;
    internal static bool HasPublishedLayout => CurrentRun?.HasPublishedLayout == true;
    internal static bool TryGetPublishedRun(out MapRun Run)
    {
        if (CurrentRun is { HasPublishedLayout: true } published)
        {
            Run = published;
            return true;
        }
        Run = null!;
        return false;
    }
    internal static int Index => Owner ? Owner.currentSegment : 0;
    internal static LayoutSegmentSource? CurrentSource
    {
        get
        {
            var CurrentIndex = Index;
            return Active && CurrentIndex >= 0 && CurrentIndex < Slots.Length && !Slots[CurrentIndex].Released
                ? Slots[CurrentIndex].Source
                : null;
        }
    }
    internal static Segment LogicalSegment(MapHandler Handler)
    {
        var Index = Handler.currentSegment;
        if (!HasPublishedLayout) return (Segment)Index;
        if (Index >= 0 && Index < Slots.Length) return Slots[Index].NavigationRole;
        if (NadirRuntime.IsVoidControllerIndex(Handler, Index)) return Segment.Void;
        throw new InvalidOperationException(
            $"Map segment index {Index} is outside the published layout and its VOID controller segment.");
    }
    internal static float ReconnectHeightLimit() => HasPublishedLayout ? float.PositiveInfinity : 1300f;
    static float ItemHeightLimit() => HasPublishedLayout ? float.PositiveInfinity : 4000f;

    internal static void OnRunReleased(MapRun Released)
    {
        NativeFeatureGeneration.ReleaseOwnership();
        Sounds.Release(Released);
        if (!ReferenceEquals(CurrentRun, Released)) return;
        if (!Released.OriginalMapSourcesRetired || Released.SceneGone) CurrentRun = null;
        AscentUIStartPatch.ResetResolved();
    }

    static void RequireReinitializable(MapHandler Handler)
    {
        if (CurrentRun is { OriginalMapSourcesRetired: true } Retired
            && Retired.SceneHandle == Handler.gameObject.scene.handle)
            throw new InvalidOperationException(
                "This scene cannot reinitialize after its original native map sources were retired.");
    }

    internal static void ReleaseCurrentRun()
    {
        if (CurrentRun is { } Run) ReleaseRun(Run);
        NativeBaseMap.ReleaseCurrent();
    }

    internal static bool OwnsScene(int SceneHandle) =>
        CurrentRun?.SceneHandle == SceneHandle || NativeBaseMap.OwnsScene(SceneHandle);

    internal static void ReleaseScene(int SceneHandle)
    {
        try
        {
            if (CurrentRun is { } Run && Run.SceneHandle == SceneHandle)
            {
                Run.MarkSceneGone();
                ReleaseRun(Run);
            }
            if (NativeBaseMap.OwnsScene(SceneHandle)) NativeBaseMap.ReleaseCurrent();
        }
        finally
        {
            try { OccurrenceHeightmapMeshes.ReleaseScene(SceneHandle); }
            finally
            {
                Sounds.ReleaseScene(SceneHandle);
                Preflight.ClearScene(SceneHandle);
                if (CurrentRun is { Released: true, SceneGone: true }) CurrentRun = null;
            }
        }
    }

    internal static void ClearTransientPreflight() => Preflight.ClearTransient();
    internal static void ReleaseRun(MapRun Run)
    {
        if (Run.Released) return;
        if (Run.RollbackOwner is not null)
        {
            MapPublication.RollbackPending(Run);
            return;
        }
        if (Run.PublicationOwner is not null)
        {
            Sounds.Clear(Run);
            MapPublication.ReleasePublished(Run);
            return;
        }
        MapPublication.ReleaseUnpublished(Run);
    }
    internal sealed class LiveCompatibility
    {
        internal LiveCompatibility(
            MapHandler Handler,
            NativeSegmentCatalog? Catalog,
            ResolvedRunConfiguration Configuration)
        {
            if (MapQuicksave.RuntimeMapProcessingRequired(Configuration) != (Catalog is not null))
                throw new InvalidOperationException(
                    "Live map compatibility catalog presence does not match runtime map processing.");
            this.Handler = Handler;
            this.Catalog = Catalog;
            this.Configuration = Configuration;
        }

        internal MapHandler Handler { get; }
        internal ResolvedRunConfiguration Configuration { get; }
        internal bool SuspendedAfterSeal { get; private set; }
        private NativeSegmentCatalog? Catalog { get; }

        internal void RecordSuspension() => SuspendedAfterSeal = true;
        internal NativeSegmentCatalog RequireCatalog() => Catalog
            ?? throw new InvalidOperationException(
                "Live map compatibility is missing its required native segment catalog.");
    }
    internal static LiveCompatibility? SealLiveCompatibility()
    {
        var Handler = Singleton<MapHandler>.Instance;
        if (!Handler || CurrentRun is { } run && run.Owns(Handler) && !run.Releasing) return null;
        RequireReinitializable(Handler);
        var Configuration = RunConfigurationResolution.ResolveStartup();
        if (!MapQuicksave.RuntimeMapProcessingRequired(Configuration))
            return new LiveCompatibility(Handler, null, Configuration);
        try
        {
            var Catalog = NativeSegmentCatalog.Create(Handler);
            BiomeRegistration.SealLive(Handler, Catalog);
            return new LiveCompatibility(Handler, Catalog, Configuration);
        }
        catch
        {
            BiomeRegistration.ClearLiveCatalogs();
            throw;
        }
    }
    internal static MapPreparation? BeginPreparation(
        LiveCompatibility? Compatibility = null,
        MapRun.GenerationCohort? InitialGenerationCohort = null,
        OriginalMapSources.StartupSuppression? Suppression = null)
    {
        var Handler = Compatibility?.Handler ?? Singleton<MapHandler>.Instance;
        if (!Handler)
        {
            if (MapQuicksave.RestoringPllQuicksave())
                throw MapQuicksave.RejectQuicksave("PEAK's map handler is unavailable during restoration.");
            if (Compatibility is not null)
                throw new InvalidOperationException(
                    "PEAK's map handler was destroyed after live compatibility sealing.");
            return null;
        }
        if (Compatibility is not null)
        {
            var CurrentHandler = Singleton<MapHandler>.Instance;
            if (!CurrentHandler || CurrentHandler.GetInstanceID() != Handler.GetInstanceID())
                throw new InvalidOperationException("PEAK's map handler changed after live compatibility sealing.");
        }
        var Existing = CurrentRun;
        RequireReinitializable(Handler);
        if (Existing is not null && Existing.Owns(Handler) && !Existing.Releasing) return null;
        if (NativeBaseMap.Owns(Handler))
        {
            NativeBaseMap.ApplyCurrent();
            return null;
        }
        var Configuration = Compatibility?.Configuration ?? RunConfigurationResolution.ResolveStartup();
        var PublicationRequired = MapQuicksave.RuntimeLayoutPublicationRequired(Configuration);
        var ProcessingRequired = MapQuicksave.RuntimeMapProcessingRequired(Configuration);
        if (MapQuicksave.RestoringPllQuicksave()
            && PublicationRequired != MapQuicksave.RestoringActivePllQuicksave())
            throw MapQuicksave.RejectQuicksave("the resolved layout publication state differs from the saved state.");
        NativeSegmentCatalog? ResolvedNativeCatalog = null;
        if (ProcessingRequired)
        {
            if (Compatibility is not null && !Compatibility.SuspendedAfterSeal)
                ResolvedNativeCatalog = Compatibility.RequireCatalog();
            else
            {
                try
                {
                    BiomeRegistration.ClearLiveCatalogs();
                    ResolvedNativeCatalog = Compatibility is null
                        ? NativeSegmentCatalog.Create(Handler)
                        : Compatibility.RequireCatalog();
                    BiomeRegistration.SealLive(Handler, ResolvedNativeCatalog);
                }
                catch
                {
                    BiomeRegistration.ClearLiveCatalogs();
                    throw;
                }
            }
            if (Compatibility is null)
            {
                var Room = PhotonNetwork.CurrentRoom;
                if (Room is null || Room.IsOffline)
                    FinalManifestConsensus.ConfirmOffline(
                        Room, Configuration, SegmentRegistry.ManifestRevision);
            }
            FinalManifestConsensus.RequireConfirmed(Configuration);
        }
        if (!Preflight.SpawnHistoryOwner
            || Preflight.SpawnHistoryOwner.GetInstanceID() != Handler.GetInstanceID())
            Preflight.ClearSpawnHistory();
        if (Existing is not null)
        {
            ReleaseRun(Existing);
            if (!Existing.Released)
                throw new InvalidOperationException("The previous map run still owns live feature generation.");
        }
        NativeBaseMap.ReleaseCurrent();
        NativeFeatureGeneration.Reset();
        if (NativeFeatureGeneration.HasLiveOperation)
            throw new InvalidOperationException("The previous native feature operation is still completing.");
        AuthoringRuntime.Clear();
        if (!ProcessingRequired)
        {
            MapQuicksave.ValidateQuicksaveSlotCount(Handler.segments.Length);
            return null;
        }
        var NativeCatalog = ResolvedNativeCatalog
            ?? throw new InvalidOperationException("Runtime map processing is missing its native segment catalog.");
        if (!PublicationRequired)
        {
            MapQuicksave.ValidateQuicksaveSlotCount(Handler.segments.Length);
            NativeBaseMap.Activate(Handler, NativeCatalog.Sources, Configuration);
            return null;
        }

        var Level = Handler.GetComponent<LevelGeneration>()
            ?? throw new InvalidOperationException("PEAK's active map has no LevelGeneration component.");
        OriginalMapSources Originals;
        try { Originals = OriginalMapSources.Capture(Handler, NativeCatalog.Sources); }
        catch
        {
            BiomeRegistration.ClearLiveCatalogs();
            throw;
        }
        var Run = new MapRun(Handler, Configuration, Preflight, InitialGenerationCohort);
        var Preparation = new MapPreparation(
            Run, Handler, Level, NativeCatalog, Originals, MapQuicksave.RestoringPllQuicksave(), Suppression);
        Run.AttachPreparation(Preparation);
        Preflight = new MapRunIngress();
        CurrentRun = Run;
        return Preparation;
    }

    [HarmonyPatch(typeof(Item), "Update")]
    static class ItemHeightPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var HeightMethod = ((Func<float>)ItemHeightLimit).Method;
            var Replaced = false;
            foreach (var Instruction in instructions)
            {
                if (Instruction.opcode == OpCodes.Ldc_R4
                    && Instruction.operand is float Limit && Limit == 4000f)
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = HeightMethod;
                    Replaced = true;
                }
                yield return Instruction;
            }
            if (!Replaced) throw new MissingMethodException("Item's upper map-height cleanup guard was not found.");
        }
    }

}
