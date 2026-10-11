using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapEnvironmentProjection
{
    internal GameObject[] WaterObjects { get; set; } = Array.Empty<GameObject>();
    internal Vector3[] AuthoredWaterPositions { get; set; } = Array.Empty<Vector3>();
    internal void Release()
    {
        WaterObjects = Array.Empty<GameObject>();
        AuthoredWaterPositions = Array.Empty<Vector3>();
    }
    internal sealed class EnvironmentState
    {
        readonly Dictionary<WaterZone, Bounds> water = new();
        readonly Dictionary<WindChillZone, Bounds> wind = new();
        readonly Dictionary<PlayerTrackParticles, Bounds> tracks = new();
        readonly Dictionary<PlayerMoveZone, Bounds> movement = new();
        readonly Dictionary<Peak.ForceHeroTextTrigger, (Bounds Placed, Bounds? Original)> heroText = new();
        readonly Dictionary<WindHeightEffect, (float From, float To)> heights = new();

        internal void CaptureWater(IEnumerable<GameObject> Roots)
        {
            foreach (var Root in Roots)
            {
                if (!Root) continue;
                CaptureWater(Root);
            }
        }

        void CaptureWater(GameObject Root)
        {
            foreach (var Zone in Root.GetComponentsInChildren<WaterZone>(true))
                water.TryAdd(Zone, Zone.zoneBounds);
        }

        internal void Rebase(GameObject Root, PlacementFrame Frame, bool Capture, Transform? Excluded = null)
        {
            if (Excluded)
            {
                var RootTransform = Root.transform;
                if (Excluded == RootTransform || !Excluded.IsChildOf(RootTransform) || !Excluded.parent)
                    throw new InvalidOperationException("The selected full Peak cannot be excluded from its owner environment pass.");
            }
            RebaseWater(Root, Frame, Capture, Excluded);
            foreach (var Zone in Root.GetComponentsInChildren<WindChillZone>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Zone.transform)) continue;
                if (Capture) wind.TryAdd(Zone, Zone.windZoneBounds);
                var Bounds = Zone.windZoneBounds;
                if (Frame.Rotated) Bounds = Frame.TransformBounds(Bounds);
                else Bounds.center = Zone.transform.position;
                Zone.windZoneBounds = Bounds;
            }
            foreach (var Particles in Root.GetComponentsInChildren<PlayerTrackParticles>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Particles.transform)) continue;
                if (Capture) tracks.TryAdd(Particles, Particles.bounds);
                var Bounds = Particles.bounds;
                if (Frame.Rotated) Bounds = Frame.TransformBounds(Bounds);
                else Bounds.center = Particles.transform.position;
                Particles.bounds = Bounds;
            }
            foreach (var Zone in Root.GetComponentsInChildren<PlayerMoveZone>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Zone.transform)) continue;
                if (Capture) movement.TryAdd(Zone, Zone.zoneBounds);
                var Bounds = Zone.zoneBounds;
                if (Frame.Rotated) Bounds = Frame.TransformBounds(Bounds);
                else Bounds.center = Zone.transform.position;
                Zone.zoneBounds = Bounds;
            }
            foreach (var Trigger in Root.GetComponentsInChildren<Peak.ForceHeroTextTrigger>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Trigger.transform)) continue;
                if (!heroText.TryGetValue(Trigger, out var State))
                {
                    Bounds? Original = null;
                    if (Capture) Original = Trigger.bounds;
                    Bounds Placed;
                    if (Frame.Rotated)
                    {
                        var Authored = Original ?? new Bounds(
                            Frame.InversePoint(Trigger.transform.position) + Trigger.centerOffset,
                            Trigger.extents);
                        Placed = Frame.TransformBounds(Authored);
                    }
                    else Placed = new Bounds(Trigger.transform.position + Trigger.centerOffset, Trigger.extents);
                    State = (Placed, Original);
                    heroText.Add(Trigger, State);
                }
                Trigger.bounds = State.Placed;
            }
            foreach (var Effect in Root.GetComponentsInChildren<WindHeightEffect>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Effect.transform)) continue;
                if (Capture) heights.TryAdd(Effect, (Effect.fromHeight, Effect.toHeight));
                Effect.fromHeight += Frame.Offset.y;
                Effect.toHeight += Frame.Offset.y;
            }
        }

        internal void ApplyHeroTextBounds(Peak.ForceHeroTextTrigger Trigger)
        {
            if (heroText.TryGetValue(Trigger, out var State)) Trigger.bounds = State.Placed;
        }

        void RebaseWater(GameObject Root, PlacementFrame Frame, bool Capture, Transform? Excluded)
        {
            foreach (var Zone in Root.GetComponentsInChildren<WaterZone>(true))
            {
                if (Excluded && TransformOwnership.Contains(Excluded!, Zone.transform)) continue;
                if (Capture) water.TryAdd(Zone, Zone.zoneBounds);
                Zone.zoneBounds = Frame.TransformBounds(Zone.zoneBounds);
            }
        }

        internal void Restore()
        {
            foreach (var Value in heroText)
            {
                if (!Value.Key || Value.Value.Original is not { } Original) continue;
                Value.Key.bounds = Original;
            }
            heroText.Clear();
            foreach (var Value in water)
                if (Value.Key) Value.Key.zoneBounds = Value.Value;
            foreach (var Value in wind)
                if (Value.Key) Value.Key.windZoneBounds = Value.Value;
            foreach (var Value in tracks)
                if (Value.Key) Value.Key.bounds = Value.Value;
            foreach (var Value in movement)
                if (Value.Key) Value.Key.zoneBounds = Value.Value;
            foreach (var Value in heights)
            {
                if (!Value.Key) continue;
                Value.Key.fromHeight = Value.Value.From;
                Value.Key.toHeight = Value.Value.To;
            }
        }
    }

    internal static void ActivateWater(int index)
    {
        if (!Map.Active || index < 0 || index >= Map.Slots.Length || Map.Slots[index].Released
            || Map.Slots[index].Source.Id != "Beach" || Map.CurrentRun!.Environment.WaterObjects.Length == 0) return;
        var Configuration = Map.CurrentRun!.Configuration.Environment;
        if (Configuration.NoWater || !Configuration.WaterFollowsBeach) return;
        MoveWater(Map.CurrentRun!.Environment.WaterObjects, Map.CurrentRun!.Environment.AuthoredWaterPositions, Map.Slots[index].Offset);
        SetActive(Map.CurrentRun!.Environment.WaterObjects, true);
    }
    internal static void MoveWater(
        IReadOnlyList<GameObject> Values,
        IReadOnlyList<Vector3> AuthoredPositions,
        Vector3 Offset)
    {
        for (var i = 0; i < Values.Count; i++)
        {
            var Value = Values[i];
            if (!Value) continue;
            var Position = AuthoredPositions[i] + Offset;
            var Delta = Position - Value.transform.position;
            foreach (var Zone in Value.GetComponentsInChildren<WaterZone>(true))
            {
                var Bounds = Zone.zoneBounds;
                Bounds.center += Delta;
                Zone.zoneBounds = Bounds;
            }
            Value.transform.position = Position;
        }
    }
    internal static void SetActive(IEnumerable<GameObject> Values, bool Active)
    {
        foreach (var Value in Values)
            if (Value) Value.SetActive(Active);
    }

    internal static GameObject[] FindWaterObjects(MapHandler Handler)
    {
        GameObject? water = null;
        GameObject? mesh = null;
        GameObject? zone = null;
        foreach (var Root in Handler.gameObject.scene.GetRootGameObjects())
        {
            if (Root.name != "Misc") continue;
            for (var i = 0; i < Root.transform.childCount; i++)
            {
                var Candidate = Root.transform.GetChild(i);
                if (Candidate.name == "Water")
                {
                    if (water) throw new InvalidOperationException("The PEAK scene contains more than one Misc/Water floor.");
                    water = Candidate.gameObject;
                }
                else if (Candidate.name == "WaterMesh")
                {
                    if (mesh) throw new InvalidOperationException("The PEAK scene contains more than one Misc/WaterMesh object.");
                    mesh = Candidate.gameObject;
                }
                else if (Candidate.name == "WaterZone")
                {
                    if (zone) throw new InvalidOperationException("The PEAK scene contains more than one Misc/WaterZone object.");
                    zone = Candidate.gameObject;
                }
            }
        }
        if (!water || water.transform.childCount != 1)
            throw new InvalidOperationException("The PEAK Misc/Water floor was not found.");
        var collision = water.transform.GetChild(0);
        if (collision.name != "Collision" || !collision.GetComponent<Collider>())
            throw new InvalidOperationException("The PEAK Misc/Water/Collision floor was not found.");
        if (!mesh || !zone)
            throw new InvalidOperationException("The PEAK water mesh or water zone was not found.");
        return new[] { water, mesh, zone };
    }
    internal static void ValidateWater(IReadOnlyList<MapSlot> Slots)
    {
        for (var i = 1; i < Slots.Count; i++)
            if (Slots[i].Source.Id == "Beach" && !Slots[i - 1].Segment.segmentCampfire
                && Slots[i - 1].ProgressionBoundary == NativeProgressionBoundaryKind.None)
                throw new InvalidOperationException($"The Beach at layout position {i + 1} has no preceding campfire to activate its water floor.");
    }
    internal static void SyncVoidWater()
    {
        if (!Map.HasPublishedLayout) return;
        var Configuration = Map.CurrentRun!.Configuration.Environment;
        var Current = Map.Index;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            var Slot = Map.Slots[i];
            if (!Slot.VoidWaterRoot) continue;
            Slot.VoidWaterRoot.gameObject.SetActive(
                !Configuration.NoVoidWater && !Slot.Released
                && (!Configuration.VoidWaterFollowsNadir || i == Current));
        }
    }
    internal static void SyncWater()
    {
        if (!Map.HasPublishedLayout) return;
        SyncVoidWater();
        var Configuration = Map.CurrentRun!.Configuration.Environment;
        if (Configuration.NoWater)
        {
            SetActive(Map.CurrentRun!.Environment.WaterObjects, false);
            return;
        }
        if (!Configuration.WaterFollowsBeach)
        {
            SetActive(Map.CurrentRun!.Environment.WaterObjects, true);
            return;
        }
        var Current = Math.Max(0, Math.Min(Map.Index, Map.Slots.Length - 1));
        for (var i = Current; i >= 0; i--)
        {
            if (Map.Slots[i].Released || Map.Slots[i].Source.Id != "Beach") continue;
            ActivateWater(i);
            return;
        }
        SetActive(Map.CurrentRun!.Environment.WaterObjects, false);
    }
    internal static void RestoreWater(PreparedMap Prepared)
    {
        MoveWater(Prepared.WaterObjects, Prepared.WaterPositions, Vector3.zero);
        for (var i = 0; i < Prepared.WaterObjects.Length; i++)
        {
            if (!Prepared.WaterObjects[i]) continue;
            Prepared.WaterObjects[i].SetActive(Prepared.WaterStates[i]);
        }
    }

    [HarmonyPatch(typeof(Peak.ForceHeroTextTrigger), "Awake")]
    static class ForceHeroTextTriggerAwakePatch
    {
        [HarmonyPostfix]
        static void Postfix(Peak.ForceHeroTextTrigger __instance)
        {
            var Run = Map.CurrentRun;
            var Prepared = Run?.RollbackOwner ?? Run?.PublicationOwner;
            Prepared?.Environment.ApplyHeroTextBounds(__instance);
        }
    }
}
