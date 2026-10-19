using CombatMeter.Shared;

namespace CombatMeter.Core;

public readonly record struct Participant(ulong Id, string Name, int Team = 0);

// Entity keys include the ECS entity version, preventing attribution after index reuse.
public readonly record struct EntityKey(int Index, int Version);
public readonly record struct CreatureParticipant(EntityKey Entity, string Name, CreatureKind Kind, EntityKey? Boss = null);

public sealed record CombatEvent(
    string EventId,
    double Timestamp,
    EntityKey Source,
    EntityKey Target,
    Participant? Attacker,
    Participant? Victim,
    double HealthDamage,
    EncounterKind Kind,
    string? EncounterKey = null,
    DamageTarget TargetClass = DamageTarget.Other,
    CreatureParticipant? CreatureAttacker = null,
    DamageSource? DamageSource = null);

public sealed record DamageSource(string Id, string Name, DamageSourceKind Kind, int PrefabHash = 0)
{
    public static readonly DamageSource Unknown = new("unknown", "Unknown source", DamageSourceKind.Unknown);
    public static readonly DamageSource Other = new("other", "Other sources", DamageSourceKind.Other);
}

public sealed class TrackingPolicy
{
    public HashSet<EncounterKind> EnabledModes { get; init; } = new()
    {
        EncounterKind.Boss, EncounterKind.Arena, EncounterKind.Duel, EncounterKind.PvP
    };
    public double TimeoutSeconds { get; init; } = 10;
}
