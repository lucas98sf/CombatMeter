using CombatMeter.Shared;

namespace CombatMeter.Core;

public sealed class EncounterTracker
{
    private sealed class Fight
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Key = "";
        public string Label = "";
        public EncounterKind Kind;
        public EncounterStatus Status;
        public double Start;
        public double Last;
        public long Sequence;
        public long DetailRevision;
        public double End;
        public double Unknown;
        public double UnknownBoss;
        public readonly Dictionary<ulong, PlayerTotals> Players = new();
        public readonly Dictionary<EntityKey, CreatureTotals> Creatures = new();
        public readonly Dictionary<string, SourceAccumulator> Sources = new();
        public readonly HashSet<EntityKey> Entities = new();
        public readonly HashSet<ulong> Observers = new();
    }

    private readonly Dictionary<string, Fight> _active = new();
    private readonly LinkedList<EncounterSnapshot> _history = new();
    private sealed record HistoryEntry(EncounterSnapshot Snapshot, Fight Fight);
    private readonly Dictionary<ulong, LinkedList<HistoryEntry>> _playerHistory = new();
    private readonly Dictionary<string, double> _seen = new();
    private readonly Queue<(string Id, double Timestamp)> _seenOrder = new();
    public TrackingPolicy Policy { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public event Action<EncounterSnapshot>? Finished;
    public event Action<EncounterSnapshot, IReadOnlyCollection<ulong>>? FinishedForAudience;
    public int ActiveCount => _active.Count;
    public int HistoryCount => _history.Count;
    public int DeduplicationCount => _seen.Count;

    public EncounterTracker(TrackingPolicy? policy = null)
    {
        Policy = policy ?? new();
        if (!double.IsFinite(Policy.TimeoutSeconds) || Policy.TimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
    }

    public string? Begin(EncounterKind kind, string key, string label, double now, IEnumerable<Participant> roster)
    {
        if (!Policy.EnabledModes.Contains(kind) || !double.IsFinite(now)) return null;
        var existing = _active.Values.FirstOrDefault(x => x.Kind == kind && x.Key == key);
        if (existing is not null) { foreach (var player in roster) AddPlayer(existing, player); return existing.Id; }
        var fight = new Fight { Kind = kind, Key = key, Label = label, Start = now, Last = now };
        foreach (var player in roster) AddPlayer(fight, player);
        _active.Add(fight.Id, fight);
        return fight.Id;
    }

    public void AddParticipants(EncounterKind kind, string key, IEnumerable<Participant> roster)
    {
        var fight = _active.Values.FirstOrDefault(x => x.Kind == kind && x.Key == key);
        if (fight is null) return;
        foreach (var player in roster) AddPlayer(fight, player);
    }

    public void SetObservers(EncounterKind kind, string key, IEnumerable<ulong> observers)
    {
        var fight = _active.Values.FirstOrDefault(x => x.Kind == kind && x.Key == key);
        if (fight is null) return;
        fight.Observers.Clear();
        fight.Observers.UnionWith(observers);
    }

    public string? Record(CombatEvent hit)
    {
        if (!Policy.EnabledModes.Contains(hit.Kind) || !double.IsFinite(hit.Timestamp)
            || !double.IsFinite(hit.HealthDamage) || hit.HealthDamage < 0) return null;
        if (hit.CreatureAttacker is { } npc && (hit.Attacker is not null || hit.Victim is null
            || hit.Kind is not (EncounterKind.Boss or EncounterKind.OpenWorld)
            || string.IsNullOrWhiteSpace(npc.Name) || !Enum.IsDefined(typeof(CreatureKind), npc.Kind))) return null;
        ExpireDeduplication(hit.Timestamp);
        if (!_seen.TryAdd(hit.EventId, hit.Timestamp)) return null;
        _seenOrder.Enqueue((hit.EventId, hit.Timestamp));
        // Starting a new ordinary fight must not resurrect an already expired segment.
        Tick(hit.Timestamp);
        Fight? fight;
        if (hit.Kind is EncounterKind.PvP or EncounterKind.OpenWorld)
        {
            var connected = _active.Values.Where(x => x.Kind == hit.Kind &&
                (x.Entities.Contains(hit.Target) || x.Entities.Contains(hit.Source)
                 || (hit.Attacker is { } a && x.Players.ContainsKey(a.Id))
                 || (hit.Victim is { } v && x.Players.ContainsKey(v.Id)))).OrderBy(x => x.Start).ToArray();
            fight = connected.FirstOrDefault();
            if (fight is null)
            {
                var id = Begin(hit.Kind, Guid.NewGuid().ToString("N"), hit.Kind == EncounterKind.PvP ? "World PvP" : "Open-world PvE", hit.Timestamp, Array.Empty<Participant>());
                fight = _active[id!];
            }
            foreach (var other in connected.Skip(1)) Merge(fight, other, hit.Timestamp);
        }
        else
        {
            // Structured encounters are created by their native lifecycle adapter, never
            // by an outside hit during countdown or after a round has finished.
            fight = _active.Values.FirstOrDefault(x => x.Kind == hit.Kind && x.Key == hit.EncounterKey);
            if (fight is null) return null;
        }
        if (hit.Timestamp < fight.Start) return null;
        fight.Last = Math.Max(fight.Last, hit.Timestamp);
        fight.Entities.Add(hit.Target);
        fight.Entities.Add(hit.Source);
        if (hit.Victim is { } victim) AddPlayer(fight, victim);
        if (hit.CreatureAttacker is { } creature)
        {
            // NPCs only contribute damage dealt to players, never outgoing
            // player totals or unresolved player attribution.
            if (!fight.Creatures.TryGetValue(creature.Entity, out var total))
                fight.Creatures[creature.Entity] = total = new CreatureTotals
                {
                    EntityId = $"{creature.Entity.Index}:{creature.Entity.Version}",
                    Name = creature.Name, Kind = creature.Kind
                };
            total.Damage += hit.HealthDamage;
            Sources(fight, "c:" + total.EntityId).Record(hit.DamageSource, hit.HealthDamage, DamageTarget.Other);
        }
        else if (hit.Attacker is { } attacker)
        {
            var total = AddPlayer(fight, attacker);
            total.Damage += hit.HealthDamage;
            if (hit.TargetClass == DamageTarget.Boss) total.BossDamage += hit.HealthDamage;
            if (hit.TargetClass == DamageTarget.BossSummon) total.SummonDamage += hit.HealthDamage;
            Sources(fight, "p:" + attacker.Id).Record(hit.DamageSource, hit.HealthDamage, hit.TargetClass);
        }
        else
        {
            fight.Unknown += hit.HealthDamage;
            if (hit.TargetClass == DamageTarget.Boss) fight.UnknownBoss += hit.HealthDamage;
        }
        return fight.Id;
    }

    private static PlayerTotals AddPlayer(Fight fight, Participant player)
    {
        if (!fight.Players.TryGetValue(player.Id, out var total))
            fight.Players.Add(player.Id, total = new PlayerTotals { PlayerId = player.Id });
        total.Name = player.Name;
        total.Team = player.Team;
        return total;
    }

    private static SourceAccumulator Sources(Fight fight, string actor)
    {
        if (!fight.Sources.TryGetValue(actor, out var sources)) fight.Sources[actor] = sources = new();
        return sources;
    }

    private void Merge(Fight target, Fight source, double now)
    {
        foreach (var value in source.Players.Values)
        {
            var total = AddPlayer(target, new(value.PlayerId, value.Name, value.Team));
            total.Damage += value.Damage;
            total.BossDamage += value.BossDamage;
            total.SummonDamage += value.SummonDamage;
        }
        target.Last = Math.Max(target.Last, source.Last);
        target.Unknown += source.Unknown;
        target.UnknownBoss += source.UnknownBoss;
        foreach (var (key, value) in source.Creatures)
        {
            if (!target.Creatures.TryGetValue(key, out var total))
                target.Creatures[key] = total = new CreatureTotals
                    { EntityId = value.EntityId, Name = value.Name, Kind = value.Kind };
            total.Damage += value.Damage;
        }
        target.Entities.UnionWith(source.Entities);
        foreach (var (actor, sources) in source.Sources) Sources(target, actor).Merge(sources);
        target.Observers.UnionWith(source.Observers);
        _active.Remove(source.Id);
        source.Status = EncounterStatus.Merged;
        var retired = Snapshot(source, now);
        retired.MergedInto = target.Id;
        Finished?.Invoke(retired);
        FinishedForAudience?.Invoke(retired, Audience(source));
    }

    public void End(EncounterKind kind, string key, double now, EncounterStatus status = EncounterStatus.Completed)
    {
        if (status is EncounterStatus.Active or EncounterStatus.Merged) throw new ArgumentOutOfRangeException(nameof(status));
        var fight = _active.Values.FirstOrDefault(x => x.Kind == kind && x.Key == key);
        if (fight is not null) Complete(fight, now, status);
    }

    public void Tick(double now)
    {
        ExpireDeduplication(now);
        foreach (var fight in _active.Values.Where(x => x.Kind is EncounterKind.PvP or EncounterKind.OpenWorld).ToArray())
            if (now - fight.Last >= Policy.TimeoutSeconds) Complete(fight, fight.Last, EncounterStatus.Completed);
    }

    private void Complete(Fight fight, double now, EncounterStatus status)
    {
        _active.Remove(fight.Id);
        fight.Status = status;
        fight.End = Math.Max(now, fight.Last);
        var snapshot = Snapshot(fight, Math.Max(now, fight.Last));
        _history.AddFirst(snapshot);
        while (_history.Count > Protocol.HistoryLimit) _history.RemoveLast();
        var audience = Audience(fight);
        foreach (var id in audience)
        {
            if (!_playerHistory.TryGetValue(id, out var history)) _playerHistory[id] = history = new();
            history.AddFirst(new HistoryEntry(snapshot, fight));
            while (history.Count > Protocol.HistoryLimit) history.RemoveLast();
        }
        Finished?.Invoke(snapshot);
        FinishedForAudience?.Invoke(snapshot, audience);
    }

    public EncounterSnapshot[] Active(double now) => _active.Values.Select(x => Snapshot(x, now)).ToArray();
    public EncounterSnapshot[] History(ulong playerId) => _playerHistory.TryGetValue(playerId, out var history) ? history.Select(x => x.Snapshot).ToArray() : Array.Empty<EncounterSnapshot>();
    private static ulong[] Audience(Fight fight) => fight.Players.Keys.Concat(fight.Observers).Distinct().ToArray();
    public bool CanObserve(string id, ulong playerId) => _active.TryGetValue(id, out var fight) &&
        (fight.Players.ContainsKey(playerId) || fight.Observers.Contains(playerId));

    public SourceResponsePacket Details(ulong requester, SourceRequestPacket request, double now)
    {
        var response = new SourceResponsePacket
        {
            ServerSessionId = SessionId, EncounterId = request.EncounterId, ActorKind = request.ActorKind,
            PlayerId = request.PlayerId, CreatureId = request.CreatureId, RequestId = request.RequestId,
            Reason = "Source details unavailable"
        };
        if (request.ProtocolVersion != Protocol.Version || request.ServerSessionId != SessionId
            || string.IsNullOrEmpty(request.EncounterId) || request.EncounterId.Length > 64
            || request.RequestId <= 0 || !double.IsFinite(now) || !Enum.IsDefined(typeof(DetailActorKind), request.ActorKind)) return response;
        Fight? fight = null;
        if (CanObserve(request.EncounterId, requester)) fight = _active[request.EncounterId];
        else if (_playerHistory.TryGetValue(requester, out var history))
            fight = history.FirstOrDefault(x => x.Snapshot.Id == request.EncounterId)?.Fight;
        if (fight is null) return response;
        var actor = request.ActorKind == DetailActorKind.Player ? "p:" + request.PlayerId : "c:" + request.CreatureId;
        if (request.ActorKind == DetailActorKind.Player)
        {
            if (!fight.Players.TryGetValue(request.PlayerId, out var player)) return response;
            response.ActorName = player.Name; response.Damage = player.Damage;
            response.BossDamage = player.BossDamage; response.SummonDamage = player.SummonDamage;
        }
        else
        {
            var creature = fight.Creatures.Values.FirstOrDefault(x => x.EntityId == request.CreatureId);
            if (creature is null) return response;
            response.ActorName = creature.Name; response.Damage = creature.Damage;
        }
        response.Available = true; response.Reason = "";
        response.EncounterLabel = fight.Label; response.Kind = fight.Kind; response.Status = fight.Status;
        response.Revision = ++fight.DetailRevision;
        response.DurationSeconds = Math.Max(0, (fight.Status == EncounterStatus.Active ? now : fight.End) - fight.Start);
        response.Sources = fight.Sources.TryGetValue(actor, out var sources) ? sources.Snapshot() : Array.Empty<SourceTotals>();
        return response;
    }

    private static EncounterSnapshot Snapshot(Fight fight, double now) => new()
    {
        Id = fight.Id, Label = fight.Label, Kind = fight.Kind, Status = fight.Status,
        Sequence = ++fight.Sequence, StartedAt = fight.Start,
        EndedAt = fight.Status == EncounterStatus.Active ? 0 : now,
        DurationSeconds = Math.Max(0, now - fight.Start), UnknownDamage = fight.Unknown,
        UnknownBossDamage = fight.UnknownBoss,
        Creatures = fight.Creatures.Values.Select(x => new CreatureTotals
        { EntityId = x.EntityId, Name = x.Name, Kind = x.Kind, Damage = x.Damage }).ToArray(),
        Players = fight.Players.Values.Select(x => new PlayerTotals
        {
            PlayerId = x.PlayerId, Name = x.Name, Team = x.Team,
            Damage = x.Damage, BossDamage = x.BossDamage, SummonDamage = x.SummonDamage
        }).ToArray()
    };

    private void ExpireDeduplication(double now)
    {
        while (_seenOrder.TryPeek(out var item) && (now - item.Timestamp > 120 || _seenOrder.Count > 65536))
        {
            _seenOrder.Dequeue();
            _seen.Remove(item.Id);
        }
    }
}
