using UnityEngine;

namespace PLL.Library;

public enum PllRestPointProfile { Permanent, NativeFragileMountedTool }
public enum PllMountedToolModel { Custom, Native }
public enum PllFixturePresentation { None, Custom, Native }
public enum PllRestPointPlacement { FixedPose, GeneratedTerrain }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Climbing/Rest Point")]
public sealed class PllRestPointAuthoring : MonoBehaviour
{
    [SerializeField] string IdValue = string.Empty;
    [SerializeField] GameObject ModelRootValue = null!;
    [SerializeField] Transform HoldAnchorValue = null!;
    [SerializeField] Vector3 InteractionCenterValue = Vector3.zero;
    [SerializeField] Vector3 InteractionSizeValue = new(0.35f, 0.35f, 0.35f);
    [SerializeField] PllRestPointProfile ProfileValue = PllRestPointProfile.Permanent;
    [SerializeField] PllMountedToolModel MountedModelValue = PllMountedToolModel.Custom;
    [SerializeField] GameObject CrackRootValue = null!;
    [SerializeField] GameObject BreakVfxRootValue = null!;
    [SerializeField] PllFixturePresentation CrackPresentationValue = PllFixturePresentation.None;
    [SerializeField] PllFixturePresentation BreakPresentationValue = PllFixturePresentation.None;
    [SerializeField] string HangHookValue = string.Empty;
    [SerializeField] string WarningHookValue = string.Empty;
    [SerializeField] string BreakHookValue = string.Empty;
    [SerializeField] PllRestPointPlacement PlacementValue = PllRestPointPlacement.FixedPose;
    [SerializeField] float InsertionOffsetValue = 0f;

    public string Id => IdValue;
    public GameObject ModelRoot => ModelRootValue;
    public Transform HoldAnchor => HoldAnchorValue;
    public Vector3 InteractionCenter => InteractionCenterValue;
    public Vector3 InteractionSize => InteractionSizeValue;
    public PllRestPointProfile Profile => ProfileValue;
    public PllMountedToolModel MountedModel => MountedModelValue;
    public GameObject CrackRoot => CrackRootValue;
    public GameObject BreakVfxRoot => BreakVfxRootValue;
    public PllFixturePresentation CrackPresentation => CrackPresentationValue;
    public PllFixturePresentation BreakPresentation => BreakPresentationValue;
    public string HangHook => HangHookValue;
    public string WarningHook => WarningHookValue;
    public string BreakHook => BreakHookValue;
    public PllRestPointPlacement Placement => PlacementValue;
    public float InsertionOffset => InsertionOffsetValue;
}
