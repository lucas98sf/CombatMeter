namespace CombatMeter.Core;

// Only cache metadata proven to belong to this exact versioned effect entity.
public sealed class SourceOriginCache
{
    private readonly Dictionary<EntityKey, (DamageSource Source, double Seen)> _sources = new();
    public void Observe(EntityKey entity, DamageSource source, double now) => _sources[entity] = (source, now);
    public DamageSource? Resolve(EntityKey entity, double now)
    {
        if (!_sources.TryGetValue(entity, out var value) || now - value.Seen > 120) return null;
        _sources[entity] = (value.Source, now);
        return value.Source;
    }
    public void Prune(double now)
    {
        foreach (var key in _sources.Where(x => now - x.Value.Seen > 120).Select(x => x.Key).ToArray()) _sources.Remove(key);
    }
    public void Clear() => _sources.Clear();
}
