using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal static class PllBiomeStingerValidation
    {
        internal static void ValidateDraft(GameObject Root, GameObject Value)
        {
            if (Value.transform.childCount != 0)
                throw new InvalidOperationException("The selected biome stinger must be a leaf GameObject.");
            if (Value.GetComponents<Component>().Length != 2 || Value.transform.GetType() != typeof(Transform)
                || !Value.TryGetComponent<AudioSource>(out var Source))
                throw new InvalidOperationException(
                    "The selected biome stinger must contain exactly Transform and one AudioSource.");
            if (!Source.enabled)
                throw new InvalidOperationException("The selected biome stinger AudioSource must be enabled.");
            var PlacementFailure = PlacementError(Root, Value);
            if (PlacementFailure != null) throw new InvalidOperationException(PlacementFailure);
        }

        internal static void Validate(
            PllBiomeStingerAuthoring Value, string PlacementError, Action<string, Object> Reject)
        {
            var HasAudio = Value.TryGetComponent<AudioSource>(out var Source);
            if (PllAuthoringValueContract.TransitionFailure(Value.Transition.CoverDelaySeconds) is { } Failure)
                Reject(Failure, Value);
            if (Value.Transition.CoverDelaySeconds > 0f && HasAudio && !Source.loop)
                Reject("Cover Delay requires looping audio.", Source);
            if (!Value.enabled) Reject("Biome stinger authoring must be enabled.", Value);
            switch (Value.Mode)
            {
                case PllBiomeStingerMode.OnEnter:
                    if (Value.EmitHook.Length != 0) Reject("Emit Hook must be empty in On Enter mode.", Value);
                    break;
                case PllBiomeStingerMode.EmitHook:
                case PllBiomeStingerMode.OnEnterAndEmitHook:
                    if (!PllAuthoringValueContract.IsHook(Value.EmitHook))
                        Reject("Hook must contain 1 to 128 UTF-16 code units and no whitespace, control character, '\\', '>', or malformed surrogate pair.", Value);
                    break;
                default:
                    Reject("Mode must be On Enter, Emit Hook, or On Enter and Emit Hook.", Value);
                    break;
            }
            if (PlacementError != null) Reject(PlacementError, Value);
            if (Value.transform.childCount != 0)
                Reject("A biome stinger must own no child hierarchy.", Value);
            if (Value.GetComponents<Component>().Length != 3 || Value.transform.GetType() != typeof(Transform) || !HasAudio)
                Reject("A biome stinger must contain exactly Transform, one AudioSource, and one PllBiomeStingerAuthoring.", Value);
            if (HasAudio) ValidateAudio(Source, Reject);
        }

        static void ValidateAudio(AudioSource Source, Action<string, Object> Reject)
        {
            var Clip = Source.clip;
            if (!Clip || Source.resource != Clip)
                Reject("Audio Source must use one direct AudioClip resource.", Source);
            else if (Clip.samples <= 0 || Clip.channels <= 0 || Clip.frequency <= 0
                || !EditorUtility.IsPersistent(Clip) || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
                Reject("Audio Source must reference one nonempty bundle-owned AudioClip.", Source);
            if (!Source.enabled) Reject("Audio Source must be enabled.", Source);
            if (Source.playOnAwake || Source.isPlaying || Source.timeSamples != 0)
                Reject("Audio Source must be stopped at sample zero and have Play On Awake disabled.", Source);
            if (!PllSourceIdentityValidator.ManifestNumber(Source.volume)
                || !PllSourceIdentityValidator.ManifestNumber(Source.pitch)
                || !PllSourceIdentityValidator.ManifestNumber(Source.panStereo)
                || !PllSourceIdentityValidator.ManifestNumber(Source.spatialBlend)
                || !PllSourceIdentityValidator.ManifestNumber(Source.reverbZoneMix)
                || !PllSourceIdentityValidator.ManifestNumber(Source.dopplerLevel)
                || !PllSourceIdentityValidator.ManifestNumber(Source.spread)
                || !PllSourceIdentityValidator.ManifestNumber(Source.minDistance)
                || !PllSourceIdentityValidator.ManifestNumber(Source.maxDistance))
                Reject("Audio Source contains a nonfinite or out-of-range synchronized numeric value.", Source);
            if (Source.pitch <= 0f || !Source.spatialBlend.Equals(0f)
                || Source.spatialize || Source.spatializePostEffects)
                Reject("Audio Source must have positive pitch and be fully 2D with spatialization disabled.", Source);
            if (!ReferenceEquals(Source.outputAudioMixerGroup, null))
                Reject("Audio Source cannot author an output AudioMixerGroup.", Source);
            var Curve = Source.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
            if (Curve != null && !AudioCurveContract.IsConstantZero(Curve.keys))
                Reject("Audio Source Spatial Blend curve must be constant zero.", Source);
        }

        internal static string PlacementError(GameObject Root, GameObject Value)
        {
            var Error = BasicPlacementError(Root, Value);
            if (Error != null) return Error;
            var Runtimes = Root.GetComponents<CustomSegmentRuntime>();
            var Anchors = Root.GetComponents<PllBiomePrefabAnchors>();
            if (Runtimes.Length != 1 || Anchors.Length != 1)
                return "Biome stingers require the stable custom biome root created by PLL.";
            var Runtime = Runtimes[0];
            var Anchor = Anchors[0];
            if (!Anchor.FloorRoot || !Anchor.CampfireRoot || !Anchor.StartAnchor
                || !Anchor.PlayerFormationRoot || !Anchor.PlaneAnchor || !Anchor.FogBeginAnchor
                || !Anchor.FogCutoffAnchor || !Runtime.FogOrigin || !Runtime.FogCutout
                || Runtime.ProgressPoint == null || !Runtime.ProgressPoint.transform)
                return "Biome stingers require a complete custom biome topology.";
            var Target = Value.transform;
            if (TransformOwnership.Contains(Anchor.FloorRoot.transform, Target) || TransformOwnership.Contains(Anchor.CampfireRoot.transform, Target)
                || Anchor.StatueRoot && TransformOwnership.Contains(Anchor.StatueRoot.transform, Target)
                || TransformOwnership.Contains(Anchor.StartAnchor, Target) || TransformOwnership.Contains(Anchor.PlayerFormationRoot.transform, Target)
                || TransformOwnership.Contains(Anchor.PlaneAnchor, Target) || TransformOwnership.Contains(Anchor.FogBeginAnchor, Target)
                || TransformOwnership.Contains(Anchor.FogCutoffAnchor, Target) || TransformOwnership.Contains(Runtime.FogOrigin.transform, Target)
                || TransformOwnership.Contains(Runtime.FogCutout.transform, Target) || TransformOwnership.Contains(Runtime.ProgressPoint.transform, Target))
                return "Biome stingers must remain outside feature roots and runtime-marker hierarchies.";
            if (TransformOwnership.HasPropSpawnerInPath(Root.transform, Target))
                return "Biome stingers cannot belong to a PropSpawner hierarchy.";
            for (var Current = Target; Current != Root.transform; Current = Current.parent)
            {
                if (Current.GetComponent<SegmentPort>() || Current.GetComponent<PllCampfireAuthoring>()
                    || Current.GetComponent<PllStatueAuthoring>() || Current.GetComponent<PllLuggageAuthoring>()
                    || Current.GetComponent<PllEffectAuthoring>()
                    || Current.GetComponent<PllEffectZoneAuthoring>() || Current.GetComponent<PllVisualAuthoring>()
                    || Current.GetComponent<PllCustomVisualAuthoring>()
                    || Current.GetComponent<PllSoundEmitterAuthoring>()
                    || (Current != Target && Current.GetComponent<PllBiomeStingerAuthoring>())
                    || Current.GetComponent<PLLNativeResolver>() || Current.GetComponent<PLLNativeVisualResolver>()
                    || Current.GetComponent<PllPropZoneAuthoring>() || Current.GetComponent<PLLNoPropSpawn>()
                    || Current.GetComponent<PllChildPropAuthoring>() || Current.GetComponent<PllItemSpawnerAuthoring>()
                    || Current.GetComponent<PLLForceSpawner>() || Current.GetComponent<PLLFinalGeneration>()
                    || Current.GetComponent<PLLHookedProp>() || Current.GetComponent<LevelGenStep>())
                    return "Biome stingers must remain outside other managed authoring roots.";
            }
            foreach (var Variation in Root.GetComponentsInChildren<PllVariationAuthoring>(true))
                foreach (var Entry in Variation.Entries)
                    if (Entry != null && Entry.Root && TransformOwnership.Contains(Entry.Root.transform, Target))
                        return "Biome stingers cannot belong to a variation entry.";
            return null;
        }

        internal static string BasicPlacementError(GameObject Root, GameObject Value)
        {
            if (!Root || !Value || Value == Root || !TransformOwnership.Contains(Root.transform, Value.transform))
                return "Biome stingers must be strict descendants of the custom biome root.";
            return TransformOwnership.DiscoverableWhenActivated(Root.transform, Value.transform)
                ? null : "A biome stinger and every ancestor below the biome root must be active.";
        }

    }

}
