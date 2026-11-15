using System;
using System.Collections;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace PLL.Library;

[HarmonyPatch(typeof(SteamLobbyHandler), nameof(SteamLobbyHandler.SetLobbyData))]
static class LobbyTagRefresh
{
    [HarmonyPostfix]
    static void Postfix(SteamLobbyHandler __instance)
    {
        try
        {
            if (!SteamManager.Initialized) return;
            var Lobby = __instance.LobbySteamId;
            if (Lobby.m_SteamID == 0 || SteamMatchmaking.GetLobbyOwner(Lobby) != SteamUser.GetSteamID()) return;
            if (!string.Equals(SteamMatchmaking.GetLobbyData(Lobby, LobbyAdmission.SteamKey), LobbyAdmission.MarkerValue, StringComparison.Ordinal)
                && !LobbyMarked(Lobby)) return;
            SteamMatchmaking.DeleteLobbyData(Lobby, LobbyAdmission.SteamKey);
            Plugin.StartRoutine(WriteLater(Lobby));
        }
        catch (Exception ex)
        {
            Plugin.PLLLog.LogWarning("The lobby tag could not be refreshed. " + ex.Message);
        }
    }

    static bool LobbyMarked(CSteamID Lobby) =>
        string.Equals(SteamMatchmaking.GetLobbyMemberData(Lobby, SteamUser.GetSteamID(), LobbyAdmission.SteamKey), LobbyAdmission.MarkerValue, StringComparison.Ordinal);

    static IEnumerator WriteLater(CSteamID Lobby)
    {
        var Until = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < Until) yield return null;
        try
        {
            if (SteamManager.Initialized && SteamMatchmaking.GetLobbyOwner(Lobby) == SteamUser.GetSteamID()
                && !SteamMatchmaking.SetLobbyData(Lobby, LobbyAdmission.SteamKey, LobbyAdmission.MarkerValue))
                Plugin.PLLLog.LogWarning("The lobby tag could not be written again.");
        }
        catch (Exception ex)
        {
            Plugin.PLLLog.LogWarning("The lobby tag could not be written again. " + ex.Message);
        }
    }
}
