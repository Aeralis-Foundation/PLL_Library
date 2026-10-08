using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class SpecialDayProjection
{
    sealed class AdapterContribution
    {
        internal readonly MapSlot Slot;
        internal readonly GameObject Root;
        internal readonly SpecialDayZone Zone;
        internal readonly NativeFogSource? Fog;
        readonly int[] ShaderIds;
        readonly float[] BaselineValues;
        readonly float[] AppliedValues;
        NativeState Baseline;
        NativeState Applied;
        bool Projected;
        internal float Coverage;
        internal float FogCoverage;

        internal AdapterContribution(MapSlot Slot, GameObject Root, SpecialDayZone Zone, NativeFogSource? Fog)
        {
            this.Slot = Slot;
            this.Root = Root;
            this.Zone = Zone;
            this.Fog = Fog;
            var DayNight = DayNightManager.instance;
            ShaderIds = new int[Zone.globalShaderVals.Length + 1];
            ShaderIds[0] = Shader.PropertyToID("SpecialDayBlend");
            for (var i = 1; i < ShaderIds.Length; i++)
                ShaderIds[i] = Shader.PropertyToID(DayNight.getShaderValue(Zone.globalShaderVals[i - 1].parameter));
            BaselineValues = new float[ShaderIds.Length];
            AppliedValues = new float[ShaderIds.Length];
        }

        internal void CaptureBaseline()
        {
            Baseline = new NativeState();
            for (var i = 0; i < ShaderIds.Length; i++)
                BaselineValues[i] = Shader.GetGlobalFloat(ShaderIds[i]);
        }

        internal void Compose()
        {
            var DayNight = DayNightManager.instance;
            var Ambience = AmbienceManager.instance;
            if (Zone.overrideFog)
                Ambience.maxFog = Mathf.Lerp(Baseline.Fog, Zone.fogDensity, Coverage);
            DayNight.lensFlareStr = Mathf.Lerp(Baseline.LensFlare, Zone.hideLensFlare ? 0f : 1f, Coverage);
            Shader.SetGlobalFloat(ShaderIds[0], Mathf.Lerp(BaselineValues[0], 1f, Coverage));
            for (var i = 1; i < ShaderIds.Length; i++)
                Shader.SetGlobalFloat(ShaderIds[i], Mathf.Lerp(BaselineValues[i], Zone.globalShaderVals[i - 1].value, Coverage));
            CaptureApplied();
        }

        internal void CaptureApplied()
        {
            Applied = new NativeState();
            for (var i = 0; i < ShaderIds.Length; i++)
                AppliedValues[i] = Shader.GetGlobalFloat(ShaderIds[i]);
            Projected = true;
        }

        internal void Restore()
        {
            if (!Projected) return;
            Projected = false;
            Baseline.Restore(Applied);
            for (var i = 0; i < ShaderIds.Length; i++)
                if (Shader.GetGlobalFloat(ShaderIds[i]).Equals(AppliedValues[i]))
                    Shader.SetGlobalFloat(ShaderIds[i], BaselineValues[i]);
            if (Zone.specialLight) Zone.specialLight.enabled = false;
        }
    }

    readonly struct NativeState
    {
        readonly DayNightManager DayNight;
        readonly AmbienceManager Ambience;
        readonly Color Sun;
        readonly Color Top;
        readonly Color Mid;
        readonly Color Bottom;
        readonly float SunBlend;
        readonly float SkyBlend;
        internal readonly float Fog;
        internal readonly float LensFlare;

        public NativeState()
        {
            DayNight = DayNightManager.instance;
            Ambience = AmbienceManager.instance;
            Sun = DayNight.specialSunColor;
            Top = DayNight.specialTopColor;
            Mid = DayNight.specialMidColor;
            Bottom = DayNight.specialBottomColor;
            SunBlend = DayNight.specialDaySunBlend;
            SkyBlend = DayNight.specialDaySkyBlend;
            LensFlare = DayNight.lensFlareStr;
            Fog = Ambience.maxFog;
        }

        internal void Restore(NativeState Applied)
        {
            if (DayNight && ReferenceEquals(DayNight, DayNightManager.instance))
            {
                if (DayNight.specialSunColor.Equals(Applied.Sun)) DayNight.specialSunColor = Sun;
                if (DayNight.specialTopColor.Equals(Applied.Top)) DayNight.specialTopColor = Top;
                if (DayNight.specialMidColor.Equals(Applied.Mid)) DayNight.specialMidColor = Mid;
                if (DayNight.specialBottomColor.Equals(Applied.Bottom)) DayNight.specialBottomColor = Bottom;
                if (DayNight.specialDaySunBlend.Equals(Applied.SunBlend)) DayNight.specialDaySunBlend = SunBlend;
                if (DayNight.specialDaySkyBlend.Equals(Applied.SkyBlend)) DayNight.specialDaySkyBlend = SkyBlend;
                if (DayNight.lensFlareStr.Equals(Applied.LensFlare)) DayNight.lensFlareStr = LensFlare;
            }
            if (Ambience && ReferenceEquals(Ambience, AmbienceManager.instance) && Ambience.maxFog.Equals(Applied.Fog))
                Ambience.maxFog = Fog;
        }
    }

    static void RestoreContributions()
    {
        if (EnvironmentFogProjected)
        {
            EnvironmentFogProjected = false;
            if (EnvironmentFogOwner && ReferenceEquals(EnvironmentFogOwner, AmbienceManager.instance)
                && EnvironmentFogOwner.maxFog.Equals(EnvironmentFogApplied))
                EnvironmentFogOwner.maxFog = EnvironmentFogBaseline;
        }
        for (var i = Contributions.Count - 1; i >= 0; i--) Contributions[i].Restore();
    }

    static AmbienceManager? EnvironmentFogOwner;
    static float EnvironmentFogBaseline;
    static float EnvironmentFogApplied;
    static bool EnvironmentFogProjected;

    static void ComposeEnvironmentalFog()
    {
        var Coverage = 0f;
        NativeFogSource? Source = null;
        foreach (var Contribution in Contributions)
            if (Contribution.Fog is { OverrideFog: true } && Contribution.FogCoverage > Coverage)
            {
                Coverage = Contribution.FogCoverage;
                Source = Contribution.Fog;
            }
        if (Source is null)
        {
            EnvironmentFogOwner = null;
            return;
        }
        EnvironmentFogOwner = AmbienceManager.instance;
        EnvironmentFogBaseline = EnvironmentFogOwner.maxFog;
        EnvironmentFogApplied = Mathf.Lerp(EnvironmentFogBaseline, Source.FogDensity, Coverage);
        EnvironmentFogOwner.maxFog = EnvironmentFogApplied;
        EnvironmentFogProjected = true;
    }

    sealed class ZoneBounds
    {
        internal ZoneBounds(SpecialDayZone Zone)
        {
            AuthoredInner = Zone.bounds;
            AuthoredOuter = Zone.outerBounds;
        }

        Bounds AuthoredInner;
        Bounds AuthoredOuter;
        Bounds ProjectedInner;
        Bounds ProjectedOuter;
        bool HasProjection;

        internal void CaptureChanges(SpecialDayZone Zone)
        {
            if (!HasProjection) return;
            var Changed = false;
            if (!Zone.bounds.Equals(ProjectedInner))
            {
                AuthoredInner = Zone.bounds;
                Changed = true;
            }
            if (!Zone.outerBounds.Equals(ProjectedOuter))
            {
                AuthoredOuter = Zone.outerBounds;
                Changed = true;
            }
            if (Changed) HasProjection = false;
        }

        internal void Project(SpecialDayZone Zone, PlacementFrame Frame)
        {
            CaptureChanges(Zone);
            var Inner = AuthoredInner;
            var Outer = AuthoredOuter;
            if (Frame.Rotated)
            {
                var Center = Frame.InversePoint(Zone.transform.position);
                Inner.center = Center;
                Inner = Frame.TransformBounds(Inner);
                Outer.center = Center;
                Outer.size = AuthoredInner.size + Vector3.one * Zone.blendSize;
                Outer = Frame.TransformBounds(Outer);
            }
            else
            {
                Inner.center = Zone.transform.position;
                Outer.center = Zone.transform.position;
                Outer.size = Inner.size + Vector3.one * Zone.blendSize;
            }
            Zone.bounds = Inner;
            Zone.outerBounds = Outer;
            ProjectedInner = Inner;
            ProjectedOuter = Outer;
            HasProjection = true;
        }

        internal void Capture(SpecialDayZone Zone)
        {
            AuthoredInner = Zone.bounds;
            AuthoredOuter = Zone.outerBounds;
            HasProjection = false;
        }

        internal void Restore(SpecialDayZone Zone)
        {
            Zone.bounds = AuthoredInner;
            Zone.outerBounds = AuthoredOuter;
            HasProjection = false;
        }
    }

    static SpecialDayManager? Owner;
    static SpecialDayZone[] NativeZones = Array.Empty<SpecialDayZone>();
    static SpecialDayZone[] ProjectedZones = Array.Empty<SpecialDayZone>();
    static Dictionary<SpecialDayZone, ZoneBounds> BoundsByZone = new();
    static readonly Dictionary<SpecialDayZone, bool> RollbackLights = new();
    static List<AdapterContribution> Contributions = new();
    static bool TransactionCaptured;
    static bool SuppressLifecycle;

    internal static void Capture()
    {
        CaptureRollbackLights();
        var Manager = Owner ? Owner : UnityEngine.Object.FindAnyObjectByType<SpecialDayManager>();
        if (Manager) Capture(Manager);
    }

    static void Capture(SpecialDayManager Manager)
    {
        if (Owner == Manager) return;
        if (Owner is not null) ReleaseProjection();
        Owner = Manager;
        CaptureNativeZones(Manager);
    }

    static void CaptureNativeZones(SpecialDayManager Manager)
    {
        var Captured = Manager.zones is { Length: > 0 }
            ? (SpecialDayZone[])Manager.zones.Clone()
            : UnityEngine.Object.FindObjectsByType<SpecialDayZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var Default = Manager.defaultZone;
        if (Default && !ContainsInstance(Captured, Default.GetInstanceID()))
        {
            Array.Resize(ref Captured, Captured.Length + 1);
            Captured[Captured.Length - 1] = Default;
        }
        NativeZones = Captured;
        foreach (var Zone in NativeZones)
            if (Zone && !BoundsByZone.ContainsKey(Zone))
                BoundsByZone.Add(Zone, new ZoneBounds(Zone));
    }

    static void CaptureRollbackLights()
    {
        RollbackLights.Clear();
        foreach (var Zone in UnityEngine.Object.FindObjectsByType<SpecialDayZone>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (Zone && Zone.specialLight)
                RollbackLights[Zone] = Zone.specialLight.enabled;
        TransactionCaptured = true;
    }

    internal static void Commit()
    {
        RollbackLights.Clear();
        TransactionCaptured = false;
    }

    internal static void Release() => ReleaseProjection();

    internal static void Rollback()
    {
        Exception? Failure = null;
        try { ReleaseProjection(); }
        catch (Exception Exception) { Failure = Exception; }
        try { RestoreRollbackLights(); }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Special-day projection and light rollback both failed.", Failure, Exception);
        }
        finally
        {
            RollbackLights.Clear();
            TransactionCaptured = false;
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    static void RestoreRollbackLights()
    {
        if (!TransactionCaptured) return;
        Exception? Failure = null;
        foreach (var State in RollbackLights)
            try
            {
                if (State.Key && State.Key.specialLight) State.Key.specialLight.enabled = State.Value;
            }
            catch (Exception Exception)
            {
                Failure ??= Exception;
            }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    internal static void Refresh()
    {
        var Manager = Owner ? Owner : UnityEngine.Object.FindAnyObjectByType<SpecialDayManager>();
        if (!Manager) return;
        if (Owner != Manager) Capture(Manager);
        if (!Map.HasPublishedLayout)
        {
            ReleaseProjection();
            return;
        }

        var Desired = BuildDesired();
        foreach (var Zone in ProjectedZones)
            if (Zone && !ContainsInstance(Desired, Zone.GetInstanceID()) && Zone.specialLight)
                Zone.specialLight.enabled = false;
        Manager.zones = Merge(Manager.zones, ProjectedZones, NativeZones, Desired);
        ProjectedZones = Desired;
    }

    static SpecialDayZone[] BuildDesired()
    {
        var Slots = Map.Slots;
        var Zones = new List<SpecialDayZone>();
        var SeenRoots = new HashSet<int>();
        var SeenZones = new HashSet<int>();
        var Default = Owner?.defaultZone;
        if (Default && SeenZones.Add(Default.GetInstanceID())) Zones.Add(Default);
        var PeakFrames = new Dictionary<Transform, PlacementFrame>();
        foreach (var Slot in Slots)
        {
            if (Slot.Released || !Slot.PeakAttachment) continue;
            PeakFrames.Add(Slot.PeakAttachment, PlacementPlan.PlacedPeakFrame(Slot));
        }

        void AddZones(Transform? Root, PlacementFrame Frame)
        {
            if (!Root || !Root.gameObject.activeInHierarchy
                || !SeenRoots.Add(Root.GetInstanceID())) return;
            foreach (var Zone in Root.GetComponentsInChildren<SpecialDayZone>(true))
            {
                if (!Publishable(Zone) || !SeenZones.Add(Zone.GetInstanceID())) continue;
                var PlacedFrame = Frame;
                for (var Parent = Zone.transform; Parent; Parent = Parent.parent)
                {
                    if (!PeakFrames.TryGetValue(Parent, out var PeakFrame)) continue;
                    PlacedFrame = PeakFrame;
                    break;
                }
                if (!BoundsByZone.TryGetValue(Zone, out var Bounds))
                {
                    Bounds = new ZoneBounds(Zone);
                    BoundsByZone.Add(Zone, Bounds);
                }
                Bounds.Project(Zone, PlacedFrame);
                Zones.Add(Zone);
            }
        }

        foreach (var Slot in Slots)
        {
            if (Slot.Released) continue;
            AddZones(Slot.Container, Slot.Frame);
            var Peak = Slot.PeakAttachment;
            if (Peak && (!Slot.Container || Peak != Slot.Container && !Peak.IsChildOf(Slot.Container)))
                AddZones(Peak, PeakFrames[Peak]);
        }
        foreach (var Contribution in Contributions)
            if (Contribution.Coverage > 0f && Publishable(Contribution.Zone)
                && SeenZones.Add(Contribution.Zone.GetInstanceID()))
                Zones.Add(Contribution.Zone);
        return Zones.ToArray();
    }

    static bool Publishable(SpecialDayZone? Zone) =>
        Zone && Zone.enabled && Zone.gameObject.activeInHierarchy;

    static SpecialDayZone[] Merge(
        SpecialDayZone[] Current,
        IReadOnlyList<SpecialDayZone> Projected,
        IReadOnlyList<SpecialDayZone> Displaced,
        IReadOnlyList<SpecialDayZone> Desired)
    {
        if (Matches(Current, Projected, Displaced, Desired)) return Current;
        var Retained = 0;
        foreach (var Zone in Current)
        {
            if (!Publishable(Zone)) continue;
            var ZoneId = Zone.GetInstanceID();
            if (!ContainsInstance(Projected, ZoneId) && !ContainsInstance(Displaced, ZoneId)
                && !ContainsInstance(Desired, ZoneId)) Retained++;
        }
        var Result = new SpecialDayZone[Retained + Desired.Count];
        var Next = 0;
        foreach (var Zone in Current)
        {
            if (!Publishable(Zone)) continue;
            var ZoneId = Zone.GetInstanceID();
            if (!ContainsInstance(Projected, ZoneId) && !ContainsInstance(Displaced, ZoneId)
                && !ContainsInstance(Desired, ZoneId)) Result[Next++] = Zone;
        }
        for (var i = 0; i < Desired.Count; i++) Result[Next++] = Desired[i];
        return Result;
    }

    static bool Matches(
        SpecialDayZone[] Current,
        IReadOnlyList<SpecialDayZone> Projected,
        IReadOnlyList<SpecialDayZone> Displaced,
        IReadOnlyList<SpecialDayZone> Desired)
    {
        if (Current.Length < Desired.Count) return false;
        var Retained = Current.Length - Desired.Count;
        for (var i = 0; i < Retained; i++)
        {
            var Zone = Current[i];
            if (!Publishable(Zone)) return false;
            var ZoneId = Zone.GetInstanceID();
            if (ContainsInstance(Projected, ZoneId) || ContainsInstance(Displaced, ZoneId)
                || ContainsInstance(Desired, ZoneId)) return false;
        }
        for (var i = 0; i < Desired.Count; i++)
        {
            var Zone = Current[Retained + i];
            if (Zone is null || Zone.GetInstanceID() != Desired[i].GetInstanceID()) return false;
        }
        return true;
    }

    static bool ContainsInstance(IReadOnlyList<SpecialDayZone> Values, int TargetId)
    {
        for (var i = 0; i < Values.Count; i++)
        {
            var Value = Values[i];
            if (Value is not null && Value.GetInstanceID() == TargetId) return true;
        }
        return false;
    }

    internal static void Reconcile()
    {
        RestoreContributions();
        Refresh();
        if (!Owner) return;
        Owner.lastBlendFactor = 0f;
        ResetNativeBlends();
        Owner.UpdateSpecialDayZone(Owner.defaultZone, 0f);
    }

    static void ResetNativeBlends()
    {
        var DayNight = DayNightManager.instance;
        if (DayNight)
        {
            DayNight.specialDaySunBlend = 0f;
            DayNight.specialDaySkyBlend = 0f;
        }
    }

    internal static void DeactivatePreservingGlobals(GameObject Value)
    {
        var DayNight = DayNightManager.instance;
        var Globals = new Dictionary<DayNightManager.ShaderParams, float>();
        foreach (var Zone in Value.GetComponentsInChildren<SpecialDayZone>(true))
        {
            if (!Publishable(Zone)) continue;
            foreach (var ShaderValue in Zone.globalShaderVals)
            {
                if (!DayNight)
                    throw new InvalidOperationException("PEAK's DayNightManager is unavailable for special-day deactivation.");
                var Parameter = ShaderValue.parameter;
                if (!Globals.ContainsKey(Parameter))
                    Globals.Add(Parameter, Shader.GetGlobalFloat(DayNight.getShaderValue(Parameter)));
            }
        }

        Exception? Failure = null;
        try
        {
            SuppressLifecycle = true;
            Value.SetActive(false);
        }
        catch (Exception Exception)
        {
            Failure = Exception;
        }
        finally
        {
            SuppressLifecycle = false;
        }
        foreach (var Global in Globals)
            try
            {
                Shader.SetGlobalFloat(DayNight.getShaderValue(Global.Key), Global.Value);
            }
            catch (Exception Exception)
            {
                Failure ??= Exception;
            }
        try
        {
            if (Map.HasPublishedLayout) Refresh();
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Special-day deactivation and projection refresh both failed.", Failure, Exception);
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    internal static void PrepareAdapter()
    {
        var Manager = Owner ? Owner : UnityEngine.Object.FindAnyObjectByType<SpecialDayManager>();
        if (!Manager)
            throw new InvalidOperationException("PEAK's SpecialDayManager is unavailable for the native visual adapter.");
        if (Owner != Manager) Capture(Manager);
    }

    internal static void Apply(MapSlot Slot, NativeSpecialDaySnapshot? Snapshot, NativeFogSource? Fog)
    {
        Clear(Slot);
        if (Snapshot is null && Fog is null) return;
        PrepareAdapter();
        var Root = new GameObject("PLL_NativeSpecialDay");
        SpecialDayZone? Zone = null;
        try
        {
            Root.SetActive(false);
            Root.transform.SetParent(Owner!.transform, false);
            Zone = Root.AddComponent<SpecialDayZone>();
            ClearAdapterValues(Zone, Root);
            Snapshot?.ApplyTo(Zone, Root.transform);
            SuppressLifecycle = true;
            Root.SetActive(true);
            Zone.globalZone = true;
            Contributions.Add(new AdapterContribution(Slot, Root, Zone, Fog));
            Contributions.Sort(static (a, b) => a.Slot.Index.CompareTo(b.Slot.Index));
            Refresh();
        }
        catch (Exception Failure)
        {
            List<Exception>? Failures = null;
            AuthoringCleanup.Capture(ref Failures, RestoreContributions);
            Contributions.RemoveAll(Value => Value.Root == Root);
            if (Zone && Owner) Owner.zones = Remove(Owner.zones, Zone);
            AuthoringCleanup.Capture(ref Failures, () => ReleaseAdapter(Root, Zone));
            if (Map.HasPublishedLayout) AuthoringCleanup.Capture(ref Failures, Refresh);
            if (Failures is not null)
            {
                Failures.Insert(0, Failure);
                throw new AggregateException("Special-day adapter creation and cleanup failed.", Failures);
            }
            throw;
        }
        finally
        {
            SuppressLifecycle = false;
        }
    }

    internal static void SetCoverage(MapSlot Slot, float Coverage, float FogCoverage)
    {
        foreach (var Contribution in Contributions)
            if (ReferenceEquals(Contribution.Slot, Slot))
            {
                var WasPresent = Contribution.Coverage > 0f;
                Contribution.Coverage = Coverage;
                Contribution.FogCoverage = FogCoverage;
                if (WasPresent != (Coverage > 0f) && Map.HasPublishedLayout) Refresh();
                return;
            }
        throw new InvalidOperationException("The native visual contribution has no owned SpecialDay adapter.");
    }

    internal static void Clear(MapSlot Slot)
    {
        for (var i = 0; i < Contributions.Count; i++)
        {
            var Contribution = Contributions[i];
            if (!ReferenceEquals(Contribution.Slot, Slot)) continue;
            RestoreContributions();
            Contributions.RemoveAt(i);
            if (Owner) Owner.zones = Remove(Owner.zones, Contribution.Zone);
            ReleaseAdapter(Contribution.Root, Contribution.Zone);
            if (Map.HasPublishedLayout) Refresh();
            if (Owner)
            {
                foreach (var Retained in Contributions)
                    Owner.UpdateSpecialDayZone(Retained.Zone, Retained.Coverage);
                ComposeEnvironmentalFog();
            }
            return;
        }
    }

    static void ClearAdapterValues(SpecialDayZone Zone, GameObject? RootObject)
    {
        Zone.specialLight = null!;
        if (RootObject)
        {
            var Root = RootObject.transform;
            for (var i = Root.childCount - 1; i >= 0; i--)
            {
                var Child = Root.GetChild(i).gameObject;
                Child.SetActive(false);
                UnityEngine.Object.DestroyImmediate(Child);
            }
        }
        Zone.globalZone = false;
        Zone.overrideSun = false;
        Zone.daylLightIntensity = 0f;
        Zone.nightLightIntensity = 0f;
        Zone.specialSunColor = default;
        Zone.useCustomSun = false;
        Zone.useCustomColorVals = false;
        Zone.specialTopColor = default;
        Zone.specialMidColor = default;
        Zone.specialBottomColor = default;
        Zone.globalShaderVals = Array.Empty<ShaderVal>();
        Zone.overrideFog = false;
        Zone.fogDensity = 0f;
        Zone.hideLensFlare = false;
        Zone.bounds = default;
        Zone.outerBounds = default;
        Zone.blendSize = 0f;
        Zone.inBounds = false;
    }

    static void AddFailure(ref List<Exception>? Failures, Exception Failure) =>
        (Failures ??= new()).Add(Failure);

    static void ThrowFailures(List<Exception>? Failures, string Message)
    {
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException(Message, Failures);
    }

    static void ReleaseAdapter(GameObject? Root, SpecialDayZone? Zone)
    {
        List<Exception>? Failures = null;
        if (Zone) Zone.globalShaderVals = Array.Empty<ShaderVal>();
        if (Root)
        {
            try
            {
                SuppressLifecycle = true;
                Root.SetActive(false);
            }
            catch (Exception Exception)
            {
                AddFailure(ref Failures, Exception);
            }
            finally
            {
                SuppressLifecycle = false;
            }
        }
        try
        {
            if (Zone) ClearAdapterValues(Zone!, Root);
        }
        catch (Exception Exception)
        {
            AddFailure(ref Failures, Exception);
        }
        try
        {
            if (Root) UnityEngine.Object.DestroyImmediate(Root);
        }
        catch (Exception Exception)
        {
            AddFailure(ref Failures, Exception);
        }
        ThrowFailures(Failures, "Special-day adapter release failed.");
    }

    static SpecialDayZone[] Remove(SpecialDayZone[] Values, SpecialDayZone Target)
    {
        var TargetId = Target.GetInstanceID();
        var Count = 0;
        foreach (var Value in Values)
            if (Value is not null && Value.GetInstanceID() == TargetId) Count++;
        if (Count == 0) return Values;
        var Result = new SpecialDayZone[Values.Length - Count];
        var Next = 0;
        foreach (var Value in Values)
            if (Value is null || Value.GetInstanceID() != TargetId) Result[Next++] = Value!;
        return Result;
    }
    static void ReleaseProjection()
    {
        var ReleasedOwner = Owner;
        var ReleasedNativeZones = NativeZones;
        var ReleasedProjectedZones = ProjectedZones;
        var ReleasedBoundsByZone = BoundsByZone;
        RestoreContributions();
        if (ReleasedOwner) ResetNativeBlends();
        var ReleasedContributions = Contributions;
        EnvironmentFogOwner = null;

        Owner = null;
        NativeZones = Array.Empty<SpecialDayZone>();
        ProjectedZones = Array.Empty<SpecialDayZone>();
        BoundsByZone = new();
        Contributions = new();

        List<Exception>? Failures = null;
        foreach (var Zone in ReleasedProjectedZones)
            try
            {
                if (Zone && Zone.specialLight) Zone.specialLight.enabled = false;
            }
            catch (Exception Exception)
            {
                AddFailure(ref Failures, Exception);
            }
        foreach (var State in ReleasedBoundsByZone)
            try
            {
                if (State.Key) State.Value.Restore(State.Key);
            }
            catch (Exception Exception)
            {
                AddFailure(ref Failures, Exception);
            }
        try
        {
            if (ReleasedOwner)
            {
                var Desired = new List<SpecialDayZone>(ReleasedNativeZones.Length);
                foreach (var Zone in ReleasedNativeZones)
                    if (Publishable(Zone) && !ContainsInstance(Desired, Zone.GetInstanceID())) Desired.Add(Zone);
                var CurrentZones = ReleasedOwner.zones;
                foreach (var Contribution in ReleasedContributions)
                    CurrentZones = Remove(CurrentZones, Contribution.Zone);
                ReleasedOwner.zones = Merge(
                    CurrentZones, ReleasedProjectedZones, ReleasedNativeZones, Desired);
            }
        }
        catch (Exception Exception)
        {
            AddFailure(ref Failures, Exception);
        }
        foreach (var Contribution in ReleasedContributions)
            AuthoringCleanup.Capture(ref Failures, () => ReleaseAdapter(Contribution.Root, Contribution.Zone));
        ThrowFailures(Failures, "Special-day projection release failed.");
    }

    static void ZoneChanged()
    {
        if (SuppressLifecycle || !Map.HasPublishedLayout) return;
        Refresh();
    }

    [HarmonyPatch(typeof(SpecialDayManager), "Update")]
    static class ManagerUpdatePatch
    {
        [HarmonyPrefix]
        static void Prefix(SpecialDayManager __instance)
        {
            if (__instance != Owner) return;
            RestoreContributions();
            ResetNativeBlends();
        }

        [HarmonyPostfix]
        static void Postfix(SpecialDayManager __instance)
        {
            if (__instance == Owner) ComposeEnvironmentalFog();
        }

        [HarmonyFinalizer]
        static void Finalizer(SpecialDayManager __instance, Exception? __exception)
        {
            if (__exception is not null && __instance == Owner) RestoreContributions();
        }
    }

    [HarmonyPatch(typeof(SpecialDayManager), nameof(SpecialDayManager.UpdateSpecialDayZone))]
    static class AdapterUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SpecialDayManager __instance, SpecialDayZone zone, ref float forceBlendFactor,
            out AdapterContribution? __state)
        {
            __state = null;
            if (__instance != Owner) return true;
            foreach (var Contribution in Contributions)
                if (Contribution.Zone == zone)
                {
                    if (Contribution.Coverage == 0f) return false;
                    __state = Contribution;
                    Contribution.CaptureBaseline();
                    forceBlendFactor = Contribution.Coverage;
                    return Contribution.Coverage > 0f;
                }
            return true;
        }

        [HarmonyPostfix]
        static void Postfix(AdapterContribution? __state) => __state?.Compose();

        [HarmonyFinalizer]
        static void Finalizer(AdapterContribution? __state, Exception? __exception)
        {
            if (__state is null || __exception is null) return;
            __state.CaptureApplied();
            __state.Restore();
        }
    }

    [HarmonyPatch(typeof(SpecialDayManager), "Start")]
    static class ManagerStartPatch
    {
        [HarmonyPostfix]
        static void Postfix(SpecialDayManager __instance)
        {
            if (!Map.HasPublishedLayout) return;
            if (Owner != __instance) Capture(__instance);
            else CaptureNativeZones(__instance);
            Refresh();
        }
    }

    [HarmonyPatch(typeof(SpecialDayZone), "Start")]
    static class ZoneStartPatch
    {
        [HarmonyPrefix]
        static void Prefix(SpecialDayZone __instance)
        {
            if (BoundsByZone.TryGetValue(__instance, out var Bounds)) Bounds.Restore(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(SpecialDayZone __instance)
        {
            if (BoundsByZone.TryGetValue(__instance, out var Bounds)) Bounds.Capture(__instance);
            ZoneChanged();
        }
    }

    [HarmonyPatch(typeof(SpecialDayZone), "OnEnable")]
    static class ZoneEnablePatch
    {
        [HarmonyPostfix]
        static void Postfix(SpecialDayZone __instance)
        {
            if (BoundsByZone.ContainsKey(__instance)) ZoneChanged();
        }
    }

    [HarmonyPatch(typeof(SpecialDayZone), "OnDisable")]
    static class ZoneDisablePatch
    {
        [HarmonyPostfix]
        static void Postfix(SpecialDayZone __instance)
        {
            if (BoundsByZone.TryGetValue(__instance, out var Bounds)) Bounds.CaptureChanges(__instance);
            ZoneChanged();
        }
    }
}
