namespace CombatMeter.Core;

public static class ResolvedHealthDamage
{
    // Health can cross below zero on a lethal hit; only the remaining health counts.
    public static double Between(double before, double after) =>
        double.IsFinite(before) && double.IsFinite(after)
            ? Math.Max(0, Math.Max(0, before) - Math.Max(0, after)) : 0;
}
