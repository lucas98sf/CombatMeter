using UnityEngine;
using Object = UnityEngine.Object;

namespace CombatMeter.Client;

// Antialiased, mipmapped silhouettes: no tiny rotated UI rectangles, new
// injected Unity types, native callbacks, or dependency on a font's glyphs.
internal sealed class HudIcons : IDisposable
{
    private readonly List<Texture2D> _textures = new();
    private readonly List<Sprite> _sprites = new();
    public Sprite Cog { get; }
    public Sprite Locked { get; }
    public Sprite Unlocked { get; }

    public HudIcons()
    {
        Cog = Create("CombatMeter Cog", Gear);
        Locked = Create("CombatMeter Locked", p => Lock(p, false));
        Unlocked = Create("CombatMeter Unlocked", p => Lock(p, true));
    }

    private Sprite Create(string name, Func<Vector2, float> distance)
    {
        const int size = 128;
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) * 2 / size - 1, (y + 0.5f) * 2 / size - 1);
                var alpha = Mathf.Clamp01(0.5f - distance(p) * size / 2);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255));
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { name = name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
        _textures.Add(texture);
        texture.SetPixels32(pixels); texture.Apply(true, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.name = name; _sprites.Add(sprite);
        return sprite;
    }

    private static float Box(Vector2 point, Vector2 half, float radius = 0)
    {
        var d = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - half + Vector2.one * radius;
        return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0) - radius;
    }
    private static Vector2 Rotate(Vector2 p, float angle) =>
        new(p.x * Mathf.Cos(angle) - p.y * Mathf.Sin(angle), p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle));
    private static float Gear(Vector2 p)
    {
        var shape = p.magnitude - 0.62f;
        for (var tooth = 0; tooth < 8; tooth++)
            shape = Mathf.Min(shape, Box(Rotate(p, tooth * Mathf.PI / 4) - new Vector2(0.67f, 0), new Vector2(0.17f, 0.13f), 0.035f));
        return Mathf.Max(shape, 0.29f - p.magnitude);
    }
    private static float Lock(Vector2 p, bool open)
    {
        var body = Box(p - new Vector2(0, -0.3f), new Vector2(0.52f, 0.38f), 0.08f);
        var hole = Mathf.Min((p - new Vector2(0, -0.22f)).magnitude - 0.09f,
            Box(p - new Vector2(0, -0.35f), new Vector2(0.045f, 0.12f)));
        body = Mathf.Max(body, -hole);
        var shacklePoint = open ? Rotate(p - new Vector2(0.36f, 0.05f), 0.55f) + new Vector2(0.36f, 0.05f) : p;
        var arch = Mathf.Max(Mathf.Abs((shacklePoint - new Vector2(0, 0.28f)).magnitude - 0.36f) - 0.085f, 0.28f - shacklePoint.y);
        var legs = Box(new Vector2(Mathf.Abs(shacklePoint.x) - 0.36f, shacklePoint.y - 0.16f), new Vector2(0.085f, 0.12f));
        return Mathf.Min(body, Mathf.Min(arch, legs));
    }

    public void Dispose()
    {
        foreach (var sprite in _sprites) if (sprite != null) Object.Destroy(sprite);
        foreach (var texture in _textures) if (texture != null) Object.Destroy(texture);
        _sprites.Clear(); _textures.Clear();
    }
}
