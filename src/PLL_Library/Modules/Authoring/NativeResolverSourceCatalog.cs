using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal static class NativeResolverSourceCatalog
{
    static Batch? Sources;

    internal static Seal BeginSeal(NativeSegmentCatalog Catalog) => new(Catalog);

    internal static void CompleteSeal(Seal Seal, int ExpectedMask)
    {
        if (Seal is null) throw new ArgumentNullException(nameof(Seal));
        Sources = Seal.Complete(ExpectedMask);
    }

    internal static void Clear() => Sources = null;

    internal static void WriteSourceFingerprint(BinaryWriter Writer)
    {
        if (Writer is null) throw new ArgumentNullException(nameof(Writer));
        var SealedSources = RequireSources();
        Writer.Write(SealedSources.RequiredMask);
        if (SealedSources.RequiredMask != 0) SealedSources.WriteManifest(Writer);
    }

    internal static void CopyWeather(PLLNativeResolverKind Kind, WindChillZone Target) =>
        RequireSources().Weather(Kind).Copy(Target);

    internal static float SporeCloudRadius => RequireSources().SporeCloud().Radius;

    internal static NativeFlowProvider Flow(PLLNativeResolverKind Kind) => RequireSources().Flow(Kind);

    internal static void CopySporeCloud(
        WindAffectedStatusEmitter Emitter,
        DisableBasedOnRunSettings Setting) => RequireSources().SporeCloud().Copy(Emitter, Setting);

    static Batch RequireSources() => Sources
        ?? throw new InvalidOperationException("The live PEAK native-resolver source catalog was not sealed.");

    internal sealed class Seal
    {
        internal const int SupportedMask = 1 << (int)PLLNativeResolverKind.Rain
            | 1 << (int)PLLNativeResolverKind.Wind
            | 1 << (int)PLLNativeResolverKind.Snow
            | 1 << (int)PLLNativeResolverKind.SporeCloud
            | 1 << (int)PLLNativeResolverKind.Water | 1 << (int)PLLNativeResolverKind.Lava;
        readonly NativeSegmentCatalog Catalog;
        readonly Dictionary<PLLNativeResolverKind, WeatherFingerprint> InventoryWeather = new();
        readonly Dictionary<PLLNativeResolverKind, WeatherFingerprint> CommittedWeather = new();
        readonly Dictionary<PLLNativeResolverKind, NativeFlowProvider> InventoryFlows = new();
        readonly Dictionary<PLLNativeResolverKind, NativeFlowProvider> CommittedFlows = new();
        SporeFingerprint? InventorySpore;
        SporeFingerprint? CommittedSpore;
        int RequiredMask;
        bool Completed;

        internal Seal(NativeSegmentCatalog Catalog)
        {
            this.Catalog = Catalog ?? throw new ArgumentNullException(nameof(Catalog));
        }

        internal Batch Prepare(int RequestedMask)
        {
            if (Completed) throw new InvalidOperationException("The native-resolver source seal is complete.");
            if ((RequestedMask & ~SupportedMask) != 0)
                throw new InvalidOperationException($"Native-resolver source mask '{RequestedMask}' is not supported.");
            if (RequestedMask == 0) return Batch.Empty;

            var PreparedWeather = new Dictionary<PLLNativeResolverKind, WeatherFingerprint>();
            var PreparedFlows = new Dictionary<PLLNativeResolverKind, NativeFlowProvider>();
            var CapturedWeather = new Dictionary<PLLNativeResolverKind, WeatherFingerprint>();
            AddWeather(PLLNativeResolverKind.Rain, "Tropics");
            AddWeather(PLLNativeResolverKind.Wind, "Roots");
            AddWeather(PLLNativeResolverKind.Snow, "Alpine");
            SporeFingerprint? CapturedSpore = null;
            var PreparedSpore = IsRequired(PLLNativeResolverKind.SporeCloud)
                ? InventorySpore ?? (CapturedSpore = FindSporeCloud())
                : null;
            foreach (var Pair in CapturedWeather) InventoryWeather.Add(Pair.Key, Pair.Value);
            InventorySpore ??= CapturedSpore;
            AddFlow(PLLNativeResolverKind.Water); AddFlow(PLLNativeResolverKind.Lava);
            return new Batch(PreparedWeather, PreparedSpore, RequestedMask, PreparedFlows);

            void AddFlow(PLLNativeResolverKind Kind)
            {
                if (!IsRequired(Kind)) return;
                if (!InventoryFlows.TryGetValue(Kind, out var Provider))
                {
                    Provider = NativeFlowProvider.Capture(Catalog, Kind);
                    InventoryFlows.Add(Kind, Provider);
                }
                PreparedFlows.Add(Kind, Provider);
            }

            void AddWeather(PLLNativeResolverKind Kind, string SourceName)
            {
                if (!IsRequired(Kind)) return;
                if (!InventoryWeather.TryGetValue(Kind, out var Fingerprint))
                {
                    Fingerprint = FindWeather(Catalog.RequireOrdinary(SourceName), Kind);
                    CapturedWeather.Add(Kind, Fingerprint);
                }
                PreparedWeather.Add(Kind, Fingerprint);
            }

            bool IsRequired(PLLNativeResolverKind Kind) =>
                (RequestedMask & (1 << (int)Kind)) != 0;
        }

        internal void Commit(Batch PreparedBatch)
        {
            if (PreparedBatch is null) throw new ArgumentNullException(nameof(PreparedBatch));
            if (Completed) throw new InvalidOperationException("The native-resolver source seal is complete.");
            PreparedBatch.ValidateCompatible(CommittedWeather, CommittedSpore);
            PreparedBatch.CopyMissingTo(CommittedWeather, ref CommittedSpore);
            PreparedBatch.CommitFlows(CommittedFlows);
            RequiredMask |= PreparedBatch.RequiredMask;
        }

        internal Batch Complete(int ExpectedMask)
        {
            if (Completed) throw new InvalidOperationException("The native-resolver source seal is already complete.");
            if (RequiredMask != ExpectedMask)
                throw new InvalidOperationException(
                    $"The retained native-resolver source mask is {RequiredMask}, expected {ExpectedMask}.");
            Completed = true;
            return new Batch(
                new Dictionary<PLLNativeResolverKind, WeatherFingerprint>(CommittedWeather),
                CommittedSpore, RequiredMask,
                new Dictionary<PLLNativeResolverKind, NativeFlowProvider>(CommittedFlows));
        }

        static WeatherFingerprint FindWeather(
            NativeOrdinarySegmentSource Source,
            PLLNativeResolverKind Kind)
        {
            var Setting = WeatherFingerprint.RequiredSetting(Kind);
            WindChillZone? Result = null;
            foreach (var Zone in Source.Container.GetComponentsInChildren<WindChillZone>(true))
            {
                if (!Zone || Zone.disableIfSettingDisabled != Setting) continue;
                if (Result)
                    throw new InvalidOperationException(
                        $"PEAK's native {Source.Id} source has multiple {Kind} weather matches.");
                Result = Zone;
            }
            if (!Result)
                throw new InvalidOperationException(
                    $"PEAK's native {Source.Id} source has no {Kind} weather match.");
            return WeatherFingerprint.Capture(Result, Kind);
        }

        SporeFingerprint FindSporeCloud()
        {
            var Source = Catalog.RequireOrdinary("Roots").SegmentParent;
            var Pending = new Queue<GameObject>();
            var SeenRoots = new HashSet<GameObject> { Source };
            var SeenEmitters = new HashSet<WindAffectedStatusEmitter>();
            SporeFingerprint? Selected = null;
            Pending.Enqueue(Source);
            while (Pending.Count != 0)
            {
                var Root = Pending.Dequeue();
                foreach (var Emitter in Root.GetComponentsInChildren<WindAffectedStatusEmitter>(true))
                {
                    if (!Emitter || !SeenEmitters.Add(Emitter)
                        || Emitter.statusType != CharacterAfflictions.STATUSTYPE.Spores) continue;
                    var Emitters = Emitter.GetComponents<WindAffectedStatusEmitter>();
                    var Settings = Emitter.GetComponents<DisableBasedOnRunSettings>();
                    if (Emitters.Length != 1 || Settings.Length != 1
                        || Settings[0].disableIfSettingDisabled != RunSettings.SETTINGTYPE.Hazard_SporeClouds) continue;
                    var Fingerprint = SporeFingerprint.Capture(Emitter, Settings[0]);
                    if (Selected is not null && !Selected.Matches(Fingerprint))
                        throw new InvalidOperationException(
                            "PEAK's native Roots source has distinct SporeCloud fingerprints.");
                    Selected ??= Fingerprint;
                }
                foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
                {
                    var Candidates = NativePrefabContracts.EffectiveProps(Spawner);
                    if (Candidates is null) continue;
                    foreach (var Candidate in Candidates)
                    {
                        if (!Candidate)
                            throw new InvalidOperationException(
                                "PEAK's Roots candidate graph contains a missing prefab.");
                        if (!SeenRoots.Add(Candidate)) continue;
                        Pending.Enqueue(Candidate);
                    }
                }
            }
            return Selected
                ?? throw new InvalidOperationException(
                    "PEAK's Roots source has no exact SporeCloud match.");
        }
    }

    internal sealed class Batch
    {
        readonly Dictionary<PLLNativeResolverKind, WeatherFingerprint> WeatherSources;
        readonly SporeFingerprint? SporeCloudSource;
        readonly Dictionary<PLLNativeResolverKind, NativeFlowProvider> FlowSources;

        internal Batch(
            Dictionary<PLLNativeResolverKind, WeatherFingerprint> WeatherSources,
            SporeFingerprint? SporeCloudSource,
            int RequiredMask,
            Dictionary<PLLNativeResolverKind, NativeFlowProvider>? FlowSources = null)
        {
            this.WeatherSources = WeatherSources;
            this.SporeCloudSource = SporeCloudSource;
            this.RequiredMask = RequiredMask;
            this.FlowSources = FlowSources ?? new();
        }

        internal static Batch Empty { get; } = new(
            new Dictionary<PLLNativeResolverKind, WeatherFingerprint>(), null, 0);
        internal int RequiredMask { get; }

        internal WeatherFingerprint Weather(PLLNativeResolverKind Kind) =>
            WeatherSources.TryGetValue(Kind, out var Value)
                ? Value
                : throw new InvalidOperationException($"PEAK's native {Kind} source was not sealed.");
        internal SporeFingerprint SporeCloud() => SporeCloudSource
            ?? throw new InvalidOperationException("PEAK's native SporeCloud source was not sealed.");

        internal NativeFlowProvider Flow(PLLNativeResolverKind Kind) => FlowSources.TryGetValue(Kind, out var Provider)
            ? Provider : throw new InvalidOperationException($"PEAK's native {Kind} flow provider was not sealed.");

        internal void CommitFlows(IDictionary<PLLNativeResolverKind, NativeFlowProvider> Committed)
        {
            foreach (var Pair in FlowSources)
                if (Committed.TryGetValue(Pair.Key, out var Current))
                {
                    if (!Current.Matches(Pair.Value))
                        throw new InvalidOperationException($"PEAK's native {Pair.Key} flow provider changed during sealing.");
                }
                else Committed.Add(Pair.Key, Pair.Value);
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(WeatherSources.Count);
            for (var Value = (int)PLLNativeResolverKind.Rain; Value <= (int)PLLNativeResolverKind.Snow; Value++)
            {
                var Kind = (PLLNativeResolverKind)Value;
                if (!WeatherSources.TryGetValue(Kind, out var Fingerprint)) continue;
                Writer.Write((byte)Kind);
                Fingerprint.WriteManifest(Writer);
            }
            Writer.Write(SporeCloudSource is not null);
            SporeCloudSource?.WriteManifest(Writer);
            Writer.Write(FlowSources.Count);
            if (FlowSources.TryGetValue(PLLNativeResolverKind.Water, out var Water)) Water.WriteManifest(Writer);
            if (FlowSources.TryGetValue(PLLNativeResolverKind.Lava, out var Lava)) Lava.WriteManifest(Writer);
        }

        internal void ValidateCompatible(
            IReadOnlyDictionary<PLLNativeResolverKind, WeatherFingerprint> CommittedWeather,
            SporeFingerprint? CommittedSpore)
        {
            foreach (var Pair in WeatherSources)
                if (CommittedWeather.TryGetValue(Pair.Key, out var Current) && !Current.Matches(Pair.Value))
                    throw new InvalidOperationException(
                        $"PEAK's live {Pair.Key} source changed during compatibility sealing.");
            if (SporeCloudSource is not null && CommittedSpore is not null
                && !CommittedSpore.Matches(SporeCloudSource))
                throw new InvalidOperationException(
                    "PEAK's live SporeCloud source changed during compatibility sealing.");
        }

        internal void CopyMissingTo(
            IDictionary<PLLNativeResolverKind, WeatherFingerprint> CommittedWeather,
            ref SporeFingerprint? CommittedSpore)
        {
            foreach (var Pair in WeatherSources)
                if (!CommittedWeather.ContainsKey(Pair.Key)) CommittedWeather.Add(Pair.Key, Pair.Value);
            CommittedSpore ??= SporeCloudSource;
        }
    }

    internal sealed class WeatherFingerprint
    {
        readonly float Stamina;
        readonly Vector2 TimeOn;
        readonly Vector2 TimeOff;
        readonly bool DebugEnabled;
        readonly float LightLower;
        readonly float LightMargin;
        readonly CharacterAfflictions.STATUSTYPE Status;
        readonly float ForceRadius;
        readonly float ForceDelay;
        readonly float RagdollMultiplier;
        readonly bool MovesItems;
        readonly float StatusPerSecond;
        readonly float Force;
        readonly bool Slippy;
        readonly float ItemFactor;
        readonly Keyframe[] Curve;
        readonly WrapMode PreWrapMode;
        readonly WrapMode PostWrapMode;
        readonly bool UseCurve;
        readonly bool UseRaycast;
        readonly float MaxRaycast;
        readonly float MinRaycast;
        readonly RunSettings.SETTINGTYPE Setting;

        WeatherFingerprint(WindChillZone Source, RunSettings.SETTINGTYPE Setting)
        {
            var SourceCurve = Source.windIntensityCurve
                ?? throw new InvalidOperationException("PEAK's native weather source has no intensity curve.");
            Stamina = Source.grabStaminaMultiplierDuringWind;
            TimeOn = Source.windTimeRangeOn;
            TimeOff = Source.windTimeRangeOff;
            DebugEnabled = Source.debubEnable;
            LightLower = Source.lightVolumeSampleThreshold_lower;
            LightMargin = Source.lightVolumeSampleThreshold_margin;
            Status = Source.statusType;
            ForceRadius = Source.forceRadius;
            ForceDelay = Source.delayBeforeForce;
            RagdollMultiplier = Source.ragdolledWindForceMult;
            MovesItems = Source.windMovesItems;
            StatusPerSecond = Source.statusApplicationPerSecond;
            Force = Source.windForce;
            Slippy = Source.setSlippy;
            ItemFactor = Source.windItemFactor;
            Curve = SourceCurve.keys;
            PreWrapMode = SourceCurve.preWrapMode;
            PostWrapMode = SourceCurve.postWrapMode;
            UseCurve = Source.useIntensityCurve;
            UseRaycast = Source.useRaycast;
            MaxRaycast = Source.maxRaycastDistance;
            MinRaycast = Source.minRaycastDistance;
            this.Setting = Setting;
        }

        internal static WeatherFingerprint Capture(WindChillZone Source, PLLNativeResolverKind Kind)
        {
            if (!Source) throw new InvalidOperationException($"PEAK's native {Kind} weather source was destroyed.");
            var Setting = RequiredSetting(Kind);
            if (Source.disableIfSettingDisabled != Setting)
                throw new InvalidOperationException($"PEAK's native {Kind} weather source has the wrong run setting.");
            var Result = new WeatherFingerprint(Source, Setting);

            return Result;
        }

        internal static RunSettings.SETTINGTYPE RequiredSetting(PLLNativeResolverKind Kind) => Kind switch
        {
            PLLNativeResolverKind.Rain => RunSettings.SETTINGTYPE.Hazard_Rain,
            PLLNativeResolverKind.Wind => RunSettings.SETTINGTYPE.Hazard_Wind,
            PLLNativeResolverKind.Snow => RunSettings.SETTINGTYPE.Hazard_Storm,
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };

        internal void Copy(WindChillZone Target)
        {
            if (!Target) throw new InvalidOperationException("A translated native-weather target was destroyed.");
            Target.grabStaminaMultiplierDuringWind = Stamina;
            Target.windTimeRangeOn = TimeOn;
            Target.windTimeRangeOff = TimeOff;
            Target.debubEnable = DebugEnabled;
            Target.lightVolumeSampleThreshold_lower = LightLower;
            Target.lightVolumeSampleThreshold_margin = LightMargin;
            Target.statusType = Status;
            Target.forceRadius = ForceRadius;
            Target.delayBeforeForce = ForceDelay;
            Target.ragdolledWindForceMult = RagdollMultiplier;
            Target.windMovesItems = MovesItems;
            Target.statusApplicationPerSecond = StatusPerSecond;
            Target.windForce = Force;
            Target.setSlippy = Slippy;
            Target.windItemFactor = ItemFactor;
            Target.windIntensityCurve = new AnimationCurve((Keyframe[])Curve.Clone())
            {
                preWrapMode = PreWrapMode,
                postWrapMode = PostWrapMode
            };
            Target.useIntensityCurve = UseCurve;
            Target.useRaycast = UseRaycast;
            Target.maxRaycastDistance = MaxRaycast;
            Target.minRaycastDistance = MinRaycast;
            Target.disableIfSettingDisabled = Setting;
        }

        internal bool Matches(WeatherFingerprint Other)
        {
            if (Other is null || !Other.Stamina.Equals(Stamina) || !Other.TimeOn.Equals(TimeOn)
                || !Other.TimeOff.Equals(TimeOff) || Other.DebugEnabled != DebugEnabled
                || !Other.LightLower.Equals(LightLower)
                || !Other.LightMargin.Equals(LightMargin) || Other.Status != Status
                || !Other.ForceRadius.Equals(ForceRadius) || !Other.ForceDelay.Equals(ForceDelay)
                || !Other.RagdollMultiplier.Equals(RagdollMultiplier) || Other.MovesItems != MovesItems
                || !Other.StatusPerSecond.Equals(StatusPerSecond) || !Other.Force.Equals(Force)
                || Other.Slippy != Slippy || !Other.ItemFactor.Equals(ItemFactor)
                || Other.PreWrapMode != PreWrapMode || Other.PostWrapMode != PostWrapMode
                || Other.UseCurve != UseCurve || Other.UseRaycast != UseRaycast
                || !Other.MaxRaycast.Equals(MaxRaycast) || !Other.MinRaycast.Equals(MinRaycast)
                || Other.Setting != Setting || Other.Curve.Length != Curve.Length) return false;
            for (var i = 0; i < Curve.Length; i++)
                if (!KeyMatches(Curve[i], Other.Curve[i])) return false;
            return true;
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(Stamina); WriteVector(Writer, TimeOn);
            WriteVector(Writer, TimeOff); Writer.Write(DebugEnabled); Writer.Write(LightLower); Writer.Write(LightMargin);
            Writer.Write((int)Status); Writer.Write(ForceRadius); Writer.Write(ForceDelay);
            Writer.Write(RagdollMultiplier); Writer.Write(MovesItems); Writer.Write(StatusPerSecond);
            Writer.Write(Force); Writer.Write(Slippy); Writer.Write(ItemFactor);
            Writer.Write(Curve.Length);
            foreach (var Key in Curve) WriteKey(Writer, Key);
            Writer.Write((int)PreWrapMode); Writer.Write((int)PostWrapMode);
            Writer.Write(UseCurve); Writer.Write(UseRaycast);
            Writer.Write(MaxRaycast); Writer.Write(MinRaycast); Writer.Write((int)Setting);
        }

        static void WriteVector(BinaryWriter Writer, Vector2 Value)
        {
            Writer.Write(Value.x); Writer.Write(Value.y);
        }

        static void WriteKey(BinaryWriter Writer, Keyframe Value)
        {
            Writer.Write(Value.time); Writer.Write(Value.value);
            Writer.Write(Value.inTangent); Writer.Write(Value.outTangent);
            Writer.Write(Value.inWeight); Writer.Write(Value.outWeight);
            Writer.Write((int)Value.weightedMode);
        }

        static bool KeyMatches(Keyframe Left, Keyframe Right) => Left.time.Equals(Right.time)
            && Left.value.Equals(Right.value) && Left.inTangent.Equals(Right.inTangent)
            && Left.outTangent.Equals(Right.outTangent) && Left.inWeight.Equals(Right.inWeight)
            && Left.outWeight.Equals(Right.outWeight) && Left.weightedMode == Right.weightedMode;
    }

    internal sealed class SporeFingerprint
    {
        readonly CharacterAfflictions.STATUSTYPE Status;
        readonly float Amount;
        readonly float CapturedRadius;
        readonly float OuterFade;
        readonly float InnerFade;
        readonly float Minimum;
        readonly bool Warning;
        readonly float ExtraWarning;
        readonly float ExtraWindDisableTime;
        readonly float RandomStart;
        readonly bool PreventOverlap;
        readonly string OverlapIdentifier;
        readonly RunSettings.SETTINGTYPE Setting;
        readonly float TickTime;

        SporeFingerprint(WindAffectedStatusEmitter Emitter, DisableBasedOnRunSettings Setting)
        {
            Status = Emitter.statusType;
            Amount = Emitter.amount;
            CapturedRadius = Emitter.radius;
            OuterFade = Emitter.outerFade;
            InnerFade = Emitter.innerFade;
            Minimum = Emitter.minAmount;
            Warning = Emitter.inZoneWarning;
            ExtraWarning = Emitter.extraWarningTime;
            ExtraWindDisableTime = Emitter.extraWindDisableTime;
            RandomStart = Emitter.randomStart;
            PreventOverlap = Emitter.preventOverlap;
            OverlapIdentifier = Emitter.overlapIdentifier
                ?? throw new InvalidOperationException("PEAK's Roots SporeCloud source has no overlap identifier.");
            this.Setting = Setting.disableIfSettingDisabled;
            TickTime = Emitter.tickTime;
        }

        internal float Radius => CapturedRadius;

        internal static SporeFingerprint Capture(
            WindAffectedStatusEmitter Emitter,
            DisableBasedOnRunSettings Setting)
        {
            if (!Emitter || !Setting || Emitter.statusType != CharacterAfflictions.STATUSTYPE.Spores
                || Setting.disableIfSettingDisabled != RunSettings.SETTINGTYPE.Hazard_SporeClouds)
                throw new InvalidOperationException("PEAK's Roots SporeCloud source has an invalid structural identity.");
            var Result = new SporeFingerprint(Emitter, Setting);
            if (!PositiveFinite(Result.CapturedRadius) || !PositiveFinite(Result.TickTime))
                throw new InvalidOperationException("PEAK's Roots SporeCloud source has an invalid radius or tick time.");

            return Result;
        }

        internal void Copy(WindAffectedStatusEmitter Emitter, DisableBasedOnRunSettings Setting)
        {
            if (!Emitter || !Setting) throw new InvalidOperationException("A translated SporeCloud target was destroyed.");
            Emitter.statusType = Status;
            Emitter.amount = Amount;
            Emitter.radius = CapturedRadius;
            Emitter.outerFade = OuterFade;
            Emitter.innerFade = InnerFade;
            Emitter.minAmount = Minimum;
            Emitter.inZoneWarning = Warning;
            Emitter.extraWarningTime = ExtraWarning;
            Emitter.extraWindDisableTime = ExtraWindDisableTime;
            Emitter.randomStart = RandomStart;
            Emitter.preventOverlap = PreventOverlap;
            Emitter.overlapIdentifier = OverlapIdentifier;
            Emitter.tickTime = TickTime;
            Setting.disableIfSettingDisabled = this.Setting;
        }

        internal bool Matches(SporeFingerprint Other) => Other is not null && Other.Status == Status
            && Other.Amount.Equals(Amount) && Other.CapturedRadius.Equals(CapturedRadius)
            && Other.OuterFade.Equals(OuterFade) && Other.InnerFade.Equals(InnerFade)
            && Other.Minimum.Equals(Minimum) && Other.Warning == Warning
            && Other.ExtraWarning.Equals(ExtraWarning) && Other.ExtraWindDisableTime.Equals(ExtraWindDisableTime)
            && Other.RandomStart.Equals(RandomStart)
            && Other.PreventOverlap == PreventOverlap
            && string.Equals(Other.OverlapIdentifier, OverlapIdentifier, StringComparison.Ordinal)
            && Other.Setting == Setting && Other.TickTime.Equals(TickTime);

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write((int)Status); Writer.Write(Amount); Writer.Write(CapturedRadius); Writer.Write(OuterFade);
            Writer.Write(InnerFade); Writer.Write(Minimum); Writer.Write(Warning); Writer.Write(ExtraWarning);
            Writer.Write(ExtraWindDisableTime); Writer.Write(RandomStart); Writer.Write(PreventOverlap);
            Writer.Write(OverlapIdentifier);
            Writer.Write((int)Setting); Writer.Write(TickTime);
        }

        static bool PositiveFinite(float Value) => Value > 0f && float.IsFinite(Value);
    }
}
