using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ProjectM;
using UnityEngine;

namespace CombatMeter.Runtime;

// Lives in the ordinary plugins directory. Native function pointers, injected
// types and Harmony patches must outlive collectible client/server assemblies.
[BepInPlugin("io.combatmeter.runtime", "CombatMeter Runtime", "0.2.0")]
public sealed class RuntimeHost : BasePlugin
{
    internal static RuntimeHost Instance = null!;
    public static event Action? Frame;
    public static event Action<InputActionSystem>? Input;
    public static event Func<StatChangeEvent, float?>? BeforeHealth;
    public static event Action<StatChangeEvent, float?>? AfterHealth;
    public static long HealthJobCalls => HealthJobBridge.Calls;
    private Harmony? _harmony;
    private NativeHealthDetour? _health;

    public override void Load()
    {
        Instance = this;
        // Resolve stable library types in the default context before a
        // collectible plugin asks Bloodpebble to resolve these dependencies.
        Log.LogInfo($"CombatMeter stable libraries ready: {typeof(CombatMeter.Core.MeterState).Assembly.GetName().Name}, {typeof(CombatMeter.Shared.Protocol).Assembly.GetName().Name}.");
        _harmony = new Harmony("io.combatmeter.runtime");
        var server = System.Diagnostics.Process.GetCurrentProcess().ProcessName == "VRisingServer";
        if (server)
        {
            _health = new NativeHealthDetour();
            _harmony.CreateClassProcessor(typeof(ServerTickPatch)).Patch();
            _harmony.CreateClassProcessor(typeof(ServerMessagePatch)).Patch();
        }
        else
        {
            _harmony.CreateClassProcessor(typeof(ReviewInputPatch)).Patch();
            _harmony.CreateClassProcessor(typeof(ClientMessagePatch)).Patch();
        }
        ClassInjector.RegisterTypeInIl2Cpp<RuntimeBehaviour>();
        AddComponent<RuntimeBehaviour>();
        Log.LogInfo("CombatMeter persistent runtime ready; client/server logic may subscribe and reload independently.");
    }

    internal static void DispatchFrame()
    {
        if (Frame is null) return;
        foreach (Action callback in Frame.GetInvocationList())
            try { callback(); }
            catch (Exception ex) { Instance.Log.LogWarning($"CombatMeter frame callback failed: {ex.Message}"); }
    }
    internal static void DispatchInput(InputActionSystem system)
    {
        try { Input?.Invoke(system); }
        catch (Exception ex) { Instance.Log.LogDebug($"CombatMeter input context unavailable: {ex.Message}"); }
    }
    internal static float? CaptureBefore(StatChangeEvent change) => BeforeHealth?.Invoke(change);
    internal static void CaptureAfter(StatChangeEvent change, float? before) => AfterHealth?.Invoke(change, before);

    public override bool Unload()
    {
        // The development installer leaves this plugin in the permanent folder.
        // Native type registrations cannot be undone for a hot reload.
        return false;
    }
}

public sealed class RuntimeBehaviour : MonoBehaviour
{
    public RuntimeBehaviour(IntPtr pointer) : base(pointer) { }
    public void Update() => RuntimeHost.DispatchFrame();
}
