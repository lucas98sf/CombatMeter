using System.Runtime.CompilerServices;
using ProjectM.Gameplay.Systems;
using Unity.Entities.Internal;

namespace CombatMeter.Runtime;

// Only the stat-change job uses the non-Burst body. Its native merging, ordering,
// mitigation, side effects and health writes remain owned by the game. Calling
// that body makes ApplyHealthChangeToEntity visible to the IL2CPP detour; the
// separately compiled Burst implementation never calls that method's detour.
internal static class HealthJobBridge
{
    private static InternalCompilerInterface.JobRunWithoutJobSystemDelegate? _original;
    private static InternalCompilerInterface.JobRunWithoutJobSystemDelegate? _bridge;
    internal static long Calls { get; private set; }

    internal static void Install()
    {
        var current = StatChangeSystem.ApplyChanges_Job.FunctionPtrFieldBurst;
        if (current is null || (_bridge is not null && current.Pointer == _bridge.Pointer)) return;
        _original = current;
        _bridge ??= (Action<IntPtr>)Run;
        StatChangeSystem.ApplyChanges_Job.FunctionPtrFieldBurst = _bridge;
        RuntimeHost.Instance.Log.LogInfo("CombatMeter stat-change job bridge installed; native non-Burst damage calculation selected for this job only.");
    }

    private static unsafe void Run(IntPtr pointer)
    {
        Calls++;
        if (Calls == 1) RuntimeHost.Instance.Log.LogInfo("CombatMeter stat-change job bridge observed its first execution.");
        ref var job = ref Unsafe.AsRef<StatChangeSystem.ApplyChanges_Job>((void*)pointer);
        // Never replay a job after a partial execution: doing so would deal damage twice.
        job.OriginalLambdaBody();
    }

    internal static void Restore()
    {
        var current = StatChangeSystem.ApplyChanges_Job.FunctionPtrFieldBurst;
        if (_bridge is not null && _original is not null && current?.Pointer == _bridge.Pointer)
            StatChangeSystem.ApplyChanges_Job.FunctionPtrFieldBurst = _original;
        _original = null;
        _bridge = null;
    }
}
