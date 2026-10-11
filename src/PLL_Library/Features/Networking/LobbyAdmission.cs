using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Steamworks;
using Unity.Collections;
using Zorro.Core.Serizalization;
using Zorro.UI.Modal;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace PLL.Library;

internal static class LobbyAdmission
{
    const string MarkerKey = PLLLibrary.PluginGuid + "/admission";
    const string ProofKey = MarkerKey + "/proof";
    const byte RequestMessageType = 3;
    const byte ResponseMessageType = 4;
    const byte Protocol = 2;
    static readonly string MarkerValue = Protocol.ToString(CultureInfo.InvariantCulture);
    const int RequestLength = 18;
    const int ResponseHeaderLength = 30;
    const int ProofLength = 9;
    const int MessageCapacity = 1024;
    const string WaitingForAirportMembers =
        "Wait for every player to join the airport before starting the island.";

    static Callback<LobbyChatUpdate_t>? LobbyChanges;
    static AdmissionNonce LocalNonce;
    static ulong CurrentLobbyId;
    static ulong RejectedLobbyId;
    static RoomRequestLifecycle RequestState;
    static bool ProofFailureLogged;
    static bool ResponseFailureLogged;
    static string? LastStartFailure;

    internal static bool CanStart(Room Room)
    {
        if (Room.IsOffline)
        {
            LastStartFailure = null;
            return true;
        }
        if (!SteamManager.Initialized || CurrentLobbyId == 0 || LocalNonce.IsZero)
            return BlockStart(
                "The Steam lobby is unavailable. Reconnect through Steam before starting the island.");

        var Lobby = new CSteamID(CurrentLobbyId);
        if (!IsAdmissionLobby(Lobby))
            return BlockStart(
                "The lobby does not support PLL Library. Create a new lobby before starting the island.");
        var MemberCount = SteamMatchmaking.GetNumLobbyMembers(Lobby);
        var SteamMembers = new HashSet<ulong>(MemberCount);
        for (var Index = 0; Index < MemberCount; Index++)
        {
            var Member = SteamMatchmaking.GetLobbyMemberByIndex(Lobby, Index);
            if (!HasMarker(Lobby, Member))
                return BlockStart(
                    "A lobby member has no compatible PLL Library version. Install the same version on every player.");
            SteamMembers.Add(Member.m_SteamID);
        }

        var CoveredMembers = new HashSet<ulong>(Room.Players.Count);
        foreach (var Player in Room.Players.Values)
        {
            if (!TryReadProof(Player.CustomProperties[ProofKey], out var SourceMember))
                return BlockStart(WaitingForAirportMembers);
            if (!SteamMembers.Contains(SourceMember))
                return BlockStart(
                    "An airport player is not in the Steam lobby. Rejoin the lobby before starting the island.");
            CoveredMembers.Add(SourceMember);
        }

        foreach (var Member in SteamMembers)
            if (!CoveredMembers.Contains(Member))
                return BlockStart(WaitingForAirportMembers);

        LastStartFailure = null;
        return true;
    }

    internal static void RoomJoined() => TryPublishProof();

    internal static void Shutdown() => Reset();

    static bool BlockStart(string Message)
    {
        if (string.Equals(LastStartFailure, Message, StringComparison.Ordinal)) return false;
        LastStartFailure = Message;
        Plugin.PLLLog.LogError(Message);
        ShowModal(Message);
        return false;
    }

    static void ShowModal(string Message) => Modal.OpenModal(
        new DefaultHeaderModalOption("PLL Library", Message),
        new ModalButtonsOption(new[] { new ModalButtonsOption.Option("OK", null) }),
        null);

    static void ShowIncompatibleHost(CSteamID Lobby)
    {
        if (RejectedLobbyId == Lobby.m_SteamID) return;
        RejectedLobbyId = Lobby.m_SteamID;
        ShowModal(
            "The host has no compatible PLL Library version. Join a host with the same version.");
    }

    static void Attach(CSteamID Lobby, bool PublishGlobalMarker)
    {
        if (!PublishGlobalMarker && !IsAdmissionLobby(Lobby)) return;

        if (CurrentLobbyId != Lobby.m_SteamID)
        {
            Reset();
            CurrentLobbyId = Lobby.m_SteamID;
            LocalNonce = AdmissionNonce.Create();
            LobbyChanges = Callback<LobbyChatUpdate_t>.Create(LobbyChanged);
        }

        if (PublishGlobalMarker
            && !SteamMatchmaking.SetLobbyData(Lobby, MarkerKey, MarkerValue))
        {
            Plugin.PLLLog.LogError(
                "The lobby could not enable PLL Library. Create a new lobby before starting the island.");
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(Lobby, MarkerKey, MarkerValue);
        TryPublishProof();
    }

    static void Reset()
    {
        var Changes = LobbyChanges;
        LobbyChanges = null;
        LocalNonce = default;
        CurrentLobbyId = 0;
        RejectedLobbyId = 0;
        RequestState = RoomRequestLifecycle.None;
        ProofFailureLogged = false;
        ResponseFailureLogged = false;
        LastStartFailure = null;
        Changes?.Dispose();
    }

    static void LobbyChanged(LobbyChatUpdate_t Change)
    {
        if (CurrentLobbyId != Change.m_ulSteamIDLobby) return;
        RetryRoomRequest();
    }

    static bool IsAdmissionLobby(CSteamID Lobby) =>
        SteamManager.Initialized
        && string.Equals(
            SteamMatchmaking.GetLobbyData(Lobby, MarkerKey),
            MarkerValue,
            StringComparison.Ordinal);

    static bool HasMarker(CSteamID Lobby, CSteamID Member) =>
        string.Equals(
            SteamMatchmaking.GetLobbyMemberData(Lobby, Member, MarkerKey),
            MarkerValue,
            StringComparison.Ordinal);

    static bool IsMember(CSteamID Lobby, ulong MemberId)
    {
        var MemberCount = SteamMatchmaking.GetNumLobbyMembers(Lobby);
        for (var Index = 0; Index < MemberCount; Index++)
            if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, Index).m_SteamID == MemberId) return true;
        return false;
    }

    static byte[] EncodeRequest(AdmissionNonce Nonce)
    {
        var Payload = new byte[RequestLength];
        Payload[0] = RequestMessageType;
        Payload[1] = Protocol;
        Nonce.Write(Payload.AsSpan(2, 16));
        return Payload;
    }

    static bool TryReadRequest(byte[] Payload, int Length, out AdmissionNonce Nonce)
    {
        Nonce = default;
        if (Length != RequestLength
            || Payload[0] != RequestMessageType
            || Payload[1] != Protocol) return false;
        Nonce = AdmissionNonce.Read(Payload.AsSpan(2, 16));
        return !Nonce.IsZero;
    }

    static byte[]? EncodeResponse(
        AdmissionNonce Nonce,
        ulong SourceMember,
        string RoomName)
    {
        var RoomNameLength = RoomName.Length;
        if (RoomNameLength == 0
            || RoomNameLength > MessageCapacity - ResponseHeaderLength) return null;
        for (var Index = 0; Index < RoomNameLength; Index++)
            if (RoomName[Index] > 0x7f) return null;

        var Payload = new byte[ResponseHeaderLength + RoomNameLength];
        Payload[0] = ResponseMessageType;
        Payload[1] = Protocol;
        Nonce.Write(Payload.AsSpan(2, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(
            Payload.AsSpan(18, 8),
            SourceMember);
        BinaryPrimitives.WriteInt32LittleEndian(
            Payload.AsSpan(26, 4),
            RoomNameLength);
        for (var Index = 0; Index < RoomNameLength; Index++)
            Payload[ResponseHeaderLength + Index] = (byte)RoomName[Index];
        return Payload;
    }

    static bool TryReadResponse(
        byte[] Payload,
        int Length,
        out AdmissionNonce Nonce,
        out ulong SourceMember)
    {
        Nonce = default;
        SourceMember = 0;
        if (Length <= ResponseHeaderLength
            || Length > MessageCapacity
            || Payload[0] != ResponseMessageType
            || Payload[1] != Protocol) return false;
        var RoomNameLength = BinaryPrimitives.ReadInt32LittleEndian(
            Payload.AsSpan(26, 4));
        if (RoomNameLength != Length - ResponseHeaderLength) return false;
        for (var Index = ResponseHeaderLength; Index < Length; Index++)
            if (Payload[Index] > 0x7f) return false;

        Nonce = AdmissionNonce.Read(Payload.AsSpan(2, 16));
        SourceMember = BinaryPrimitives.ReadUInt64LittleEndian(
            Payload.AsSpan(18, 8));
        return !Nonce.IsZero && SourceMember != 0;
    }

    static byte[] EncodeProof(ulong SourceMember)
    {
        var Proof = new byte[ProofLength];
        Proof[0] = Protocol;
        BinaryPrimitives.WriteUInt64LittleEndian(
            Proof.AsSpan(1, 8),
            SourceMember);
        return Proof;
    }

    static bool TryReadProof(object? Value, out ulong SourceMember)
    {
        SourceMember = 0;
        if (Value is not byte[] { Length: ProofLength } Proof
            || Proof[0] != Protocol) return false;
        SourceMember = BinaryPrimitives.ReadUInt64LittleEndian(
            Proof.AsSpan(1, 8));
        return SourceMember != 0;
    }

    static void TryPublishProof()
    {
        if (CurrentLobbyId == 0
            || LocalNonce.IsZero
            || PhotonNetwork.CurrentRoom is null
            || PhotonNetwork.LocalPlayer is not { } LocalPlayer) return;

        var SourceMember = SteamUser.GetSteamID().m_SteamID;
        if (SourceMember == 0) return;

        var Proof = EncodeProof(SourceMember);
        if (LocalPlayer.CustomProperties[ProofKey] is byte[] Existing
            && Existing.AsSpan().SequenceEqual(Proof)) return;
        if (LocalPlayer.SetCustomProperties(
                new PhotonHashtable { [ProofKey] = Proof })) return;
        if (ProofFailureLogged) return;
        ProofFailureLogged = true;
        Plugin.PLLLog.LogError(
            "The player's lobby membership could not be confirmed. Rejoin the lobby before starting the island.");
    }

    static bool SendRoomRequest(CSteamID Lobby, byte[] NativePayload, int NativeLength)
    {
        if (CurrentLobbyId != Lobby.m_SteamID || LocalNonce.IsZero)
        {
            ShowIncompatibleHost(Lobby);
            return false;
        }

        var Payload = EncodeRequest(LocalNonce);
        var Sent = SteamMatchmaking.SendLobbyChatMsg(
            Lobby,
            Payload,
            Payload.Length);
        RequestState = Sent
            ? RoomRequestLifecycle.Sent
            : RoomRequestLifecycle.None;
        return Sent;
    }

    static void RetryRoomRequest()
    {
        if (RequestState != RoomRequestLifecycle.Sent
            || CurrentLobbyId == 0
            || LocalNonce.IsZero) return;
        var Lobby = new CSteamID(CurrentLobbyId);
        var Payload = EncodeRequest(LocalNonce);
        _ = SteamMatchmaking.SendLobbyChatMsg(
            Lobby,
            Payload,
            Payload.Length);
    }

    static void HandleRoomRequest(
        CSteamID Lobby,
        CSteamID Sender,
        byte[] Payload,
        int Length)
    {
        if (!TryReadRequest(Payload, Length, out var Nonce)
            || !IsMember(Lobby, Sender.m_SteamID)
            || SteamMatchmaking.GetLobbyOwner(Lobby) != SteamUser.GetSteamID()
            || PhotonNetwork.CurrentRoom is not { } Room) return;

        var Response = EncodeResponse(Nonce, Sender.m_SteamID, Room.Name);
        if (Response is not null
            && SteamMatchmaking.SendLobbyChatMsg(
                Lobby,
                Response,
                Response.Length)) return;
        if (ResponseFailureLogged) return;
        ResponseFailureLogged = true;
        Plugin.PLLLog.LogError(
            "The room details could not be sent. The joining player must rejoin the lobby.");
    }

    static void HandleRoomResponse(
        SteamLobbyHandler Handler,
        CSteamID Lobby,
        CSteamID Sender,
        byte[] Payload,
        int Length)
    {
        if (RequestState != RoomRequestLifecycle.Sent
            || Sender != SteamMatchmaking.GetLobbyOwner(Lobby)
            || !TryReadResponse(
                Payload,
                Length,
                out var Nonce,
                out var SourceMember)
            || Nonce != LocalNonce || SourceMember != SteamUser.GetSteamID().m_SteamID) return;

        using var Deserializer = new BinaryDeserializer(Payload, Allocator.Temp);
        _ = Deserializer.ReadByte();
        _ = Deserializer.ReadByte();
        _ = Deserializer.ReadUlong();
        _ = Deserializer.ReadUlong();
        _ = Deserializer.ReadUlong();
        Handler.HandleMessage(
            SteamLobbyHandler.MessageType.RoomID,
            Deserializer,
            Lobby);
        RequestState = RoomRequestLifecycle.Accepted;
    }

    enum RoomRequestLifecycle : byte
    {
        None,
        Sent,
        Accepted
    }

    readonly struct AdmissionNonce : IEquatable<AdmissionNonce>
    {
        internal AdmissionNonce(ulong Low, ulong High)
        {
            this.Low = Low;
            this.High = High;
        }

        internal ulong Low { get; }
        internal ulong High { get; }
        internal bool IsZero => Low == 0 && High == 0;

        internal static AdmissionNonce Create()
        {
            var Bytes = Guid.NewGuid().ToByteArray();
            return Read(Bytes);
        }

        internal static AdmissionNonce Read(ReadOnlySpan<byte> Bytes) => new(
            BinaryPrimitives.ReadUInt64LittleEndian(Bytes[..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(Bytes[8..16]));

        internal void Write(Span<byte> Bytes)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(Bytes[..8], Low);
            BinaryPrimitives.WriteUInt64LittleEndian(Bytes[8..16], High);
        }

        public bool Equals(AdmissionNonce Other) =>
            Low == Other.Low && High == Other.High;

        public override bool Equals(object? Value) =>
            Value is AdmissionNonce Other && Equals(Other);

        public override int GetHashCode() => HashCode.Combine(Low, High);

        public static bool operator ==(AdmissionNonce Left, AdmissionNonce Right) =>
            Left.Equals(Right);

        public static bool operator !=(AdmissionNonce Left, AdmissionNonce Right) =>
            !Left.Equals(Right);
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "JoinLobby", new[] { typeof(CSteamID) })]
    static class JoinLobbyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref CSteamID lobbyID)
        {
            if (IsAdmissionLobby(lobbyID))
            {
                RejectedLobbyId = 0;
                return true;
            }
            ShowIncompatibleHost(lobbyID);
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyCreated", new[] { typeof(LobbyCreated_t) })]
    static class LobbyCreatedPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref LobbyCreated_t param)
        {
            if (param.m_eResult == EResult.k_EResultOK)
                Attach(new CSteamID(param.m_ulSteamIDLobby), true);
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyEnter", new[] { typeof(LobbyEnter_t) })]
    static class LobbyEnteredPatch
    {
        [HarmonyPostfix]
        static void Postfix(SteamLobbyHandler __instance, ref LobbyEnter_t param)
        {
            var Lobby = new CSteamID(param.m_ulSteamIDLobby);
            if (param.m_EChatRoomEnterResponse
                    == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess
                && __instance.LobbySteamId == Lobby)
                Attach(Lobby, false);
            else if (CurrentLobbyId == param.m_ulSteamIDLobby) Reset();
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), nameof(SteamLobbyHandler.RequestPhotonRoomID))]
    static class RoomRequestPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var NativeSend = ((Func<CSteamID, byte[], int, bool>)SteamMatchmaking.SendLobbyChatMsg).Method;
            var AdmissionSend = ((Func<CSteamID, byte[], int, bool>)SendRoomRequest).Method;
            var ReplacementCount = 0;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.opcode == OpCodes.Call
                    && Equals(Instruction.operand, NativeSend))
                {
                    Instruction.operand = AdmissionSend;
                    ReplacementCount++;
                }
                yield return Instruction;
            }
            if (ReplacementCount != 1)
                throw new InvalidOperationException(
                    $"SteamLobbyHandler.RequestPhotonRoomID contains {ReplacementCount} room request sends; expected 1.");
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyDataUpdate", new[] { typeof(LobbyDataUpdate_t) })]
    static class LobbyDataUpdatePatch
    {
        [HarmonyPostfix]
        static void Postfix(ref LobbyDataUpdate_t param)
        {
            if (param.m_bSuccess != 0 && CurrentLobbyId == param.m_ulSteamIDLobby)
                RetryRoomRequest();
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyChat", new[] { typeof(LobbyChatMsg_t) })]
    static class LobbyChatPatch
    {
        [HarmonyPrefix]
        static bool Prefix(SteamLobbyHandler __instance, ref LobbyChatMsg_t param)
        {
            if (CurrentLobbyId == 0
                || LocalNonce.IsZero
                || CurrentLobbyId != param.m_ulSteamIDLobby) return true;

            var Lobby = new CSteamID(CurrentLobbyId);
            var Buffer = new byte[MessageCapacity];
            var Length = SteamMatchmaking.GetLobbyChatEntry(
                Lobby,
                checked((int)param.m_iChatID),
                out var Sender,
                Buffer,
                Buffer.Length,
                out _);
            if (Length <= 0) return false;

            switch (Buffer[0])
            {
                case (byte)SteamLobbyHandler.MessageType.RequestRoomID:
                case (byte)SteamLobbyHandler.MessageType.RoomID:
                    return false;
                case RequestMessageType:
                    HandleRoomRequest(Lobby, Sender, Buffer, Length);
                    return false;
                case ResponseMessageType:
                    HandleRoomResponse(__instance, Lobby, Sender, Buffer, Length);
                    return false;
                default:
                    return true;
            }
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), nameof(SteamLobbyHandler.LeaveLobby))]
    static class LobbyLeftPatch
    {
        [HarmonyPrefix]
        static void Prefix() => Reset();
    }
}
