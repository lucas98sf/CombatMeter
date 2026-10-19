using BepInEx.Configuration;
using CombatMeter.Core;
using CombatMeter.Shared;
using CombatMeter.Runtime;
using ProjectM;
using ProjectM.Behaviours;
using ProjectM.Contest;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Stunlock.Core;

namespace CombatMeter.Server;

internal sealed class ServerRuntime : IDisposable
{
    private readonly Plugin _plugin;
    private readonly Dictionary<EncounterKind, ConfigEntry<bool>> _modes = new();
    private readonly ConfigEntry<double> _timeout;
    private readonly ConfigEntry<bool> _audit;
    private readonly OwnershipCache _ownership = new();
    private readonly Dictionary<EntityKey, (CreatureParticipant Creature, double Seen)> _creatureOrigins = new();
    private readonly Dictionary<int, string> _prefabNames = new();
    private readonly Dictionary<EntityKey, Entity> _bosses = new();
    private readonly Dictionary<EntityKey, EntityKey> _bossOwners = new();
    private readonly Dictionary<EntityKey, (EncounterKind Kind, string Key)> _contests = new();
    private readonly Dictionary<EntityKey, (EncounterKind Kind, string Key, int Team)> _contestPlayers = new();
    private World? _world;
    private EntityManager _manager;
    private DamageSourceResolver? _sources;
    private readonly Stack<DamageSource> _healthSources = new();
    private EntityQuery _contestQuery;
    private EntityQuery _userQuery;
    private EntityQuery _bossQuery;
    private EntityQuery _timeQuery;
    private EntityQuery _finishedQuery;
    private EntityQuery _cancelledQuery;
    private EntityQuery _spawnQuery;
    private EntityQuery _buffQuery;
    private double _nextPush;
    private double _nextPrune;
    private long _serial;
    private double _nextErrorLog;
    private bool _clockReady;
    private double _nextDiagnostics;
    internal long HealthHookCalls { get; private set; }
    internal long RoutedDamageEvents { get; private set; }
    public long CapturedHealthChanges { get; private set; }
    internal EncounterTracker Tracker { get; private set; } = new();
    internal NativeTransport Transport { get; }
    internal EntityManager Manager => _manager;
    internal double Now => _world is not null && _world.IsCreated ? _timeQuery.GetSingleton<ServerTime>().Time : 0;

    public ServerRuntime(Plugin plugin)
    {
        _plugin = plugin;
        foreach (var kind in Enum.GetValues<EncounterKind>())
            _modes[kind] = plugin.Config.Bind("Tracking", kind.ToString(), true,
                "Collect this encounter type. Client visibility settings cannot enable server-disabled tracking.");
        _timeout = plugin.Config.Bind("Tracking", "CombatTimeoutSeconds", 10d,
            new ConfigDescription("Quiet period that ends ordinary PvP/PvE segments. The idle tail is excluded from DPS.", new AcceptableValueRange<double>(1, 120)));
        _audit = plugin.Config.Bind("Diagnostics", "LogResolvedDamage", false,
            "Log each measured target health delta for real-game verification. Leave off for normal play.");
        Transport = new NativeTransport(this, plugin);
    }

    private bool EnsureWorld()
    {
        if (_world is not null && !_world.IsCreated) ResetWorld();
        if (_world is not null) return _timeQuery.CalculateEntityCount() == 1;
        foreach (var world in World.s_AllWorlds)
        {
            if (!world.IsCreated || !world.Name.Equals("Server", StringComparison.OrdinalIgnoreCase)) continue;
            _world = world; _manager = world.EntityManager; _sources = new(world);
            _contestQuery = Query<ContestInstance>();
            _userQuery = Query<User>();
            _bossQuery = Query<VBloodUnit>();
            _timeQuery = _manager.CreateEntityQuery(new EntityQueryDesc[] { new()
            {
                All = new ComponentType[] { ComponentType.ReadOnly<ServerTime>() },
                Options = EntityQueryOptions.IncludeSystems
            } });
            _finishedQuery = Query<ContestInstance.FinishedEvent>();
            _cancelledQuery = Query<ContestInstance.CancelledEvent>();
            _spawnQuery = _manager.CreateEntityQuery(new EntityQueryDesc[] { new()
            {
                All = new ComponentType[] { ComponentType.ReadOnly<SpawnTag>(), ComponentType.ReadOnly<EntityOwner>() },
                Options = EntityQueryOptions.IncludeSpawnTag
            } });
            _buffQuery = _manager.CreateEntityQuery(new ComponentType[] { ComponentType.ReadOnly<Buff>(), ComponentType.ReadOnly<PrefabGUID>(), ComponentType.ReadOnly<EntityOwner>() });
            Tracker = new EncounterTracker(new TrackingPolicy
            {
                EnabledModes = _modes.Where(x => x.Value.Value).Select(x => x.Key).ToHashSet(),
                TimeoutSeconds = _timeout.Value
            });
            Tracker.FinishedForAudience += OnFinished;
            _plugin.Log.LogInfo($"CombatMeter server world ready; session={Tracker.SessionId}; snapshot interval=250ms.");
            return _timeQuery.CalculateEntityCount() == 1;
        }
        return false;
    }

    private EntityQuery Query<T>() where T : struct => _manager.CreateEntityQuery(new EntityQueryDesc[] { new()
    {
        All = new ComponentType[] { ComponentType.ReadOnly<T>() },
        Options = EntityQueryOptions.IncludeSpawnTag | EntityQueryOptions.IncludeDestroyTag
    } });
    private bool Has<T>(Entity entity) where T : struct => entity != Entity.Null && _manager.Exists(entity) && _manager.HasComponent<T>(entity);
    private T Read<T>(Entity entity) where T : struct => _manager.GetComponentData<T>(entity);
    private static EntityKey Key(Entity entity) => new(entity.Index, entity.Version);

    internal void Tick()
    {
        try
        {
            if (!EnsureWorld()) return;
            var now = Now;
            if (_audit.Value && now >= _nextDiagnostics)
            {
                _nextDiagnostics = now + 15;
                _plugin.Log.LogInfo($"[CombatMeter:Diagnostics] healthJobCalls={RuntimeHost.HealthJobCalls}; healthHookCalls={HealthHookCalls}; captured={CapturedHealthChanges}; routed={RoutedDamageEvents}; active={Tracker.ActiveCount}; subscribers={Transport.SubscriberCount}");
            }
            if (!_clockReady)
            {
                _clockReady = true;
                _plugin.Log.LogInfo($"CombatMeter collector ticking; native server time={now:R}; V Blood entities={_bossQuery.CalculateEntityCount()}.");
            }
            CaptureSpawnOwnership(now);
            PollEndEvents(now);
            if (now < _nextPush) return;
            _nextPush = now + Protocol.SnapshotInterval;
            PollContests(now);
            PollBosses(now);
            Tracker.Tick(now);
            Transport.Flush(now);
            if (now >= _nextPrune)
            {
                _nextPrune = now + 10;
                _ownership.Prune(now);
                _sources?.Prune(now);
                foreach (var key in _creatureOrigins.Where(x => now - x.Value.Seen > 120).Select(x => x.Key).ToArray())
                    _creatureOrigins.Remove(key);
                foreach (var key in _bossOwners.Keys.Where(x => !Exists(x)).ToArray()) _bossOwners.Remove(key);
            }
        }
        catch (Exception ex)
        {
            if (Time.realtimeSinceStartupAsDouble >= _nextErrorLog)
            {
                _nextErrorLog = Time.realtimeSinceStartupAsDouble + 10;
                _plugin.Log.LogError($"CombatMeter update failed: {ex}");
            }
        }
    }

    private bool Exists(EntityKey key) => _manager.Exists(new Entity { Index = key.Index, Version = key.Version });

    private void CaptureSpawnOwnership(double now)
    {
        using var spawned = _spawnQuery.ToEntityArrayAccessor(Allocator.Temp);
        foreach (var entity in spawned)
        {
            _ownership.ObserveOwner(Key(entity), Key(Read<EntityOwner>(entity).Owner), now);
            ResolvePlayer(entity);
            ResolveCreature(entity);
            TryBossOwner(entity, out _);
            try { _sources?.Observe(entity, now); }
            catch (Exception ex) { _plugin.Log.LogDebug($"CombatMeter spawn source unavailable: {ex.Message}"); }
        }
    }

    internal User? FindUser(ulong id)
    {
        if (!EnsureWorld()) return null;
        using var users = _userQuery.ToComponentDataArrayAccessor<User>(Allocator.Temp);
        foreach (var user in users) if (user.PlatformId == id && user.IsConnected) return user;
        return null;
    }

    internal PolicyPacket Policy(bool accepted, string reason = "") => new()
    {
        Accepted = accepted, Reason = reason, ServerSessionId = Tracker.SessionId,
        EnabledModes = Tracker.Policy.EnabledModes.ToArray(), CombatTimeoutSeconds = Tracker.Policy.TimeoutSeconds
    };

    private Participant? ResolvePlayer(Entity source)
    {
        var cursor = source;
        for (var depth = 0; depth < 32 && cursor != Entity.Null; depth++)
        {
            if (!_manager.Exists(cursor)) break;
            if (Has<PlayerCharacter>(cursor))
            {
                var character = Read<PlayerCharacter>(cursor);
                if (!Has<User>(character.UserEntity)) break;
                var user = Read<User>(character.UserEntity);
                var player = new Participant(user.PlatformId, character.Name.ToString());
                _ownership.ObservePlayer(Key(cursor), player, Now);
                break;
            }
            if (!Has<EntityOwner>(cursor)) break;
            var owner = Read<EntityOwner>(cursor).Owner;
            if (owner == cursor) break;
            _ownership.ObserveOwner(Key(cursor), Key(owner), Now);
            cursor = owner;
        }
        return _ownership.Resolve(Key(source), Now);
    }

    // Prefix/postfix surround one native resolved health operation. Measuring the target
    // directly avoids attributing a whole frame's net health loss to its last attacker.
    internal float? BeforeHealth(StatChangeEvent change)
    {
        _healthSources.Push(DamageSource.Unknown);
        HealthHookCalls++;
        if (HealthHookCalls == 1) _plugin.Log.LogInfo("CombatMeter native health hook observed its first operation.");
        if (!EnsureWorld()) return null;
        if (_audit.Value) _plugin.Log.LogInfo($"[CombatMeter:HealthOperation] source={change.Source.Index}:{change.Source.Version} target={change.Entity.Index}:{change.Entity.Version} type={change.StatType} change={change.Change:R} hasHealth={Has<Health>(change.Entity)}");
        PollEndEvents(Now);
        if (change.Change >= 0 || !Has<Health>(change.Entity)) return null;
        try
        {
            var metadata = _sources?.Resolve(change, Now) ?? DamageSource.Unknown;
            if (_healthSources.Count > 0) _healthSources.Pop();
            _healthSources.Push(metadata);
        }
        catch (Exception ex) { _plugin.Log.LogDebug($"CombatMeter damage source unavailable: {ex.Message}"); }
        ResolvePlayer(change.Source);
        ResolveCreature(change.Source);
        var health = Read<Health>(change.Entity);
        return health.IsDead || health.Value <= 0 ? null : health.Value;
    }

    internal void AfterHealth(StatChangeEvent change, float? before)
    {
        var metadata = _healthSources.TryPop(out var captured) ? captured : DamageSource.Unknown;
        if (before is null || !Has<Health>(change.Entity)) return;
        var after = Read<Health>(change.Entity).Value;
        var amount = ResolvedHealthDamage.Between(before.Value, after);
        CapturedHealthChanges++;
        if (_audit.Value) _plugin.Log.LogInfo($"[CombatMeter:ResolvedDamage] source={change.Source.Index}:{change.Source.Version} target={change.Entity.Index}:{change.Entity.Version} before={before.Value:R} after={after:R} amount={amount:R} change={change.Change:R} reason={change.Reason}");
        var source = ResolvePlayer(change.Source);
        var victim = ResolvePlayer(change.Entity);
        var creature = source is null ? ResolveCreature(change.Source) : null;
        var now = Now;
        var targetKey = Key(change.Entity);
        var sourceKey = Key(change.Source);
        EncounterKind kind;
        string? encounterKey = null;
        var targetClass = DamageTarget.Other;
        if (_contestPlayers.TryGetValue(targetKey, out var contest))
        {
            // Outsiders and NPC damage must not enter a structured match's rankings.
            if (source is null || !TryContestForPlayer(source.Value.Id, contest.Key, out var attacker)) return;
            source = source.Value with { Team = attacker.Team };
            if (victim is { } v) victim = v with { Team = contest.Team };
            kind = contest.Kind; encounterKey = contest.Key;
        }
        else if (victim is not null)
        {
            if (source is not null)
            {
                if (source.Value.Id == victim.Value.Id) return;
                kind = EncounterKind.PvP;
            }
            else if (creature is { Boss: { } bossKey })
            {
                var attackingBoss = new Entity { Index = bossKey.Index, Version = bossKey.Version };
                if (!Has<Health>(attackingBoss)) return;
                var bossHealth = Read<Health>(attackingBoss);
                if (bossHealth.IsDead || bossHealth.Value <= 0) return;
                BeginBoss(attackingBoss, now, victim);
                kind = EncounterKind.Boss; encounterKey = BossKey(attackingBoss);
            }
            else
            {
                if (creature is null) return;
                kind = EncounterKind.OpenWorld;
            }
        }
        else if (TryBossOwner(change.Entity, out var boss))
        {
            if (source is null && creature is not null) return; // NPC-vs-NPC damage is not unresolved player damage.
            kind = EncounterKind.Boss; encounterKey = BossKey(boss);
            targetClass = Key(boss) == targetKey ? DamageTarget.Boss : DamageTarget.BossSummon;
            BeginBoss(boss, now, source);
        }
        else
        {
            // Exclude structures and resource nodes from ordinary creature fights.
            if (source is null || !Has<Movement>(change.Entity) || Has<YieldResourcesOnDamageTaken>(change.Entity)) return;
            kind = EncounterKind.OpenWorld;
        }
        if (Tracker.Record(new CombatEvent($"{sourceKey}:{targetKey}:{++_serial}", now, sourceKey, targetKey,
            source, victim, amount, kind, encounterKey, targetClass,
            victim is not null && source is null ? creature : null, metadata)) is not null) RoutedDamageEvents++;
        if (_audit.Value && amount > 0) _plugin.Log.LogInfo($"[CombatMeter:DamageSource] source={metadata.Id} name={metadata.Name} statEntity={change.StatChangeEntity.Index}:{change.StatChangeEntity.Version} amount={amount:R}");
        if (kind == EncounterKind.Boss && targetClass == DamageTarget.Boss && after <= 0)
        {
            Tracker.End(kind, encounterKey!, now);
            _bosses.Remove(targetKey);
        }
    }

    private CreatureParticipant? ResolveCreature(Entity source)
    {
        var original = Key(source);
        if (_creatureOrigins.TryGetValue(original, out var cached))
        {
            _creatureOrigins[original] = (cached.Creature, Now);
            return cached.Creature;
        }
        var cursor = source;
        var seen = new HashSet<EntityKey>();
        while (_manager.Exists(cursor) && seen.Add(Key(cursor)) && seen.Count <= 32)
        {
            if (Has<PlayerCharacter>(cursor)) return null;
            if (Has<Health>(cursor) && (Has<Movement>(cursor) || Has<VBloodUnit>(cursor) || Has<UnitStats>(cursor))
                && !Has<YieldResourcesOnDamageTaken>(cursor))
            {
                var hasBoss = TryBossOwner(cursor, out var boss);
                var kind = Has<VBloodUnit>(cursor) ? CreatureKind.Boss
                    : hasBoss ? CreatureKind.BossSummon : CreatureKind.Monster;
                var creature = new CreatureParticipant(Key(cursor), CreatureName(cursor, kind), kind, hasBoss ? Key(boss) : null);
                foreach (var key in seen) _creatureOrigins[key] = (creature, Now);
                return creature;
            }
            if (!Has<EntityOwner>(cursor)) break;
            cursor = Read<EntityOwner>(cursor).Owner;
        }
        return null;
    }

    private string CreatureName(Entity entity, CreatureKind kind)
    {
        if (Has<NameableInteractable>(entity))
        {
            var name = Read<NameableInteractable>(entity).Name.ToString();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        if (Has<PrefabGUID>(entity))
        {
            var prefab = Read<PrefabGUID>(entity);
            if (_prefabNames.Count == 0)
            {
                var collection = _world!.GetExistingSystemManaged<PrefabCollectionSystem>();
                if (collection is not null)
                    foreach (var entry in collection.SpawnableNameToPrefabGuidDictionary)
                        _prefabNames[entry.Value.GuidHash] = entry.Key.Replace("CHAR_", "")
                            .Replace("_VBlood", "").Replace("_", " ");
            }
            if (_prefabNames.TryGetValue(prefab.GuidHash, out var label)) return label;
        }
        return kind switch { CreatureKind.Boss => "V Blood boss", CreatureKind.BossSummon => "Boss summon", _ => "Monster" };
    }

    private bool TryContestForPlayer(ulong id, string contestKey, out (EncounterKind Kind, string Key, int Team) result)
    {
        foreach (var entry in _contestPlayers)
        {
            if (entry.Value.Key != contestKey) continue;
            var entity = new Entity { Index = entry.Key.Index, Version = entry.Key.Version };
            if (ResolvePlayer(entity)?.Id == id) { result = entry.Value; return true; }
        }
        result = default; return false;
    }

    private string BossKey(Entity boss) => $"boss:{boss.Index}:{boss.Version}";
    private void BeginBoss(Entity boss, double now, Participant? player)
    {
        _bosses[Key(boss)] = boss;
        var label = Has<NameableInteractable>(boss) ? Read<NameableInteractable>(boss).Name.ToString() : "V Blood encounter";
        if (string.IsNullOrWhiteSpace(label)) label = "V Blood encounter";
        Tracker.Begin(EncounterKind.Boss, BossKey(boss), label, now, player is { } p ? new[] { p } : Array.Empty<Participant>());
    }

    private bool TryBossOwner(Entity entity, out Entity boss)
    {
        var original = Key(entity);
        if (_bossOwners.TryGetValue(original, out var cached) && Exists(cached))
        { boss = new Entity { Index = cached.Index, Version = cached.Version }; return true; }
        var cursor = entity;
        var seen = new HashSet<EntityKey>();
        for (var depth = 0; depth < 32 && _manager.Exists(cursor) && seen.Add(Key(cursor)); depth++)
        {
            if (Has<VBloodUnit>(cursor))
            {
                boss = cursor; _bossOwners[original] = Key(cursor); return true;
            }
            if (Has<EntityOwner>(cursor)) cursor = Read<EntityOwner>(cursor).Owner;
            else if (Has<Minion>(cursor)) cursor = Read<Minion>(cursor).SpawnEventTarget;
            else break;
        }
        boss = Entity.Null; return false;
    }

    private void PollBosses(double now)
    {
        using var bosses = _bossQuery.ToEntityArrayAccessor(Allocator.Temp);
        foreach (var boss in bosses)
        {
            if (!Has<Health>(boss) || Read<Health>(boss).IsDead || Read<Health>(boss).Value <= 0) continue;
            if (Has<BehaviourTreeState>(boss) && Read<BehaviourTreeState>(boss).Value == GenericEnemyState.Combat)
            {
                if (_manager.HasBuffer<AggroBuffer>(boss))
                {
                    var aggro = _manager.GetBuffer<AggroBuffer>(boss);
                    var roster = new List<Participant>();
                    for (var i = 0; i < aggro.Length; i++)
                        if (ResolvePlayer(aggro[i].Entity) is { } player) roster.Add(player);
                    if (roster.Count > 0)
                    {
                        var began = Has<BehaviourTreeStateMetadata>(boss) ? Read<BehaviourTreeStateMetadata>(boss).LastChangeTime : now;
                        BeginBoss(boss, Math.Min(now, began), roster[0]);
                        Tracker.AddParticipants(EncounterKind.Boss, BossKey(boss), roster);
                    }
                }
            }
        }
        foreach (var pair in _bosses.ToArray())
        {
            var boss = pair.Value;
            if (!Has<Health>(boss) || Read<Health>(boss).IsDead || Read<Health>(boss).Value <= 0)
            {
                Tracker.End(EncounterKind.Boss, BossKey(boss), now); _bosses.Remove(pair.Key);
            }
            else if (Has<BehaviourTreeState>(boss) && Read<BehaviourTreeState>(boss).Value == GenericEnemyState.Return)
            {
                Tracker.End(EncounterKind.Boss, BossKey(boss), now, EncounterStatus.Reset); _bosses.Remove(pair.Key);
            }
        }
    }

    private void PollContests(double now)
    {
        var seen = new HashSet<EntityKey>();
        _contestPlayers.Clear();
        using var contests = _contestQuery.ToEntityArrayAccessor(Allocator.Temp);
        foreach (var entity in contests)
        {
            var data = Read<ContestInstance>(entity);
            if (data.CurrentContestState != ContestInstance.ContestState.Started) continue;
            var entityKey = Key(entity); seen.Add(entityKey);
            var kind = Has<DuelInstance>(entity) ? EncounterKind.Duel : EncounterKind.Arena;
            var key = $"contest:{entity.Index}:{entity.Version}:{data.ActiveId}";
            var roster = new List<Participant>();
            if (_manager.HasBuffer<ContestParticipant>(entity))
            {
                var participants = _manager.GetBuffer<ContestParticipant>(entity);
                for (var i = 0; i < participants.Length; i++)
                {
                    var participant = participants[i];
                    if (ResolvePlayer(participant.Character) is not { } player) continue;
                    roster.Add(player with { Team = participant.ContestTeam });
                    _contestPlayers[Key(participant.Character)] = (kind, key, participant.ContestTeam);
                }
            }
            if (_contests.TryGetValue(entityKey, out var previous) && previous.Key != key)
                Tracker.End(previous.Kind, previous.Key, now);
            _contests[entityKey] = (kind, key);
            var began = data.StartTime + data.StartDuration;
            Tracker.Begin(kind, key, kind == EncounterKind.Duel ? "Duel" : "Castle arena", Math.Min(now, began), roster);
            if (kind == EncounterKind.Arena) Tracker.SetObservers(kind, key, ArenaObservers(entity, data.Creator));
        }
        foreach (var pair in _contests.Where(x => !seen.Contains(x.Key)).ToArray())
        { Tracker.End(pair.Value.Kind, pair.Value.Key, now); _contests.Remove(pair.Key); }
    }

    private IEnumerable<ulong> ArenaObservers(Entity contest, Entity creator)
    {
        var prefabs = _world!.GetExistingSystemManaged<PrefabCollectionSystem>();
        if (prefabs is null) return Array.Empty<ulong>();
        var observerPrefab = prefabs.RootPrefabs.ContestObserverBuffPrefabGuid;
        var observers = new HashSet<ulong>();
        using var buffs = _buffQuery.ToEntityArrayAccessor(Allocator.Temp);
        foreach (var entity in buffs)
        {
            if (Read<PrefabGUID>(entity) != observerPrefab) continue;
            var owner = Read<EntityOwner>(entity).Owner;
            // Membership comes from the native observer buff and its owning contest,
            // never from physical proximity to a fight.
            if (owner != contest && owner != creator) continue;
            if (ResolvePlayer(Read<Buff>(entity).Target) is { } player) observers.Add(player.Id);
        }
        return observers;
    }

    private void PollEndEvents(double now)
    {
        using var finished = _finishedQuery.ToComponentDataArrayAccessor<ContestInstance.FinishedEvent>(Allocator.Temp);
        foreach (var item in finished) EndContestId(item.ContestId, now, false);
        using var cancelled = _cancelledQuery.ToComponentDataArrayAccessor<ContestInstance.CancelledEvent>(Allocator.Temp);
        foreach (var item in cancelled) EndContestId(item.ContestId, now, true);
    }

    private void EndContestId(int id, double now, bool cancelled)
    {
        foreach (var entry in _contests.Where(x => x.Value.Key.EndsWith(":" + id, StringComparison.Ordinal)).ToArray())
        {
            Tracker.End(entry.Value.Kind, entry.Value.Key, now, cancelled ? EncounterStatus.Cancelled : EncounterStatus.Completed);
            _contests.Remove(entry.Key);
            foreach (var player in _contestPlayers.Where(x => x.Value.Key == entry.Value.Key).Select(x => x.Key).ToArray()) _contestPlayers.Remove(player);
        }
    }

    internal void ContestEnded(Entity contestEntity, bool cancelled)
    {
        if (_contests.Remove(Key(contestEntity), out var contest))
            Tracker.End(contest.Kind, contest.Key, Now, cancelled ? EncounterStatus.Cancelled : EncounterStatus.Completed);
    }

    private void OnFinished(EncounterSnapshot snapshot, IReadOnlyCollection<ulong> audience) => Transport.PublishFinal(snapshot, audience);
    private void ResetWorld()
    {
        Tracker.FinishedForAudience -= OnFinished;
        if (_world is { IsCreated: true })
            foreach (var query in new[] { _contestQuery, _userQuery, _bossQuery, _timeQuery,
                _finishedQuery, _cancelledQuery, _spawnQuery, _buffQuery })
                try { query.Dispose(); }
                catch (Exception ex) { _plugin.Log.LogDebug($"CombatMeter query already released: {ex.Message}"); }
        _world = null; _sources = null; _healthSources.Clear(); _ownership.Clear(); _creatureOrigins.Clear(); _prefabNames.Clear(); _bosses.Clear(); _bossOwners.Clear();
        _contests.Clear(); _contestPlayers.Clear(); Transport.Reset();
        _nextPush = _nextPrune = 0;
        _clockReady = false;
    }

    public void Dispose() { ResetWorld(); Transport.Dispose(); }
    internal void NotifyReload() => Transport.NotifyReload();
}
