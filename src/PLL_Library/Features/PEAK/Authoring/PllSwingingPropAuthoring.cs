using UnityEngine;

namespace PLL.Library;

public enum PllPropMotion { VisualPivot, PhysicalPivot, NativeBridgeResponse }
public enum PllBridgeResponseProfile { Native, Custom }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Props/Swinging Prop")]
public sealed class PllSwingingPropAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] string IdValue = string.Empty;
    [SerializeField] PllPropMotion ProfileValue = PllPropMotion.VisualPivot;
    [SerializeField] Transform MotionRootValue = null!;
    [SerializeField] Vector3 AxisValue = Vector3.forward;
    [SerializeField] float CenterDegreesValue = 0f;
    [SerializeField, Min(0f)] float AmplitudeDegreesValue = 15f;
    [SerializeField, Min(0f)] float PeriodSecondsValue = 4f;
    [SerializeField, Range(0f, 1f)] float InitialPhaseValue = 0f;
    [SerializeField] bool InitiallyRunningValue = true;
    [SerializeField] string StartedHookValue = string.Empty;
    [SerializeField] string PausedHookValue = string.Empty;
    [SerializeField] PllBridgeResponseProfile BridgeResponseValue = PllBridgeResponseProfile.Native;
    [SerializeField, Min(0f)] float WarningSecondsValue = 5f;
    [SerializeField] AudioClip? WarningClipValue = null;
    [SerializeField, Min(0f)] float StartShakeDistanceValue = 10f;
    [SerializeField, Min(0f)] float StartShakeAmountValue = 400f;
    [SerializeField, Min(0f)] float ClimbingShakeAmountValue = 240f;
    [SerializeField, Min(0f)] float ClimbingShakeIntervalValue = 0.2f;

    public string Id => IdValue;
    public PllPropMotion Profile => ProfileValue;
    public Transform MotionRoot => MotionRootValue;
    public Vector3 Axis => AxisValue;
    public float CenterDegrees => CenterDegreesValue;
    public float AmplitudeDegrees => AmplitudeDegreesValue;
    public float PeriodSeconds => PeriodSecondsValue;
    public float InitialPhase => InitialPhaseValue;
    public bool InitiallyRunning => InitiallyRunningValue;
    public string StartedHook => StartedHookValue ?? string.Empty;
    public string PausedHook => PausedHookValue ?? string.Empty;
    public PllBridgeResponseProfile BridgeResponse => BridgeResponseValue;
    public float WarningSeconds => WarningSecondsValue;
    public AudioClip? WarningClip => WarningClipValue;
    public float StartShakeDistance => StartShakeDistanceValue;
    public float StartShakeAmount => StartShakeAmountValue;
    public float ClimbingShakeAmount => ClimbingShakeAmountValue;
    public float ClimbingShakeInterval => ClimbingShakeIntervalValue;
}
