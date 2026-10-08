using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Biomes/Biome Anchors")]
public sealed class PllBiomePrefabAnchors : MonoBehaviour
{
    [SerializeField] GameObject floorRoot = null!;
    [SerializeField] GameObject campfireRoot = null!;
    [SerializeField] GameObject? statueRoot = null;
    [SerializeField] Transform startAnchor = null!;
    [SerializeField] GameObject playerFormationRoot = null!;
    [SerializeField] Transform planeAnchor = null!;
    [SerializeField] Transform fogBeginAnchor = null!;
    [SerializeField] Transform fogCutoffAnchor = null!;

    public GameObject FloorRoot => floorRoot;
    public GameObject CampfireRoot => campfireRoot;
    public GameObject? StatueRoot => statueRoot;
    public Transform StartAnchor => startAnchor;
    public GameObject PlayerFormationRoot => playerFormationRoot;
    public Transform PlaneAnchor => planeAnchor;
    public Transform FogBeginAnchor => fogBeginAnchor;
    public Transform FogCutoffAnchor => fogCutoffAnchor;
}
