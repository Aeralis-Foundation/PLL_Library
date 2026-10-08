using System;
using UnityEngine;

namespace PLL.Library;

public enum PllPropBreakTrigger { Impact, SupportLoad, Manual }
public enum PllBridgeCapacity { NativeRandom, Fixed }
public enum PllPropOperation { Break, SetBreakableEnabled, StartMotion, PauseMotion, ResumeMotion }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Props/Breakable Prop")]
public sealed class PllBreakablePropAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] string IdValue = string.Empty;
    [SerializeField] bool InitiallyEnabledValue = true;
    [SerializeField] GameObject IntactModelValue = null!;
    [SerializeField] Collider[] SupportCollidersValue = Array.Empty<Collider>();
    [SerializeField] PllPropBreakTrigger TriggerValue = PllPropBreakTrigger.Manual;
    [SerializeField, Min(0f)] float ImpactSpeedValue = 5f;
    [SerializeField, Min(1)] int CapacityValue = 1;
    [SerializeField, Min(0f)] float OverloadSecondsValue = 0f;
    [SerializeField] GameObject? BrokenModelValue = null;
    [SerializeField] ParticleSystem? BreakParticlesValue = null;
    [SerializeField] AudioClip? BreakClipValue = null;
    [SerializeField] PllBridgeCapacity BridgeCapacityValue = PllBridgeCapacity.NativeRandom;
    [SerializeField] string EmitHookValue = string.Empty;
    [SerializeField, Min(2)] int NativeMaximumPeopleValue = 5;

    public string Id => IdValue;
    public bool InitiallyEnabled => InitiallyEnabledValue;
    public GameObject IntactModel => IntactModelValue;
    public Collider[] SupportColliders => SupportCollidersValue;
    public PllPropBreakTrigger Trigger => TriggerValue;
    public float ImpactSpeed => ImpactSpeedValue;
    public int Capacity => CapacityValue;
    public float OverloadSeconds => OverloadSecondsValue;
    public GameObject? BrokenModel => BrokenModelValue;
    public ParticleSystem? BreakParticles => BreakParticlesValue;
    public AudioClip? BreakClip => BreakClipValue;
    public PllBridgeCapacity BridgeCapacity => BridgeCapacityValue;
    public int NativeMaximumPeople => NativeMaximumPeopleValue;
    public string EmitHook => EmitHookValue ?? string.Empty;
}
