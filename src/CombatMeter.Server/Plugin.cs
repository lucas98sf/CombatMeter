using BepInEx;
using BepInEx.Unity.IL2CPP;
using CombatMeter.Runtime;
using System.Reflection;

[assembly: AssemblyMetadata("Reloadable", "true")]

namespace CombatMeter.Server;

[BepInPlugin("io.combatmeter.server", "CombatMeter Server", "0.2.1")]
[BepInProcess("VRisingServer.exe")]
[BepInDependency("io.combatmeter.runtime")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static ServerRuntime? Runtime;

    public override void Load()
    {
        Instance = this;
        Runtime = new ServerRuntime(this);
        RuntimeHost.Frame += Runtime.Tick;
        RuntimeHost.BeforeHealth += Runtime.BeforeHealth;
        RuntimeHost.AfterHealth += Runtime.AfterHealth;
        Log.LogInfo("CombatMeter server loaded; native damage callbacks attached to persistent runtime.");
    }

    public override bool Unload()
    {
        if (Runtime is { } runtime)
        {
            RuntimeHost.Frame -= runtime.Tick;
            RuntimeHost.BeforeHealth -= runtime.BeforeHealth;
            RuntimeHost.AfterHealth -= runtime.AfterHealth;
            Runtime = null;
            runtime.NotifyReload();
            runtime.Dispose();
        }
        Log.LogInfo("CombatMeter server unloaded; collector callbacks and packet handlers released.");
        return true;
    }
}



