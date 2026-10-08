using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using IEnumerator = System.Collections.IEnumerator;

namespace PLL.Library;

internal static class IslandLoadPublication
{
    static Publication? Pending;
    static Publication? Releasing;
    static ProcessingSceneLoad? ProcessingLoad;

    internal static bool ConsumeOnlineStartupIntent() =>
        ProcessingLoad?.ConsumeStartupIntent() == true;

    internal static void RoomLeft() => ProcessingLoad?.LeaveRoom();

    internal static void CancelCurrentLoad()
    {
        var Handler = LoadingScreenHandler.Instance;
        var Screen = Handler._lastActiveLoadingScreen;
        Handler.StopAllCoroutines();
        if (Screen) Screen.StopAllCoroutines();
        try { (Screen ? Screen.currentProcess as IDisposable : null)?.Dispose(); }
        finally
        {
            if (Screen) UnityEngine.Object.Destroy(Screen.gameObject);
            Handler._lastActiveLoadingScreen = null;
            LoadingScreenHandler.loading = false;
            PhotonNetwork.IsMessageQueueRunning = true;
        }
    }

    internal static bool IsOnlineStartupState(ConnectionState State) =>
        State is JoinSpecificRoomState or HostState or InRoomState;

    internal sealed class Publication
    {
        internal Publication(
            Room Room,
            AirportCheckInKiosk? Kiosk,
            int Ascent,
            byte[]? SerializedRunSettings,
            string Payload,
            Hashtable Properties,
            NativeContentTools.StartupTicket? StartupTicket)
        {
            this.Room = Room;
            this.Kiosk = Kiosk;
            this.Ascent = Ascent;
            this.SerializedRunSettings = SerializedRunSettings;
            this.Payload = Payload;
            this.Properties = Properties;
            this.StartupTicket = StartupTicket;
        }

        internal Room Room { get; }
        internal AirportCheckInKiosk? Kiosk { get; }
        internal int Ascent { get; }
        internal byte[]? SerializedRunSettings { get; }
        internal string Payload { get; }
        internal Hashtable Properties { get; }
        internal NativeContentTools.StartupTicket? StartupTicket { get; }
        internal bool Confirmed { get; set; }
        internal string? Failure { get; set; }
    }

    internal static Publication PublishQuicksave(CapturedLayoutSelection Selection) =>
        Publish(null, 0, null, Selection) ?? throw new InvalidOperationException(
            "PLL Library cannot publish a room configuration without host authority.");

    internal static bool Owns(Publication Request) => ReferenceEquals(Pending, Request);

    internal static bool Consume(Publication Request)
    {
        if (!ReferenceEquals(Pending, Request)) return false;
        Pending = null;
        return true;
    }

    internal static void Cancel(Publication Request)
    {
        if (ReferenceEquals(Pending, Request)) Pending = null;
    }

    internal static void Fail(Publication Request, string Message)
    {
        if (!ReferenceEquals(Pending, Request)) return;
        Request.Failure = Message;
        Pending = null;
        NativeContentTools.StartupFailed(Request.StartupTicket, new InvalidOperationException(Message));
        Plugin.PLLLog.LogError(Message);
    }

    internal static bool HasServerEcho(Publication Request)
    {
        foreach (var Key in Request.Properties.Keys)
            if (!Request.Room.CustomProperties.TryGetValue(Key, out var Actual)
                || !PhotonPropertyProtocol.SameValue(Actual, Request.Properties[Key])) return false;
        foreach (var Key in Request.Room.CustomProperties.Keys)
            if (Key is string Name
                && Name.StartsWith(PhotonPropertyProtocol.StatePrefix, StringComparison.Ordinal)
                && Request.Room.CustomProperties[Key] is not object[] { Length: 0 }) return false;
        return true;
    }

    internal static void Tick()
    {
        var Request = Pending;
        if (Request is null) return;
        if (!ReferenceEquals(PhotonNetwork.CurrentRoom, Request.Room) || !PhotonNetwork.IsMasterClient)
        {
            Fail(Request, "PLL Library cancelled the pending room configuration because room authority changed.");
            return;
        }
        TryConfirm(Request);
    }

    internal static void RoomPropertiesChanged(Hashtable PropertiesThatChanged)
    {
        if (Pending is not { } Request) return;
        if (!ReferenceEquals(PhotonNetwork.CurrentRoom, Request.Room) || !PhotonNetwork.IsMasterClient)
        {
            Fail(Request, "PLL Library cancelled the pending room configuration because room authority changed.");
            return;
        }
        if (PropertiesThatChanged.TryGetValue(RunConfigurationCodec.PropertyKey, out var Raw)
            && Raw is string Payload && string.Equals(Payload, Request.Payload, StringComparison.Ordinal))
            TryConfirm(Request);
    }

    internal static void ConnectionStateChanged(ClientState Current)
    {
        if (Pending is { } Request && !Request.Room.IsOffline && Current != ClientState.Joined)
            Fail(Request, "PLL Library cancelled the pending room configuration because the room connection changed.");
    }

    internal static void MasterClientChanged()
    {
        if (Pending is { } Request)
            Fail(Request, "PLL Library cancelled the pending room configuration because the master client changed.");
    }

    static Publication? Publish(
        AirportCheckInKiosk? Kiosk,
        int Ascent,
        byte[]? SerializedRunSettings,
        CapturedLayoutSelection? RestoredSelection = null)
    {
        var Room = PhotonNetwork.CurrentRoom;
        if (Room is null || !PhotonNetwork.IsMasterClient) return null;
        if (Pending is not null)
            throw new InvalidOperationException("PLL Library already has a pending room configuration.");
        SegmentRegistry.SealStartup();
        Publication? Request = null;
        NativeContentTools.StartupTicket? StartupTicket = null;
        try
        {
            var StartupOverride = RestoredSelection is null && Kiosk != null && SerializedRunSettings is not null
                ? NativeContentTools.BeginStartup(Ascent, SerializedRunSettings, out StartupTicket) : null;
            var Configuration = RunConfigurationCodec.CaptureLocal(RestoredSelection, StartupOverride);
            NativeContentTools.ConfigurationCaptured(StartupTicket, Configuration);
            if (Kiosk != null && SerializedRunSettings is not null
                && MapQuicksave.RuntimeLayoutPublicationRequired(Configuration) && !MapQuicksave.ClearBufferedCampfireRpcs())
                throw new InvalidOperationException(
                    "PLL Library could not clear stale PEAK campfire state before loading the island.");
            RunConfigurationResolution.AdoptForPublication(Configuration);
            var Payload = RunConfigurationCodec.Serialize(Configuration);
            var Properties = new Hashtable { [RunConfigurationCodec.PropertyKey] = Payload };
            foreach (var Key in Room.CustomProperties.Keys)
                if (Key is string Name
                    && Name.StartsWith(PhotonPropertyProtocol.StatePrefix, StringComparison.Ordinal))
                    Properties[Key] = Array.Empty<object>();

            Request = new Publication(Room, Kiosk, Ascent, SerializedRunSettings, Payload, Properties, StartupTicket);
            Pending = Request;
            if (Room.SetCustomProperties(Properties)) return Request;
            throw new InvalidOperationException("PLL Library could not publish the room configuration.");
        }
        catch (Exception Failure)
        {
            if (Request is not null) Cancel(Request);
            NativeContentTools.StartupFailed(StartupTicket, Failure);
            throw;
        }
    }

    static bool TryConfirm(Publication Request)
    {
        if (ReferenceEquals(Releasing, Request)) return true;
        if (Request.StartupTicket is { } StartupTicket && !NativeContentTools.Owns(StartupTicket))
        {
            Cancel(Request);
            return false;
        }
        if (!HasServerEcho(Request)) return false;
        if (Request.Kiosk is null)
        {
            Request.Confirmed = true;
            return true;
        }
        if (Request.SerializedRunSettings is null || LoadingScreenHandler.loading)
        {
            Fail(
                Request,
                "PLL Library cancelled the pending island load because the airport load target is unavailable.");
            return false;
        }

        if (!LobbyAdmission.CanStart(Request.Room))
        {
            Cancel(Request);
            NativeContentTools.StartupFailed(Request.StartupTicket,
                new InvalidOperationException("The room could not admit the requested native content run."));
            return false;
        }

        Releasing = Request;
        try
        {
            Request.Kiosk.LoadIslandMaster(Request.Ascent, Request.SerializedRunSettings);
            Cancel(Request);
            return true;
        }
        catch (Exception Failure)
        {
            Cancel(Request);
            NativeContentTools.StartupFailed(Request.StartupTicket, Failure);
            throw;
        }
        finally
        {
            Releasing = null;
        }
    }

    sealed class ProcessingSceneLoad : IEnumerator, IDisposable
    {
        readonly IEnumerator Load;
        readonly Room? Room;
        MapLoadKeepAliveLease? Lease;
        IEnumerator? ServicedLoad;
        bool StartupIntent = true;
        bool RoomLost;
        bool Disposed;

        internal ProcessingSceneLoad(IEnumerator Load, Room? Room)
        {
            this.Load = Load;
            this.Room = Room;
        }

        public object Current => ServicedLoad?.Current!;
        internal bool HasJoinedRoom => Room is not null;

        internal bool ConsumeStartupIntent()
        {
            if (!StartupIntent || Disposed) return false;
            StartupIntent = false;
            return true;
        }

        internal void LeaveRoom()
        {
            StartupIntent = false;
            RoomLost = true;
        }

        internal bool MustWaitForCharacter(
            Room? CurrentRoom,
            bool HasCharacter) =>
            !Disposed && !RoomLost && ReferenceEquals(CurrentRoom, Room)
            && !HasCharacter;

        internal IEnumerator WaitForCharacterSpawn()
        {
            while (MustWaitForCharacter(
                PhotonNetwork.CurrentRoom,
                Character.localCharacter)) yield return null;
        }

        public bool MoveNext()
        {
            if (Disposed) return false;
            try
            {
                if (ServicedLoad is null)
                {
                    if (Room is not null)
                    {
                        Lease = MapLoadKeepAliveLease.Acquire();
                        ServicedLoad = SuspendedPhotonTransport.SendOutgoingAtYields(Load, Room);
                    }
                    else ServicedLoad = Load;
                }
                if (ServicedLoad.MoveNext()) return true;
                Dispose();
                return false;
            }
            catch (Exception Failure)
            {
                try { Dispose(); }
                catch (Exception CleanupFailure)
                {
                    throw new AggregateException(
                        "Processing scene load and iterator cleanup both failed.", Failure, CleanupFailure);
                }
                throw;
            }
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            StartupIntent = false;
            if (ReferenceEquals(ProcessingLoad, this)) ProcessingLoad = null;
            try { ((ServicedLoad ?? Load) as IDisposable)?.Dispose(); }
            finally { Lease?.Dispose(); }
        }
    }

    static IEnumerator SelectCharacterSpawnWait(float Timeout)
    {
        var Load = ProcessingLoad;
        return Load?.HasJoinedRoom == true
            ? Load.WaitForCharacterSpawn()
            : LoadingScreenHandler.WaitForCharacterSpawn(Timeout);
    }

    static string SelectMapBakerScene(string SceneName)
    {
        var Configuration = RunConfigurationResolution.RequireCurrent();
        return MapQuicksave.RuntimeMapProcessingRequired(Configuration) ? MapQuicksave.ProcessingSceneName : SceneName;
    }

    [HarmonyPatch(typeof(LoadingScreenHandler), nameof(LoadingScreenHandler.LoadSceneProcess),
        new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(float) })]
    static class ProcessingSceneLoadPatch
    {
        [HarmonyPostfix]
        static void Postfix(string sceneName, bool networked, ref IEnumerator __result)
        {
            if (!string.Equals(sceneName, MapQuicksave.ProcessingSceneName, StringComparison.Ordinal)
                || PhotonNetwork.OfflineMode
                || !networked && !IsOnlineStartupState(NetworkConnector.CurrentConnectionState)) return;

            var Room = networked && PhotonNetwork.NetworkClientState == ClientState.Joined
                ? PhotonNetwork.CurrentRoom
                : null;
            var Load = new ProcessingSceneLoad(__result, Room);
            ProcessingLoad = Load;
            __result = Load;
        }
    }

    [HarmonyPatch]
    static class ProcessingCharacterSpawnWaitPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var Factory = AccessTools.DeclaredMethod(
                typeof(LoadingScreenHandler),
                "LoadSceneProcessNetworked",
                new[] { typeof(string), typeof(bool), typeof(float) })
                ?? throw new MissingMethodException(
                    typeof(LoadingScreenHandler).FullName,
                    "LoadSceneProcessNetworked");
            return AccessTools.EnumeratorMoveNext(Factory)
                ?? throw new MissingMethodException(
                    "LoadingScreenHandler.LoadSceneProcessNetworked iterator was not found.");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var NativeWait = ((Func<float, IEnumerator>)
                LoadingScreenHandler.WaitForCharacterSpawn).Method;
            var Selector = ((Func<float, IEnumerator>)SelectCharacterSpawnWait).Method;
            var Replaced = 0;

            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(NativeWait))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Selector;
                    Replaced++;
                }
                yield return Instruction;
            }

            if (Replaced != 1)
                throw new MissingMethodException(
                    $"LoadingScreenHandler.LoadSceneProcessNetworked contained {Replaced} character spawn waits.");
        }
    }

    [HarmonyPatch(typeof(AirportCheckInKiosk), "LoadIslandMaster",
        new Type[] { typeof(int), typeof(byte[]) })]
    static class AirportLoadPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var GetLevel = AccessTools.DeclaredMethod(
                typeof(MapBaker), nameof(MapBaker.GetLevel), new[] { typeof(int) })
                ?? throw new MissingMethodException("MapBaker.GetLevel(int) was not found.");
            var Selector = ((Func<string, string>)SelectMapBakerScene).Method;
            var Replaced = 0;
            foreach (var Instruction in Instructions)
            {
                yield return Instruction;
                if (!Instruction.Calls(GetLevel)) continue;
                yield return new CodeInstruction(OpCodes.Call, Selector);
                Replaced++;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"AirportCheckInKiosk.LoadIslandMaster's MapBaker.GetLevel call was found {Replaced} times.");
        }

        [HarmonyPrefix]
        static bool Prefix(AirportCheckInKiosk __instance, int ascent, byte[] serializedRunSettings)
        {
            var Release = Releasing;
            if (Release is not null)
                return ReferenceEquals(Release.Kiosk, __instance)
                    && Release.Ascent == ascent
                    && ReferenceEquals(Release.SerializedRunSettings, serializedRunSettings);
            if (LoadingScreenHandler.loading || serializedRunSettings is null) return false;

            var Room = PhotonNetwork.CurrentRoom;
            if (Room is null || !PhotonNetwork.IsMasterClient
                || !LobbyAdmission.CanStart(Room)) return false;
            if (Pending is { } PendingRequest)
            {
                Cancel(PendingRequest);
                NativeContentTools.StartupFailed(PendingRequest.StartupTicket,
                    new InvalidOperationException("The pending native content startup was replaced."));
            }
            if (!Room.IsOffline && !Room.BroadcastPropertiesChangeToAll)
            {
                Plugin.PLLLog.LogError(
                    "PLL Library cannot verify room configuration publication without server property echoes.");
                return false;
            }
            return Publish(__instance, ascent, serializedRunSettings) is null;
        }
    }
}
