using HarmonyLib;
using ProjectM.Gameplay.Systems;

namespace CombatMeter.Runtime;

[HarmonyPatch(typeof(StatChangeSystem), nameof(StatChangeSystem.OnUpdate))]
internal static class ServerTickPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        HealthJobBridge.Install();
    }

    // The native system can initialize the job delegates on its first update.
    [HarmonyPostfix]
    private static void Postfix() => HealthJobBridge.Install();
}

