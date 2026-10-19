using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using ProjectM;
using ProjectM.Gameplay.Systems;

namespace CombatMeter.Runtime;

internal sealed class NativeHealthDetour : IDisposable
{
    // Windows x64 passes these large by-value structs by address. A managed
    // Harmony delegate that marshals them as IL2CPP values corrupts the call.
    // Entity is eight bytes; every other argument is an unmodified native pointer.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ApplyHealth(IntPtr input, ulong changeEntity, IntPtr change,
        IntPtr commands, double time, IntPtr balance, IntPtr debug, IntPtr method);

    private readonly INativeDetour _detour;
    private readonly ApplyHealth _original;

    internal NativeHealthDetour()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("CombatMeter native health capture currently requires Windows x64.");
        RuntimeHelpers.RunClassConstructor(typeof(StatChangeSystem).TypeHandle);
        var field = typeof(StatChangeSystem).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(x => x.Name.StartsWith("NativeMethodInfoPtr_ApplyHealthChangeToEntity_", StringComparison.Ordinal));
        var method = (IntPtr)field.GetValue(null)!;
        var pointer = Marshal.ReadIntPtr(method); // MethodInfo.methodPointer, Unity 2022 IL2CPP.
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Native health method pointer is unavailable.");
        _detour = INativeDetour.CreateAndApply<ApplyHealth>(pointer, Invoke, out _original);
    }

    private unsafe void Invoke(IntPtr input, ulong changeEntity, IntPtr changePointer,
        IntPtr commands, double time, IntPtr balance, IntPtr debug, IntPtr method)
    {
        var change = Unsafe.Read<StatChangeEvent>((void*)changePointer);
        float? before = null;
        try { before = RuntimeHost.CaptureBefore(change); }
        catch (Exception ex) { RuntimeHost.Instance.Log.LogWarning($"CombatMeter health capture failed: {ex.Message}"); }
        // Call once, with precisely the original native arguments. The collector
        // cannot suppress, duplicate, or alter the game's health operation.
        _original(input, changeEntity, changePointer, commands, time, balance, debug, method);
        try { RuntimeHost.CaptureAfter(change, before); }
        catch (Exception ex) { RuntimeHost.Instance.Log.LogWarning($"CombatMeter resolved damage capture failed: {ex.Message}"); }
    }

    public void Dispose() => _detour.Dispose();
}
