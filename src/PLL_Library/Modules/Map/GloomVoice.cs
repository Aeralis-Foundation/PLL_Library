using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class GloomVoiceRuntime
{
    internal static readonly MethodInfo UpdateMethod = AccessTools.DeclaredMethod(
        typeof(Peak.Island.GloomVoiceConfuser), nameof(Peak.Island.GloomVoiceConfuser.Update), Type.EmptyTypes)
        ?? throw new MissingMethodException(
            typeof(Peak.Island.GloomVoiceConfuser).FullName, nameof(Peak.Island.GloomVoiceConfuser.Update));
    static readonly ConditionalWeakTable<Peak.Island.GloomVoiceConfuser, GloomVoiceClone> GloomVoiceBindings = new();

    internal sealed class GloomProfileClone
    {
        internal GloomProfileClone(DayNightProfile SourceProfile, DayNightProfile Value)
        {
            Source = SourceProfile;
            this.Value = Value;
        }

        DayNightProfile Source { get; }
        internal DayNightProfile Value { get; }

        internal void Release()
        {
            if (!Value) return;
            var Manager = DayNightManager.instance;
            if (Manager)
            {
                if (ReferenceEquals(Manager.currentProfile, Value)) Manager.currentProfile = Source;
                if (ReferenceEquals(Manager.newProfile, Value)) Manager.newProfile = Source;
            }
            UnityEngine.Object.DestroyImmediate(Value);
        }
    }

    internal sealed class GloomVoiceClone
    {
        bool ExitClaimedValue;
        bool ExitDeferredValue;
        bool HostAuthoritySeeded;

        internal GloomVoiceClone(
            long RunIdentityValue,
            Peak.Island.GloomVoiceConfuser ManagerValue,
            Transform OwnerValue,
            DayNightProfile ProfileValue,
            int FirstSlotValue,
            int LastSlotValue)
        {
            RunIdentity = RunIdentityValue;
            Manager = ManagerValue;
            Owner = OwnerValue;
            Profile = ProfileValue;
            FirstSlot = FirstSlotValue;
            LastSlot = LastSlotValue;
        }

        internal long RunIdentity { get; }
        internal Peak.Island.GloomVoiceConfuser Manager { get; }
        internal Transform Owner { get; }
        internal DayNightProfile Profile { get; }
        internal int FirstSlot { get; }
        internal int LastSlot { get; }
        internal Peak.Island.RavenPerch[]? FullCatalog { get; private set; }
        internal bool Ready => FullCatalog is not null;
        internal bool ExitClaimed => ExitClaimedValue;
        internal bool ExitDeferred => ExitDeferredValue;

        internal void RequireSlotOwnership(IReadOnlyList<MapSlot> SlotsValue)
        {
            for (var i = FirstSlot; i <= LastSlot; i++)
                if (i < 0 || i >= SlotsValue.Count || SlotsValue[i].Released
                    || !ReferenceEquals(SlotsValue[i].GloomVoice, this))
                    throw new InvalidOperationException("The generated Gloom voice manager lost its paired slot ownership.");
        }

        internal void Commit(Peak.Island.RavenPerch[] FullCatalogValue)
        {
            if (ExitClaimedValue)
                throw new InvalidOperationException("An exited Gloom voice clone cannot accept a new Raven perch catalog.");
            if (FullCatalog is not null)
                throw new InvalidOperationException("A Gloom voice clone was committed more than once.");
            FullCatalog = FullCatalogValue;
        }

        internal void BeginExitDeferral()
        {
            if (ExitDeferredValue)
                throw new InvalidOperationException("A Gloom voice clone already owns a deferred jump exit.");
            ExitDeferredValue = true;
        }

        internal void EndExitDeferral()
        {
            if (!ExitDeferredValue)
                throw new InvalidOperationException("A Gloom voice clone has no deferred jump exit to release.");
            ExitDeferredValue = false;
        }

        internal void SeedHostAuthority(bool IsHost)
        {
            if (HostAuthoritySeeded) return;
            Manager._wasHost = IsHost;
            HostAuthoritySeeded = true;
        }

        internal bool TryClaimExit()
        {
            if (ExitClaimedValue) return false;
            ExitClaimedValue = true;
            return true;
        }
    }

    static void RegisterGloomVoiceClone(GloomVoiceClone Binding)
    {
        if (GloomVoiceBindings.TryGetValue(Binding.Manager, out _))
            throw new InvalidOperationException("A cloned Gloom voice manager was registered more than once.");
        GloomVoiceBindings.Add(Binding.Manager, Binding);
    }
    internal static bool NativeGloomPair(IReadOnlyList<LayoutSegmentSource> Selected) =>
        Selected.Count >= 2 && NativeSegmentCatalog.IsCompositePair(Selected[0], Selected[1])
        && Selected[0].Id.Equals("Gloom", StringComparison.Ordinal)
        && Selected[1].Id.Equals("Citadel", StringComparison.Ordinal);

    static GloomProfileClone CreateGloomProfile(LayoutSegmentSource Source, Peak.StatusFieldGloom FogFieldValue)
    {
        var SourceProfile = Source.DayNightProfile
            ?? throw new InvalidOperationException("PEAK's native Gloom segment has no DayNightProfile.");
        var Value = UnityEngine.Object.Instantiate(SourceProfile);
        try
        {
            var SourceValues = SourceProfile.globalStaticShaderFloats
                ?? throw new InvalidOperationException("PEAK's native Gloom DayNightProfile has no static shader values.");
            var Values = new ShaderParameters[SourceValues.Length];
            var HeightIndex = -1;
            for (var i = 0; i < SourceValues.Length; i++)
            {
                var SourceValue = SourceValues[i]
                    ?? throw new InvalidOperationException("PEAK's native Gloom DayNightProfile has a null static shader value.");
                Values[i] = new ShaderParameters
                {
                    parameter = SourceValue.parameter,
                    paramValue = SourceValue.paramValue
                };
                if (SourceValue.parameter != DayNightManager.ShaderParams.HeightFogHeight) continue;
                if (HeightIndex >= 0)
                    throw new InvalidOperationException(
                        "PEAK's native Gloom DayNightProfile has multiple HeightFogHeight static shader values.");
                HeightIndex = i;
            }
            if (HeightIndex < 0)
                throw new InvalidOperationException(
                    "PEAK's native Gloom DayNightProfile has no HeightFogHeight static shader value.");
            Values[HeightIndex].paramValue = FogFieldValue.transform.position.y;
            Value.globalStaticShaderFloats = Values;
            return new GloomProfileClone(SourceProfile, Value);
        }
        catch
        {
            if (Value) UnityEngine.Object.DestroyImmediate(Value);
            throw;
        }
    }
    internal static GloomVoiceClone? PrepareGloomVoiceClone(
        IReadOnlyList<LayoutSegmentSource> Selected,
        Transform RootValue,
        int FirstSlotValue,
        List<GloomProfileClone> CreatedProfiles)
    {
        if (!NativeGloomPair(Selected)) return null;

        var Managers = RootValue.GetComponentsInChildren<Peak.Island.GloomVoiceConfuser>(true);
        if (Managers.Length != 1 || !Managers[0] || !TransformOwnership.Contains(RootValue, Managers[0].transform))
            throw new InvalidOperationException("The cloned native Gloom/Citadel container must own exactly one voice manager.");
        var ManagerValue = Managers[0];
        if (ManagerValue.fogField is not { } FogFieldValue
            || !FogFieldValue || !TransformOwnership.Contains(RootValue, FogFieldValue.transform))
            throw new InvalidOperationException("The cloned native Gloom/Citadel fog field ownership is invalid.");
        var Run = Map.CurrentRun
            ?? throw new InvalidOperationException("The cloned Gloom voice manager has no preparing map run.");
        var Profile = CreateGloomProfile(Selected[0], FogFieldValue);
        CreatedProfiles.Add(Profile);
        var Binding = new GloomVoiceClone(
            Run.Identity, ManagerValue, RootValue, Profile.Value, FirstSlotValue, FirstSlotValue + 1);
        RegisterGloomVoiceClone(Binding);
        return Binding;
    }

    static Peak.Island.RavenPerch[] TrimGloomPerches(Peak.Island.RavenPerch[] SourcePerches, int Length)
    {
        if (SourcePerches.Length == Length) return SourcePerches;
        var Result = new Peak.Island.RavenPerch[Length];
        Array.Copy(SourcePerches, Result, Length);
        return Result;
    }

    static (
        Peak.Island.RavenPerch[] FullCatalog,
        Peak.Island.RavenPerch[] ActivePerches,
        Peak.Island.RavenPerch[] ValidPerches) CollectGloomPerches(GloomVoiceClone Binding)
    {
        if (!Binding.Manager || !Binding.Owner || !Binding.Owner.gameObject.activeInHierarchy
            || !TransformOwnership.Contains(Binding.Owner, Binding.Manager.transform))
            throw new InvalidOperationException("The cloned native Gloom/Citadel voice manager ownership is invalid.");
        var Components = Binding.Owner.GetComponentsInChildren<Peak.Island.RavenPerch>(true);
        if (Components.Length == 0)
            throw new InvalidOperationException("The generated native Gloom/Citadel container owns no Raven perch catalog.");
        var Boundary = Binding.Manager.validPerchBoundary;
        var ActivePerches = new Peak.Island.RavenPerch[Components.Length];
        var ValidPerches = new Peak.Island.RavenPerch[Components.Length];
        var ActiveCount = 0;
        var ValidCount = 0;
        for (var i = 0; i < Components.Length; i++)
        {
            var Perch = Components[i];
            if (!Perch || !TransformOwnership.Contains(Binding.Owner, Perch.transform))
                throw new InvalidOperationException("The generated native Gloom/Citadel Raven perch ownership is invalid.");
            if (!Perch.gameObject.activeInHierarchy) continue;
            ActivePerches[ActiveCount++] = Perch;
            if (Perch.CanSpawnVoiceStealRavens && Mathf.Abs(Perch.transform.position.x) < Boundary)
                ValidPerches[ValidCount++] = Perch;
        }
        if (ActiveCount == 0)
            throw new InvalidOperationException("The generated native Gloom/Citadel container owns no active Raven perches.");
        return (
            Components,
            TrimGloomPerches(ActivePerches, ActiveCount),
            TrimGloomPerches(ValidPerches, ValidCount));
    }

    internal static void CommitGloomVoiceClone(int IndexValue)
    {
        if (IndexValue < 0 || IndexValue >= Map.Slots.Length)
            throw new ArgumentOutOfRangeException(nameof(IndexValue));
        var Binding = Map.Slots[IndexValue].GloomVoice;
        if (Binding is null || Binding.Ready) return;
        var Run = Map.CurrentRun;
        if (Run is null || !Run.HasPublishedLayout || Binding.RunIdentity != Run.Identity
            || IndexValue < Binding.FirstSlot || IndexValue > Binding.LastSlot)
            throw new InvalidOperationException("The generated Gloom voice manager is not owned by the current map run and slot span.");
        Binding.RequireSlotOwnership(Map.Slots);

        var PerchesValue = CollectGloomPerches(Binding);
        Binding.Manager.allPerches = PerchesValue.ActivePerches;
        Binding.Manager._validPerches = PerchesValue.ValidPerches;
        PublishGloomVoiceClone(Binding, PerchesValue.FullCatalog);
        Binding.Commit(PerchesValue.FullCatalog);
    }
    internal static GloomVoiceClone? CaptureCurrentGloomVoiceClone()
    {
        var Run = Map.CurrentRun;
        if (Run?.HasPublishedLayout != true) return null;
        var IndexValue = Map.Index;
        if (IndexValue < 0 || IndexValue >= Map.Slots.Length
            || Map.Slots[IndexValue].GloomVoice is not { Ready: true, ExitClaimed: false } Binding)
            return null;
        if (Binding.RunIdentity != Run.Identity || IndexValue < Binding.FirstSlot || IndexValue > Binding.LastSlot)
            throw new InvalidOperationException("The current Gloom voice manager is outside its published slot ownership.");
        Binding.RequireSlotOwnership(Map.Slots);
        return Binding;
    }
    internal static void DismissGloomVoiceClone(GloomVoiceClone? Binding)
    {
        if (Binding is null || !Binding.Ready || Binding.ExitClaimed) return;
        var Run = Map.CurrentRun;
        if (Run is null || Binding.RunIdentity != Run.Identity)
            throw new InvalidOperationException("A Gloom voice clone exit is outside its owning map run.");
        if (!Binding.Manager || !Binding.Owner || !TransformOwnership.Contains(Binding.Owner, Binding.Manager.transform))
            throw new InvalidOperationException("The exiting Gloom voice manager ownership is invalid.");
        if (!Binding.TryClaimExit()) return;
        try { Binding.Manager.DismissAllRavens(); }
        finally
        {
            if (Binding.Manager) Binding.Manager.enabled = false;
        }
    }
    internal static void DismissReleasedGloomVoiceClone(GloomVoiceClone? Binding)
    {
        if (Binding is null || Binding.ExitDeferred) return;
        var IndexValue = Map.Index;
        if (IndexValue >= Binding.FirstSlot && IndexValue <= Binding.LastSlot) return;
        DismissGloomVoiceClone(Binding);
    }
    internal static void DismissAllGloomVoiceClones()
    {
        foreach (var Slot in Map.Slots) DismissGloomVoiceClone(Slot.GloomVoice);
    }

    static void PublishGloomVoiceClone(GloomVoiceClone Binding, Peak.Island.RavenPerch[] FullCatalog)
    {
        var Run = Map.CurrentRun;
        if (Run?.HasPublishedLayout != true) return;
        var IndexValue = Map.Index;
        if (IndexValue < 0 || IndexValue >= Map.Slots.Length || !ReferenceEquals(Map.Slots[IndexValue].GloomVoice, Binding)) return;
        if (Binding.RunIdentity != Run.Identity || IndexValue < Binding.FirstSlot || IndexValue > Binding.LastSlot)
            throw new InvalidOperationException("The current Gloom voice manager is outside its published slot ownership.");
        Peak.Island.GloomVoiceConfuser.AllPerches = FullCatalog;
    }
    internal static void PublishCurrentGloomVoiceClone()
    {
        var IndexValue = Map.Index;
        if (IndexValue < 0 || IndexValue >= Map.Slots.Length
            || Map.Slots[IndexValue].GloomVoice is not { FullCatalog: { } FullCatalog } Binding)
            return;
        Binding.RequireSlotOwnership(Map.Slots);
        PublishGloomVoiceClone(Binding, FullCatalog);
    }
    internal static void DestroyGloomProfiles(IReadOnlyList<GloomProfileClone> Profiles)
    {
        foreach (var Profile in Profiles) Profile.Release();
    }

    static bool GloomVoiceConfuserMayUpdate(Peak.Island.GloomVoiceConfuser Value)
    {
        if (!GloomVoiceBindings.TryGetValue(Value, out var Binding)) return true;
        var Run = Map.CurrentRun;
        if (!Value || Run is null || !Run.HasPublishedLayout || Binding.RunIdentity != Run.Identity
            || !Binding.Ready || Binding.ExitClaimed)
            return false;
        var IndexValue = Map.Index;
        var MayUpdate = IndexValue >= Binding.FirstSlot && IndexValue <= Binding.LastSlot
            && !Map.Slots[IndexValue].Released
            && ReferenceEquals(Map.Slots[IndexValue].GloomVoice, Binding);
        if (MayUpdate) Binding.SeedHostAuthority(Peak.Network.NetCode.Session.IsHost);
        return MayUpdate;
    }

    static bool GloomVoiceCloneMayRunNativeLifecycle(Peak.Island.GloomVoiceConfuser Value) =>
        !GloomVoiceBindings.TryGetValue(Value, out _);

    [HarmonyPatch(typeof(Peak.Island.RavenPerch), nameof(Peak.Island.RavenPerch.SpawnAmbienceRaven))]
    static class RavenPerchSpawnAmbiencePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => !Application.isPlaying || Peak.Network.NetCode.Session.IsHost;
    }

    [HarmonyPatch(typeof(Peak.Island.GloomVoiceConfuser), nameof(Peak.Island.GloomVoiceConfuser.Awake))]
    static class GloomVoiceConfuserAwakePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Peak.Island.GloomVoiceConfuser __instance) =>
            GloomVoiceCloneMayRunNativeLifecycle(__instance);
    }

    [HarmonyPatch(typeof(Peak.Island.GloomVoiceConfuser),
        nameof(Peak.Island.GloomVoiceConfuser.OnLevelGenFinished))]
    static class GloomVoiceConfuserFinalizerPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Peak.Island.GloomVoiceConfuser __instance) =>
            GloomVoiceCloneMayRunNativeLifecycle(__instance);
    }

    [HarmonyPatch(typeof(Peak.Island.GloomVoiceConfuser), nameof(Peak.Island.GloomVoiceConfuser.Update))]
    static class GloomVoiceConfuserUpdatePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Peak.Island.GloomVoiceConfuser __instance) =>
            GloomVoiceConfuserMayUpdate(__instance);

        [HarmonyPostfix]
        static void Postfix(Peak.Island.GloomVoiceConfuser __instance)
        {
            var ManagerValue = __instance;
            if (ManagerValue.enabled || !GloomVoiceBindings.TryGetValue(ManagerValue, out var Binding)) return;
            var Run = Map.CurrentRun;
            var IndexValue = Map.Index;
            if (Run?.HasPublishedLayout != true || Binding.RunIdentity != Run.Identity
                || !Binding.Ready || IndexValue != Binding.LastSlot || Map.Slots[IndexValue].Released
                || !ReferenceEquals(Map.Slots[IndexValue].GloomVoice, Binding)) return;
            Binding.TryClaimExit();
        }
    }
}
