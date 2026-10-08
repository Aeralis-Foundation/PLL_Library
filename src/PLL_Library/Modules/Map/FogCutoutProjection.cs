using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class FogCutoutProjection
{
    static readonly int FogCutoutAmount = Shader.PropertyToID("FogCutoutAmount");
    static readonly int FogCutoutPosition = Shader.PropertyToID("FogCutoutPosition");
    static readonly int FogCutoutMinimum = Shader.PropertyToID("FogCutoutMin");
    static readonly int FogCutoutMaximum = Shader.PropertyToID("FogCutoutMax");
    static readonly FogCutoutZone[] EmptyZones = Array.Empty<FogCutoutZone>();
    static readonly ZoneProjection[] EmptyProjections = Array.Empty<ZoneProjection>();
    static FogCutoutHandler? Owner;
    static FogCutoutZone[] NativeZones = EmptyZones;
    static NativeFogCutoutTable? NativeTemplates;
    static FogCutoutZone? NativeCurrent;
    static int NativeIndex;
    static bool NativeEnabled;
    static ShaderState NativeShader;
    static ZoneProjection[] ProjectedZones = EmptyProjections;
    static readonly Dictionary<MapSlot, float> EnvironmentContributions = new();
    static NativeFogSource? EnvironmentSource;
    static ShaderState EnvironmentBaseline;
    static ShaderState EnvironmentApplied;
    static bool EnvironmentProjected;
    static GameObject? ProjectionRoot;
    static bool Captured;
    static bool Publishing;

    sealed class EnvironmentComposition : MonoBehaviour
    {
        void Update() => RestoreEnvironmentProjection();
        void LateUpdate() => ComposeEnvironmentProjection();
    }

    static void RestoreEnvironmentProjection()
    {
        if (!EnvironmentProjected) return;
        EnvironmentProjected = false;
        if (Shader.GetGlobalFloat(FogCutoutAmount).Equals(EnvironmentApplied.Amount))
            Shader.SetGlobalFloat(FogCutoutAmount, EnvironmentBaseline.Amount);
        if (Shader.GetGlobalFloat(FogCutoutMinimum).Equals(EnvironmentApplied.Minimum))
            Shader.SetGlobalFloat(FogCutoutMinimum, EnvironmentBaseline.Minimum);
        if (Shader.GetGlobalFloat(FogCutoutMaximum).Equals(EnvironmentApplied.Maximum))
            Shader.SetGlobalFloat(FogCutoutMaximum, EnvironmentBaseline.Maximum);
    }

    static void ComposeEnvironmentProjection()
    {
        var Coverage = 0f;
        foreach (var Value in EnvironmentContributions.Values) Coverage = Mathf.Max(Coverage, Value);
        if (Coverage == 0f) return;
        var Source = EnvironmentSource!;
        EnvironmentBaseline = ShaderState.Capture();
        Shader.SetGlobalFloat(FogCutoutMinimum, Mathf.Lerp(EnvironmentBaseline.Minimum, Source.CutoutMin, Coverage));
        Shader.SetGlobalFloat(FogCutoutMaximum, Mathf.Lerp(EnvironmentBaseline.Maximum, Source.CutoutMax, Coverage));
        Shader.SetGlobalFloat(FogCutoutAmount, Mathf.Lerp(EnvironmentBaseline.Amount, Source.CutoutAmount, Coverage));
        EnvironmentApplied = ShaderState.Capture();
        EnvironmentProjected = true;
    }

    readonly struct ShaderState
    {
        internal ShaderState(float Amount, Vector4 Position, float Minimum, float Maximum)
        {
            this.Amount = Amount;
            this.Position = Position;
            this.Minimum = Minimum;
            this.Maximum = Maximum;
        }

        internal float Amount { get; }
        internal Vector4 Position { get; }
        internal float Minimum { get; }
        internal float Maximum { get; }

        internal static ShaderState Capture() => new(
            Shader.GetGlobalFloat(FogCutoutAmount),
            Shader.GetGlobalVector(FogCutoutPosition),
            Shader.GetGlobalFloat(FogCutoutMinimum),
            Shader.GetGlobalFloat(FogCutoutMaximum));

        internal void Apply()
        {
            Shader.SetGlobalFloat(FogCutoutAmount, Amount);
            Shader.SetGlobalVector(FogCutoutPosition, Position);
            Shader.SetGlobalFloat(FogCutoutMinimum, Minimum);
            Shader.SetGlobalFloat(FogCutoutMaximum, Maximum);
        }
    }

    readonly struct HandlerState
    {
        internal HandlerState(FogCutoutHandler Handler) : this(
            Handler.enabled,
            Handler.cutoutZones,
            Handler.currentCutoutZone,
            Handler.index,
            ShaderState.Capture())
        {
        }

        internal HandlerState(
            bool Enabled,
            FogCutoutZone[] Zones,
            FogCutoutZone? Current,
            int Index,
            ShaderState Shader)
        {
            this.Enabled = Enabled;
            this.Zones = Zones;
            this.Current = Current;
            this.Index = Index;
            this.Shader = Shader;
        }

        internal bool Enabled { get; }
        internal FogCutoutZone[] Zones { get; }
        internal FogCutoutZone? Current { get; }
        internal int Index { get; }
        internal ShaderState Shader { get; }
    }

    readonly struct ZoneProjection
    {
        internal ZoneProjection(MapSlot Slot, FogCutoutZone Zone)
        {
            this.Slot = Slot;
            this.Zone = Zone;
        }

        internal MapSlot Slot { get; }
        internal FogCutoutZone Zone { get; }

    }

    internal static void Capture()
    {
        var Handler = Owner ? Owner : UnityEngine.Object.FindAnyObjectByType<FogCutoutHandler>();
        if (Handler) Capture(Handler);
    }

    static void Capture(FogCutoutHandler Handler)
    {
        if (Captured && Owner == Handler) return;
        if (Captured) Release(true);
        var Zones = Handler.cutoutZones ?? EmptyZones;
        var Current = Handler.currentCutoutZone;
        if (Handler.enabled && Zones.Length == 0)
            throw new InvalidOperationException("PEAK's enabled FogCutoutHandler has no cutout zones.");
        if (Handler.isActiveAndEnabled && !Current)
            throw new InvalidOperationException("PEAK's active FogCutoutHandler has no current cutout zone.");
        var Templates = Zones.Length == 0 ? null : NativeFogCutoutTable.Capture(Handler);
        Owner = Handler;
        NativeZones = Zones;
        NativeTemplates = Templates;
        NativeCurrent = Current;
        NativeIndex = Handler.index;
        NativeEnabled = Handler.enabled;
        NativeShader = ShaderState.Capture();
        Captured = true;
    }

    internal static void Refresh()
    {
        var Handler = Owner ? Owner : UnityEngine.Object.FindAnyObjectByType<FogCutoutHandler>();
        if (!Handler) return;
        if (!Captured || Owner != Handler) Capture(Handler);
        if (!Map.HasPublishedLayout)
        {
            Release(true);
            return;
        }

        var Slots = Map.Slots;
        var Configuration = Map.CurrentRun!.Configuration.Biomes;
        GameObject? Root = null;
        var Zones = new List<ZoneProjection>(Slots.Length);
        try
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                var Slot = Slots[i];
                if (Slot.Released) continue;
                var FogOverride = Configuration.SpatialFor(Slot.Source).FogCutout;
                if (Slot.Source is NadirLayoutSegmentSource && FogOverride is null) continue;
                (CustomLayoutSegmentSource Source, ResolvedSegment Runtime)? Custom = null;
                var CustomSource = Slot.Source as CustomLayoutSegmentSource;
                FogCutoutZone? Template = null;
                if (CustomSource is not null)
                {
                    var Runtime = Slot.CustomResolved
                        ?? throw new InvalidOperationException($"Custom segment '{Slot.Source.Id}' has no resolved clone-local fog cutout.");
                    Custom = (CustomSource, Runtime);
                    Template = Runtime.Runtime.FogCutout;
                }
                if (!Template)
                {
                    if (NativeTemplates is null)
                        throw new InvalidOperationException($"The {Slot.Source.Id} fog cutout template was not found.");
                    Template = NativeTemplates.Select(Slot.Source);
                }

                if (!Root)
                {
                    Root = new GameObject("PLL_FogCutouts");
                    Root.SetActive(false);
                    Root.transform.SetParent(Handler.transform, false);
                    Root.AddComponent<EnvironmentComposition>();
                }
                var Clone = UnityEngine.Object.Instantiate(Template.gameObject, Root.transform, true);
                Clone.name = $"PLL_FogCutout_{i:D3}_{Slot.Source.Id}";
                var Zone = Clone.GetComponent<FogCutoutZone>();
                if (Custom is { } CustomValue)
                {
                    if (CustomValue.Source.Registration.FogMode == PllFogMode.Fogless)
                        NativeFogRuntime.Require().ApplyCutout(Zone);
                    var Anchors = CustomValue.Runtime.AuthoringAnchors;
                    if (Anchors)
                        Zone.transitionPoint = Anchors.FogCutoffAnchor.position.z - Zone.transform.position.z;
                }
                else if (Slot.Source is NadirLayoutSegmentSource)
                {
                    var Position = Template.transform.position;
                    var Center = Slot.Frame.Point(Position);
                    Clone.transform.position = Center;
                    Position.z += Template.transitionPoint;
                    Zone.transitionPoint = Slot.Frame.Point(Position).z - Center.z;
                }
                else Clone.transform.position = Template.transform.position + Slot.Offset;
                if (FogOverride is { } Override)
                {
                    Zone.min = Override.Minimum;
                    Zone.max = Override.Maximum;
                    Zone.amount = Override.Amount;
                }
                var Projection = new ZoneProjection(Slot, Zone);
                Clone.SetActive(true);
                Zones.Add(Projection);
            }
            var Projections = Zones.Count == 0
                ? EmptyProjections
                : Zones.ToArray();
            var Cutouts = Projections.Length == 0
                ? EmptyZones
                : new FogCutoutZone[Projections.Length];
            for (var Index = 0; Index < Cutouts.Length; Index++)
                Cutouts[Index] = Projections[Index].Zone;
            Publish(Handler, Cutouts, Projections, Root);
            Root = null;
        }
        finally
        {
            if (Root) UnityEngine.Object.DestroyImmediate(Root);
        }
    }

    static void Publish(
        FogCutoutHandler Handler, FogCutoutZone[] Zones,
        ZoneProjection[] Projections, GameObject? Root)
    {
        RestoreEnvironmentProjection();
        var Previous = new HandlerState(Handler);
        var PreviousRoot = ProjectionRoot;
        try
        {
            Publishing = true;
            Handler.StopAllCoroutines();
            Handler.enabled = false;
            Handler.cutoutZones = Zones;
            if (Zones.Length == 0)
            {
                Handler.currentCutoutZone = NativeCurrent!;
                Handler.index = 0;
                Shader.SetGlobalFloat(FogCutoutAmount, 0f);
            }
            else
            {
                Root!.SetActive(true);
                PublishOrdinaryProjection(Handler, Projections,
                    CurrentProjectionIndex(Projections, CurrentSlot()));
            }
            ProjectedZones = Projections;
            ProjectionRoot = Root;
        }
        catch
        {
            Apply(Handler, Previous);
            throw;
        }
        finally
        {
            Publishing = false;
        }

        if (PreviousRoot) UnityEngine.Object.DestroyImmediate(PreviousRoot);
    }

    internal static void SetEnvironmentFog(MapSlot Slot, float Coverage)
    {
        RestoreEnvironmentProjection();
        if (Map.CurrentRun is { SceneGone: true })
        {
            EnvironmentContributions.Clear();
            EnvironmentSource = null;
            return;
        }
        if (Coverage == 0f) EnvironmentContributions.Remove(Slot);
        else
        {
            EnvironmentSource ??= NativeFogRuntime.Require();
            EnvironmentContributions[Slot] = Coverage;
        }
    }

    internal static void SelectProjection(MapSlot Slot)
    {
        var Handler = Owner;
        if (!Captured || !Handler) return;
        var Selected = CurrentProjectionIndex(ProjectedZones, Slot);
        if (Selected >= 0) PublishOrdinaryProjection(Handler, ProjectedZones, Selected);
    }
    static void PublishOrdinaryProjection(
        FogCutoutHandler Handler, IReadOnlyList<ZoneProjection> Projections,
        int Selected)
    {
        RestoreEnvironmentProjection();
        if (!Handler.gameObject.activeInHierarchy)
        {
            PublishProjection(Handler, Projections, Selected);
            return;
        }
        RequireProjectionZone(Handler, Projections, Selected);
        Handler.StopAllCoroutines();
        Handler.setFogCutoutZone(Selected);
        Handler.index = Math.Min(Selected + 1, Projections.Count);
    }

    static void PublishProjection(
        FogCutoutHandler Handler, IReadOnlyList<ZoneProjection> Projections,
        int Selected)
    {
        RestoreEnvironmentProjection();
        var Zone = RequireProjectionZone(Handler, Projections, Selected);
        Handler.StopAllCoroutines();
        Handler.currentCutoutZone = Zone;
        Handler.index = Math.Min(Selected + 1, Projections.Count);
        Shader.SetGlobalVector(FogCutoutPosition, Zone.transform.position);
        Shader.SetGlobalFloat(FogCutoutMinimum, Zone.min);
        Shader.SetGlobalFloat(FogCutoutMaximum, Zone.max);
        Shader.SetGlobalFloat(FogCutoutAmount, Zone.amount);
    }

    static FogCutoutZone RequireProjectionZone(
        FogCutoutHandler Handler, IReadOnlyList<ZoneProjection> Projections,
        int Selected)
    {
        var Zones = Handler.cutoutZones;
        if (Zones.Length != Projections.Count)
            throw new InvalidOperationException(
                "The fog-cutout projection lost its sealed zone catalog.");
        return Zones[Selected];
    }

    static int RequiredProjectionIndex(
        IReadOnlyList<ZoneProjection> Projections, MapSlot Slot)
    {
        var Selected = -1;
        for (var Index = 0; Index < Projections.Count; Index++)
        {
            if (!ReferenceEquals(Projections[Index].Slot, Slot)) continue;
            if (Selected >= 0)
                throw new InvalidOperationException(
                    "A map slot resolves to more than one projected fog-cutout zone.");
            Selected = Index;
        }
        return Selected >= 0
            ? Selected
            : throw new InvalidOperationException(
                $"The {Slot.Source.Id} map slot has no projected fog-cutout zone.");
    }

    static int CurrentProjectionIndex(
        IReadOnlyList<ZoneProjection> Projections, MapSlot Slot)
    {
        if (Slot.Source is not NadirLayoutSegmentSource)
            return RequiredProjectionIndex(Projections, Slot);
        var Previous = -1;
        var PreviousSlotIndex = -1;
        var Next = -1;
        var NextSlotIndex = int.MaxValue;
        for (var Index = 0; Index < Projections.Count; Index++)
        {
            var Candidate = Projections[Index].Slot;
            if (ReferenceEquals(Candidate, Slot)) return Index;
            var CandidateIndex = Candidate.Index;
            if (CandidateIndex < Slot.Index && CandidateIndex > PreviousSlotIndex)
            {
                Previous = Index;
                PreviousSlotIndex = CandidateIndex;
            }
            else if (CandidateIndex > Slot.Index && CandidateIndex < NextSlotIndex)
            {
                Next = Index;
                NextSlotIndex = CandidateIndex;
            }
        }
        return Previous >= 0 ? Previous : Next;
    }

    static MapSlot CurrentSlot()
    {
        var Run = Map.CurrentRun;
        var Handler = Run?.Handler;
        if (Run is null || !Handler || !Run.Owns(Handler)
            || !Run.HasPublishedLayout && !Run.Releasing)
            throw new InvalidOperationException(
                "No live map run owns the current fog-cutout projection.");
        var Index = Handler.currentSegment;
        if (Index < 0 || Index >= Run.Slots.Length || Run.Slots[Index].Released)
            throw new InvalidOperationException(
                $"Map segment index {Index} has no active fog-cutout slot.");
        return Run.Slots[Index];
    }

    static void Apply(FogCutoutHandler Handler, HandlerState State)
    {
        ApplyHandler(Handler, State);
        State.Shader.Apply();
    }

    static void ApplyHandler(FogCutoutHandler Handler, HandlerState State)
    {
        Handler.StopAllCoroutines();
        Handler.enabled = false;
        Handler.cutoutZones = State.Zones;
        Handler.currentCutoutZone = State.Current!;
        Handler.index = State.Index;
        Handler.enabled = State.Enabled;
        Handler.StopAllCoroutines();
        Handler.cutoutZones = State.Zones;
        Handler.currentCutoutZone = State.Current!;
        Handler.index = State.Index;
    }

    internal static void Release(bool RestoreNative)
    {
        var Handler = Owner;
        var State = new HandlerState(
            NativeEnabled, NativeZones, NativeCurrent, NativeIndex, NativeShader);
        var HadCapture = Captured;
        var Root = ProjectionRoot;

        Owner = null;
        NativeZones = EmptyZones;
        NativeTemplates = null;
        NativeCurrent = null;
        NativeIndex = 0;
        NativeEnabled = false;
        NativeShader = default;
        ProjectedZones = EmptyProjections;
        RestoreEnvironmentProjection();
        EnvironmentContributions.Clear();
        EnvironmentSource = null;
        ProjectionRoot = null;
        Captured = false;

        List<Exception>? Failures = null;
        void Attempt(Action Operation)
        {
            try { Operation(); }
            catch (Exception Exception) { (Failures ??= new()).Add(Exception); }
        }

        try
        {
            Publishing = true;
            if (Root) Attempt(() => UnityEngine.Object.DestroyImmediate(Root));
            if (RestoreNative && HadCapture && Handler)
                Attempt(() => ApplyHandler(Handler, State));
            if (HadCapture) Attempt(State.Shader.Apply);
        }
        finally
        {
            Publishing = false;
        }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Fog-cutout projection release failed.", Failures);
    }

    [HarmonyPatch(typeof(FogCutoutHandler), "OnEnable")]
    static class EnablePatch
    {
        [HarmonyPrefix]
        static bool Prefix(FogCutoutHandler __instance)
        {
            if (Publishing || !Map.HasPublishedLayout || !Captured || Owner != __instance)
                return true;
            __instance.enabled = false;
            return false;
        }

        [HarmonyPostfix]
        static void Postfix(FogCutoutHandler __instance)
        {
            if (Publishing) return;
            if (!Map.HasPublishedLayout)
            {
                if (Captured && Owner == __instance) Release(true);
                return;
            }
            if (!Captured || Owner != __instance) Capture(__instance);
            Refresh();
        }
    }
}
