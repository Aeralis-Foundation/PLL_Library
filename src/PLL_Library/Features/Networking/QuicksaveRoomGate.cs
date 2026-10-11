using System;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using IEnumerator = System.Collections.IEnumerator;

namespace PLL.Library;

internal static class QuicksaveRoomGate
{
    static bool Active;
    static string? Failure;

    internal static void ConnectionStateChanged(ClientState Current)
    {
        if (Active && Failure is null
            && Current is ClientState.Disconnecting or ClientState.Disconnected)
            Failure = "The room connection failed. Quicksave restoration stopped.";
    }

    internal static void MatchmakingFailed(string Operation, short ReturnCode, string Message)
    {
        if (Active)
            Failure = $"Photon rejected quicksave {Operation} ({ReturnCode}): {Message}";
    }

    internal static void RoomLeft()
    {
        if (Active && Failure is null)
            Failure = "The host left the room. Quicksave restoration stopped.";
    }

    static Exception? DisposeLoad(IEnumerator Load, Exception? FailureValue)
    {
        try
        {
            (Load as IDisposable)?.Dispose();
            return FailureValue;
        }
        catch (Exception ExceptionValue)
        {
            return FailureValue is null
                ? ExceptionValue
                : new AggregateException(
                    "Quicksave load and iterator cleanup both failed.", FailureValue, ExceptionValue);
        }
    }

    static bool TryPublish(out IslandLoadPublication.Publication? Request)
    {
        if (!MapQuicksave.TryCaptureQuicksaveLayoutSelection(out var Selection, out var CaptureFailure))
        {
            Request = null;
            Plugin.PLLLog.LogError(
                $"PLL Library cancelled quicksave restoration because {CaptureFailure}");
            return false;
        }
        Request = IslandLoadPublication.PublishQuicksave(Selection);
        return true;
    }

    static bool TryStartRoom(ref bool Started)
    {
        if (Started || PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer) return true;
        if (NetworkConnector.CurrentConnectionState is not HostState Host
            || string.IsNullOrEmpty(Host.RoomName))
        {
            Plugin.PLLLog.LogError(
                "The quicksave host room is unavailable.");
            return false;
        }
        if (!PhotonNetwork.CreateRoom(
            Host.RoomName, Peak.Network.NetworkingUtilities.HostRoomOptions(), null, null))
        {
            Plugin.PLLLog.LogError(
                "The quicksave room could not be created.");
            return false;
        }
        Started = true;
        return true;
    }

    static IEnumerator Gate(IEnumerator Load)
    {
        if (Active)
        {
            Plugin.PLLLog.LogError(
                "Quicksave restoration is already running.");
            if (DisposeLoad(Load, null) is { } DisposeFailure) ExceptionPropagation.Throw(DisposeFailure);
            yield break;
        }

        IslandLoadPublication.Publication? Request = null;
        var HandedOff = false;
        Exception? StagingFailure = null;
        Active = true;
        Failure = null;
        try
        {
            PhotonNetwork.IsMessageQueueRunning = true;
            var EnteredGameServer = false;
            var RoomCreationStarted = false;
            while (PhotonNetwork.CurrentRoom is null
                || PhotonNetwork.NetworkClientState != ClientState.Joined)
            {
                var State = PhotonNetwork.NetworkClientState;
                if (State is ClientState.ConnectingToGameServer
                    or ClientState.ConnectedToGameServer or ClientState.Joining)
                    EnteredGameServer = true;
                if (Failure is { } RoomFailure)
                {
                    Plugin.PLLLog.LogError(RoomFailure);
                    yield break;
                }
                if (!Peak.Quicksave.ShouldUseSaveData || PhotonNetwork.OfflineMode
                    || State is ClientState.Disconnecting or ClientState.Disconnected
                    || EnteredGameServer && State is (ClientState.DisconnectingFromGameServer
                        or ClientState.ConnectingToMasterServer or ClientState.ConnectedToMasterServer))
                {
                    Plugin.PLLLog.LogError(
                        "The host did not join the room. Quicksave restoration stopped.");
                    yield break;
                }
                bool RoomStarted;
                try
                {
                    RoomStarted = TryStartRoom(ref RoomCreationStarted);
                }
                catch (Exception ExceptionValue)
                {
                    StagingFailure = ExceptionValue;
                    throw;
                }
                if (!RoomStarted) yield break;
                yield return null;
            }

            var Room = PhotonNetwork.CurrentRoom;
            if (!Peak.Quicksave.ShouldUseSaveData || Room is null || Room.IsOffline
                || !PhotonNetwork.IsMasterClient
                || PhotonNetwork.NetworkClientState != ClientState.Joined)
            {
                Plugin.PLLLog.LogError(
                    "The host no longer controls the room. Quicksave restoration stopped.");
                yield break;
            }
            if (!Room.BroadcastPropertiesChangeToAll)
            {
                Plugin.PLLLog.LogError(
                    "The server does not confirm room settings. Quicksave restoration stopped.");
                yield break;
            }

            bool Published;
            try
            {
                Published = TryPublish(out Request);
            }
            catch (Exception ExceptionValue)
            {
                StagingFailure = ExceptionValue;
                throw;
            }
            if (!Published || Request is null) yield break;
            while (!Request.Confirmed && Failure is null && Request.Failure is null
                && IslandLoadPublication.Owns(Request))
                yield return null;
            if (Failure is { } PublicationFailure)
            {
                Plugin.PLLLog.LogError(PublicationFailure);
                yield break;
            }

            if (!IslandLoadPublication.Owns(Request)
                || !ReferenceEquals(PhotonNetwork.CurrentRoom, Request.Room)
                || !Request.Confirmed || Request.Failure is not null
                || !Peak.Quicksave.ShouldUseSaveData || !PhotonNetwork.IsMasterClient
                || PhotonNetwork.NetworkClientState != ClientState.Joined
                || !IslandLoadPublication.HasServerEcho(Request))
            {
                IslandLoadPublication.Fail(
                    Request,
                    "The host or room settings changed. Quicksave restoration stopped.");
                if (Request.Failure is null)
                    Plugin.PLLLog.LogError(
                        "The confirmed quicksave room settings changed.");
                yield break;
            }
            if (!IslandLoadPublication.Consume(Request))
            {
                Plugin.PLLLog.LogError(
                    "The quicksave room settings are no longer available for loading.");
                yield break;
            }
            HandedOff = true;
        }
        finally
        {
            if (Request is not null && !HandedOff) IslandLoadPublication.Cancel(Request);
            if (!HandedOff)
            {
                Active = false;
                Failure = null;
            }
            PhotonNetwork.IsMessageQueueRunning = !HandedOff;
            if (!HandedOff && DisposeLoad(Load, StagingFailure) is { } CleanupFailure
                && !ReferenceEquals(CleanupFailure, StagingFailure)) ExceptionPropagation.Throw(CleanupFailure);
        }

        var Completed = false;
        Exception? LoadFailure = null;
        try
        {
            while (true)
            {
                object? Current = null;
                bool Moved;
                try
                {
                    Moved = Load.MoveNext();
                    if (Moved) Current = Load.Current;
                }
                catch (Exception ExceptionValue)
                {
                    LoadFailure = ExceptionValue;
                    break;
                }
                if (!Moved)
                {
                    Completed = true;
                    break;
                }
                yield return Current;
            }
        }
        finally
        {
            Active = false;
            Failure = null;
            if (!Completed) PhotonNetwork.IsMessageQueueRunning = true;
            if (DisposeLoad(Load, LoadFailure) is { } CleanupFailure) ExceptionPropagation.Throw(CleanupFailure);
        }
    }

    [HarmonyPatch(typeof(LoadingScreenHandler), nameof(LoadingScreenHandler.LoadSceneProcess),
        new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(float) })]
    static class LoadPatch
    {
        [HarmonyPostfix]
        static void Postfix(bool networked, ref IEnumerator __result)
        {
            if (networked && Peak.Quicksave.ShouldUseSaveData) __result = Gate(__result);
        }
    }
}
