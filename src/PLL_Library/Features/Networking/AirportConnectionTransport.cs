using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Steamworks;
using Zorro.Core.Serizalization;

namespace PLL.Library;

internal static class AirportConnectionTransport
{
    static readonly MethodInfo HandleMessage = AccessTools.DeclaredMethod(
        typeof(SteamLobbyHandler), "HandleMessage",
        new[] { typeof(SteamLobbyHandler.MessageType), typeof(BinaryDeserializer), typeof(CSteamID) })
        ?? throw new MissingMethodException(typeof(SteamLobbyHandler).FullName, "HandleMessage");
    static readonly MethodInfo OnLobbyCreated = AccessTools.DeclaredMethod(
        typeof(SteamLobbyHandler), "OnLobbyCreated", new[] { typeof(LobbyCreated_t) })
        ?? throw new MissingMethodException(typeof(SteamLobbyHandler).FullName, "OnLobbyCreated");
    static readonly MethodInfo LoadSceneProcess = AccessTools.DeclaredMethod(
        typeof(LoadingScreenHandler), nameof(LoadingScreenHandler.LoadSceneProcess),
        new[] { typeof(string), typeof(bool), typeof(bool), typeof(float) })
        ?? throw new MissingMethodException(typeof(LoadingScreenHandler).FullName,
            nameof(LoadingScreenHandler.LoadSceneProcess));
    static readonly MethodInfo Load = AccessTools.DeclaredMethod(
        typeof(LoadingScreenHandler), nameof(LoadingScreenHandler.Load),
        new[] { typeof(LoadingScreen.LoadingScreenType), typeof(Action), typeof(IEnumerator[]) })
        ?? throw new MissingMethodException(typeof(LoadingScreenHandler).FullName,
            nameof(LoadingScreenHandler.Load));
    static readonly MethodInfo WrapLoadSceneProcess = ((Func<LoadingScreenHandler, string, bool, bool, float, IEnumerator>)Wrap).Method;

    static IEnumerator Wrap(
        LoadingScreenHandler Handler,
        string SceneName,
        bool Networked,
        bool YieldForCharacterSpawn,
        float ExtraYieldTimeOnEnd) =>
        SuspendedPhotonTransport.SendOutgoingAtYields(Handler.LoadSceneProcess(
            SceneName, Networked, YieldForCharacterSpawn, ExtraYieldTimeOnEnd), null);

    [HarmonyPatch]
    static class AirportLoadPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return HandleMessage;
            yield return OnLobbyCreated;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions,
            MethodBase __originalMethod)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var ProcessCalls = 0;
            var LoadCalls = 0;
            foreach (var Instruction in Codes)
            {
                if (Instruction.Calls(LoadSceneProcess))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = WrapLoadSceneProcess;
                    ProcessCalls++;
                    continue;
                }
                if (Instruction.Calls(Load)) LoadCalls++;
            }
            if (ProcessCalls != 1 || LoadCalls != 1)
                throw new MissingMethodException(
                    $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} contained "
                    + $"{ProcessCalls} Airport scene process calls and {LoadCalls} loading calls.");
            return Codes;
        }
    }
}
