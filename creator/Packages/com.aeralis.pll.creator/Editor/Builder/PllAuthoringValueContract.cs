#nullable enable

namespace PLL.Library
{
    internal static class PllAuthoringValueContract
    {
        internal const double ManifestQuantum = 10000d;

        internal static bool IsHook(string? Value) => HookFailure(Value) is null;

        internal static string? HookFailure(string? Value)
        {
            if (Value is null || Value.Length is < 1 or > 128)
                return "The hook must contain 1 through 128 UTF-16 code units.";
            for (var Index = 0; Index < Value.Length; Index++)
            {
                var Character = Value[Index];
                if (char.IsWhiteSpace(Character) || char.IsControl(Character) || Character is '\\' or '>')
                    return "The hook cannot contain whitespace, control characters, '\\', or '>'.";
                if (char.IsHighSurrogate(Character))
                {
                    if (++Index >= Value.Length || !char.IsLowSurrogate(Value[Index]))
                        return "The hook contains malformed UTF-16.";
                }
                else if (char.IsLowSurrogate(Character)) return "The hook contains malformed UTF-16.";
            }
            return null;
        }

        internal static bool DestroyedReference(UnityEngine.Object? Value) =>
            Value is not null && Value.GetInstanceID() != 0 && !Value;

        internal static string? ParticleCallbackFailure(UnityEngine.ParticleSystem Particle)
        {
            if (Particle.main.stopAction != UnityEngine.ParticleSystemStopAction.None)
                return "The particle system uses a Stop callback.";
            var Collision = Particle.collision;
            if (Collision.enabled && Collision.sendCollisionMessages)
                return "The particle system sends collision callbacks.";
            var Trigger = Particle.trigger;
            if (Trigger.enabled && (Trigger.inside == UnityEngine.ParticleSystemOverlapAction.Callback
                || Trigger.outside == UnityEngine.ParticleSystemOverlapAction.Callback
                || Trigger.enter == UnityEngine.ParticleSystemOverlapAction.Callback
                || Trigger.exit == UnityEngine.ParticleSystemOverlapAction.Callback))
                return "The particle system uses trigger callbacks.";
            return null;
        }

        internal static string? TransitionFailure(float Seconds) =>
            CanEncode(Seconds) && Seconds >= 0f ? null : "Cover Delay must be finite nonnegative seconds within the synchronized range.";

        internal static string? ParticleTransitionFailure(UnityEngine.ParticleSystem Particle) =>
            Particle.main.loop && !Particle.main.prewarm
                && Particle.main.stopAction == UnityEngine.ParticleSystemStopAction.None
                && Particle.emission.enabled && Particle.emission.burstCount == 0
                && CanEncode(Particle.emission.rateOverTimeMultiplier) && Particle.emission.rateOverTimeMultiplier >= 0f
                && CanEncode(Particle.emission.rateOverDistanceMultiplier) && Particle.emission.rateOverDistanceMultiplier >= 0f
                ? null : "Cover Delay requires continuous looping particles without prewarm, bursts, or Stop callbacks.";

        internal static bool IsDefined(PllBiomeBehavior Value) =>
            (byte)Value is >= (byte)PllBiomeBehavior.Authored
                and <= (byte)PllBiomeBehavior.Citadel;

        internal static bool IsDefined(PllEffectZoneEnvironmentFog Value) =>
            Value is PllEffectZoneEnvironmentFog.Inherit
                or PllEffectZoneEnvironmentFog.EnvironmentalFogless;

        internal static bool IsDefined(PllLuggageContentsMode Value) =>
            Value is PllLuggageContentsMode.NativeProgression
                or PllLuggageContentsMode.NativeLuggage
                or PllLuggageContentsMode.CustomItems;

        internal static bool IsDefined(PllLuggageRepeatMode Value) =>
            Value is PllLuggageRepeatMode.UniqueUntilExhausted
                or PllLuggageRepeatMode.AllowRepeats;

        internal static bool IsLocalId(string? Value)
        {
            if (Value is null || Value.Length is < 1 or > 64) return false;
            for (var Index = 0; Index < Value.Length; Index++)
            {
                var Character = Value[Index];
                if (Character is '/' or '\\' or '>'
                    || char.IsControl(Character) || char.IsWhiteSpace(Character)) return false;
                if (char.IsHighSurrogate(Character))
                {
                    if (++Index >= Value.Length || !char.IsLowSurrogate(Value[Index])) return false;
                }
                else if (char.IsLowSurrogate(Character)) return false;
            }
            return true;
        }

        internal static bool IsDisplayText(string? Value)
        {
            if (Value is null || Value.Length is < 1 or > 128) return false;
            var Content = false;
            for (var Index = 0; Index < Value.Length; Index++)
            {
                var Character = Value[Index];
                if (char.IsControl(Character)) return false;
                if (!char.IsWhiteSpace(Character)) Content = true;
                if (char.IsHighSurrogate(Character))
                {
                    if (++Index >= Value.Length || !char.IsLowSurrogate(Value[Index])) return false;
                }
                else if (char.IsLowSurrogate(Character)) return false;
            }
            return Content;
        }

        internal static bool CanEncode(float Value)
        {
            var Scaled = Value * ManifestQuantum;
            return !double.IsNaN(Scaled) && !double.IsInfinity(Scaled)
                && Scaled > long.MinValue && Scaled < long.MaxValue;
        }

        internal static bool CanEncodeExactly(float Value) => CanEncode(Value)
            && ((float)(System.Math.Round(
                Value * ManifestQuantum, System.MidpointRounding.AwayFromZero)
                / ManifestQuantum)).Equals(Value);

        internal static (string PropertyPath, string Message)? ZoneVolumeFailure(PllZoneVolume Volume) =>
            ZoneVolumeFailure(Volume.Shape, Volume.Center, Volume.Size, Volume.Radius);

        internal static (string PropertyPath, string Message)? ZoneVolumeFailure(
            PllZoneShape Shape, UnityEngine.Vector3 Center, UnityEngine.Vector3 Size, float Radius)
        {
            if (!CanEncode(Center.x)) return InvalidScalar("center.x", Center.x);
            if (!CanEncode(Center.y)) return InvalidScalar("center.y", Center.y);
            if (!CanEncode(Center.z)) return InvalidScalar("center.z", Center.z);
            if (!CanEncode(Size.x)) return InvalidScalar("size.x", Size.x);
            if (!CanEncode(Size.y)) return InvalidScalar("size.y", Size.y);
            if (!CanEncode(Size.z)) return InvalidScalar("size.z", Size.z);
            if (!CanEncode(Radius)) return InvalidScalar("radius", Radius);
            switch (Shape)
            {
                case PllZoneShape.Box:
                    if (Size.x <= 0f) return NonpositiveScalar("size.x", Size.x);
                    if (Size.y <= 0f) return NonpositiveScalar("size.y", Size.y);
                    if (Size.z <= 0f) return NonpositiveScalar("size.z", Size.z);
                    return null;
                case PllZoneShape.Sphere:
                    return Radius > 0f ? null : NonpositiveScalar("radius", Radius);
                default:
                    return ("volume.shape", "Volume Shape is unsupported: " + (byte)Shape + ".");
            }
        }

        static (string PropertyPath, string Message) InvalidScalar(string Property, float Value) =>
            ("volume." + Property, System.FormattableString.Invariant(
                $"Volume {Property} must be finite and within the synchronized range; received {Value:R}."));

        static (string PropertyPath, string Message) NonpositiveScalar(string Property, float Value) =>
            ("volume." + Property, System.FormattableString.Invariant(
                $"Volume {Property} must be greater than zero; received {Value:R}."));

        internal static bool IsVariationWeight(float Value) =>
            Value is >= 0f and <= 100f && CanEncodeExactly(Value);

        internal static bool IsVariationEntryId(string? Value)
        {
            if (Value is null || Value.Length != 32) return false;
            foreach (var Character in Value)
                if (!(Character is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
            return true;
        }

        internal static bool ValidLuggageContents(
            PllLuggageContentsMode Mode,
            int CustomItemCount,
            string NativeContentsId) => Mode switch
        {
            PllLuggageContentsMode.NativeProgression =>
                CustomItemCount == 0 && NativeContentsId.Length == 0,
            PllLuggageContentsMode.NativeLuggage =>
                CustomItemCount == 0 && NativeContentsId.Length != 0,
            PllLuggageContentsMode.CustomItems =>
                CustomItemCount != 0 && NativeContentsId.Length == 0,
            _ => false
        };
    }
}
