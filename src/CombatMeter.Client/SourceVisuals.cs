using CombatMeter.Core;
using CombatMeter.Shared;
using ProjectM;
using Stunlock.Core;
using Stunlock.Localization;
using Unity.Entities;
using UnityEngine;

namespace CombatMeter.Client;

internal sealed class SourceVisuals
{
    private readonly Dictionary<int, (string? Name, Sprite? Icon)> _cache = new();
    private readonly Dictionary<int, double> _retryAfter = new();
    private int _languageVersion = -1;
    public (string Name, Sprite? Icon, Color Color) Get(SourceRow source)
    {
        var (hash, fallback, kind) = (source.PrefabHash, source.Name, source.Kind);
        var color = SourceColor(DamagePresentation.ColorKey(source), fallback, kind);
        if (hash == 0) return (fallback, null, color);
        if (_languageVersion != Localization.Version) { _cache.Clear(); _retryAfter.Clear(); _languageVersion = Localization.Version; }
        _cache.TryGetValue(hash, out var visual);
        if ((visual.Icon == null || string.IsNullOrWhiteSpace(visual.Name))
            && (!_retryAfter.TryGetValue(hash, out var retry) || Time.realtimeSinceStartupAsDouble >= retry))
        {
            var found = Find(hash);
            visual = (found.Name ?? visual.Name, found.Icon != null ? found.Icon : visual.Icon);
            // Native UI data can arrive after the first encounter packet.
            _cache[hash] = visual;
            _retryAfter[hash] = Time.realtimeSinceStartupAsDouble + 2;
        }
        // Preserve summon prefixes even if an ability has a localized name.
        var prefix = fallback.Contains(" · ") ? fallback[..(fallback.LastIndexOf(" · ", StringComparison.Ordinal) + 3)] : "";
        var displayName = visual.Name is { Length: > 0 } name ? prefix + name : fallback;
        if (DamagePresentation.IsTargetGroup(source)) displayName = DamagePresentation.TargetName(displayName);
        return (displayName, visual.Icon, color);
    }
    private static Color SourceColor(int hash, string name, DamageSourceKind kind)
    {
        if (kind is DamageSourceKind.Unknown or DamageSourceKind.Other) return new Color(0.58f, 0.6f, 0.65f);
        // Server fallback names retain the source prefab identity, independently
        // of the client's display language. These colors are presentation only.
        var color = kind == DamageSourceKind.Weapon ? new Color(0.86f, 0.66f, 0.32f)
            : name.Contains("Chaos", StringComparison.OrdinalIgnoreCase) ? new Color(0.8f, 0.4f, 0.95f)
            : name.Contains("Blood", StringComparison.OrdinalIgnoreCase) ? new Color(0.92f, 0.26f, 0.37f)
            : name.Contains("Frost", StringComparison.OrdinalIgnoreCase) ? new Color(0.35f, 0.73f, 0.95f)
            : name.Contains("Storm", StringComparison.OrdinalIgnoreCase) ? new Color(0.95f, 0.79f, 0.3f)
            : name.Contains("Unholy", StringComparison.OrdinalIgnoreCase) ? new Color(0.51f, 0.81f, 0.3f)
            : name.Contains("Illusion", StringComparison.OrdinalIgnoreCase) ? new Color(0.3f, 0.83f, 0.7f)
            : kind switch
            {
                DamageSourceKind.Debuff => new Color(0.87f, 0.43f, 0.29f),
                DamageSourceKind.Proc => new Color(0.73f, 0.53f, 0.93f),
                DamageSourceKind.Summon => new Color(0.32f, 0.78f, 0.66f),
                _ => new Color(0.36f, 0.64f, 0.92f)
            };
        // Small, stable variations distinguish sources within the same family;
        // row order, damage totals and hot reloads never change a source's tint.
        var key = unchecked((uint)hash * 2654435761u + (uint)kind * 2246822519u);
        Color.RGBToHSV(color, out var hue, out var saturation, out var value);
        return Color.HSVToRGB(Mathf.Repeat(hue + ((key & 255) / 255f - 0.5f) * 0.06f, 1),
            saturation, Mathf.Clamp01(value + (((key >> 8) & 255) / 255f - 0.5f) * 0.08f));
    }
    private static (string? Name, Sprite? Icon) Find(int hash)
    {
        try
        {
            foreach (var world in World.s_AllWorlds)
            {
                if (!world.IsCreated || !world.Name.Equals("Client", StringComparison.OrdinalIgnoreCase)) continue;
                var game = world.GetExistingSystemManaged<GameDataSystem>();
                var registry = game?.ManagedDataRegistry;
                if (registry is null) continue;
                string? name = null;
                Sprite? icon = null;
                if (registry.TryGetWithoutLogging<ManagedAbilityGroupData>(new PrefabGUID(hash), out var ability))
                {
                    name = Localization.Get(ability.Name, false); icon = ability.Icon;
                }
                if ((string.IsNullOrWhiteSpace(name) || icon == null)
                    && registry.TryGetWithoutLogging<AbilityTooltipData>(new PrefabGUID(hash), out var tooltip))
                {
                    if (string.IsNullOrWhiteSpace(name)) name = Localization.Get(tooltip.Name, false);
                    if (icon == null) icon = tooltip.Icon;
                }
                if (icon == null && registry.TryGetWithoutLogging<ShowOnBuffBarData>(new PrefabGUID(hash), out var buff)
                    && buff.Entries is { } entries)
                {
                    foreach (var entry in entries)
                        if (entry != null && entry.Icon != null)
                        {
                            if (string.IsNullOrWhiteSpace(name) && !entry.UseParentNameAndDescription)
                                name = Localization.Get(entry.Header, false);
                            icon = entry.Icon;
                            break;
                        }
                }
                if ((string.IsNullOrWhiteSpace(name) || icon == null)
                    && registry.TryGetWithoutLogging<SpellSchoolPassiveData>(new PrefabGUID(hash), out var passive))
                {
                    if (string.IsNullOrWhiteSpace(name)) name = Localization.Get(passive.Header, false);
                    if (icon == null) icon = passive.Icon;
                }
                return (name, icon);
            }
        }
        catch { /* Missing native metadata is a normal fallback, not lost damage. */ }
        return (null, null);
    }
}
