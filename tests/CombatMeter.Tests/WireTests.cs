using CombatMeter.Shared;
using Xunit;

namespace CombatMeter.Tests;

public class WireTests
{
    private static readonly string Channel = Guid.NewGuid().ToString("N");
    [Fact]
    public void RequestsFitOneNativeMessageAndRoundTrip()
    {
        object[] requests = { new RegistrationPacket(), new HistoryRequestPacket { ServerSessionId = new string('a', 32) },
            new SourceRequestPacket { ServerSessionId = new string('a', 32), EncounterId = Guid.NewGuid().ToString("N"),
                CreatureId = "2147483647:2147483647", PlayerId = ulong.MaxValue, RequestId = long.MaxValue } };
        foreach (var request in requests)
        {
            var frame = Assert.Single(WireCodec.Encode(Channel, 1, request));
            Assert.True(frame.Length <= 500);
            var result = new WireInbox().Receive(frame, null, true, 0, out var channel);
            Assert.Equal(Channel, channel); Assert.Equal(request.GetType(), result!.GetType());
        }
    }
    [Fact]
    public void LargeDetailsReassembleOutOfOrderWithoutDuplicateDelivery()
    {
        var packet = new SourceResponsePacket { Reason = string.Join("", Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString("N"))) };
        var frames = WireCodec.Encode(Channel, 5, packet).ToArray();
        Assert.True(frames.Length > 1); Assert.All(frames, x => Assert.True(x.Length <= 500));
        var inbox = new WireInbox(); object? result = null;
        foreach (var frame in frames.Reverse()) result = inbox.Receive(frame, Channel, false, 0, out _) ?? result;
        Assert.Equal(packet.Reason, Assert.IsType<SourceResponsePacket>(result).Reason);
        foreach (var frame in frames) Assert.Null(inbox.Receive(frame, Channel, false, 0, out _));
    }
    [Fact]
    public void ConnectionChannelAndDirectionAreEnforced()
    {
        var inbox = new WireInbox();
        var response = Assert.Single(WireCodec.Encode(Channel, 1, new PolicyPacket()));
        Assert.Null(inbox.Receive(response, Guid.NewGuid().ToString("N"), false, 0, out _));
        Assert.Null(inbox.Receive(response, null, true, 0, out _));
        var request = Assert.Single(WireCodec.Encode(Channel, 2, new RegistrationPacket()));
        Assert.Null(inbox.Receive(request, Channel, false, 0, out _));
        Assert.IsType<PolicyPacket>(inbox.Receive(response, Channel, false, 0, out _));
    }
    [Theory]
    [InlineData("hello")]
    [InlineData("~CM4|bad|1|P|0|1|a")]
    [InlineData("~CM4|00000000000000000000000000000000|1|P|0|257|a")]
    [InlineData("~CM4|00000000000000000000000000000000|1|P|-1|1|a")]
    [InlineData("~CM4|00000000000000000000000000000000|1|P|0|1|not base64")]
    public void MalformedFramesAreIgnored(string frame) => Assert.Null(new WireInbox().Receive(frame, null, false, 0, out _));
    [Fact]
    public void PartialTransfersExpireAndDisconnectClearsCompletedIds()
    {
        var packet = new SourceResponsePacket { Reason = string.Join("", Enumerable.Range(0, 60).Select(_ => Guid.NewGuid().ToString("N"))) };
        var frames = WireCodec.Encode(Channel, 1, packet).ToArray(); Assert.True(frames.Length > 1);
        var inbox = new WireInbox();
        Assert.Null(inbox.Receive(frames[0], Channel, false, 0, out _));
        foreach (var frame in frames.Skip(1)) Assert.Null(inbox.Receive(frame, Channel, false, 6, out _));
        Assert.NotNull(inbox.Receive(frames[0], Channel, false, 6, out _));
        inbox.Clear();
        object? result = null;
        foreach (var frame in frames) result = inbox.Receive(frame, Channel, false, 7, out _) ?? result;
        Assert.NotNull(result);
    }
    [Fact]
    public void ExpandedClientRequestsAreBounded()
    {
        var frame = Assert.Single(WireCodec.Encode(Channel, 1, new RegistrationPacket { ClientVersion = new string('x', 50000) }));
        Assert.Null(new WireInbox().Receive(frame, null, true, 0, out _));
    }
    [Fact]
    public void ServerRejectsFragmentedRequestsAndOversizedOutgoingPackets()
    {
        var frames = WireCodec.Encode(Channel, 1, new RegistrationPacket {
            ClientVersion = string.Join("", Enumerable.Range(0, 100).Select(_ => Guid.NewGuid().ToString("N"))) }).ToArray();
        Assert.True(frames.Length > 1);
        foreach (var frame in frames) Assert.Null(new WireInbox().Receive(frame, null, true, 0, out _));
        Assert.Throws<InvalidDataException>(() => WireCodec.Encode(Channel, 2, new PolicyPacket { Reason = new string('x', WireCodec.MaxJsonBytes + 1) }).ToArray());
    }
    [Fact]
    public void ThirtyTwoPlayerSnapshotsAndFullSourceListsRemainBounded()
    {
        var random = new Random(42);
        var snapshot = new EncounterPacket { ServerSessionId = Guid.NewGuid().ToString("N"), Encounter = new() {
            Id = Guid.NewGuid().ToString("N"), Sequence = 42, DurationSeconds = 123.456,
            Players = Enumerable.Range(1, 32).Select(i => new PlayerTotals { PlayerId = (ulong)i,
                Name = "Player " + i, Damage = random.NextDouble() * 1e6, BossDamage = random.NextDouble() * 1e5 }).ToArray() } };
        var frames = WireCodec.Encode(Channel, 1, snapshot).ToArray();
        var inbox = new WireInbox(); object? result = null;
        foreach (var frame in frames) result = inbox.Receive(frame, Channel, false, 0, out _) ?? result;
        Assert.Equal(snapshot.Encounter.Players.Select(x => x.Damage), Assert.IsType<EncounterPacket>(result).Encounter.Players.Select(x => x.Damage));
        var details = new SourceResponsePacket { Sources = Enumerable.Range(1, 128).Select(i => new SourceTotals {
            Id = "Ability:" + i, Name = "Damage-producing ability " + i, Damage = random.NextDouble() * 1e6 }).ToArray() };
        frames = WireCodec.Encode(Channel, 2, details).ToArray(); result = null;
        foreach (var frame in frames) result = inbox.Receive(frame, Channel, false, 0, out _) ?? result;
        Assert.Equal(128, Assert.IsType<SourceResponsePacket>(result).Sources.Length);
        Assert.True(frames.Length <= WireCodec.MaxChunks);
    }
}
