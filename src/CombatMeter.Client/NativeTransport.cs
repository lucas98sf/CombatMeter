using CombatMeter.Shared;
using CombatMeter.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace CombatMeter.Client;

internal sealed class NativeTransport : IDisposable
{
    private readonly Action<PolicyPacket> _policy;
    private readonly Action<EncounterPacket> _encounter;
    private readonly Action<SourceResponsePacket> _sources;
    private readonly Action _ready;
    private readonly WireInbox _inbox = new();
    private World? _world;
    private Entity _user;
    private string _channel = Guid.NewGuid().ToString("N");
    private long _serial;
    private double _nextSearch;
    private bool _receivedEncounter, _receivedSources;
    public bool IsReady => _world is { IsCreated: true } && _world.EntityManager.Exists(_user)
        && _world.EntityManager.HasComponent<User>(_user)
        && _world.EntityManager.HasComponent<NetworkId>(_user);

    public NativeTransport(Action<PolicyPacket> policy, Action<EncounterPacket> encounter, Action<SourceResponsePacket> sources, Action ready)
    {
        _policy = policy; _encounter = encounter; _sources = sources; _ready = ready;
        MessageBridge.ClientReceived += Receive;
    }
    public bool PollConnection(double now)
    {
        if (IsReady) return false;
        if (_world != null) { _world = null; _user = Entity.Null; _channel = Guid.NewGuid().ToString("N"); _serial = 0; _inbox.Clear(); }
        if (now < _nextSearch) return false;
        _nextSearch = now + 1;
        foreach (var world in World.s_AllWorlds)
        {
            // World names can include a numeric suffix. Native local-player data
            // identifies the active client world without depending on its name.
            if (!world.IsCreated) continue;
            // User.IsConnected is server-side state. Query the actual local user
            // entity; the interop ref-return LocalUser accessor is not reliable here.
            var users = world.EntityManager.CreateEntityQuery(new ComponentType[] {
                ComponentType.ReadOnly<LocalUser>(), ComponentType.ReadOnly<User>(), ComponentType.ReadOnly<NetworkId>() });
            try
            {
                if (users.CalculateEntityCount() == 1)
                {
                    var candidate = users.GetSingletonEntity();
                    _world = world; _user = candidate;
                    if (IsReady)
                    {
                        Plugin.Instance.Log.LogInfo($"[CombatMeter:Transport] local user ready in {world.Name}");
                        _ready(); return true;
                    }
                    _world = null;
                }
            }
            finally { users.Dispose(); }
        }
        return false;
    }
    private void Receive(string frame)
    {
        if (!IsReady) return;
        var packet = _inbox.Receive(frame, _channel, false, UnityEngine.Time.realtimeSinceStartupAsDouble, out _);
        switch (packet)
        {
            case PolicyPacket policy: _policy(policy); break;
            case EncounterPacket encounter:
                if (!_receivedEncounter) Plugin.Instance.Log.LogInfo($"[CombatMeter:Transport] first encounter received; players={encounter.Encounter.Players.Length}; creatures={encounter.Encounter.Creatures.Length}");
                _receivedEncounter = true; _encounter(encounter); break;
            case SourceResponsePacket sources:
                if (!_receivedSources) Plugin.Instance.Log.LogInfo($"[CombatMeter:Transport] first source response received; available={sources.Available}; sources={sources.Sources.Length}");
                _receivedSources = true; _sources(sources); break;
        }
    }
    private void Send<T>(T packet)
    {
        if (!IsReady) return;
        foreach (var frame in WireCodec.Encode(_channel, ++_serial, packet)) MessageBridge.SendClient(_world!, _user, frame);
    }
    public void Register() => Send(new RegistrationPacket());
    public void RequestHistory(string sessionId) => Send(new HistoryRequestPacket { ServerSessionId = sessionId });
    public void RequestSources(SourceRequestPacket packet) => Send(packet);
    public void Dispose() { MessageBridge.ClientReceived -= Receive; _inbox.Clear(); _world = null; }
}
