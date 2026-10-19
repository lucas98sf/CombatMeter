using CombatMeter.Shared;

namespace CombatMeter.Core;

public sealed record RankedPlayer(ulong Id, string Name, int Team, double Damage, double Dps, double Share);

public sealed class MeterState
{
    private readonly Dictionary<string, EncounterSnapshot> _active = new();
    private readonly LinkedList<EncounterSnapshot> _history = new();
    private readonly Dictionary<string, long> _sequences = new();
    public string SessionId { get; private set; } = "";
    public string StatusMessage { get; private set; } = "Waiting for server tracking";
    public PolicyPacket? Policy { get; private set; }
    public IReadOnlyCollection<EncounterSnapshot> Active => _active.Values;
    public IEnumerable<EncounterSnapshot> History => _history;
    public EncounterSnapshot? Latest => _active.Values.OrderByDescending(x => x.StartedAt).FirstOrDefault() ?? _history.First?.Value;

    public bool Apply(PolicyPacket packet)
    {
        if (packet.ProtocolVersion != Protocol.Version || !packet.Accepted || string.IsNullOrEmpty(packet.ServerSessionId))
        {
            Disconnect();
            StatusMessage = packet.Reason.Length > 0 ? packet.Reason : "Incompatible server tracking protocol";
            return false;
        }
        if (packet.ServerSessionId != SessionId) { Disconnect(); SessionId = packet.ServerSessionId; }
        Policy = packet;
        StatusMessage = "Connected";
        return true;
    }

    public bool Apply(EncounterPacket packet)
    {
        var snapshot = packet.Encounter;
        if (Policy is null || packet.ProtocolVersion != Protocol.Version || packet.ServerSessionId != SessionId
            || snapshot is null || string.IsNullOrEmpty(snapshot.Id) || snapshot.Players is null || snapshot.Players.Length > 256
            || snapshot.Creatures is null || snapshot.Creatures.Length > 2048
            || snapshot.Creatures.Any(c => c is null || string.IsNullOrEmpty(c.EntityId) || string.IsNullOrWhiteSpace(c.Name)
                || !Enum.IsDefined(typeof(CreatureKind), c.Kind) || !double.IsFinite(c.Damage) || c.Damage < 0)
            || snapshot.Creatures.Select(c => c.EntityId).Distinct().Count() != snapshot.Creatures.Length
            || !Enum.IsDefined(typeof(EncounterKind), snapshot.Kind) || !Enum.IsDefined(typeof(EncounterStatus), snapshot.Status)
            || !double.IsFinite(snapshot.StartedAt) || !double.IsFinite(snapshot.EndedAt)
            || !double.IsFinite(snapshot.UnknownDamage) || snapshot.UnknownDamage < 0
            || !double.IsFinite(snapshot.UnknownBossDamage) || snapshot.UnknownBossDamage < 0
            || !double.IsFinite(snapshot.DurationSeconds) || snapshot.DurationSeconds < 0
            || snapshot.Players.Any(p => p is null || !double.IsFinite(p.Damage) || p.Damage < 0
                || !double.IsFinite(p.BossDamage) || p.BossDamage < 0 || !double.IsFinite(p.SummonDamage) || p.SummonDamage < 0)
            || snapshot.Players.Select(p => p.PlayerId).Distinct().Count() != snapshot.Players.Length) return false;
        if (_sequences.TryGetValue(snapshot.Id, out var last) && snapshot.Sequence <= last) return false;
        _sequences[snapshot.Id] = snapshot.Sequence;
        if (snapshot.Status == EncounterStatus.Active) _active[snapshot.Id] = snapshot;
        else
        {
            _active.Remove(snapshot.Id);
            var old = _history.First;
            while (old is not null) { var next = old.Next; if (old.Value.Id == snapshot.Id) _history.Remove(old); old = next; }
            if (snapshot.Status != EncounterStatus.Merged) _history.AddFirst(snapshot);
            while (_history.Count > Protocol.HistoryLimit) _history.RemoveLast();
            // Keep retirement tombstones for the session, bounded to avoid unbounded state.
            if (_sequences.Count > 4096)
            {
                var keep = _active.Keys.Concat(_history.Select(x => x.Id)).ToHashSet();
                foreach (var id in _sequences.Keys.Where(x => !keep.Contains(x)).Take(2048).ToArray()) _sequences.Remove(id);
            }
        }
        return true;
    }

    public void Disconnect()
    {
        _active.Clear(); _history.Clear(); _sequences.Clear();
        SessionId = ""; Policy = null; StatusMessage = "Waiting for server tracking";
    }

    public static RankedPlayer[] Rank(EncounterSnapshot encounter, MeterMetric metric, BossTargetFilter filter)
    {
        var total = encounter.Players.Sum(x => x.ForFilter(encounter.Kind, filter))
            + (encounter.Kind == EncounterKind.Boss && filter == BossTargetFilter.BossOnly ? encounter.UnknownBossDamage : encounter.UnknownDamage);
        return encounter.Players.Select(x =>
        {
            var damage = x.ForFilter(encounter.Kind, filter);
            return new RankedPlayer(x.PlayerId, x.Name, x.Team, damage,
                encounter.DurationSeconds > 0 ? damage / encounter.DurationSeconds : 0,
                total > 0 ? damage / total : 0);
        }).OrderByDescending(x => metric == MeterMetric.Damage ? x.Damage : x.Dps)
          .ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
    }
}
