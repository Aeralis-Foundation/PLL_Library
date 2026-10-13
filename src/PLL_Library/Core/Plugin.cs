using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

namespace PLL.Library;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin, IInRoomCallbacks, IMatchmakingCallbacks
{
    Harmony? harmony;
    readonly HashSet<IEnumerator> mapRoutines = new();
    readonly HashSet<IEnumerator> AdvancingMapRoutines = new();
    bool teardownStarted;
    static Plugin instance = null!;

    public static ManualLogSource PLLLog { get; private set; } = null!;
    internal static Coroutine StartRoutine(IEnumerator routine) => instance.StartMapRoutine(routine);

    private void Awake()
    {
        PLLLog = Logger;
        try
        {
            FileManager.Initialize(Info.Metadata);
            BiomeRegistration.DiscoverStartupBundles();
            AuthoringRuntime.Initialize(gameObject);
            instance = this;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll(typeof(Plugin).Assembly);
            PhotonNetwork.AddCallbackTarget(this);
            PhotonNetwork.NetworkingClient.StateChanged += OnNetworkStateChanged;
        }
        catch (Exception exception)
        {
            enabled = false;
            try
            {
                Teardown();
            }
            catch (Exception teardownException)
            {
                throw new AggregateException(
                    "PLL Library initialization and rollback both failed.", exception, teardownException);
            }
            throw;
        }
    }

    private void Start()
    {
        try
        {
            LayoutRegistry.Seal();
        }
        catch (Exception Failure)
        {
            enabled = false;
            try
            {
                Teardown();
            }
            catch (Exception TeardownFailure)
            {
                throw new AggregateException(
                    "PLL Library layout registration and rollback both failed.", Failure, TeardownFailure);
            }
            throw;
        }
    }

    void Teardown()
    {
        if (teardownStarted) return;
        teardownStarted = true;
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, () => NativeContentTools.Cancel(
            "The plugin stopped before the native content request completed."));
        CleanupOperations.Capture(ref Failures, () => ReleaseMapOwnership(Map.ReleaseCurrentRun));
        CleanupOperations.Capture(ref Failures, LightVolumeHarmonyAdapter.Shutdown);
        CleanupOperations.Capture(ref Failures, Map.ClearTransientPreflight);
        CleanupOperations.Capture(ref Failures, () => SceneManager.sceneUnloaded -= OnSceneUnloaded);
        CleanupOperations.Capture(ref Failures, () => PhotonNetwork.NetworkingClient.StateChanged -= OnNetworkStateChanged);
        CleanupOperations.Capture(ref Failures, () => PhotonNetwork.RemoveCallbackTarget(this));
        CleanupOperations.Capture(ref Failures, LobbyAdmission.Shutdown);
        CleanupOperations.Capture(ref Failures, PllSettingsSurface.Shutdown);
        CleanupOperations.Capture(ref Failures, MenuVersionMark.Shutdown);
        CleanupOperations.Capture(ref Failures, MainMenuDeveloperBar.Shutdown);
        CleanupOperations.Capture(ref Failures, TerminalLayoutSelector.Shutdown);
        CleanupOperations.Capture(ref Failures, TerminalLayoutSelection.Shutdown);
        CleanupOperations.Capture(ref Failures, () => harmony?.UnpatchSelf());
        harmony = null;
        CleanupOperations.Capture(ref Failures, BiomeRegistration.ClearLiveCatalogs);
        CleanupOperations.Capture(ref Failures, AuthoringRuntime.Shutdown);
        if (instance == this) instance = null!;
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException("PLL Library teardown failed.", Failures);
    }

    Coroutine StartMapRoutine(IEnumerator routine)
    {
        if (routine is null) throw new ArgumentNullException(nameof(routine));
        if (!mapRoutines.Add(routine))
            throw new InvalidOperationException("A map coroutine is already owned by the plugin.");
        try
        {
            return StartCoroutine(ObserveMapRoutine(routine));
        }
        catch
        {
            mapRoutines.Remove(routine);
            throw;
        }
    }

    IEnumerator ObserveMapRoutine(IEnumerator routine)
    {
        try
        {
            while (true)
            {
                bool Moved;
                AdvancingMapRoutines.Add(routine);
                try { Moved = routine.MoveNext(); }
                finally { AdvancingMapRoutines.Remove(routine); }
                if (!Moved || !mapRoutines.Contains(routine)) yield break;
                yield return routine.Current;
            }
        }
        finally
        {
            try { (routine as IDisposable)?.Dispose(); }
            finally { mapRoutines.Remove(routine); }
        }
    }

    void StopMapRoutines()
    {
        StopAllCoroutines();
        if (mapRoutines.Count == 0) return;
        var routines = new List<IEnumerator>(mapRoutines);
        mapRoutines.Clear();
        Exception? failure = null;
        foreach (var routine in routines)
        {
            if (AdvancingMapRoutines.Contains(routine)) continue;
            try
            {
                (routine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException("Multiple map coroutine releases failed.", failure, exception);
            }
        }
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    void ReleaseMapOwnership(Action Release)
    {
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, Release);
        CleanupOperations.Capture(ref Failures, StopMapRoutines);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException("Map ownership release failed.", Failures);
    }

    void ReleaseScene(Scene scene)
    {
        if (Map.OwnsScene(scene.handle)) ReleaseMapOwnership(() => Map.ReleaseScene(scene.handle));
        else Map.ReleaseScene(scene.handle);
    }

    void OnSceneUnloaded(Scene scene)
    {
        if (teardownStarted) return;
        ReleaseScene(scene);
    }

    void OnNetworkStateChanged(ClientState previous, ClientState current)
    {
        if (teardownStarted) return;
        if (Map.CurrentRun is { Releasing: false, Released: false } Run)
            Run.Network.GenerationBarrier.ConnectionStateChanged(current);
        QuicksaveRoomGate.ConnectionStateChanged(current);
        IslandLoadPublication.ConnectionStateChanged(current);
        FinalManifestConsensus.ConnectionStateChanged(current);
    }

    private void OnDestroy() => Teardown();

    private void Update()
    {
        NativeContentTools.Tick();
        IslandLoadPublication.Tick();
        Map.CurrentRun?.Network.DeferredOperations.Tick();
    }

    void IInRoomCallbacks.OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        if (teardownStarted) return;
        IslandLoadPublication.RoomPropertiesChanged(propertiesThatChanged);
        FinalManifestConsensus.MaintainCurrent();
    }

    void IInRoomCallbacks.OnMasterClientSwitched(PhotonPlayer newMasterClient)
    {
        if (teardownStarted) return;
        IslandLoadPublication.MasterClientChanged();
        FinalManifestConsensus.MaintainCurrent();
        if (newMasterClient.IsLocal) TerminalLayoutSelection.Reset();
        else TerminalLayoutSelection.RefreshAuthority();
    }

    void IInRoomCallbacks.OnPlayerEnteredRoom(PhotonPlayer newPlayer)
    {
        if (teardownStarted) return;
        FinalManifestConsensus.MaintainCurrent();
    }

    void IInRoomCallbacks.OnPlayerLeftRoom(PhotonPlayer otherPlayer)
    {
        if (teardownStarted) return;
        Map.CurrentRun?.Network.ViewBatches.PlayerLeft(otherPlayer);
        FinalManifestConsensus.MaintainCurrent();
    }

    void IInRoomCallbacks.OnPlayerPropertiesUpdate(PhotonPlayer targetPlayer, PhotonHashtable changedProps) { }

    void IMatchmakingCallbacks.OnFriendListUpdate(List<FriendInfo> friendList) { }
    void IMatchmakingCallbacks.OnCreatedRoom()
    {
        if (teardownStarted) return;
        TerminalLayoutSelection.Reset();
    }

    void IMatchmakingCallbacks.OnCreateRoomFailed(short returnCode, string message)
    {
        if (teardownStarted) return;
        QuicksaveRoomGate.MatchmakingFailed("room creation", returnCode, message);
    }

    void IMatchmakingCallbacks.OnJoinedRoom()
    {
        if (teardownStarted) return;
        LobbyAdmission.RoomJoined();
        if (PhotonNetwork.IsMasterClient) TerminalLayoutSelection.Reset();
    }

    void IMatchmakingCallbacks.OnJoinRoomFailed(short returnCode, string message)
    {
        if (teardownStarted) return;
        QuicksaveRoomGate.MatchmakingFailed("room join", returnCode, message);
    }

    void IMatchmakingCallbacks.OnJoinRandomFailed(short returnCode, string message)
    {
        if (teardownStarted) return;
        QuicksaveRoomGate.MatchmakingFailed("random room join", returnCode, message);
    }

    void IMatchmakingCallbacks.OnLeftRoom()
    {
        if (teardownStarted) return;
        IslandLoadPublication.RoomLeft();
        Map.ClearTransientPreflight();
        FinalManifestConsensus.Reset();
        AscentUIStartPatch.ResetResolved();
        QuicksaveRoomGate.RoomLeft();
        TerminalLayoutSelection.Reset();
    }

    [HarmonyPatch(typeof(NetworkConnector), nameof(NetworkConnector.HandleConnectionState))]
    static class DisconnectingPatch
    {
        [HarmonyPrefix]
        static void Prefix(ConnectionState state)
        {
            if (state is DisconnectingState)
                instance.ReleaseMapOwnership(Map.ReleaseCurrentRun);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.LeaveCurrentGame))]
    static class PlayerLeavePatch
    {
        [HarmonyPrefix]
        static void Prefix() => instance.ReleaseMapOwnership(Map.ReleaseCurrentRun);
    }
}
