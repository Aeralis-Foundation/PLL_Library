using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllLuggageContentsMode : byte
{
    NativeProgression = 0,
    NativeLuggage = 1,
    CustomItems = 2
}

public enum PllLuggageRepeatMode : byte
{
    UniqueUntilExhausted = 0,
    AllowRepeats = 1
}

[Serializable]
public sealed class PllLuggageSpawnGroup
{
    [SerializeField] Transform[] PointsValue = Array.Empty<Transform>();
    [SerializeField, Min(1)] int WeightValue = 1;

    public IReadOnlyList<Transform> Points => PointsValue ?? Array.Empty<Transform>();
    public int Weight => WeightValue;
}

[Serializable]
public sealed class PllLuggageItemEntry
{
    [SerializeField] GameObject ItemValue = null!;
    [SerializeField, Min(1)] int WeightValue = 1;

    public GameObject Item => ItemValue;
    public int Weight => WeightValue;
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Props/Luggage")]
public sealed class PllLuggageAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
    [SerializeField] string IdValue = "";
    [SerializeField] string DisplayNameValue = "";
#if PLL_CREATOR_SDK
    [SerializeField] PllLuggageSpawnGroup[] SpawnGroupsValue = Array.Empty<PllLuggageSpawnGroup>();
#else
    PllLuggageSpawnGroup[]? PreparedSpawnGroups;
#endif
    [SerializeReference, HideInInspector] object[]? BundleSpawnGroups = null;
    [SerializeField] RuntimeAnimatorController? AnimatorControllerValue = null;
    [SerializeField, Min(0f)] float OpenTimeValue = 0.5f;
    [SerializeField] GameObject? ActivateOnOpenValue = null;
    [SerializeField] PllLuggageContentsMode ContentsModeValue = PllLuggageContentsMode.NativeProgression;
#if PLL_CREATOR_SDK
    [SerializeField] PllLuggageItemEntry[] CustomItemsValue = Array.Empty<PllLuggageItemEntry>();
#else
    PllLuggageItemEntry[]? PreparedCustomItems;
#endif
    [SerializeReference, HideInInspector] object[]? BundleCustomItems = null;
    [SerializeField] string NativeContentsIdValue = "";
    [SerializeField] PllLuggageRepeatMode RepeatModeValue = PllLuggageRepeatMode.UniqueUntilExhausted;
    [SerializeField, Range(0f, 1f)] float SpawnChanceValue = 1f;
    [SerializeField] PllLuggageAuthoring? ReplacesAuthoredValue = null;
    [SerializeField] string ReplacesNativeIdValue = "";

    public string Id => IdValue ?? "";
    public string DisplayName => DisplayNameValue ?? "";
#if PLL_CREATOR_SDK
    public IReadOnlyList<PllLuggageSpawnGroup> SpawnGroups =>
        SpawnGroupsValue ?? Array.Empty<PllLuggageSpawnGroup>();
#else
    public IReadOnlyList<PllLuggageSpawnGroup> SpawnGroups => PreparedSpawnGroups ??=
        PllBundleSerialization.ReadArray<PllLuggageSpawnGroup>(BundleSpawnGroups, "luggage spawn groups");
#endif
    public RuntimeAnimatorController? AnimatorController => AnimatorControllerValue;
    public float OpenTime => OpenTimeValue;
    public GameObject? ActivateOnOpen => ActivateOnOpenValue;
    public PllLuggageContentsMode ContentsMode => ContentsModeValue;
#if PLL_CREATOR_SDK
    public IReadOnlyList<PllLuggageItemEntry> CustomItems =>
        CustomItemsValue ?? Array.Empty<PllLuggageItemEntry>();
#else
    public IReadOnlyList<PllLuggageItemEntry> CustomItems => PreparedCustomItems ??=
        PllBundleSerialization.ReadArray<PllLuggageItemEntry>(BundleCustomItems, "luggage custom items");
#endif
    public string NativeContentsId => NativeContentsIdValue ?? "";
    public PllLuggageRepeatMode RepeatMode => RepeatModeValue;
    public float SpawnChance => SpawnChanceValue;
    public PllLuggageAuthoring? ReplacesAuthored => ReplacesAuthoredValue;
    public string ReplacesNativeId => ReplacesNativeIdValue ?? "";

#if PLL_CREATOR_SDK
    internal void SealBundleSerialization()
    {
        BundleSpawnGroups = PllBundleSerialization.SealArray(
            SpawnGroupsValue ?? Array.Empty<PllLuggageSpawnGroup>());
        BundleCustomItems = PllBundleSerialization.SealArray(
            CustomItemsValue ?? Array.Empty<PllLuggageItemEntry>());
    }
#else
    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        PreparedSpawnGroups = null; PreparedCustomItems = null;
    }
#endif
}
