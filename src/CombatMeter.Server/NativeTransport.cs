using CombatMeter.Shared;
using CombatMeter.Runtime;
using Unity.Entities;
using ProjectM.Network;

namespace CombatMeter.Server;

// Typed packets over directed native messages; identities come from FromCharacter.User.
internal sealed class NativeTransport : IDisposable
{
    private readonly ServerRuntime _runtime;
    private readonly Plugin _plugin;
    private readonly HashSet<ulong> _subscribers = new();
    private readonly Dictionary<ulong, double> _historyRequested = new();
    private readonly Dictionary<ulong, double> _detailsRequested = new();
    private sealed class Peer
    {
        public string Channel = "";
        public readonly WireInbox Inbox = new();
        public long Serial;
        public double Window, LastRegistration = double.NegativeInfinity;
        public int Requests;
    }
    private readonly Dictionary<ulong, Peer> _peers = new();
    internal int SubscriberCount => _subscribers.Count;

    public NativeTransport(ServerRuntime runtime, Plugin plugin)
    {
        _runtime = runtime; _plugin = plugin;
        MessageBridge.ServerReceived += Receive;
    }

    private void Receive(EntityManager manager, Entity senderEntity, string frame)
    {
        var sender = manager.GetComponentData<User>(senderEntity);
        // Only connected native users may allocate a peer. Requests never supply an identity.
        if (!sender.IsConnected) return;
        if (!_peers.TryGetValue(sender.PlatformId, out var peer))
        {
            if (_peers.Count >= 128) return;
            _peers[sender.PlatformId] = peer = new();
        }
        var now = _runtime.Now;
        if (now - peer.Window >= 1) { peer.Window = now; peer.Requests = 0; }
        if (++peer.Requests > 12) return;
        var packet = peer.Inbox.Receive(frame, null, true, now, out var channel);
        if (packet is RegistrationPacket registration)
        {
            if (now - peer.LastRegistration < 2) return;
            peer.LastRegistration = now; peer.Channel = channel;
            Register(sender, registration);
        }
        else if (channel == peer.Channel)
        {
            if (packet is HistoryRequestPacket history) History(sender, history);
            else if (packet is SourceRequestPacket source) Details(sender, source);
        }
    }
    private void SendPacket<T>(User user, T packet)
    {
        if (!_peers.TryGetValue(user.PlatformId, out var peer) || peer.Channel.Length == 0) return;
        foreach (var frame in WireCodec.Encode(peer.Channel, ++peer.Serial, packet))
            MessageBridge.SendServer(_runtime.Manager, user, frame);
    }

    private void Register(User sender, RegistrationPacket packet)
    {
        if (!sender.IsConnected) return;
        var accepted = packet.ProtocolVersion == Protocol.Version;
        SendPacket(sender, _runtime.Policy(accepted, accepted ? "" : "CombatMeter protocol version does not match the server"));
        if (!accepted) { _subscribers.Remove(sender.PlatformId); return; }
        _subscribers.Add(sender.PlatformId);
        _plugin.Log.LogInfo($"[CombatMeter:Transport] client registration received; user={sender.PlatformId}; protocol={packet.ProtocolVersion}");
        SendHistory(sender);
    }

    private void History(User sender, HistoryRequestPacket packet)
    {
        if (!_subscribers.Contains(sender.PlatformId) || packet.ProtocolVersion != Protocol.Version
            || packet.ServerSessionId != _runtime.Tracker.SessionId) return;
        var now = _runtime.Now;
        if (_historyRequested.TryGetValue(sender.PlatformId, out var last) && now - last < 1) return;
        _historyRequested[sender.PlatformId] = now;
        SendHistory(sender);
    }

    private void Details(User sender, SourceRequestPacket packet)
    {
        if (!sender.IsConnected || !_subscribers.Contains(sender.PlatformId) || packet.ProtocolVersion != Protocol.Version
            || packet.ServerSessionId != _runtime.Tracker.SessionId) return;
        var now = _runtime.Now;
        if (_detailsRequested.TryGetValue(sender.PlatformId, out var last) && now - last < Protocol.DetailInterval - 0.02) return;
        _detailsRequested[sender.PlatformId] = now;
        try { SendPacket(sender, _runtime.Tracker.Details(sender.PlatformId, packet, now)); }
        catch (Exception ex) { _plugin.Log.LogWarning($"CombatMeter source delivery failed: {ex.Message}"); }
    }

    private void SendHistory(User user)
    {
        foreach (var snapshot in _runtime.Tracker.History(user.PlatformId).Reverse()) Send(user, snapshot);
        foreach (var snapshot in _runtime.Tracker.Active(_runtime.Now))
            if (_runtime.Tracker.CanObserve(snapshot.Id, user.PlatformId)) Send(user, snapshot);
    }

    public void Flush(double now)
    {
        foreach (var id in _peers.Keys.Where(id => _runtime.FindUser(id) is null).ToArray())
        { _peers.Remove(id); _subscribers.Remove(id); _historyRequested.Remove(id); _detailsRequested.Remove(id); }
        var snapshots = _runtime.Tracker.Active(now);
        foreach (var id in _subscribers.ToArray())
        {
            if (_runtime.FindUser(id) is not { } user) { _subscribers.Remove(id); _historyRequested.Remove(id); _detailsRequested.Remove(id); _peers.Remove(id); continue; }
            foreach (var snapshot in snapshots)
                if (_runtime.Tracker.CanObserve(snapshot.Id, id)) Send(user, snapshot);
        }
    }

    public void PublishFinal(EncounterSnapshot snapshot, IReadOnlyCollection<ulong> audience)
    {
        foreach (var id in _subscribers.ToArray())
        {
            if (!audience.Contains(id)) continue;
            if (_runtime.FindUser(id) is { } user) Send(user, snapshot);
        }
    }

    private void Send(User user, EncounterSnapshot snapshot)
    {
        try
        {
            if (snapshot.Status == EncounterStatus.Active)
                SendPacket(user, new EncounterPacket { ServerSessionId = _runtime.Tracker.SessionId, Encounter = snapshot });
            else
                SendPacket(user, new EncounterCompletedPacket { ServerSessionId = _runtime.Tracker.SessionId, Encounter = snapshot });
        }
        catch (Exception ex) { _plugin.Log.LogWarning($"CombatMeter packet delivery failed: {ex.Message}"); }
    }

    public void Reset() { _subscribers.Clear(); _historyRequested.Clear(); _detailsRequested.Clear(); _peers.Clear(); }
    internal void NotifyReload()
    {
        foreach (var id in _subscribers.ToArray())
            if (_runtime.FindUser(id) is { } user)
                try { SendPacket(user, _runtime.Policy(false, "CombatMeter collector reloading")); }
                catch (Exception ex) { _plugin.Log.LogDebug($"Reload notification unavailable: {ex.Message}"); }
    }
    public void Dispose()
    {
        Reset();
        MessageBridge.ServerReceived -= Receive;
    }
}
