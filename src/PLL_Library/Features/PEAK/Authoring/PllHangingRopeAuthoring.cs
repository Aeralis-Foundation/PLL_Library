using UnityEngine;

namespace PLL.Library;

public enum PllRopeAppearance { Native, CompatibleSkin, AutomaticSkin }
public enum PllRopeAnchorBehavior { Permanent, NativeBreakableAnchor }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Climbing/Hanging Rope")]
public sealed class PllHangingRopeAuthoring : MonoBehaviour
{
    public const int MaximumSegments = 40;

    [SerializeField] string IdValue = string.Empty;
    [SerializeField] GameObject ModelRootValue = null!;
    [SerializeField] GameObject GhostRootValue = null!;
    [SerializeField] Transform AnchorPointValue = null!;
    [SerializeField, Min(1f)] float SegmentsValue = 30f;
    [SerializeField, Min(0f)] float SpoolOutSecondsValue = 5f;
    [SerializeField] PllRopeAppearance RopeAppearanceValue = PllRopeAppearance.Native;
    [SerializeField] Mesh RopeMeshValue = null!;
    [SerializeField] Mesh RopeRestMeshValue = null!;
    [SerializeField] Material RopeMaterialValue = null!;
    [SerializeField] Material RopeGhostMaterialValue = null!;
    [SerializeField] string RopeRendererPathValue = string.Empty;
    [SerializeField] string[] RopeBonePathsValue = System.Array.Empty<string>();
    [SerializeField] string RopeNativeRigFingerprintValue = string.Empty;
    [SerializeField] Bounds RopeBoundsValue = default;
    [SerializeField] Vector3 RopeRestStartValue = Vector3.zero;
    [SerializeField] Vector3 RopeRestEndValue = Vector3.up;
    [SerializeField] PllRopeAnchorBehavior AnchorBehaviorValue = PllRopeAnchorBehavior.Permanent;
    [SerializeField, Min(0f)] float BreakTimeMinimumValue = 3f;
    [SerializeField, Min(0f)] float BreakTimeMaximumValue = 8f;
    [SerializeField, Min(0f)] float BreakAnimationSecondsValue = 3f;
    [SerializeField, Min(0f)] float DropSegmentsValue = 1f;
    [SerializeField] string WarningHookValue = string.Empty;
    [SerializeField] string BreakHookValue = string.Empty;

    public string Id => IdValue;
    public GameObject ModelRoot => ModelRootValue;
    public GameObject GhostRoot => GhostRootValue;
    public Transform AnchorPoint => AnchorPointValue;
    public float Segments => SegmentsValue;
    public float SpoolOutSeconds => SpoolOutSecondsValue;
    public PllRopeAppearance RopeAppearance => RopeAppearanceValue;
    public Mesh RopeMesh => RopeMeshValue;
    public Mesh RopeRestMesh => RopeRestMeshValue;
    public Material RopeMaterial => RopeMaterialValue;
    public Material RopeGhostMaterial => RopeGhostMaterialValue;
    public string RopeRendererPath => RopeRendererPathValue;
    public string[] RopeBonePaths => RopeBonePathsValue;
    public string RopeNativeRigFingerprint => RopeNativeRigFingerprintValue;
    public Bounds RopeBounds => RopeBoundsValue;
    public Vector3 RopeRestStart => RopeRestStartValue;
    public Vector3 RopeRestEnd => RopeRestEndValue;
    public PllRopeAnchorBehavior AnchorBehavior => AnchorBehaviorValue;
    public float BreakTimeMinimum => BreakTimeMinimumValue;
    public float BreakTimeMaximum => BreakTimeMaximumValue;
    public float BreakAnimationSeconds => BreakAnimationSecondsValue;
    public float DropSegments => DropSegmentsValue;
    public string WarningHook => WarningHookValue;
    public string BreakHook => BreakHookValue;
}
