using CombatMeter.Core;
using CombatMeter.Shared;
using Xunit;

namespace CombatMeter.Tests;

public sealed class SourceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public SourceTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
    private static readonly Participant Alice = new(1, "Alice");
    private static readonly Participant Bob = new(2, "Bob");
    private static readonly DamageSource Spell = new("spell", "Chaos Volley", DamageSourceKind.Ability, 123);
    private static readonly DamageSource Burn = new("burn", "Ignite", DamageSourceKind.Debuff, 456);
    private static CombatEvent Hit(string id, double at, DamageSource? source, double damage = 10,
        DamageTarget target = DamageTarget.Boss, Participant? player = null) =>
        new(id, at, new((int)(player ?? Alice).Id, 1), new(50, 1), player ?? Alice, null, damage,
            EncounterKind.Boss, "boss", target, DamageSource: source);
    private static SourceRequestPacket Request(EncounterTracker tracker, string fight, ulong player = 1, long id = 1) =>
        new() { ServerSessionId = tracker.SessionId, EncounterId = fight, PlayerId = player, RequestId = id };

    [Fact]
    public void SourcesReconcileAndFiltersUseCommonDuration()
    {
        var tracker = new EncounterTracker();
        var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice })!;
        var hit = Hit("spell", 1, Spell, 100);
        tracker.Record(hit); Assert.Null(tracker.Record(hit));
        tracker.Record(Hit("burn", 2, Burn, 50, DamageTarget.BossSummon));
        tracker.Record(Hit("unknown", 3, null, 20));
        tracker.Record(Hit("late", 9, Spell, 30, player: Bob));
        tracker.End(EncounterKind.Boss, "boss", 10);
        var details = tracker.Details(1, Request(tracker, fight), 20);
        Assert.Equal(170, details.Sources.Sum(x => x.Damage));
        Assert.Equal(10, details.DurationSeconds);
        Assert.Equal(3, details.Sources.Length);
        var all = SourceDetailState.Rank(details, BossTargetFilter.BossAndSummons);
        Assert.Equal(17, all.Sum(x => x.Dps)); Assert.Equal(1, all.Sum(x => x.Share), 10);
        var boss = SourceDetailState.Rank(details, BossTargetFilter.BossOnly);
        Assert.Equal(120, boss.Sum(x => x.Damage)); Assert.DoesNotContain(boss, x => x.Id == "burn");
        Assert.Equal(3, SourceDetailState.Rank(tracker.Details(1, Request(tracker, fight, 2), 20), BossTargetFilter.BossOnly).Single().Dps);
        Assert.Equal(10, tracker.Details(1, Request(tracker, fight), 999).DurationSeconds);
    }

    [Fact]
    public void OverflowMergeAndSnapshotsConserveSources()
    {
        var a = new SourceAccumulator(); var b = new SourceAccumulator();
        for (var i = 0; i < 100; i++) a.Record(new("a" + i, "Attack", DamageSourceKind.Weapon), 1, DamageTarget.Boss);
        for (var i = 0; i < 100; i++) b.Record(new("b" + i, "Debuff", DamageSourceKind.Debuff), 2, DamageTarget.BossSummon);
        a.Record(null, 3, DamageTarget.Boss);
        var before = a.Snapshot(); a.Merge(b);
        var after = a.Snapshot(); Assert.Equal(303, after.Sum(x => x.Damage));
        Assert.Equal(103, after.Sum(x => x.BossDamage)); Assert.Equal(200, after.Sum(x => x.SummonDamage));
        Assert.Equal(128, after.Count(x => x.Kind is not (DamageSourceKind.Unknown or DamageSourceKind.Other)));
        Assert.Equal(144, after.Single(x => x.Kind == DamageSourceKind.Other).Damage);
        Assert.Equal(103, before.Sum(x => x.Damage));
    }

    [Fact]
    public void EnemyAndPlayerBreakdownsNeverMix()
    {
        var tracker = new EncounterTracker();
        var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice })!;
        tracker.Record(Hit("p", 1, Spell, 20));
        tracker.Record(new("c", 2, new(50, 1), new(1, 1), null, Alice, 40, EncounterKind.Boss, "boss",
            CreatureAttacker: new(new(50, 1), "Boss", CreatureKind.Boss), DamageSource: Burn));
        var request = Request(tracker, fight); request.ActorKind = DetailActorKind.Creature; request.PlayerId = 0; request.CreatureId = "50:1";
        var enemy = tracker.Details(1, request, 10);
        Assert.Equal(40, enemy.Damage); Assert.Equal("burn", enemy.Sources.Single().Id);
        Assert.Equal(4, SourceDetailState.Rank(enemy, BossTargetFilter.BossOnly).Single().Dps);
        Assert.Equal(20, tracker.Details(1, Request(tracker, fight), 10).Damage);
        request.CreatureId = "50:2"; Assert.False(tracker.Details(1, request, 10).Available);
    }

    [Fact]
    public void PersonalHistoryAndObserversControlAccessAndRetention()
    {
        var tracker = new EncounterTracker();
        var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice })!;
        tracker.SetObservers(EncounterKind.Boss, "boss", new ulong[] { 99 });
        tracker.Record(Hit("p", 1, Spell));
        Assert.False(tracker.Details(2, Request(tracker, fight), 2).Available);
        Assert.True(tracker.Details(99, Request(tracker, fight), 2).Available);
        tracker.End(EncounterKind.Boss, "boss", 2, EncounterStatus.Reset);
        for (var i = 0; i < 25; i++)
        { tracker.Begin(EncounterKind.Duel, "d" + i, "Duel", 10 + i, new[] { Bob }); tracker.End(EncounterKind.Duel, "d" + i, 11 + i); }
        Assert.True(tracker.Details(1, Request(tracker, fight), 40).Available);
        for (var i = 0; i < 20; i++)
        { tracker.Begin(EncounterKind.Duel, "a" + i, "Duel", 50 + i, new[] { Alice }); tracker.End(EncounterKind.Duel, "a" + i, 51 + i); }
        Assert.False(tracker.Details(1, Request(tracker, fight), 80).Available);
        Assert.True(tracker.Details(99, Request(tracker, fight), 80).Available);
    }

    [Fact]
    public void VanishedEffectsVersionReuseAndExpirationAreSafe()
    {
        var cache = new SourceOriginCache(); cache.Observe(new(42, 1), Spell, 0);
        Assert.Equal(Spell, cache.Resolve(new(42, 1), 1));
        Assert.Null(cache.Resolve(new(42, 2), 1)); Assert.Null(cache.Resolve(new(42, 1), 122));
        cache.Prune(122); cache.Observe(new(42, 2), Burn, 122);
        Assert.Equal(Burn, cache.Resolve(new(42, 2), 123)); cache.Clear(); Assert.Null(cache.Resolve(new(42, 2), 123));
    }

    [Fact]
    public void RepliesRejectDuplicatesStaleSelectionsVersionsSessionsAndInvalidTotals()
    {
        var tracker = new EncounterTracker(); var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice, Bob })!;
        tracker.Record(Hit("p", 1, Spell)); var state = new SourceDetailState();
        state.Select(tracker.SessionId, new(fight, DetailActorKind.Player, 1), 0);
        var first = state.Poll(0)!; Assert.Null(state.Poll(0.49)); var second = state.Poll(0.5)!;
        var fresh = tracker.Details(1, second, 2); Assert.True(state.Apply(fresh)); Assert.False(state.Apply(fresh));
        Assert.False(state.Apply(tracker.Details(1, first, 2)));
        state.Select(tracker.SessionId, new(fight, DetailActorKind.Player, 2), 3); Assert.False(state.Apply(fresh));
        var request = state.Poll(3)!; var packet = tracker.Details(1, request, 3);
        packet.ProtocolVersion = 2; Assert.False(state.Apply(packet)); packet.ProtocolVersion = Protocol.Version;
        packet.ServerSessionId = "other"; Assert.False(state.Apply(packet)); packet.ServerSessionId = tracker.SessionId;
        packet.Damage = 5; Assert.False(state.Apply(packet)); packet.Damage = 0; Assert.True(state.Apply(packet));
        Assert.Equal("No damage recorded", state.Message);
        state.Clear(); Assert.Null(state.Current); Assert.False(state.Apply(packet));
    }

    [Fact]
    public void CompletedRepliesAreCachedAndUnavailableResponsesRecover()
    {
        var tracker = new EncounterTracker(); var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, new[] { Alice })!;
        tracker.Record(Hit("p", 1, Spell)); tracker.End(EncounterKind.Boss, "boss", 2);
        var state = new SourceDetailState(); var selection = new DetailSelection(fight, DetailActorKind.Player, 1);
        state.Select(tracker.SessionId, selection, 0);
        var request = state.Poll(0)!; var packet = tracker.Details(1, request, 2);
        packet.Available = false; Assert.True(state.Apply(packet)); Assert.Equal("Source details unavailable", state.Message);
        request = state.Poll(0.5)!; Assert.True(state.Apply(tracker.Details(1, request, 2)));
        Assert.Null(state.Poll(10)); state.Select(tracker.SessionId, null, 10); state.Select(tracker.SessionId, selection, 11);
        Assert.NotNull(state.Current); Assert.Null(state.Poll(11));
        state.Select("new", selection, 12); Assert.Null(state.Current); Assert.NotNull(state.Poll(12));
    }

    [Fact]
    public void ConnectedWorldMergeRetainsSeparateSourceBuckets()
    {
        var tracker = new EncounterTracker(new TrackingPolicy { EnabledModes = new() { EncounterKind.PvP } });
        var a = new CombatEvent("a", 1, new(1, 1), new(20, 1), Alice, new(20, "X"), 10, EncounterKind.PvP, DamageSource: Spell);
        var b = new CombatEvent("b", 1, new(2, 1), new(30, 1), Bob, new(30, "Y"), 20, EncounterKind.PvP, DamageSource: Burn);
        var first = tracker.Record(a)!; var retired = tracker.Record(b)!;
        tracker.Record(new("join", 2, new(1, 1), new(2, 1), Alice, Bob, 5, EncounterKind.PvP, DamageSource: Burn));
        Assert.False(tracker.Details(1, Request(tracker, retired), 3).Available);
        Assert.Equal(15, tracker.Details(1, Request(tracker, first), 3).Sources.Sum(x => x.Damage));
        Assert.Equal(20, tracker.Details(1, Request(tracker, first, 2), 3).Sources.Sum(x => x.Damage));
        tracker.Tick(12); Assert.Equal(1, tracker.Details(1, Request(tracker, first), 100).DurationSeconds);
    }

    [Fact]
    public void ThirtyTwoPlayersOnlyRequestTheInspectedParticipant()
    {
        var tracker = new EncounterTracker(); var players = Enumerable.Range(1, 32).Select(x => new Participant((ulong)x, "Player " + x)).ToArray();
        var fight = tracker.Begin(EncounterKind.Boss, "boss", "Boss", 0, players)!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 32000; i++) tracker.Record(Hit(i.ToString(), 1 + i / 1000d,
            new("source" + i % 19, "Attack " + i % 19, DamageSourceKind.Ability), player: players[i % 32]));
        var state = new SourceDetailState(); state.Select(tracker.SessionId, new(fight, DetailActorKind.Player, 1), 0);
        var accountingMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
        var packets = 0; var bytes = 0;
        for (var tick = 0; tick < 1000; tick++) if (state.Poll(tick / 100d) is { } request)
        {
            packets++; var response = tracker.Details(1, request, 34 + tick / 100d);
            bytes += System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response).Length;
            Assert.Equal(19, response.Sources.Length); Assert.True(state.Apply(response));
        }
        Assert.InRange(packets, 19, 20); Assert.Equal(10000, state.Current!.Damage);
        Assert.DoesNotContain("Sources", System.Text.Json.JsonSerializer.Serialize(tracker.Active(45).Single()));
        _output.WriteLine($"32 players / 32000 events: accounting={accountingMs:F1}ms; detail queries={watch.Elapsed.TotalMilliseconds:F1}ms; 10 simulated seconds={packets} replies / {bytes} JSON bytes. Emberglass framing adds overhead.");
    }
}
