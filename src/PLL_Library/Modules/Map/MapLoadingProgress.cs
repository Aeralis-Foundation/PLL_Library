using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapLoadingProgress
{
    static LoadingScreen? CapturedScreen;
    static MapLoadingProgress? ActiveProgress;

    readonly LoadingScreen Screen;
    readonly TMP_Text LoadingText;
    readonly bool OriginalRichText;
    readonly long RenderIntervalTicks = Stopwatch.Frequency;
    string RawLoadingText;
    string ProgressMarkup = string.Empty;
    string Activity = "Preparing map";
    int TotalUnits;
    int CompletedUnits;
    long CompletedTicks;
    long? CurrentUnitStartedAt;
    long NextProgressRenderAt;
    bool ReadyState;

    MapLoadingProgress(LoadingScreen Screen, TMP_Text LoadingText)
    {
        this.Screen = Screen;
        this.LoadingText = LoadingText;
        OriginalRichText = LoadingText.richText;
        RawLoadingText = LoadingText.text;
    }

    internal static MapLoadingProgress? BeginInitial()
    {
        var Screen = CapturedScreen;
        if (!Screen) return null;
        var Animation = Screen.GetComponent<LoadingScreenAnimation>();
        var NativeText = Animation
            ? Animation.loadingText
            : Screen.GetComponent<LoadingScreenAnimationSimple>()?.loading;
        if (!NativeText) return null;

        var Progress = new MapLoadingProgress(Screen, NativeText);
        Progress.Activate();
        ActiveProgress = Progress;
        return Progress;
    }

    internal static void StopInitial()
    {
        var Progress = ActiveProgress;
        ActiveProgress = null;
        Progress?.Stop();
    }

    internal void SetActivity(string Activity) => this.Activity = Activity;

    internal void SetPlan(int PlacementCount, int InitialGenerationOccurrences)
    {
        if (TotalUnits != 0)
            throw new InvalidOperationException("Loading progress already has a unit plan.");
        TotalUnits = PlacementCount + InitialGenerationOccurrences + 4;
    }

    internal void BeginUnit(string Activity)
    {
        if (CurrentUnitStartedAt is not null)
            throw new InvalidOperationException("A loading progress unit is already active.");
        if (CompletedUnits >= TotalUnits)
            throw new InvalidOperationException("Loading progress cannot exceed its planned unit count.");
        this.Activity = Activity;
        CurrentUnitStartedAt = Stopwatch.GetTimestamp();
    }

    internal void CompleteUnit()
    {
        var StartedAt = CurrentUnitStartedAt
            ?? throw new InvalidOperationException("Loading progress has no active unit to complete.");
        CompletedTicks += Stopwatch.GetTimestamp() - StartedAt;
        CompletedUnits++;
        CurrentUnitStartedAt = null;
    }

    internal void Ready()
    {
        if (TotalUnits == 0)
            throw new InvalidOperationException("Loading progress has no unit plan.");
        if (CurrentUnitStartedAt is not null || CompletedUnits != TotalUnits)
            throw new InvalidOperationException(
                $"Loading progress completed {CompletedUnits} of {TotalUnits} planned units.");
        ReadyState = true;
        ProgressMarkup = "<size=75%><color=#59FF73>Ready</color></size>\n";
        ApplyCombinedText();
    }

    internal void Stop()
    {
        if (!LoadingText) return;
        LoadingText.richText = OriginalRichText;
        LoadingText.text = RawLoadingText;
    }

    void Activate()
    {
        try
        {
            LoadingText.richText = true;
            RefreshProgress(Stopwatch.GetTimestamp(), true);
            ApplyCombinedText();
        }
        catch
        {
            LoadingText.richText = OriginalRichText;
            LoadingText.text = RawLoadingText;
            throw;
        }
    }

    void ApplyNativeText(string NativeText)
    {
        var RawChanged = !string.Equals(RawLoadingText, NativeText, StringComparison.Ordinal);
        if (RawChanged) RawLoadingText = NativeText;
        var ProgressChanged = RefreshProgress(Stopwatch.GetTimestamp(), false);
        if (RawChanged || ProgressChanged) ApplyCombinedText();
    }

    void ApplyCombinedText() => LoadingText.text = ProgressMarkup + RawLoadingText;

    bool RefreshProgress(long Now, bool Force)
    {
        if (ReadyState || !Force && Now < NextProgressRenderAt) return false;
        NextProgressRenderAt = Now + RenderIntervalTicks;
        var Fraction = TotalUnits == 0 ? 0f : Mathf.Clamp01((float)CompletedUnits / TotalUnits);
        var ColorHex = ColorUtility.ToHtmlStringRGB(Color.Lerp(
            new Color(0.25f, 0.8f, 1f, 1f),
            new Color(0.35f, 1f, 0.45f, 1f),
            Fraction));
        var Updated = $"<size=75%><color=#{ColorHex}>{Activity}  {EstimateRemaining()}</color></size>\n";
        if (string.Equals(ProgressMarkup, Updated, StringComparison.Ordinal)) return false;
        ProgressMarkup = Updated;
        return true;
    }

    string EstimateRemaining()
    {
        if (TotalUnits == 0 || CompletedUnits == 0) return "Estimating remaining time";
        var AverageTicks = CompletedTicks / CompletedUnits;
        var RemainingUnits = Math.Max(0, TotalUnits - CompletedUnits);
        var RemainingTicks = AverageTicks * RemainingUnits;
        if (CurrentUnitStartedAt is { } StartedAt && RemainingUnits != 0)
        {
            var CurrentTicks = Stopwatch.GetTimestamp() - StartedAt;
            RemainingTicks = Math.Max(0, AverageTicks - CurrentTicks)
                + AverageTicks * Math.Max(0, RemainingUnits - 1);
        }
        var RemainingSeconds = Math.Max(
            1, (int)Math.Ceiling((double)RemainingTicks / Stopwatch.Frequency));
        if (RemainingSeconds < 60) return $"About {RemainingSeconds}s remaining";
        return $"About {RemainingSeconds / 60}m {RemainingSeconds % 60}s remaining";
    }

    static void SetLoadingText(TMP_Text Text, string NativeText)
    {
        var Progress = ActiveProgress;
        if (Progress is null || !ReferenceEquals(Progress.LoadingText, Text))
        {
            Text.text = NativeText;
            return;
        }
        Progress.ApplyNativeText(NativeText);
    }

    static IEnumerable<CodeInstruction> ReplaceTextSetters(
        IEnumerable<CodeInstruction> Instructions,
        int ExpectedCount,
        string Target)
    {
        var NativeSetter = AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text))
            ?? throw new MissingMethodException("TMP_Text.text setter was not found.");
        var Replacement = ((Action<TMP_Text, string>)SetLoadingText).Method;
        var Replaced = 0;
        foreach (var Instruction in Instructions)
        {
            if (Instruction.Calls(NativeSetter))
            {
                Instruction.opcode = OpCodes.Call;
                Instruction.operand = Replacement;
                Replaced++;
            }
            yield return Instruction;
        }
        if (Replaced != ExpectedCount)
            throw new InvalidOperationException(
                $"{Target} contains {Replaced} TMP_Text.text writes instead of {ExpectedCount}.");
    }

    static bool IsEnabled() => FileManager.DisplayProgress?.Value == true;

    static IEnumerator ObserveScreen(LoadingScreen Screen, IEnumerator Original)
    {
        CapturedScreen = Screen;
        ExceptionDispatchInfo? LoadingFailure = null;
        ExceptionDispatchInfo? CleanupFailure = null;
        try
        {
            while (true)
            {
                object? Value = null;
                bool Moved;
                try
                {
                    Moved = Original.MoveNext();
                    if (Moved) Value = Original.Current;
                }
                catch (Exception Exception)
                {
                    Moved = false;
                    LoadingFailure = ExceptionDispatchInfo.Capture(Exception);
                }
                if (!Moved) break;
                yield return Value;
            }
        }
        finally
        {
            try { (Original as IDisposable)?.Dispose(); }
            catch (Exception Exception)
            {
                CleanupFailure = ExceptionDispatchInfo.Capture(Exception);
            }
            finally
            {
                if (ReferenceEquals(CapturedScreen, Screen)) CapturedScreen = null;
                if (ActiveProgress is { } Progress && ReferenceEquals(Progress.Screen, Screen))
                {
                    ActiveProgress = null;
                    try { Progress.Stop(); }
                    catch (Exception Exception)
                    {
                        CleanupFailure = CleanupFailure is null
                            ? ExceptionDispatchInfo.Capture(Exception)
                            : ExceptionDispatchInfo.Capture(new AggregateException(
                                "PEAK iterator and progress cleanup both failed.",
                                CleanupFailure.SourceException,
                                Exception));
                    }
                }
            }
            if (CleanupFailure is not null)
            {
                if (LoadingFailure is not null)
                    throw new AggregateException(
                        "PEAK loading and its cleanup both failed.",
                        LoadingFailure.SourceException,
                        CleanupFailure.SourceException);
                CleanupFailure.Throw();
            }
        }
        LoadingFailure?.Throw();
    }

    [HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.LoadingRoutine))]
    static class LoadingRoutinePatch
    {
        [HarmonyPrepare]
        static bool Prepare() => IsEnabled();

        [HarmonyPostfix]
        static void Postfix(LoadingScreen __instance, ref IEnumerator __result) =>
            __result = ObserveScreen(__instance, __result);
    }

    [HarmonyPatch(typeof(LoadingScreenAnimation), nameof(LoadingScreenAnimation.Update))]
    static class CurvedLoadingTextPatch
    {
        [HarmonyPrepare]
        static bool Prepare() => IsEnabled();

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions) =>
            ReplaceTextSetters(Instructions, 1, "LoadingScreenAnimation.Update");
    }

    [HarmonyPatch]
    static class SimpleLoadingTextPatch
    {
        [HarmonyPrepare]
        static bool Prepare() => IsEnabled();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var Routine = AccessTools.DeclaredMethod(
                typeof(LoadingScreenAnimationSimple),
                nameof(LoadingScreenAnimationSimple.AnimateRoutine),
                Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    typeof(LoadingScreenAnimationSimple).FullName,
                    nameof(LoadingScreenAnimationSimple.AnimateRoutine));
            return AccessTools.EnumeratorMoveNext(Routine)
                ?? throw new MissingMethodException(
                    "LoadingScreenAnimationSimple.AnimateRoutine iterator was not found.");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions) =>
            ReplaceTextSetters(
                Instructions,
                4,
                "LoadingScreenAnimationSimple.AnimateRoutine.MoveNext");
    }
}
