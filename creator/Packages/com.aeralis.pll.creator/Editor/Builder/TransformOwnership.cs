#nullable enable
using System;
using UnityEngine;

namespace PLL.Library
{
    internal static class TransformOwnership
    {
        internal static bool Contains(Transform Root, Transform? Value) =>
            Value is not null && Value && (Value == Root || Value.IsChildOf(Root));

        internal static bool Overlaps(Transform Left, Transform Right) =>
            Contains(Left, Right) || Contains(Right, Left);

        internal static bool SamePose(Transform Left, Transform Right) =>
            SamePose(new Pose(Left.position, Left.rotation), new Pose(Right.position, Right.rotation));

        internal static bool SamePose(Pose Left, Pose Right) =>
            (Left.position - Right.position).sqrMagnitude <= 0.000001f
            && Math.Abs(Quaternion.Dot(Left.rotation.normalized, Right.rotation.normalized)) >= 0.999999f;

        internal static bool HasPropSpawnerInPath(Transform Root, Transform Value)
        {
            if (!Contains(Root, Value)) return false;
            for (var Current = Value; Current; Current = Current.parent)
            {
                if (Current.GetComponent<PropSpawner>()) return true;
                if (Current == Root) return false;
            }
            return false;
        }

        internal static bool HasConfigurationBranch(Transform Root, Transform Value)
        {
            if (!Contains(Root, Value)) return false;
            for (var Current = Value;; Current = Current.parent)
            {
                if (Current.GetComponent<VariantObject>()
                    || Current.GetComponent<PllVariationAuthoring>()) return true;
                if (Current == Root) return false;
            }
        }

        internal static bool HasManagedHookAncestor(Transform Root, Transform Value)
        {
            for (var Current = Value; Current; Current = Current.parent)
            {
                if (Current.GetComponent<SegmentPort>()
                    || Current != Root && (Current.GetComponent<CustomSegmentRuntime>()
                        || Current.GetComponent<PllBiomePrefabAnchors>() || Current.GetComponent<PllBiomeRoot>())
                    || Current.GetComponent<PllEffectZoneAuthoring>() || Current.GetComponent<PLLNativeResolver>()
                    || Current.GetComponent<PllItemSpawnerAuthoring>() || Current.GetComponent<PllStatueAuthoring>()
                    || Current.GetComponent<PllCampfireAuthoring>() || Current.GetComponent<PllLuggageAuthoring>()
                    || Current.GetComponent<PllBiomeStingerAuthoring>()) return true;
                if (Current == Root) return false;
            }
            return false;
        }

        internal static bool ParentDiscoverable(Transform Owner, Transform Value)
        {
            if (Value == Owner) return false;
            var Parent = Value.parent;
            return Parent && DiscoverableWhenActivated(Owner, Parent);
        }

        internal static bool DiscoverableWhenActivated(Transform Root, Transform Value)
        {
            if (!Contains(Root, Value)) return false;
            for (var Current = Value; Current != Root; Current = Current.parent)
                if (!Current || !Current.gameObject.activeSelf) return false;
            return true;
        }
    }
}
