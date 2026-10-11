using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class VisualState
{
    readonly Renderer[] Renderers;
    readonly bool[] RendererStates;
    readonly Renderer? PropertyRenderer;
    readonly int MaterialIndex;
    readonly int Property;
    readonly float TriggerValue;
    readonly MaterialPropertyBlock? OriginalBlock;
    readonly bool PropertyDriven;
    readonly float CoverDelaySeconds;
    readonly float OriginalValue;
    readonly MaterialPropertyBlock? AppliedBlock;
    PllPresentationCoverage Transition;
    bool PropertyApplied;
    bool IsActive;

    internal VisualState(ResolvedVisual Resolved)
    {
        var Authoring = Resolved.Authoring;
        Renderers = Resolved.Renderers;
        RendererStates = new bool[Renderers.Length];
        for (var i = 0; i < Renderers.Length; i++) RendererStates[i] = Renderers[i].enabled;
        PropertyRenderer = Resolved.PropertyRenderer;
        MaterialIndex = Resolved.MaterialIndex;
        var PropertyName = Authoring.PropertyName;
        EffectIndex = Resolved.EffectIndex;
        Requested = Authoring.EnabledByDefault;
        PropertyDriven = PropertyName.Length != 0 && PropertyRenderer;
        CoverDelaySeconds = Authoring.Transition.CoverDelaySeconds;
        if (PropertyDriven)
        {
            Property = Shader.PropertyToID(PropertyName);
            TriggerValue = Authoring.TriggerValue;
            OriginalBlock = new MaterialPropertyBlock();
            PropertyRenderer!.GetPropertyBlock(OriginalBlock, MaterialIndex);
            AppliedBlock = new MaterialPropertyBlock();
            if (OriginalBlock.isEmpty) PropertyRenderer.GetPropertyBlock(AppliedBlock);
            else PropertyRenderer.GetPropertyBlock(AppliedBlock, MaterialIndex);
            OriginalValue = AppliedBlock.HasFloat(Property) ? AppliedBlock.GetFloat(Property)
                : PropertyRenderer.sharedMaterials[MaterialIndex].GetFloat(Property);
        }
        foreach (var Renderer in Renderers)
            if (Renderer) Renderer.enabled = false;
    }

    internal int EffectIndex { get; }
    internal bool Requested { get; }

    internal void Apply(bool Value, double PresentationClock)
    {
        if (!Transition.SetTarget(Value, PresentationClock, CoverDelaySeconds)) return;
        ApplyCoverage();
    }

    internal void Tick(double PresentationClock)
    {
        if (Transition.Tick(PresentationClock)) ApplyCoverage();
    }

    internal void Stop()
    {
        Transition.Complete(false);
        ApplyCoverage();
    }

    void ApplyCoverage()
    {
        var Value = Transition.Target || Transition.Active;
        if (IsActive != Value)
        {
            IsActive = Value;
            for (var i = 0; i < Renderers.Length; i++)
                if (Renderers[i]) Renderers[i].enabled = Value && RendererStates[i];
        }
        if (!PropertyDriven) return;
        if (Value)
        {
            AppliedBlock!.SetFloat(Property, Mathf.LerpUnclamped(
                OriginalValue, TriggerValue, Transition.Coverage));
            PropertyRenderer!.SetPropertyBlock(AppliedBlock, MaterialIndex);
            PropertyApplied = true;
        }
        else RestoreProperty();
    }

    internal void Restore()
    {
        IsActive = false;
        Transition.Complete(false);
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, RestoreProperty);
        for (var i = 0; i < Renderers.Length; i++)
            CleanupOperations.Capture(ref Failures, () =>
            {
                if (Renderers[i]) Renderers[i].enabled = RendererStates[i];
            });
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Visual restoration failed.", Failures);
    }

    void RestoreProperty()
    {
        if (!PropertyApplied) return;
        if (PropertyRenderer) PropertyRenderer.SetPropertyBlock(OriginalBlock, MaterialIndex);
        PropertyApplied = false;
    }
}
