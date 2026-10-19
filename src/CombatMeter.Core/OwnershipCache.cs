namespace CombatMeter.Core;

public sealed class OwnershipCache
{
    private readonly Dictionary<EntityKey, (Participant Player, double Seen)> _players = new();
    private readonly Dictionary<EntityKey, (EntityKey Owner, double Seen)> _owners = new();
    public int Count => _players.Count + _owners.Count;

    public void ObservePlayer(EntityKey entity, Participant player, double now) => _players[entity] = (player, now);
    public void ObserveOwner(EntityKey entity, EntityKey owner, double now = 0) => _owners[entity] = (owner, now);

    public Participant? Resolve(EntityKey entity, double now)
    {
        var path = new HashSet<EntityKey>();
        var cursor = entity;
        for (var depth = 0; depth < 32 && path.Add(cursor); depth++)
        {
            if (_players.TryGetValue(cursor, out var entry))
            {
                foreach (var key in path) _players[key] = (entry.Player, now);
                return entry.Player;
            }
            if (!_owners.TryGetValue(cursor, out var link)) return null;
            cursor = link.Owner;
        }
        return null;
    }

    public void Prune(double now, double retentionSeconds = 120)
    {
        foreach (var key in _players.Where(x => now - x.Value.Seen > retentionSeconds).Select(x => x.Key).ToArray())
            _players.Remove(key);
        // Pruning must not refresh stale links: otherwise every expired projectile
        // remains alive forever merely because a prune traversed its owner chain.
        foreach (var key in _owners.Where(x => now - x.Value.Seen > retentionSeconds).Select(x => x.Key).ToArray())
            _owners.Remove(key);
    }

    public void Clear() { _players.Clear(); _owners.Clear(); }
}
