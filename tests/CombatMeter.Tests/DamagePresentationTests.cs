using CombatMeter.Client;
using CombatMeter.Core;
using CombatMeter.Shared;
using Xunit;

namespace CombatMeter.Tests;

public sealed class DamagePresentationTests
{
    private static SourceTotals Source(string id, string name, double damage, DamageSourceKind kind = DamageSourceKind.Ability,
        double boss = 0, int hash = 0) => new()
        { Id = id, Name = name, Kind = kind, PrefabHash = hash, Damage = damage, BossDamage = boss };

    [Fact]
    public void NumberedTargetsCombineInRankingsWithoutChangingRetainedSources()
    {
        var packet = new SourceResponsePacket
        {
            DurationSeconds = 20, Damage = 1012, Sources = new[]
            {
                Source("weapon", "Great Sword Leap Attack", 543, DamageSourceKind.Weapon),
                Source("target1", "Storm Vampire Static Chain Lightning Target 01", 201, hash: 11),
                Source("target3", "Storm Vampire Static Chain Lightning Target 03", 155, hash: 33),
                Source("target2", "Storm Vampire Static Chain Lightning Target 02", 113, hash: 22)
            }
        };
        var rows = DamagePresentation.Sources(packet, BossTargetFilter.BossAndSummons);
        Assert.Equal(2, rows.Length);
        var chain = rows.Single(x => DamagePresentation.IsTargetGroup(x));
        Assert.Equal("Storm Vampire Static Chain Lightning", chain.Name);
        Assert.Equal(469, chain.Damage);
        Assert.Equal(469d / 20, chain.Dps, 10);
        Assert.Equal(469d / 1012, chain.Share, 10);
        Assert.Equal(packet.Damage, rows.Sum(x => x.Damage));
        Assert.Equal(1, rows.Sum(x => x.Share), 10);
        Assert.Equal(4, packet.Sources.Length);
        Assert.EndsWith("Target 01", packet.Sources[1].Name);
        var before = DamagePresentation.ColorKey(chain);
        packet.Sources = packet.Sources.Reverse().ToArray();
        var after = DamagePresentation.Sources(packet, BossTargetFilter.BossAndSummons).Single(DamagePresentation.IsTargetGroup);
        Assert.Equal(chain.Id, after.Id);
        Assert.Equal(chain.PrefabHash, after.PrefabHash);
        Assert.Equal(before, DamagePresentation.ColorKey(after with { PrefabHash = 99 }));
    }

    [Fact]
    public void TargetGroupingKeepsDebuffsSummonsAndOtherAttackVariantsSeparate()
    {
        var packet = new SourceResponsePacket
        {
            Damage = 60, Sources = new[]
            {
                Source("hit", "Lightning Target 01", 10),
                Source("dot", "Lightning Target 02", 10, DamageSourceKind.Debuff),
                Source("s1", "Skeleton · Lightning Target 01", 10, DamageSourceKind.Summon),
                Source("s2", "Golem · Lightning Target 02", 10, DamageSourceKind.Summon),
                Source("combo1", "Sword Primary 01", 10, DamageSourceKind.Weapon),
                Source("combo2", "Sword Primary 02", 10, DamageSourceKind.Weapon)
            }
        };
        var rows = DamagePresentation.Sources(packet, BossTargetFilter.BossAndSummons);
        Assert.Equal(6, rows.Length);
        Assert.Equal(6, rows.Select(x => x.Id).Distinct().Count());
        Assert.Contains(rows, x => x.Name == "Skeleton · Lightning");
        Assert.Contains(rows, x => x.Name == "Golem · Lightning");
        Assert.Contains(rows, x => x.Name == "Sword Primary 01");
    }

    [Fact]
    public void TargetGroupingRespectsBossFiltersAndEnemyDamageSemantics()
    {
        var packet = new SourceResponsePacket
        {
            Kind = EncounterKind.Boss, ActorKind = DetailActorKind.Player, DurationSeconds = 10, Damage = 100, BossDamage = 40,
            Sources = new[]
            {
                Source("first", "Storm Target01", 50, boss: 10),
                Source("second", "Storm Target_02", 50, boss: 30)
            }
        };
        var boss = DamagePresentation.Sources(packet, BossTargetFilter.BossOnly).Single();
        Assert.Equal(40, boss.Damage); Assert.Equal(4, boss.Dps); Assert.Equal(1, boss.Share);
        Assert.Equal(100, DamagePresentation.Sources(packet, BossTargetFilter.BossAndSummons).Single().Damage);
        packet.ActorKind = DetailActorKind.Creature;
        Assert.Equal(100, DamagePresentation.Sources(packet, BossTargetFilter.BossOnly).Single().Damage);
        Assert.Equal("Storm", boss.Name);
        Assert.Empty(DamagePresentation.Sources(new SourceResponsePacket(), BossTargetFilter.BossOnly));
    }

    [Fact]
    public void SharedBarScaleComparesEnemiesWithPlayersAndStaysInsideViewport()
    {
        var fight = new EncounterSnapshot
        {
            Players = new[] { new PlayerTotals { Name = "Alice", Damage = 1000 } },
            Creatures = new[] { new CreatureTotals { Name = "Boss", Damage = 250 } }
        };
        var players = MeterPresentation.Players(fight, BossTargetFilter.BossAndSummons);
        var enemies = MeterPresentation.Enemies(fight);
        var max = DamagePresentation.BarMaximum(players, enemies);
        Assert.Equal(1, players.Single().Damage / max);
        Assert.Equal(0.25, enemies.Single().Damage / max);
        var strongEnemy = enemies.Single() with { Damage = 2000 };
        max = DamagePresentation.BarMaximum(players, new[] { strongEnemy });
        Assert.Equal(0.5, players.Single().Damage / max);
        Assert.Equal(1, strongEnemy.Damage / max);
        Assert.Equal(0, DamagePresentation.BarMaximum(Array.Empty<MeterRow>(), Array.Empty<MeterRow>()));
        Assert.Equal(250, DamagePresentation.BarMaximum(Array.Empty<MeterRow>(), enemies));
        Assert.Equal(1000, DamagePresentation.BarMaximum(players, Array.Empty<MeterRow>()));
    }
}
