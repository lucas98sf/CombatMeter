using Unity.Collections;
using Unity.Entities;

namespace CombatMeter.Server;

// The game's IL2CPP NativeArray does not implement the managed IDisposable interface.
internal sealed class NativeArrayLease<T> : IDisposable where T : unmanaged
{
    private readonly NativeArray<T> _array;
    public int Length => _array.Length;
    public T this[int index] => _array[index];
    public NativeArrayLease(NativeArray<T> array) => _array = array;
    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < _array.Length; index++) yield return _array[index];
    }
    public void Dispose() => _array.Dispose();
}

internal static class NativeArrayQueries
{
    public static NativeArrayLease<Entity> ToEntityArrayAccessor(this EntityQuery query, Allocator allocator) => new(query.ToEntityArray(allocator));
    public static NativeArrayLease<T> ToComponentDataArrayAccessor<T>(this EntityQuery query, Allocator allocator) where T : unmanaged => new(query.ToComponentDataArray<T>(allocator));
}
