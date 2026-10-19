using CombatMeter.Core;
using CombatMeter.Shared;
using Xunit;

namespace CombatMeter.Tests;

public sealed class EncounterTests
{
    [Theory]
    [InlineData(100, 70, 30)]
    [InlineData(12, -900, 12)]
    [InlineData(100, 100, 0)]
    [InlineData(80, 100, 0)]
    [InlineData(-1, -3, 0)]
    public void ResolvedHealthDeltaExcludesAbsorptionOverkillAndHealing(double before, double after, double expected)
        => Assert.Equal(expected, ResolvedHealthDamage.Between(before, after));

    [Fact] public void MultipleSourcesInSameFrameConserveActualHealthLoss()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Arena, "arena", "Arena", 0, new[] { Alice, Bob });
        tracker.Record(Hit("a", 1, kind: EncounterKind.Arena, key: "arena", amount: ResolvedHealthDamage.Between(100, 80)));
        tracker.Record(Hit("b", 1, Bob, kind: EncounterKind.Arena, key: "arena", amount: ResolvedHealthDamage.Between(80, -50)));
        tracker.End(EncounterKind.Arena, "arena", 2);
        Assert.Equal(100, tracker.History(1).Single().Players.Sum(x => x.Damage));
    }

    [Fact] public void ObserverReceivesFinalAndPersonalHistorySurvivesOtherFights()
    {
        var tracker = new EncounterTracker();
        IReadOnlyCollection<ulong>? recipients = null;
        tracker.FinishedForAudience += (_, audience) => recipients = audience;
        tracker.Begin(EncounterKind.Arena, "watched", "Arena", 0, new[] { Alice });
        tracker.SetObservers(EncounterKind.Arena, "watched", new ulong[] { 99 });
        tracker.End(EncounterKind.Arena, "watched", 10);
        Assert.Contains(99UL, recipients!);
        for (var i = 0; i < 25; i++)
        {
            tracker.Begin(EncounterKind.Duel, i.ToString(), "Other duel", 20 + i, new[] { Bob });
            tracker.End(EncounterKind.Duel, i.ToString(), 21 + i);
        }
        Assert.Single(tracker.History(1));
        Assert.Single(tracker.History(99));
        Assert.Equal(20, tracker.History(2).Length);
    }

    [Fact] public void ReconnectGetsAuthoritativeTotalsIncludingDisconnectedPlayers()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Arena, "arena", "Arena", 0, new[] { Alice, Bob });
        tracker.Record(Hit("a", 1, kind: EncounterKind.Arena, key: "arena"));
        var state = new MeterState();
        var policy = new PolicyPacket { Accepted = true, ServerSessionId = tracker.SessionId };
        state.Apply(policy);
        state.Apply(new EncounterPacket { ServerSessionId = tracker.SessionId, Encounter = tracker.Active(2).Single() });
        state.Disconnect();
        tracker.Record(Hit("b", 3, Bob, kind: EncounterKind.Arena, key: "arena"));
        state.Apply(policy);
        Assert.True(state.Apply(new EncounterPacket { ServerSessionId = tracker.SessionId, Encounter = tracker.Active(4).Single() }));
        Assert.Equal(200, state.Latest!.Players.Sum(x => x.Damage));
        tracker.End(EncounterKind.Arena, "arena", 5);
        var complete = new EncounterCompletedPacket { ServerSessionId = tracker.SessionId, Encounter = tracker.History(1).Single() };
        var copy = System.Text.Json.JsonSerializer.Deserialize<EncounterCompletedPacket>(System.Text.Json.JsonSerializer.Serialize(complete))!;
        Assert.True(state.Apply(copy));
        Assert.Empty(state.Active);
        Assert.Equal(5, state.History.Single().DurationSeconds);
    }

    [Fact] public void PacketValidationRejectsInvalidNumbersAndDuplicateRoster()
    {
        var state = new MeterState(); state.Apply(new PolicyPacket { Accepted = true, ServerSessionId = "s" });
        var snapshot = new EncounterSnapshot { Id = "bad", Sequence = 1, Players = new[] { new PlayerTotals { PlayerId = 1, BossDamage = double.NaN } } };
        var packet = new EncounterPacket { ServerSessionId = "s", Encounter = snapshot };
        Assert.False(state.Apply(packet));
        snapshot.Players = new[] { new PlayerTotals { PlayerId = 1 }, new PlayerTotals { PlayerId = 1 } };
        Assert.False(state.Apply(packet));
        snapshot.Players = Array.Empty<PlayerTotals>(); snapshot.DurationSeconds = double.PositiveInfinity;
        Assert.False(state.Apply(packet));
    }

    [Fact] public void DisabledModesAndNearbyWorldFightDoNotPolluteBossTotals()
    {
        var tracker = new EncounterTracker();
        Assert.Null(tracker.Record(Hit("pve", 1, kind: EncounterKind.OpenWorld)));
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice });
        tracker.Record(Hit("boss", 1, kind: EncounterKind.Boss, key: "boss", targetClass: DamageTarget.Boss));
        tracker.Record(Hit("pvp", 1));
        Assert.Equal(2, tracker.ActiveCount);
        Assert.Equal(100, tracker.Active(2).Single(x => x.Kind == EncounterKind.Boss).Players.Sum(x => x.Damage));
    }
    private static readonly Participant Alice = new(1, "Alice");
    private static readonly Participant Bob = new(2, "Bob");
    private static CombatEvent Hit(string id, double at, Participant? attacker = null,
        EntityKey? target = null, EncounterKind kind = EncounterKind.PvP, string? key = null,
        DamageTarget targetClass = DamageTarget.Other, double amount = 100) =>
        new(id, at, new((int)(attacker ?? Alice).Id, 1), target ?? new(50, 1), attacker ?? Alice,
            kind == EncounterKind.PvP ? Bob : null, amount, kind, key, targetClass);

    [Fact] public void DuplicateEventsAreNotCountedTwice()
    {
        var tracker = new EncounterTracker();
        tracker.Record(Hit("a", 1)); tracker.Record(Hit("a", 1));
        Assert.Equal(100, tracker.Active(3).Single().Players.Single(x => x.PlayerId == 1).Damage);
    }

    [Fact] public void BossFilterAndCommonDenominatorIncludeLateJoiners()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice });
        tracker.Record(Hit("a", 1, kind: EncounterKind.Boss, key: "boss", targetClass: DamageTarget.Boss));
        tracker.Record(Hit("b", 9, Bob, kind: EncounterKind.Boss, key: "boss", targetClass: DamageTarget.BossSummon));
        tracker.End(EncounterKind.Boss, "boss", 10);
        var fight = tracker.History(1).Single();
        var all = MeterState.Rank(fight, MeterMetric.Dps, BossTargetFilter.BossAndSummons);
        Assert.All(all, x => Assert.Equal(10, x.Dps));
        var bosses = MeterState.Rank(fight, MeterMetric.Damage, BossTargetFilter.BossOnly);
        Assert.Equal(0, bosses.Single(x => x.Id == 2).Damage);
        Assert.Equal(1, bosses.Single(x => x.Id == 1).Share);
    }

    [Fact] public void TimeoutExcludesIdleTailAndCreatesNewSegment()
    {
        var tracker = new EncounterTracker();
        tracker.Record(Hit("a", 1)); tracker.Record(Hit("b", 5)); tracker.Tick(15);
        Assert.Equal(4, tracker.History(1).Single().DurationSeconds);
        tracker.Record(Hit("c", 16));
        Assert.Equal(100, tracker.Active(18).Single().Players.Single(x => x.PlayerId == 1).Damage);
    }

    [Fact] public void BossPhasePauseDoesNotSplitFightAndResetCreatesNewAttempt()
    {
        var tracker = new EncounterTracker();
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice });
        tracker.Record(Hit("a", 1, kind: EncounterKind.Boss, key: "boss")); tracker.Tick(100);
        Assert.Equal(1, tracker.ActiveCount);
        tracker.End(EncounterKind.Boss, "boss", 101, EncounterStatus.Reset);
        tracker.Begin(EncounterKind.Boss, "boss", "Boss", 110, new[] { Alice });
        Assert.Equal(EncounterStatus.Reset, tracker.History(1).Single().Status);
        Assert.Equal(0, tracker.Active(111).Single().Players.Single().Damage);
    }

    [Fact] public void UnrelatedFightsStaySeparateButBridgingHitMergesTotals()
    {
        var tracker = new EncounterTracker();
        tracker.Record(Hit("a", 1) with { Victim = null });
        tracker.Record(Hit("b", 2, new(3, "Chris"), new(70, 1)) with { Victim = null });
        Assert.Equal(2, tracker.ActiveCount);
        tracker.Record(Hit("c", 3, target: new(70, 1)) with { Victim = null });
        Assert.Equal(1, tracker.ActiveCount);
        Assert.Equal(300, tracker.Active(4).Single().Players.Sum(x => x.Damage));
    }

    [Fact] public void ArenaLifecycleIncludesZeroDamageRosterAndObservers()
    {
        var tracker = new EncounterTracker();
        Assert.Null(tracker.Record(Hit("countdown", 0, kind: EncounterKind.Arena, key: "round1")));
        var id = tracker.Begin(EncounterKind.Arena, "round1", "Arena", 5, new[] { Alice, Bob });
        tracker.SetObservers(EncounterKind.Arena, "round1", new ulong[] { 99 });
        Assert.True(tracker.CanObserve(id!, 99));
        tracker.End(EncounterKind.Arena, "round1", 10, EncounterStatus.Cancelled);
        Assert.Equal(2, tracker.History(1).Single().Players.Length);
        Assert.Equal(5, tracker.History(2).Single().DurationSeconds);
        Assert.Null(tracker.Record(Hit("after", 11, kind: EncounterKind.Arena, key: "round1")));
    }

    [Fact] public void OwnershipSurvivesDestroyedSourceAndRejectsEntityIndexReuseAndCycles()
    {
        var cache = new OwnershipCache();
        cache.ObservePlayer(new(1, 1), Alice, 0);
        cache.ObserveOwner(new(3, 1), new(2, 1)); cache.ObserveOwner(new(2, 1), new(1, 1));
        Assert.Equal(Alice, cache.Resolve(new(3, 1), 1));
        Assert.Null(cache.Resolve(new(3, 2), 1));
        cache.ObserveOwner(new(4, 1), new(5, 1)); cache.ObserveOwner(new(5, 1), new(4, 1));
        Assert.Null(cache.Resolve(new(4, 1), 1));
        cache.Prune(200); Assert.Equal(0, cache.Count);
    }

    [Fact] public void ClientRejectsOldDuplicateAndForeignSessionPackets()
    {
        var state = new MeterState();
        Assert.False(state.Apply(new EncounterPacket()));
        state.Apply(new PolicyPacket { Accepted = true, ServerSessionId = "server" });
        EncounterPacket Packet(long seq, string session = "server") => new()
        {
            ServerSessionId = session, Encounter = new() { Id = "fight", Sequence = seq }
        };
        Assert.True(state.Apply(Packet(2))); Assert.False(state.Apply(Packet(1)));
        Assert.False(state.Apply(Packet(2))); Assert.False(state.Apply(Packet(3, "old")));
        state.Apply(new PolicyPacket { Accepted = true, ServerSessionId = "new" });
        Assert.Empty(state.Active);
        Assert.False(state.Apply(Packet(4)));
        Assert.False(state.Apply(new PolicyPacket { ProtocolVersion = 999, Accepted = true }));
        Assert.Empty(state.Active);
    }

    [Fact] public void HistoryAndDeduplicationAreBounded()
    {
        var tracker = new EncounterTracker();
        for (var i = 0; i < 50; i++)
        {
            tracker.Begin(EncounterKind.Duel, i.ToString(), "Duel", i * 5, new[] { Alice });
            tracker.End(EncounterKind.Duel, i.ToString(), i * 5 + 1);
        }
        Assert.Equal(20, tracker.HistoryCount);
        tracker.Record(Hit("a", 300)); tracker.Tick(500);
        Assert.Equal(0, tracker.DeduplicationCount);
    }

    [Fact] public void Synthetic32PlayerArenaConservesDamageAndRankings()
    {
        var tracker = new EncounterTracker();
        var roster = Enumerable.Range(1, 32).Select(x => new Participant((ulong)x, "Player " + x, (x % 4) + 1)).ToArray();
        tracker.Begin(EncounterKind.Arena, "arena", "Arena", 0, roster);
        var live = new MeterState();
        live.Apply(new PolicyPacket { Accepted = true, ServerSessionId = tracker.SessionId });
        var updates = 0;
        for (var i = 0; i < 32000; i++)
        {
            tracker.Record(Hit(i.ToString(), i / 1000d, roster[i % 32], kind: EncounterKind.Arena, key: "arena", amount: 1));
            if (i % 250 != 0) continue;
            var packet = new EncounterPacket { ServerSessionId = tracker.SessionId, Encounter = tracker.Active(i / 1000d).Single() };
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(packet);
            Assert.True(json.Length < 32768);
            Assert.True(live.Apply(System.Text.Json.JsonSerializer.Deserialize<EncounterPacket>(json)!));
            Assert.Equal(32, MeterState.Rank(live.Latest!, MeterMetric.Dps, BossTargetFilter.BossAndSummons).Length);
            updates++;
        }
        Assert.Equal(128, updates);
        tracker.End(EncounterKind.Arena, "arena", 32);
        var fight = tracker.History(1).Single();
        Assert.Equal(32000, fight.Players.Sum(x => x.Damage));
        Assert.All(MeterState.Rank(fight, MeterMetric.Dps, BossTargetFilter.BossAndSummons), x => Assert.Equal(31.25, x.Dps));
    }
}
