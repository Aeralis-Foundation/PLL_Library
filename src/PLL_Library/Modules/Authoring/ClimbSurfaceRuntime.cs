using System;
using System.Collections.Generic;

namespace PLL.Library;

internal sealed class ClimbSurfaceRuntime
{
    readonly DefinitionState[] Definitions;
    ResolvedClimbSurfaceTarget[] Targets;
    int[][] TargetsByDefinition;
    bool[] DirtyTargets;
    bool Active;

    internal ClimbSurfaceRuntime(ResolvedClimbSurfaceCatalog Catalog)
    {
        Definitions = new DefinitionState[Catalog.Definitions.Length];
        for (var Index = 0; Index < Definitions.Length; Index++)
            Definitions[Index] = new DefinitionState(Catalog.Definitions[Index]);
        Targets = Array.Empty<ResolvedClimbSurfaceTarget>();
        TargetsByDefinition = Array.Empty<int[]>();
        DirtyTargets = Array.Empty<bool>();
        Refresh(Catalog);
    }

    internal void Activate()
    {
        ResetDefinitions();
        Active = true;
        ApplyAll();
    }

    internal void Deactivate()
    {
        Active = false;
        ResetDefinitions();
        foreach (var Target in Targets) ApplyNeutral(Target.Surface);
    }

    internal void Refresh(ResolvedClimbSurfaceCatalog Catalog)
    {
        if (Catalog.Definitions.Length != Definitions.Length)
            throw new InvalidOperationException(
                "A climb-surface runtime changed its definition count.");
        for (var Index = 0; Index < Definitions.Length; Index++)
            Definitions[Index].Validate(Catalog.Definitions[Index]);

        var PreviousSurfaces = new HashSet<ClimbModifierSurface>();
        foreach (var Target in Targets)
            if (Target.Surface) PreviousSurfaces.Add(Target.Surface);
        var SeenSurfaces = new HashSet<ClimbModifierSurface>();
        foreach (var Target in Catalog.Targets)
        {
            if (!Target.Surface || !SeenSurfaces.Add(Target.Surface))
                throw new InvalidOperationException(
                    "A climb-surface runtime resolved a missing or duplicate native target.");
            ValidateDefinitionIndex(Target.ObjectDefinition);
            ValidateDefinitionIndex(Target.MaterialDefinition);
            if (Target.ObjectDefinition < 0 && Target.MaterialDefinition < 0)
                throw new InvalidOperationException(
                    "A climb-surface runtime resolved a target without a rule.");
            if (!PreviousSurfaces.Contains(Target.Surface)
                && (!Target.Surface.staminaUsageMultiplier.Equals(1f)
                    || !Target.Surface.speedMultiplier.Equals(1f)))
                throw new InvalidOperationException(
                    "A newly resolved climb surface was not neutral.");
        }

        var NewTargetsByDefinition = BuildTargetIndex(
            Catalog.Targets, Definitions.Length);
        foreach (var Previous in Targets)
            if (Previous.Surface && !SeenSurfaces.Contains(Previous.Surface))
                ApplyNeutral(Previous.Surface);
        Targets = Catalog.Targets;
        TargetsByDefinition = NewTargetsByDefinition;
        DirtyTargets = new bool[Targets.Length];
        if (Active) ApplyAll();
        else foreach (var Target in Targets) ApplyNeutral(Target.Surface);

        void ValidateDefinitionIndex(int Definition)
        {
            if (Definition < -1 || Definition >= Definitions.Length)
                throw new InvalidOperationException(
                    "A climb-surface runtime resolved an invalid rule index.");
        }
    }

    internal int Emit(string Hook)
    {
        var Count = 0;
        for (var Index = 0; Index < Definitions.Length; Index++)
        {
            var Definition = Definitions[Index];
            if (!Definition.Accepts(Hook)) continue;
            Count++;
            if (!Definition.EnableAuthored()) continue;
            MarkDefinition(Index);
        }
        ApplyDirty();
        return Count;
    }

    internal int Emit(
        string Hook, float StaminaUsageMultiplier,
        float SpeedMultiplier)
    {
        var Count = 0;
        for (var Index = 0; Index < Definitions.Length; Index++)
        {
            var Definition = Definitions[Index];
            if (!Definition.Accepts(Hook)) continue;
            Count++;
            if (!Definition.EnableOverride(
                    StaminaUsageMultiplier, SpeedMultiplier)) continue;
            MarkDefinition(Index);
        }
        ApplyDirty();
        return Count;
    }

    internal int Stop(string Hook)
    {
        var Count = 0;
        for (var Index = 0; Index < Definitions.Length; Index++)
        {
            var Definition = Definitions[Index];
            if (!Definition.Accepts(Hook) || !Definition.Disable()) continue;
            Count++;
            MarkDefinition(Index);
        }
        ApplyDirty();
        return Count;
    }

    void MarkDefinition(int Definition)
    {
        foreach (var Target in TargetsByDefinition[Definition])
            DirtyTargets[Target] = true;
    }

    void ApplyDirty()
    {
        for (var Index = 0; Index < DirtyTargets.Length; Index++)
        {
            if (!DirtyTargets[Index]) continue;
            DirtyTargets[Index] = false;
            Apply(Targets[Index]);
        }
    }

    void ApplyAll()
    {
        foreach (var Target in Targets) Apply(Target);
    }

    void Apply(ResolvedClimbSurfaceTarget Target)
    {
        if (!Active || !Target.Surface)
        {
            if (Target.Surface) ApplyNeutral(Target.Surface);
            return;
        }
        var DefinitionIndex = Target.ObjectDefinition;
        if (DefinitionIndex < 0 || !Definitions[DefinitionIndex].Active)
            DefinitionIndex = Target.MaterialDefinition;
        if (DefinitionIndex < 0 || !Definitions[DefinitionIndex].Active)
        {
            ApplyNeutral(Target.Surface);
            return;
        }
        var Definition = Definitions[DefinitionIndex];
        Target.Surface.staminaUsageMultiplier =
            Definition.EffectiveStaminaUsageMultiplier;
        Target.Surface.speedMultiplier = Definition.EffectiveSpeedMultiplier;
    }

    void ResetDefinitions()
    {
        foreach (var Definition in Definitions) Definition.Reset();
    }

    static int[][] BuildTargetIndex(
        ResolvedClimbSurfaceTarget[] Targets,
        int DefinitionCount)
    {
        var Counts = new int[DefinitionCount];
        foreach (var Target in Targets)
        {
            if (Target.ObjectDefinition >= 0) Counts[Target.ObjectDefinition]++;
            if (Target.MaterialDefinition >= 0) Counts[Target.MaterialDefinition]++;
        }
        var Result = new int[DefinitionCount][];
        for (var Index = 0; Index < Result.Length; Index++)
            Result[Index] = new int[Counts[Index]];
        Array.Clear(Counts, 0, Counts.Length);
        for (var TargetIndex = 0; TargetIndex < Targets.Length; TargetIndex++)
        {
            var Target = Targets[TargetIndex];
            if (Target.ObjectDefinition >= 0)
                Result[Target.ObjectDefinition][Counts[Target.ObjectDefinition]++] =
                    TargetIndex;
            if (Target.MaterialDefinition >= 0)
                Result[Target.MaterialDefinition][Counts[Target.MaterialDefinition]++] =
                    TargetIndex;
        }
        return Result;
    }

    static void ApplyNeutral(ClimbModifierSurface Surface)
    {
        Surface.staminaUsageMultiplier = 1f;
        Surface.speedMultiplier = 1f;
    }

    sealed class DefinitionState
    {
        readonly PllClimbSurfaceMode Mode;
        readonly string Hook;
        readonly float AuthoredStaminaUsageMultiplier;
        readonly float AuthoredSpeedMultiplier;
        bool Override;
        float OverrideStaminaUsageMultiplier;
        float OverrideSpeedMultiplier;

        internal DefinitionState(ResolvedClimbSurfaceDefinition Definition)
        {
            Mode = Definition.Mode;
            Hook = Definition.Hook;
            AuthoredStaminaUsageMultiplier =
                Definition.StaminaUsageMultiplier;
            AuthoredSpeedMultiplier = Definition.SpeedMultiplier;
            Reset();
        }

        internal bool Active { get; private set; }
        internal float EffectiveStaminaUsageMultiplier => Override
            ? OverrideStaminaUsageMultiplier
            : AuthoredStaminaUsageMultiplier;
        internal float EffectiveSpeedMultiplier => Override
            ? OverrideSpeedMultiplier : AuthoredSpeedMultiplier;

        internal bool Accepts(string Value) =>
            Mode == PllClimbSurfaceMode.EmitHook
            && string.Equals(Hook, Value, StringComparison.Ordinal);

        internal bool EnableAuthored()
        {
            var Changed = !Active || Override;
            Active = true;
            Override = false;
            return Changed;
        }

        internal bool EnableOverride(
            float StaminaUsageMultiplier, float SpeedMultiplier)
        {
            var Changed = !Active || !Override
                || !OverrideStaminaUsageMultiplier.Equals(
                    StaminaUsageMultiplier)
                || !OverrideSpeedMultiplier.Equals(SpeedMultiplier);
            Active = true;
            Override = true;
            OverrideStaminaUsageMultiplier = StaminaUsageMultiplier;
            OverrideSpeedMultiplier = SpeedMultiplier;
            return Changed;
        }

        internal bool Disable()
        {
            if (!Active) return false;
            Active = false;
            Override = false;
            return true;
        }

        internal void Reset()
        {
            Active = Mode == PllClimbSurfaceMode.Always;
            Override = false;
            OverrideStaminaUsageMultiplier = 0f;
            OverrideSpeedMultiplier = 0f;
        }

        internal void Validate(ResolvedClimbSurfaceDefinition Definition)
        {
            if (Definition.Mode != Mode
                || !string.Equals(
                    Definition.Hook, Hook, StringComparison.Ordinal)
                || !Definition.StaminaUsageMultiplier.Equals(
                    AuthoredStaminaUsageMultiplier)
                || !Definition.SpeedMultiplier.Equals(
                    AuthoredSpeedMultiplier))
                throw new InvalidOperationException(
                    "A climb-surface runtime changed its sealed definition.");
        }
    }
}