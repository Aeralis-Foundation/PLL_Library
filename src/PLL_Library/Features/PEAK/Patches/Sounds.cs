using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace PLL.Library;

internal static class Sounds
{
    sealed class OccurrenceOwner
    {
        internal OccurrenceOwner(MapRun Run, MapSlot Slot)
        {
            RunIdentity = Run.Identity;
            SlotIndex = Slot.Index;
            Source = Slot.Source;
            Frame = Slot.Frame;
            if (Slot.PeakAttachment) PeakExitPose = PlacementPlan.PlacedPeakExitPose(Slot);
            Volcano = FindVolcano(Slot);
            Citadel = FindCitadel(Slot);
        }

        internal long RunIdentity { get; }
        internal int SlotIndex { get; }
        internal LayoutSegmentSource Source { get; }
        internal PlacementFrame Frame { get; }
        internal Pose? PeakExitPose { get; }
        internal GameObject? Volcano { get; }
        internal CitadelAmbience? Citadel { get; }
    }

    sealed class NativeAudioBinding
    {
        readonly AudioSource[] Sources;
        readonly bool[] OriginalMute;

        internal NativeAudioBinding(AmbienceAudio Ambience, OccurrenceOwner Owner)
        {
            RunIdentity = Owner.RunIdentity;
            SlotIndex = Owner.SlotIndex;
            var Values = new List<AudioSource>();
            var Seen = new HashSet<int>();
            var Animator = Ambience.ambienceVolumes;
            if (Animator)
                foreach (var Source in Animator.GetComponentsInChildren<AudioSource>(true)) Add(Source);
            Sources = Values.ToArray();
            OriginalMute = new bool[Sources.Length];
            for (var Index = 0; Index < Sources.Length; Index++)
                OriginalMute[Index] = Sources[Index].mute;
            SetMuted(Owner.Source is CustomLayoutSegmentSource
                { Behavior: PllBiomeBehavior.Authored });

            void Add(AudioSource? Source)
            {
                if (!Source || ReferenceEquals(Source, Ambience.mainMusic)
                    || ReferenceEquals(Source, Ambience.stingerSource)
                    || Ambience.voice
                        && TransformOwnership.Contains(Ambience.voice, Source.transform)
                    || !Seen.Add(Source.GetInstanceID())) return;
                Values.Add(Source);
            }
        }

        internal long RunIdentity { get; }
        internal int SlotIndex { get; private set; }
        bool Muted { get; set; }

        internal void MoveTo(OccurrenceOwner Owner)
        {
            if (Owner.RunIdentity != RunIdentity)
                throw new InvalidOperationException(
                    "A PEAK ambience binding cannot move between map runs.");
            SlotIndex = Owner.SlotIndex;
            SetMuted(Owner.Source is CustomLayoutSegmentSource
                { Behavior: PllBiomeBehavior.Authored });
        }

        internal void Restore() => SetMuted(false);

        void SetMuted(bool Value)
        {
            if (Muted == Value) return;
            for (var Index = 0; Index < Sources.Length; Index++)
            {
                var Source = Sources[Index];
                if (!Source) continue;
                if (Value) Source.mute = true;
                else if (Source.mute) Source.mute = OriginalMute[Index];
            }
            Muted = Value;
        }
    }

    sealed class RouteProvider
    {
        internal RouteProvider(
            int SceneHandle,
            AmbienceAudio Ambience,
            Character Character,
            AudioMixerGroup? Group)
        {
            this.SceneHandle = SceneHandle;
            this.Ambience = Ambience;
            this.Character = Character;
            this.Group = Group;
        }

        internal int SceneHandle { get; }
        internal AmbienceAudio Ambience { get; }
        internal Character Character { get; }
        internal AudioMixerGroup? Group { get; }
    }

    sealed class RunRoute
    {
        internal RunRoute(long RunIdentity, int SceneHandle, AudioMixerGroup Group)
        {
            this.RunIdentity = RunIdentity;
            this.SceneHandle = SceneHandle;
            this.Group = Group;
        }

        internal long RunIdentity { get; }
        internal int SceneHandle { get; }
        internal AudioMixerGroup Group { get; }
    }
    static readonly MethodInfo AmbienceStart = BindAmbienceStart();
    static readonly MethodInfo BiomeIsPresent = AccessTools.DeclaredMethod(
        typeof(MapHandler), nameof(MapHandler.BiomeIsPresent), new[] { typeof(Biome.BiomeType) })!;
    static readonly FieldInfo PositionZ = AccessTools.DeclaredField(typeof(Vector3), nameof(Vector3.z))!;
    static readonly FieldInfo BeachStingerZ =
        AccessTools.DeclaredField(typeof(AmbienceAudio), nameof(AmbienceAudio.beachStingerZ))!;
    static readonly FieldInfo CalderaStingerZ =
        AccessTools.DeclaredField(typeof(AmbienceAudio), nameof(AmbienceAudio.calderaStingerZ))!;
    static readonly FieldInfo PlayedCalderaOrGloom =
        AccessTools.DeclaredField(
            typeof(AmbienceAudio), nameof(AmbienceAudio.playedCalderaOrGloom))!;
    static readonly FieldInfo KilnStingerY =
        AccessTools.DeclaredField(typeof(AmbienceAudio), nameof(AmbienceAudio.kilnStingerY))!;
    static readonly FieldInfo PeakStingerZ =
        AccessTools.DeclaredField(typeof(AmbienceAudio), nameof(AmbienceAudio.peaksTingerZ))!;
    static readonly MethodInfo CurrentBiomeMethod =
        ((Func<MapHandler, Biome.BiomeType, bool>)CurrentBiome).Method;
    static readonly MethodInfo CurrentBeachStingerPositionMethod =
        ((Func<Vector3, float>)CurrentBeachStingerPosition).Method;
    static readonly MethodInfo CurrentRoleStingerThresholdMethod =
        ((Func<float, Segment, float>)CurrentRoleStingerThreshold).Method;
    static readonly MethodInfo CurrentPeakStingerPositionMethod =
        ((Func<Vector3, float>)CurrentPeakStingerPosition).Method;
    static readonly MethodInfo CurrentPeakStingerThresholdMethod =
        ((Func<float, AmbienceAudio, float>)CurrentPeakStingerThreshold).Method;
    static readonly Dictionary<AmbienceAudio, NativeAudioBinding> bindings = new();
    static OccurrenceOwner? current;
    static RouteProvider? routeProvider;
    static RunRoute? runRoute;

    static long releasingRunIdentity;

    static MethodInfo BindAmbienceStart()
    {
        var method = AccessTools.DeclaredMethod(typeof(AmbienceAudio), "Start", Type.EmptyTypes);
        if (method is null || method.IsStatic || method.ReturnType != typeof(void))
            throw new MissingMethodException("AmbienceAudio.Start must be an instance parameterless Void method.");
        return method;
    }

    internal static bool TryGetStingerRoute(out AudioMixerGroup group)
    {
        if (runRoute is { } route && route.Group && Map.TryGetPublishedRun(out var published)
            && published.Active && published.Identity == route.RunIdentity
            && published.SceneHandle == route.SceneHandle)
        {
            group = route.Group;
            return true;
        }
        group = null!;
        return false;
    }

    internal static void ReleaseScene(int sceneHandle)
    {
        if (routeProvider?.SceneHandle == sceneHandle) routeProvider = null;
    }

    static void CaptureRouteProvider(AmbienceAudio instance)
    {
        if (!instance) return;
        var character = instance.character;
        if (!character || !character.IsLocal || character != Character.localCharacter) return;
        var MainMusic = instance.mainMusic;
        var provider = new RouteProvider(
            character.gameObject.scene.handle,
            instance,
            character,
            MainMusic ? MainMusic.outputAudioMixerGroup : null);
        routeProvider = provider;
        if (Map.TryGetPublishedRun(out var run) && run.Active && run.SceneHandle == provider.SceneHandle)
            AdoptRoute(run);
    }

    static void AdoptRoute(MapRun run)
    {
        if (!run.Active) return;
        var route = runRoute;
        if (route is not null && (route.RunIdentity != run.Identity || route.SceneHandle != run.SceneHandle))
            throw new InvalidOperationException("A PEAK music route is still owned by another map run.");
        var provider = routeProvider;
        if (route is not null)
        {
            if (provider is not null && provider.SceneHandle == run.SceneHandle && ProviderCurrent(provider, run))
            {
                var observed = ProviderGroup(provider);
                if (provider.Group is not { } captured || !captured || !observed
                    || captured != route.Group || observed != route.Group)
                {
                    AbortChangedRoute(run, provider);
                    return;
                }

            }
            if (route.Group) AuthoringRuntime.AdoptStingerRoute(route.Group);
            return;
        }
        if (provider is null || provider.SceneHandle != run.SceneHandle || !ProviderCurrent(provider, run)) return;
        var group = ProviderGroup(provider);
        if (provider.Group is not { } capturedGroup || !capturedGroup || !group || group != capturedGroup)
        {
            AbortChangedRoute(run, provider);
            return;
        }
        runRoute = new RunRoute(run.Identity, run.SceneHandle, group);
        AuthoringRuntime.AdoptStingerRoute(group);
    }

    static AudioMixerGroup? ProviderGroup(RouteProvider provider)
    {
        var MainMusic = provider.Ambience.mainMusic;
        return MainMusic ? MainMusic.outputAudioMixerGroup : null;
    }

    static bool ProviderCurrent(RouteProvider provider, MapRun run) =>
        ReferenceEquals(routeProvider, provider) && provider.Ambience && provider.Character
        && provider.Character.IsLocal && provider.Character == Character.localCharacter
        && provider.Character.gameObject.scene.handle == provider.SceneHandle
        && Map.TryGetPublishedRun(out var currentRun) && ReferenceEquals(currentRun, run)
        && currentRun.Identity == run.Identity && currentRun.Active && currentRun.SceneHandle == provider.SceneHandle;

    static void AbortChangedRoute(MapRun run, RouteProvider provider)
    {
        AuthoringRuntime.ClearStingerRoute();
        if (!ProviderCurrent(provider, run)) return;
        LazySlotGenerator.AbortLazyGeneration(Map.Index, "The local PEAK music output changed");
    }

    static bool CurrentBiome(MapHandler handler, Biome.BiomeType biome)
    {
        var owner = current;
        if (owner is not null)
            return LayoutSegmentBehavior.Resolve(owner.Source)?.Biome == biome;
        return releasingRunIdentity == 0 && !Map.HasPublishedLayout && handler.BiomeIsPresent(biome);
    }

    static float CurrentBeachStingerPosition(Vector3 Position) =>
        current is { } Owner ? Owner.Frame.InversePoint(Position).z : Position.z;

    static float CurrentRoleStingerThreshold(float NativeThreshold, Segment Role)
    {
        var Owner = current;
        if (Owner is null)
            return releasingRunIdentity == 0 && !Map.HasPublishedLayout
                ? NativeThreshold : float.PositiveInfinity;
        return LayoutSegmentBehavior.Resolve(Owner.Source)?.Role == Role
            ? NativeThreshold : float.PositiveInfinity;
    }

    static float CurrentPeakStingerPosition(Vector3 position)
    {
        var owner = current;
        return owner?.PeakExitPose is { } exit
            ? Vector3.Dot(position, exit.rotation * Vector3.forward)
            : position.z;
    }

    static float CurrentPeakStingerThreshold(float nativePeakStingerZ, AmbienceAudio instance)
    {
        var owner = current;
        if (owner?.PeakExitPose is { } exit)
        {
            var volumes = instance.ambienceVolumes;
            if (volumes && volumes.GetBool(instance.ANIM_CITADEL)) return float.PositiveInfinity;
            var normal = exit.rotation * Vector3.forward;
            return Vector3.Dot(exit.position, normal);
        }
        return owner is null && releasingRunIdentity == 0 && !Map.HasPublishedLayout
            ? nativePeakStingerZ
            : float.PositiveInfinity;
    }

    internal static void Commit(MapRun run, int slotIndex)
    {
        if (!run.Active || slotIndex < 0 || slotIndex >= run.Slots.Length)
            throw new InvalidOperationException("A sound occurrence requires an active map slot.");
        var slot = run.Slots[slotIndex];
        if (slot.Released || slot.Index != slotIndex)
            throw new InvalidOperationException("A sound occurrence requires its live indexed map slot.");
        if (current is { } owner && owner.RunIdentity == run.Identity && owner.SlotIndex == slotIndex)
        {
            AdoptRoute(run);
            return;
        }
        current = new OccurrenceOwner(run, slot);
        AdoptRoute(run);
    }

    internal static void Clear(MapRun run)
    {
        var ownsOccurrence = current is { } owner && owner.RunIdentity == run.Identity;
        var ownsRoute = runRoute?.RunIdentity == run.Identity;
        if (!ownsOccurrence && !ownsRoute) return;
        if (ownsOccurrence)
        {
            foreach (var Binding in bindings)
                if (Binding.Key)
                {
                    Binding.Value.Restore();
                    BindFixtures(Binding.Key, null, null);
                }
            bindings.Clear();
            current = null;
            releasingRunIdentity = run.Identity;
        }
        try { AuthoringRuntime.ClearStingerRoute(); }
        finally
        {
            if (ownsRoute) runRoute = null;
        }
    }

    internal static void Release(MapRun run)
    {
        if (releasingRunIdentity == run.Identity) releasingRunIdentity = 0;
    }

    static float HeightFor(Segment? role) => role switch
    {
        Segment.Alpine => 300f,
        Segment.Caldera or Segment.TheKiln or Segment.Peak or Segment.Void => 500f,
        _ => 0f
    };

    static GameObject? FindVolcano(MapSlot Slot)
    {
        var Source = Slot.Source;
        if (Source is not INativeLayoutSegmentSource
            { NativeRole: Segment.Caldera or Segment.TheKiln, NativeBiome: Biome.BiomeType.Volcano }) return null;
        GameObject? result = null;
        foreach (var Candidate in Slot.Occurrence.Slots)
        {
            if (Candidate.NativeCampfireSurface is not { } Surface) continue;
            var Root = Surface.Source.transform;
            while (Root.parent && Root.parent != Slot.Container) Root = Root.parent;
            if (Root.parent != Slot.Container || Root.name != "VolcanoModel" || result)
                throw new InvalidOperationException("A Volcano sound occurrence requires one exact VolcanoModel fixture.");
            result = Root.gameObject;
        }
        return result ? result : throw new InvalidOperationException(
            "A Volcano sound occurrence requires one exact VolcanoModel fixture.");
    }

    static CitadelAmbience? FindCitadel(MapSlot Slot)
    {
        if (Slot.Source is not NativeOrdinarySegmentSource
            { Id: "Citadel", NativeRole: Segment.TheKiln }) return null;
        var Fixtures = Slot.Container.GetComponentsInChildren<CitadelAmbience>(true);
        return Fixtures.Length == 1 ? Fixtures[0] : throw new InvalidOperationException(
            "A Citadel sound occurrence requires one exact CitadelAmbience fixture.");
    }

    static void BindFixtures(AmbienceAudio instance, GameObject? volcano, CitadelAmbience? citadel)
    {
        instance._volcanoObj = volcano!;
        instance._citadelAmbience = citadel!;
        instance.volcano = false;
        instance._volcanoTime = 0f;
        instance._citadelTime = 0f;
        var volumes = instance.ambienceVolumes;
        if (volumes)
        {
            volumes.SetBool(instance.ANIM_VOLCANO, false);
            volumes.SetBool(instance.ANIM_CITADEL, false);
            if (volcano is null && citadel is null) volumes.SetBool(instance.ANIM_DESERT, false);
        }
        var ReverbZone = instance.reverb;
        if (ReverbZone) ReverbZone.enabled = true;
    }

    [HarmonyPatch]
    static class AmbienceStartPatch
    {
        static MethodBase TargetMethod() => AmbienceStart;

        [HarmonyPostfix]
        static void Postfix(AmbienceAudio __instance)
        {
            try { CaptureRouteProvider(__instance); }
            catch (Exception exception)
            {
                if (!Map.TryGetPublishedRun(out var run) || !run.Active) return;
                try { AuthoringRuntime.ClearStingerRoute(); }
                catch (Exception cleanup)
                {
                    exception = new AggregateException(
                        "Biome stinger route adoption and cleanup both failed.", exception, cleanup);
                }
                try { LazySlotGenerator.AbortLazyGeneration(Map.Index, "Biome stinger route adoption failed", exception); }
                catch (Exception AbortFailure)
                {
                    throw new AggregateException(
                        "Biome stinger route adoption and run abort both failed.", exception, AbortFailure);
                }
            }
        }
    }

    [HarmonyPatch(typeof(AmbienceAudio), "Update")]
    static class AmbiencePatch
    {
        [HarmonyPrefix]
        static void Prefix(AmbienceAudio __instance)
        {
            var owner = current;
            if (owner is null)
            {
                if (releasingRunIdentity != 0)
                {
                    __instance._volcanoObj = null!;
                    __instance._citadelAmbience = null!;
                }
                return;
            }
            if (bindings.TryGetValue(__instance, out var Binding))
            {
                var SameSlot = Binding.RunIdentity == owner.RunIdentity
                    && Binding.SlotIndex == owner.SlotIndex;
                if (Binding.RunIdentity != owner.RunIdentity)
                {
                    Binding.Restore();
                    bindings.Remove(__instance);
                    Binding = new NativeAudioBinding(__instance, owner);
                    bindings.Add(__instance, Binding);
                }
                else if (!SameSlot) Binding.MoveTo(owner);
                if (SameSlot)
                {
                    __instance._volcanoObj = owner.Volcano!;
                    __instance._citadelAmbience = owner.Citadel!;
                    return;
                }
            }
            else
            {
                Binding = new NativeAudioBinding(__instance, owner);
                bindings.Add(__instance, Binding);
            }
            BindFixtures(__instance, owner.Volcano, owner.Citadel);
        }

        [HarmonyPostfix]
        static void Postfix(AmbienceAudio __instance)
        {
            var owner = current;
            if (owner is null) return;
            var volumes = __instance.ambienceVolumes;
            if (!volumes) return;
            var Compatibility = LayoutSegmentBehavior.Resolve(owner.Source);
            volumes.SetBool(__instance.ANIM_DESERT,
                Compatibility?.Biome == Biome.BiomeType.Mesa);
            volumes.SetFloat(__instance.ANIM_HEIGHT,
                HeightFor(Compatibility?.Role));
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var biomes = 0;
            var BeachStingers = 0;
            var CalderaStingers = 0;
            var KilnStingers = 0;
            var peakStingers = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];
                if (instruction.Calls(BiomeIsPresent))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CurrentBiomeMethod;
                    biomes++;
                }
                if (instruction.LoadsField(BeachStingerZ))
                {
                    if (i < 2 || !codes[i - 2].LoadsField(PositionZ)
                        || codes[i - 1].opcode != OpCodes.Ldarg_0)
                        throw new MissingFieldException(
                            "AmbienceAudio.Update's paired Beach position and stinger threshold were not found.");
                    codes[i - 2].opcode = OpCodes.Call;
                    codes[i - 2].operand = CurrentBeachStingerPositionMethod;
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Ldc_I4, (int)Segment.Beach));
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Call, CurrentRoleStingerThresholdMethod));
                    BeachStingers++;
                    continue;
                }
                if (instruction.LoadsField(CalderaStingerZ))
                {
                    if (i + 3 >= codes.Count
                        || codes[i + 1].opcode != OpCodes.Ble_Un
                            && codes[i + 1].opcode != OpCodes.Ble_Un_S
                        || codes[i + 2].opcode != OpCodes.Ldarg_0
                        || !codes[i + 3].LoadsField(PlayedCalderaOrGloom)) continue;
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Ldc_I4, (int)Segment.Caldera));
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Call, CurrentRoleStingerThresholdMethod));
                    CalderaStingers++;
                    continue;
                }
                if (instruction.LoadsField(KilnStingerY))
                {
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Ldc_I4, (int)Segment.TheKiln));
                    codes.Insert(++i, new CodeInstruction(
                        OpCodes.Call, CurrentRoleStingerThresholdMethod));
                    KilnStingers++;
                    continue;
                }
                if (!instruction.LoadsField(PeakStingerZ)) continue;
                if (i < 2 || !codes[i - 2].LoadsField(PositionZ) || codes[i - 1].opcode != OpCodes.Ldarg_0)
                    throw new MissingFieldException(
                        "AmbienceAudio.Update's paired Peak position and stinger threshold were not found.");
                codes[i - 2].opcode = OpCodes.Call;
                codes[i - 2].operand = CurrentPeakStingerPositionMethod;
                codes.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(++i, new CodeInstruction(OpCodes.Call, CurrentPeakStingerThresholdMethod));
                peakStingers++;
            }
            if (biomes == 0)
                throw new MissingMethodException("AmbienceAudio.Update biome routing was not found.");
            if (BeachStingers != 1 || CalderaStingers != 1 || KilnStingers != 1)
                throw new MissingFieldException(
                    $"AmbienceAudio.Update projected {BeachStingers} Beach, {CalderaStingers} Caldera, "
                    + $"and {KilnStingers} Kiln stinger thresholds; expected 1 each.");
            if (peakStingers == 0)
                throw new MissingFieldException("AmbienceAudio.Update's Peak stinger threshold was not found.");
            if (peakStingers != 1)
                throw new AmbiguousMatchException("AmbienceAudio.Update's Peak stinger threshold was ambiguous.");
            return codes;
        }
    }
}
