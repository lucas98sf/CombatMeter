using System.Text.RegularExpressions;
using CombatMeter.Core;
using CombatMeter.Shared;

namespace CombatMeter.Client;

// Client presentation only: retained native sources and authoritative totals
// remain untouched, including the separation of direct hits and debuffs.
internal static class DamagePresentation
{
    private const string TargetGroupPrefix = "targets:";
    private static readonly Regex TargetSuffix = new(@"[ _]+Target[ _]*[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string TargetName(string name) => TargetSuffix.Replace(name, "").TrimEnd();
    public static bool IsTargetGroup(SourceRow source) => source.Id.StartsWith(TargetGroupPrefix, StringComparison.Ordinal);

    public static SourceRow[] Sources(SourceResponsePacket detail, BossTargetFilter filter) => SourceDetailState.Rank(detail, filter)
        .GroupBy(source =>
        {
            var name = TargetName(source.Name);
            return source.Kind is not (DamageSourceKind.Unknown or DamageSourceKind.Other) && name.Length > 0 && name != source.Name
                ? TargetGroupPrefix + (int)source.Kind + ":" + name : source.Id;
        }, StringComparer.Ordinal)
        .Select(group =>
        {
            // A stable representative provides the native icon/localized name.
            var first = group.OrderBy(x => x.Id, StringComparer.Ordinal).First();
            return first with
            {
                Id = group.Key,
                Name = group.Key.StartsWith(TargetGroupPrefix, StringComparison.Ordinal) ? TargetName(first.Name) : first.Name,
                Damage = group.Sum(x => x.Damage), Dps = group.Sum(x => x.Dps), Share = group.Sum(x => x.Share)
            };
        }).OrderByDescending(x => x.Damage).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();

    public static double BarMaximum(IEnumerable<MeterRow> players, IEnumerable<MeterRow> enemies) =>
        players.Concat(enemies).Select(x => x.Damage).DefaultIfEmpty().Max();

    public static int ColorKey(SourceRow source)
    {
        if (!IsTargetGroup(source)) return source.PrefabHash;
        // Target 01/02/03 may appear in any order. Their combined bar keeps its
        // color even when the chosen native icon representative changes.
        var hash = 2166136261u;
        foreach (var character in source.Id) hash = unchecked((hash ^ character) * 16777619u);
        return unchecked((int)hash);
    }
}
