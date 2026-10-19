using CombatMeter.Shared;

namespace CombatMeter.Core;

public sealed record DetailSelection(string EncounterId, DetailActorKind Kind, ulong PlayerId = 0, string CreatureId = "");
public sealed record SourceRow(string Id, string Name, DamageSourceKind Kind, int PrefabHash, double Damage, double Dps, double Share);

public sealed class SourceDetailState
{
    private readonly Dictionary<DetailSelection, SourceResponsePacket> _cache = new();
    private string _session = "";
    private long _serial = DateTime.UtcNow.Ticks;
    private long _firstRequest;
    private long _lastSent;
    private long _lastAccepted;
    private double _next;
    private double _waitingSince;
    public DetailSelection? Selection { get; private set; }
    public SourceResponsePacket? Current => Selection is { } selection && _cache.TryGetValue(selection, out var data) ? data : null;
    public string Message { get; private set; } = "Loading damage sources…";

    public void Select(string session, DetailSelection? selection, double now)
    {
        if (_session != session) { Clear(); _session = session; }
        if (Selection == selection) return;
        Selection = selection; _next = 0; _waitingSince = now; _firstRequest = _serial + 1;
        _lastSent = _lastAccepted = 0; Message = "Loading damage sources…";
    }
    public SourceRequestPacket? Poll(double now)
    {
        if (Selection is not { } selection || string.IsNullOrEmpty(_session)) return null;
        var current = Current;
        if (current is { Available: true, Status: not EncounterStatus.Active }) return null;
        if (now - _waitingSince >= 5 && current is null) Message = "Source details unavailable";
        if (now < _next) return null;
        _next = now + Protocol.DetailInterval;
        _lastSent = ++_serial;
        return new SourceRequestPacket
        {
            ServerSessionId = _session, EncounterId = selection.EncounterId, ActorKind = selection.Kind,
            PlayerId = selection.PlayerId, CreatureId = selection.CreatureId, RequestId = _lastSent
        };
    }
    public bool Apply(SourceResponsePacket packet)
    {
        if (Selection is not { } selected || packet.ProtocolVersion != Protocol.Version || packet.ServerSessionId != _session
            || selected != new DetailSelection(packet.EncounterId, packet.ActorKind, packet.PlayerId, packet.CreatureId)
            || packet.RequestId < _firstRequest || packet.RequestId > _lastSent || packet.RequestId <= _lastAccepted) return false;
        if (packet.Available)
        {
            if (!Enum.IsDefined(typeof(EncounterKind), packet.Kind) || !Enum.IsDefined(typeof(EncounterStatus), packet.Status)
                || packet.Status == EncounterStatus.Merged || packet.Revision <= 0 || packet.Sources is null
                || packet.Sources.Length > Protocol.SourceLimit + 2 || string.IsNullOrWhiteSpace(packet.ActorName)
                || !Finite(packet.DurationSeconds) || !Finite(packet.Damage) || !Finite(packet.BossDamage) || !Finite(packet.SummonDamage)
                || packet.Sources.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name)
                    || !Enum.IsDefined(typeof(DamageSourceKind), x.Kind) || !Finite(x.Damage) || !Finite(x.BossDamage) || !Finite(x.SummonDamage)
                    || x.BossDamage + x.SummonDamage > x.Damage + 1e-6)
                || packet.Sources.Select(x => x.Id).Distinct().Count() != packet.Sources.Length
                || !Close(packet.Sources.Sum(x => x.Damage), packet.Damage)
                || !Close(packet.Sources.Sum(x => x.BossDamage), packet.BossDamage)
                || !Close(packet.Sources.Sum(x => x.SummonDamage), packet.SummonDamage)) return false;
            if (Current is { Available: true } previous && packet.Revision <= previous.Revision) return false;
        }
        _lastAccepted = packet.RequestId;
        if (_cache.Count >= 512 && !_cache.ContainsKey(selected)) _cache.Remove(_cache.Keys.First());
        _cache[selected] = packet;
        Message = packet.Available ? packet.Sources.Length == 0 ? "No damage recorded" : "" : "Source details unavailable";
        return true;
    }
    public void Clear()
    {
        _cache.Clear(); Selection = null; _session = ""; _firstRequest = _lastSent = _lastAccepted = 0; _next = 0;
        Message = "Loading damage sources…";
    }
    private static bool Finite(double value) => double.IsFinite(value) && value >= 0;
    private static bool Close(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-9);
    public static SourceRow[] Rank(SourceResponsePacket detail, BossTargetFilter filter)
    {
        var bossOnly = detail.ActorKind == DetailActorKind.Player && detail.Kind == EncounterKind.Boss && filter == BossTargetFilter.BossOnly;
        var total = bossOnly ? detail.BossDamage : detail.Damage;
        return detail.Sources.Select(x =>
        {
            var amount = bossOnly ? x.BossDamage : x.Damage;
            return new SourceRow(x.Id, x.Name, x.Kind, x.PrefabHash, amount,
                detail.DurationSeconds > 0 ? amount / detail.DurationSeconds : 0, total > 0 ? amount / total : 0);
        }).Where(x => x.Damage > 0).OrderByDescending(x => x.Damage).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
}
