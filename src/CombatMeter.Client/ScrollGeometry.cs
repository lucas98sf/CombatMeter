namespace CombatMeter.Client;

internal readonly record struct ScrollGeometry(float Range, float ThumbHeight, float ThumbTop, float Travel)
{
    public bool CanScroll => Range > 0;

    public static ScrollGeometry Calculate(float contentHeight, float viewportHeight, float offset)
    {
        var height = Math.Max(0, viewportHeight);
        var range = Math.Max(0, contentHeight - height);
        if (height <= 0 || range <= 0) return new(0, height, 0, 0);
        var thumb = Math.Clamp(height * (height / contentHeight), Math.Min(24, height), height);
        var travel = height - thumb;
        return new(range, thumb, travel * Math.Clamp(offset / range, 0, 1), travel);
    }

    // Coordinates are measured downward from the track's top in canvas units.
    public float OffsetAt(float pointer, float grabOffset) => Travel > 0
        ? Math.Clamp((pointer - grabOffset) / Travel, 0, 1) * Range : 0;
}
