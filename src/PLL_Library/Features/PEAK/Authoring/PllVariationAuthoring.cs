using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllVariationEntry
{
    [SerializeField] GameObject root = null!;
    [SerializeField, Range(0f, 100f)] float weight = 1f;
    [SerializeField, HideInInspector] string EntryIdValue = "";

    public GameObject Root => root;
    public float Weight => weight;
    public string EntryId => EntryIdValue ?? "";
}

[AddComponentMenu("PLL/Variations/Variation Group")]
public sealed class PllVariationAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
    [SerializeField] string id = "";
#if PLL_CREATOR_SDK
    [SerializeField] PllVariationEntry[] entries = Array.Empty<PllVariationEntry>();
#else
    PllVariationEntry[]? PreparedEntries;
#endif
    [SerializeReference, HideInInspector] object[]? BundleEntries = null;

    public string Id => id ?? "";
#if PLL_CREATOR_SDK
    public IReadOnlyList<PllVariationEntry> Entries => entries ?? Array.Empty<PllVariationEntry>();

    internal void SealBundleSerialization() => BundleEntries =
        PllBundleSerialization.SealArray(entries ?? Array.Empty<PllVariationEntry>());
#else
    public IReadOnlyList<PllVariationEntry> Entries => PreparedEntries ??=
        PllBundleSerialization.ReadArray<PllVariationEntry>(BundleEntries, "variation entries");

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize() => PreparedEntries = null;
#endif
}
