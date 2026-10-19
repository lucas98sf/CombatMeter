using System.Globalization;
using CombatMeter.Shared;

namespace CombatMeter.Core;

public sealed record MeterRow(string Name, int Team, double Damage, double Dps, double? Share, bool IsEnemy, ulong PlayerId = 0, string CreatureId = "");

public static class MeterPresentation
{
    public static MeterRow[] Players(EncounterSnapshot fight, BossTargetFilter filter)
    {
        var showShares = fight.Players.Length > 1;
        var rows = MeterState.Rank(fight, MeterMetric.Damage, filter)
            .Select(x => new MeterRow(x.Name, x.Team, x.Damage, x.Dps, showShares ? x.Share : null, false, x.Id)).ToList();
        var unknown = fight.Kind == EncounterKind.Boss && filter == BossTargetFilter.BossOnly
            ? fight.UnknownBossDamage : fight.UnknownDamage;
        if (unknown > 0) rows.Add(new MeterRow("Unresolved source", 0, unknown,
            fight.DurationSeconds > 0 ? unknown / fight.DurationSeconds : 0, null, false));
        return rows.ToArray();
    }

    public static MeterRow[] Enemies(EncounterSnapshot fight) => fight.Creatures
        .OrderByDescending(x => x.Damage).ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.EntityId, StringComparer.Ordinal)
        .Select(x => new MeterRow(x.Name, 0, x.Damage,
            fight.DurationSeconds > 0 ? x.Damage / fight.DurationSeconds : 0, null, true, CreatureId: x.EntityId)).ToArray();

    public static string Values(MeterRow row) => string.Format(CultureInfo.CurrentCulture,
        "{0:N0} · {1:N1} DPS{2}", row.Damage, row.Dps,
        row.Share is { } share ? string.Format(CultureInfo.CurrentCulture, " · {0:P0}", share) : "");
}
