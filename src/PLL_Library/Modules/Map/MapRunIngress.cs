using System.Collections.Generic;
using Photon.Realtime;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapRunIngress
{
    internal MapQuicksave.QuicksaveLayout? LoadedQuicksaveLayout { get; set; }
    internal string? LoadedQuicksaveSceneName { get; set; }
    internal MapHandler? SpawnHistoryOwner { get; set; }
    internal Dictionary<Hash128, List<Peak.SpawnedItemTracker.SpawnRecord>>? SavedSpawnHistories { get; set; }
    internal FakeItemManager? DeferredFakeItemOwner { get; set; }
    internal byte[]? DeferredFakeItemSync { get; set; }
    internal Room? DeferredFakeItemRoom { get; set; }
    internal bool ReplayingFakeItemSync { get; set; }

    internal void ClearScene(int SceneHandle)
    {
        if (!SpawnHistoryOwner || SpawnHistoryOwner.gameObject.scene.handle == SceneHandle) ClearSpawnHistory();
        if (!DeferredFakeItemOwner || DeferredFakeItemOwner.gameObject.scene.handle == SceneHandle) ClearFakeItemSync();
    }

    internal void ClearTransient()
    {
        LoadedQuicksaveLayout = null;
        LoadedQuicksaveSceneName = null;
        ClearSpawnHistory();
        ClearFakeItemSync();
    }

    internal void ClearSpawnHistory()
    {
        SpawnHistoryOwner = null;
        SavedSpawnHistories = null;
    }

    internal void ClearFakeItemSync()
    {
        DeferredFakeItemOwner = null;
        DeferredFakeItemSync = null;
        DeferredFakeItemRoom = null;
        ReplayingFakeItemSync = false;
    }
}