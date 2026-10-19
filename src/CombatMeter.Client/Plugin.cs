using BepInEx;
using BepInEx.Unity.IL2CPP;
using CombatMeter.Runtime;
using System.Reflection;

[assembly: AssemblyMetadata("Reloadable", "true")]

namespace CombatMeter.Client;

[BepInPlugin("io.combatmeter.client", "CombatMeter Client", "0.2.1")]
[BepInProcess("VRising.exe")]
[BepInDependency("io.combatmeter.runtime")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static ClientRuntime? Runtime;
    public override void Load()
    {
        Instance = this;
        Runtime = new ClientRuntime(this);
        RuntimeHost.Frame += Runtime.Update;
        RuntimeHost.Input += Runtime.SynchronizeInput;
        Log.LogInfo("CombatMeter client loaded; native HUD will attach after entering a world.");
    }
    public override bool Unload()
    {
        if (Runtime is { } runtime)
        {
            RuntimeHost.Frame -= runtime.Update;
            RuntimeHost.Input -= runtime.SynchronizeInput;
            Runtime = null;
            runtime.Dispose();
        }
        Log.LogInfo("CombatMeter client unloaded; HUD, input and packet handlers released.");
        return true;
    }
}



