using CombatMeter.Core;
using CombatMeter.Shared;
using Xunit;

namespace CombatMeter.Tests;

public sealed class RefinementTests
{
    private static readonly Participant Alice = new(1, "Alice");
    private static readonly Participant Bob = new(2, "Bob");
    private static CombatEvent PlayerHit(string id, double at, Participant player, double amount, DamageTarget target) =>
        new(id, at, new((int)player.Id, 1), new(50, 1), player, null, amount, EncounterKind.Boss, "boss", target);
    private static CombatEvent EnemyHit(string id, double at, CreatureParticipant enemy, Participant victim, double amount,
        EncounterKind kind = EncounterKind.Boss) =>
        new(id, at, enemy.Entity, new((int)victim.Id, 1), null, victim, amount, kind,
            kind == EncounterKind.Boss ? "boss" : null, CreatureAttacker: enemy);

    [Fact]
    public void BossAndSummonDamageRemainSeparateFromPlayersAndFilters()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice });
        tracker.Record(PlayerHit("p1", 1, Alice, 100, DamageTarget.Boss));
        tracker.Record(PlayerHit("p2", 2, Alice, 100, DamageTarget.BossSummon));
        var boss = new CreatureParticipant(new(50, 1), "Boss", CreatureKind.Boss);
        var summon = new CreatureParticipant(new(51, 1), "Summon", CreatureKind.BossSummon);
        var incoming = EnemyHit("n1", 3, boss, Alice, 70);
        Assert.NotNull(tracker.Record(incoming));
        Assert.Null(tracker.Record(incoming));
        tracker.Record(EnemyHit("n2", 4, summon, Bob, 30));
        tracker.Record(PlayerHit("p3", 9, Bob, 100, DamageTarget.Boss));
        tracker.End(EncounterKind.Boss, "boss", 10);
        var fight = tracker.History(1).Single();
        Assert.Equal(0, fight.UnknownDamage);
        Assert.Equal(300, fight.Players.Sum(x => x.Damage));
        Assert.Equal(100, fight.Creatures.Sum(x => x.Damage));
        var players = MeterPresentation.Players(fight, BossTargetFilter.BossAndSummons);
        Assert.Equal(20, players.Single(x => x.Name == "Alice").Dps);
        Assert.Equal(2d / 3, players.Single(x => x.Name == "Alice").Share);
        var bossesOnly = MeterPresentation.Players(fight, BossTargetFilter.BossOnly);
        Assert.Equal(100, bossesOnly.Single(x => x.Name == "Alice").Damage);
        Assert.All(bossesOnly, x => Assert.Equal(0.5, x.Share));
        var enemies = MeterPresentation.Enemies(fight);
        Assert.Equal(new[] { 7d, 3d }, enemies.Select(x => x.Dps));
        Assert.All(enemies, x => Assert.Null(x.Share));
        Assert.All(enemies, x => Assert.True(x.IsEnemy));
        Assert.Equal(100, tracker.History(2).Single().Creatures.Sum(x => x.Damage));
    }

    [Fact]
    public void SoloShowsDamageAndDpsWithoutPercentEvenWithEnemiesAndUnknownSources()
    {
        var fight = new EncounterSnapshot
        {
            DurationSeconds = 10, UnknownDamage = 5,
            Players = new[] { new PlayerTotals { PlayerId = 1, Name = "Alice", Damage = 123 } },
            Creatures = new[] { new CreatureTotals { EntityId = "50:1", Name = "Boss", Damage = 50, Kind = CreatureKind.Boss } }
        };
        var players = MeterPresentation.Players(fight, BossTargetFilter.BossAndSummons);
        Assert.All(players, x => Assert.Null(x.Share));
        Assert.Equal(123, players[0].Damage);
        Assert.Equal(12.3, players[0].Dps);
        Assert.Contains("123", MeterPresentation.Values(players[0]));
        Assert.Contains("DPS", MeterPresentation.Values(players[0]));
        Assert.DoesNotContain("%", MeterPresentation.Values(players[0]));
        Assert.Single(MeterPresentation.Enemies(fight));
    }

    [Fact]
    public void EnemyOnlyCombatAddsZeroDamagePlayerAndRetainsCommonDuration()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, Array.Empty<Participant>());
        var enemy = new CreatureParticipant(new(50, 1), "Boss", CreatureKind.Boss);
        tracker.Record(EnemyHit("incoming", 9, enemy, Alice, 60));
        var earlier = tracker.Active(10).Single();
        Assert.Equal(0, earlier.Players.Single().Damage);
        Assert.Equal(6, MeterPresentation.Enemies(earlier).Single().Dps);
        tracker.Record(EnemyHit("incoming2", 11, enemy, Alice, 10));
        Assert.Equal(60, earlier.Creatures.Single().Damage); // Snapshots are immutable copies.
        Assert.Equal(70, tracker.Active(12).Single().Creatures.Single().Damage);
    }

    [Fact]
    public void WorldMergesPreserveBothEnemyTotalsAndQuietTailEndpoint()
    {
        var tracker = new EncounterTracker(new TrackingPolicy
            { EnabledModes = new() { EncounterKind.OpenWorld }, TimeoutSeconds = 10 });
        var wolf = new CreatureParticipant(new(50, 1), "Wolf", CreatureKind.Monster);
        var bear = new CreatureParticipant(new(60, 1), "Bear", CreatureKind.Monster);
        tracker.Record(EnemyHit("wolf", 0, wolf, Alice, 10, EncounterKind.OpenWorld));
        tracker.Record(EnemyHit("bear", 1, bear, Bob, 20, EncounterKind.OpenWorld));
        Assert.Equal(2, tracker.ActiveCount);
        tracker.Record(EnemyHit("bridge", 2, bear, Alice, 30, EncounterKind.OpenWorld));
        Assert.Equal(1, tracker.ActiveCount);
        tracker.Tick(12);
        var fight = tracker.History(1).Single();
        Assert.Equal(2, fight.DurationSeconds);
        Assert.Equal(60, fight.Creatures.Sum(x => x.Damage));
        Assert.Equal(0, fight.Players.Sum(x => x.Damage));
        Assert.Equal(0, fight.UnknownDamage);
    }

    [Fact]
    public void EnemyEntitiesWithReusedIndicesStayDistinctAndInvalidEventsDoNotStartFights()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice });
        var creature = new CreatureParticipant(new(50, 1), "Summon", CreatureKind.BossSummon);
        tracker.Record(EnemyHit("first", 1, creature, Alice, 10));
        tracker.Record(EnemyHit("reused", 2, creature with { Entity = new(50, 2) }, Alice, 20));
        Assert.Equal(2, tracker.Active(3).Single().Creatures.Length);
        Assert.Null(tracker.Record(EnemyHit("invalid", 4, creature, Alice, 10) with { Attacker = Bob }));
        Assert.Null(tracker.Record(EnemyHit("invalid2", 4, creature, Alice, 10) with { Victim = null }));
        Assert.Null(tracker.Record(EnemyHit("pvp", 4, creature, Alice, 10, EncounterKind.PvP)));
        Assert.Equal(1, tracker.ActiveCount);
        Assert.Equal(30, tracker.Active(5).Single().Creatures.Sum(x => x.Damage));
    }

    [Fact]
    public void EnemyPacketsRoundTripAndRejectDuplicatesBadNumbersAndOldProtocol()
    {
        var state = new MeterState(); state.Apply(new PolicyPacket { Accepted = true, ServerSessionId = "s" });
        var packet = new EncounterPacket
        {
            ServerSessionId = "s", Encounter = new()
            {
                Id = "fight", Sequence = 1, DurationSeconds = 10,
                Creatures = new[] { new CreatureTotals { EntityId = "50:1", Name = "Boss", Kind = CreatureKind.Boss, Damage = 20 } }
            }
        };
        var copy = System.Text.Json.JsonSerializer.Deserialize<EncounterPacket>(System.Text.Json.JsonSerializer.Serialize(packet))!;
        Assert.True(state.Apply(copy));
        Assert.Equal(2, MeterPresentation.Enemies(state.Latest!).Single().Dps);
        Assert.False(state.Apply(copy));
        packet.Encounter.Sequence++;
        packet.Encounter.Creatures[0].Damage = double.NaN;
        Assert.False(state.Apply(packet));
        packet.Encounter.Creatures[0].Damage = 20;
        packet.Encounter.Creatures = new[] { packet.Encounter.Creatures[0], packet.Encounter.Creatures[0] };
        Assert.False(state.Apply(packet));
        packet.Encounter.Creatures = Array.Empty<CreatureTotals>();
        packet.ProtocolVersion = 1;
        Assert.False(state.Apply(packet));
        Assert.False(state.Apply(new PolicyPacket { ProtocolVersion = 1, Accepted = true, ServerSessionId = "s" }));
    }
}
