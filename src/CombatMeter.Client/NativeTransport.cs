using CombatMeter.Shared;
using CombatMeter.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
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
    public bool IsReady => _world is { IsCreated: true } && _world.EntityManager.Exists(_user)
        && _world.EntityManager.GetComponentData<User>(_user).IsConnected;

    public NativeTransport(Action<PolicyPacket> policy, Action<EncounterPacket> encounter, Action<SourceResponsePacket> sources, Action ready)
    {
        _policy = policy; _encounter = encounter; _sources = sources; _ready = ready;
        MessageBridge.ClientReceived += Receive;
    }
    public bool PollConnection(double now)
    {
        if (IsReady) return false;
        if (_world != null) { _world = null; _user = Entity.Null; _channel = Guid.NewGuid().ToString("N"); _serial = 0; _inbox.Clear(); }
        foreach (var world in World.s_AllWorlds)
        {
            if (!world.IsCreated || !world.Name.Equals("Client", StringComparison.OrdinalIgnoreCase)) continue;
            var query = world.EntityManager.CreateEntityQuery(new ComponentType[] { ComponentType.ReadOnly<LocalUser>(), ComponentType.ReadOnly<User>() });
            try
            {
                if (query.CalculateEntityCount() != 1) continue;
                var user = query.GetSingletonEntity();
                if (!world.EntityManager.GetComponentData<User>(user).IsConnected) continue;
                _world = world; _user = user; _ready(); return true;
            }
            finally { query.Dispose(); }
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
            case EncounterPacket encounter: _encounter(encounter); break;
            case SourceResponsePacket sources: _sources(sources); break;
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
