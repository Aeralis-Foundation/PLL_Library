using System;
using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class SuspendedPhotonTransport : MonoBehaviour
{
    LoadingScreen Screen = null!;
    PhotonLoadKeepAliveLease? KeepAlive;

    void Begin(LoadingScreen Screen)
    {
        this.Screen = Screen;
        KeepAlive ??= PhotonLoadKeepAliveLease.Acquire();
    }

    void Update()
    {
        if (KeepAlive is null) return;
        if (!Screen.runningProcess)
        {
            Release();
            return;
        }
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.IsConnected && !PhotonNetwork.IsMessageQueueRunning)
            _ = PhotonNetwork.NetworkingClient.LoadBalancingPeer.SendOutgoingCommands();
    }

    void OnDestroy()
    {
        if (KeepAlive is null) return;
        try { (Screen.currentProcess as IDisposable)?.Dispose(); }
        finally { Release(); }
    }

    void Release()
    {
        var Released = KeepAlive;
        KeepAlive = null;
        Released?.Dispose();
    }

    [HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.RunProcess), new[] { typeof(IEnumerator) })]
    static class LoadingProcessPatch
    {
        [HarmonyPrefix]
        static void Prefix(LoadingScreen __instance)
        {
            if (PhotonNetwork.OfflineMode) return;
            var Transport = __instance.GetComponent<SuspendedPhotonTransport>()
                ?? __instance.gameObject.AddComponent<SuspendedPhotonTransport>();
            Transport.Begin(__instance);
        }
    }
}
