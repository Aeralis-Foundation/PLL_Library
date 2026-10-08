using System;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;

namespace PLL.Library;

internal static class SuspendedPhotonTransport
{
    internal static IEnumerator SendOutgoingAtYields(IEnumerator Process, Room? JoinedRoom)
    {
        try
        {
            while (Process.MoveNext())
            {
                var Yielded = Process.Current;
                if (Yielded is IEnumerator Nested)
                {
                    yield return SendOutgoingAtYields(Nested, JoinedRoom);
                    continue;
                }

                if (!PhotonNetwork.OfflineMode && !PhotonNetwork.IsMessageQueueRunning)
                {
                    var CurrentRoom = PhotonNetwork.CurrentRoom;
                    var State = PhotonNetwork.NetworkClientState;
                    var IsExpectedConnection = JoinedRoom is null
                        ? CurrentRoom is null && State is (ClientState.ConnectingToGameServer
                            or ClientState.ConnectedToGameServer or ClientState.Joining)
                        : ReferenceEquals(CurrentRoom, JoinedRoom) && State == ClientState.Joined;
                    if (IsExpectedConnection)
                        _ = PhotonNetwork.NetworkingClient.LoadBalancingPeer.SendOutgoingCommands();
                }
                yield return Yielded;
            }
        }
        finally
        {
            (Process as IDisposable)?.Dispose();
        }
    }
}