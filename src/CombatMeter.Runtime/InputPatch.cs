using HarmonyLib;
using ProjectM;

namespace CombatMeter.Runtime;

[HarmonyPatch(typeof(InputActionSystem), nameof(InputActionSystem.OnUpdate))]
internal static class ReviewInputPatch
{
    [HarmonyPrefix]
    private static void Prefix(InputActionSystem __instance)
    {
        RuntimeHost.DispatchInput(__instance);
    }
}
