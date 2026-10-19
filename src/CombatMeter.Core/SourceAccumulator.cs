using CombatMeter.Shared;

namespace CombatMeter.Core;

public sealed class SourceAccumulator
{
    private readonly Dictionary<string, SourceTotals> _totals = new();
    private int _named;
    public void Record(DamageSource? source, double damage, DamageTarget target)
    {
        if (damage <= 0) return;
        Add(source ?? DamageSource.Unknown, damage,
            target == DamageTarget.Boss ? damage : 0, target == DamageTarget.BossSummon ? damage : 0);
    }
    private void Add(DamageSource source, double damage, double boss, double summon)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Name)
            || !Enum.IsDefined(typeof(DamageSourceKind), source.Kind)) source = DamageSource.Unknown;
        var special = source.Kind is DamageSourceKind.Unknown or DamageSourceKind.Other;
        if (special) source = source.Kind == DamageSourceKind.Unknown ? DamageSource.Unknown : DamageSource.Other;
        if (!_totals.ContainsKey(source.Id) && !special && _named >= Protocol.SourceLimit) source = DamageSource.Other;
        if (!_totals.TryGetValue(source.Id, out var total))
        {
            total = new SourceTotals { Id = source.Id, Name = source.Name, Kind = source.Kind, PrefabHash = source.PrefabHash };
            _totals.Add(source.Id, total);
            if (source.Kind is not (DamageSourceKind.Unknown or DamageSourceKind.Other)) _named++;
        }
        total.Damage += damage; total.BossDamage += boss; total.SummonDamage += summon;
    }
    public void Merge(SourceAccumulator other)
    {
        foreach (var total in other._totals.Values)
            Add(new(total.Id, total.Name, total.Kind, total.PrefabHash), total.Damage, total.BossDamage, total.SummonDamage);
    }
    public SourceTotals[] Snapshot() => _totals.Values.Select(x => new SourceTotals
    {
        Id = x.Id, Name = x.Name, Kind = x.Kind, PrefabHash = x.PrefabHash,
        Damage = x.Damage, BossDamage = x.BossDamage, SummonDamage = x.SummonDamage
    }).ToArray();
}
