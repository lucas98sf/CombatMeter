namespace CombatMeter.Shared;

public enum EncounterKind { Boss, Arena, Duel, PvP, OpenWorld }
public enum EncounterStatus { Active, Completed, Reset, Cancelled, Merged }
public enum DamageTarget { Other, Boss, BossSummon }
public enum MeterMetric { Damage, Dps }
public enum BossTargetFilter { BossAndSummons, BossOnly }
public enum CreatureKind { Monster, Boss, BossSummon }
public enum DamageSourceKind { Ability, Weapon, Debuff, Proc, Summon, Unknown, Other }
public enum DetailActorKind { Player, Creature }

public static class Protocol
{
    public const int Version = 4;
    public const int SourceLimit = 128;
    public const double DetailInterval = 0.5;
    public const int HistoryLimit = 20;
    public const double SnapshotInterval = 0.25;
}

// Network DTOs have public setters for the bounded JSON wire codec.
public sealed class RegistrationPacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ClientVersion { get; set; } = "0.2.1";
}

public sealed class HistoryRequestPacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ServerSessionId { get; set; } = "";
}

public sealed class PolicyPacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ServerSessionId { get; set; } = "";
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
    public EncounterKind[] EnabledModes { get; set; } = Array.Empty<EncounterKind>();
    public double CombatTimeoutSeconds { get; set; } = 10;
}

public class EncounterPacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ServerSessionId { get; set; } = "";
    public EncounterSnapshot Encounter { get; set; } = new();
}

public sealed class EncounterCompletedPacket : EncounterPacket { }

public sealed class SourceRequestPacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ServerSessionId { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public DetailActorKind ActorKind { get; set; }
    public ulong PlayerId { get; set; }
    public string CreatureId { get; set; } = "";
    public long RequestId { get; set; }
}

public sealed class SourceResponsePacket
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string ServerSessionId { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public DetailActorKind ActorKind { get; set; }
    public ulong PlayerId { get; set; }
    public string CreatureId { get; set; } = "";
    public long RequestId { get; set; }
    public long Revision { get; set; }
    public bool Available { get; set; }
    public string Reason { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string EncounterLabel { get; set; } = "";
    public EncounterKind Kind { get; set; }
    public EncounterStatus Status { get; set; }
    public double DurationSeconds { get; set; }
    public double Damage { get; set; }
    public double BossDamage { get; set; }
    public double SummonDamage { get; set; }
    public SourceTotals[] Sources { get; set; } = Array.Empty<SourceTotals>();
}

public sealed class SourceTotals
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DamageSourceKind Kind { get; set; }
    public int PrefabHash { get; set; }
    public double Damage { get; set; }
    public double BossDamage { get; set; }
    public double SummonDamage { get; set; }
}

public sealed class EncounterSnapshot
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public EncounterKind Kind { get; set; }
    public EncounterStatus Status { get; set; }
    public long Sequence { get; set; }
    public double StartedAt { get; set; }
    public double EndedAt { get; set; }
    public double DurationSeconds { get; set; }
    public string MergedInto { get; set; } = "";
    public PlayerTotals[] Players { get; set; } = Array.Empty<PlayerTotals>();
    public CreatureTotals[] Creatures { get; set; } = Array.Empty<CreatureTotals>();
    public double UnknownDamage { get; set; }
    public double UnknownBossDamage { get; set; }
}

// Enemy damage dealt to encounter participants. This is separate from player
// damage done and is never part of a player's damage-share denominator.
public sealed class CreatureTotals
{
    public string EntityId { get; set; } = "";
    public string Name { get; set; } = "";
    public CreatureKind Kind { get; set; }
    public double Damage { get; set; }
}

public sealed class PlayerTotals
{
    public ulong PlayerId { get; set; }
    public string Name { get; set; } = "";
    public int Team { get; set; }
    public double Damage { get; set; }
    public double BossDamage { get; set; }
    public double SummonDamage { get; set; }

    public double ForFilter(EncounterKind kind, BossTargetFilter filter) =>
        kind == EncounterKind.Boss && filter == BossTargetFilter.BossOnly ? BossDamage : Damage;
}


