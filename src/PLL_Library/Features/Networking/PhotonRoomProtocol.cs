using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace PLL.Library;

internal static class PhotonRoomRejection
{
    internal static InvalidOperationException Disconnect(string Message)
    {
        PhotonNetwork.Disconnect();
        return new InvalidOperationException(Message);
    }
}

internal sealed class RoomPropertyPublication
{
    Room? PendingRoom;
    string? PendingKey;
    object[]? PendingValue;

    internal string? Failure { get; private set; }

    internal bool AwaitEcho(Room Room, string Key, object[] Value, string FailureMessage)
    {
        if (PhotonPropertyProtocol.SameValue(Room.CustomProperties[Key], Value))
        {
            Reset();
            return true;
        }
        if (!ReferenceEquals(PhotonNetwork.CurrentRoom, Room)
            || !Room.IsOffline && PhotonNetwork.NetworkClientState != ClientState.Joined)
        {
            Failure = "The Photon room changed before the synchronized property was confirmed.";
            return false;
        }
        if (ReferenceEquals(PendingRoom, Room) && string.Equals(PendingKey, Key, StringComparison.Ordinal)
            && PendingValue is not null && PhotonPropertyProtocol.SameValue(PendingValue, Value)) return false;

        PendingRoom = Room;
        PendingKey = Key;
        PendingValue = Value;
        Failure = null;
        if (!Room.SetCustomProperties(new Hashtable { [Key] = Value }))
        {
            Failure = FailureMessage;
            return false;
        }
        if (PhotonPropertyProtocol.SameValue(Room.CustomProperties[Key], Value))
        {
            Reset();
            return true;
        }
        return false;
    }

    internal void Reset()
    {
        PendingRoom = null;
        PendingKey = null;
        PendingValue = null;
        Failure = null;
    }
}
