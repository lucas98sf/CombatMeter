using CombatMeter.Core;
using CombatMeter.Shared;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using System.Runtime.CompilerServices;
using System.Text;

namespace CombatMeter.Server;

internal sealed class DamageSourceResolver
{
    private readonly World _world;
    private readonly EntityManager _manager;
    private readonly SourceOriginCache _cache = new();
    private readonly Dictionary<int, string> _names = new();
    public DamageSourceResolver(World world) { _world = world; _manager = world.EntityManager; }
    private static class Component<T> { internal static readonly ComponentType Type = ComponentType.ReadOnly(Il2CppType.Of<T>()); }
    private bool Has<T>(Entity e) => e != Entity.Null && _manager.Exists(e) && _manager.HasComponent(e, Component<T>.Type);
    private unsafe T Read<T>(Entity e) where T : unmanaged => Unsafe.Read<T>(_manager.GetComponentDataRawRO(e, Component<T>.Type.TypeIndex));
    private static EntityKey Key(Entity e) => new(e.Index, e.Version);
    public DamageSource Resolve(StatChangeEvent change, double now)
    {
        var effect = change.Source;
        // Only follow an exact event relationship; never match by time or last cast.
        if (Has<DealDamageEvent>(change.StatChangeEntity)) effect = Read<DealDamageEvent>(change.StatChangeEntity).SpellSource;
        else if (Has<DealDamageEvent>(effect)) effect = Read<DealDamageEvent>(effect).SpellSource;
        return Observe(effect, now) ?? DamageSource.Unknown;
    }
    public DamageSource? Observe(Entity effect, double now)
    {
        if (!_manager.Exists(effect)) return _cache.Resolve(Key(effect), now);
        if (Has<PlayerCharacter>(effect)) return null;
        var remembered = _cache.Resolve(Key(effect), now);
        DamageSource? result = null;
        var cursor = effect;
        var seen = new HashSet<EntityKey>();
        while (_manager.Exists(cursor) && seen.Add(Key(cursor)) && seen.Count <= 32)
        {
            if (Has<PlayerCharacter>(cursor) || Has<Health>(cursor) && Has<Movement>(cursor)) break;
            if (Has<Buff>(cursor) && Read<Buff>(cursor).BuffEffectType == BuffEffectType.Debuff)
            { result = Describe(cursor, DamageSourceKind.Debuff); break; }
            if (Has<AbilityOwner>(cursor))
            {
                var owner = Read<AbilityOwner>(cursor);
                var group = owner.AbilityGroup.GetEntityOnServer();
                if (_manager.Exists(group))
                {
                    result = Describe(group, IsWeapon(group) || IsWeapon(cursor)
                        ? DamageSourceKind.Weapon : DamageSourceKind.Ability);
                    if (result is not null) break;
                }
            }
            result ??= DescribeEffect(cursor);
            if (!Has<EntityOwner>(cursor)) break;
            cursor = Read<EntityOwner>(cursor).Owner;
        }
        var summon = SummonOrigin(effect);
        if (summon is { } origin)
        {
            var unit = Describe(origin, DamageSourceKind.Summon);
            if (unit is not null)
                result = result is null ? unit with { Name = unit.Name + " attacks" }
                    : result with { Id = unit.Id + ":" + result.Id, Name = unit.Name + " · " + result.Name };
        }
        // A live projectile can outlast its ability-group or summon entity.
        // Retain the proven origin instead of downgrading to its raw prefab.
        if (remembered is not null && (result is null
            || remembered.Id.StartsWith("Summon:", StringComparison.Ordinal) && !result.Id.StartsWith("Summon:", StringComparison.Ordinal)
            || Has<PrefabGUID>(effect) && remembered.PrefabHash != Read<PrefabGUID>(effect).GuidHash
                && result.PrefabHash == Read<PrefabGUID>(effect).GuidHash)) result = remembered;
        if (result is not null && (!(Has<Health>(effect) && Has<Movement>(effect)) || result.Kind == DamageSourceKind.Summon))
            _cache.Observe(Key(effect), result, now);
        return result;
    }
    private Entity? SummonOrigin(Entity effect)
    {
        Entity? unit = null;
        var seen = new HashSet<EntityKey>();
        for (var cursor = effect; _manager.Exists(cursor) && seen.Add(Key(cursor)) && seen.Count <= 32;)
        {
            if (Has<PlayerCharacter>(cursor)) return unit;
            if (Has<Health>(cursor) && Has<Movement>(cursor)) unit ??= cursor;
            if (!Has<EntityOwner>(cursor)) break;
            cursor = Read<EntityOwner>(cursor).Owner;
        }
        return null;
    }
    private DamageSource? DescribeEffect(Entity entity)
    {
        if (!Has<PrefabGUID>(entity)) return null;
        var hash = Read<PrefabGUID>(entity).GuidHash;
        var raw = RawName(hash);
        if (IsWeapon(entity)) return Describe(entity, DamageSourceKind.Weapon);
        if (Has<Buff>(entity)) return Describe(entity, DamageSourceKind.Proc);
        return raw.StartsWith("AB_", StringComparison.Ordinal)
            ? Describe(entity, DamageSourceKind.Ability) : null;
    }
    private bool IsWeapon(Entity entity)
    {
        if (Has<WeaponAbilityData>(entity)) return true;
        if (!Has<PrefabGUID>(entity)) return false;
        // Primary attacks lack WeaponAbilityData in this game. Classify their
        // own native prefab identity, never the attacker's current equipment.
        var name = RawName(Read<PrefabGUID>(entity).GuidHash);
        return Regex.IsMatch(name, "^AB_Vampire_(Sword|Axe|Axes|Mace|Spear|Reaper|Slasher|Slashers|GreatSword|Pistols|Crossbow|Longbow|Whip|Rapier|Daggers|Twinblade|Twinblades|Claws)_");
    }
    private DamageSource? Describe(Entity entity, DamageSourceKind kind)
    {
        if (!Has<PrefabGUID>(entity)) return null;
        var hash = Read<PrefabGUID>(entity).GuidHash;
        var raw = RawName(hash);
        if (raw.Length == 0) return null;
        var name = Regex.Replace(raw, "^(AB_|BUFF_|Buff_|CHAR_)", "");
        name = Regex.Replace(name, "_(AbilityGroup|Group)$", "");
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ');
        name = Regex.Replace(name, "\\s+", " ").Trim();
        if (name.Length > 120) name = name[..120];
        return new($"{kind}:{hash}", name, kind, hash);
    }
    private string RawName(int hash)
    {
        if (_names.TryGetValue(hash, out var cached)) return cached;
        var prefabs = _world.GetExistingSystemManaged<PrefabCollectionSystem>();
        var metadata = default(ConvertedAssetData);
        if (prefabs is not null && PrefabLookupUtility.TryGetConvertedAssetDataForPrefab(new PrefabGUID(hash), out metadata))
        {
            var assetName = AssetName(metadata);
            if (!string.IsNullOrWhiteSpace(assetName)) { _names[hash] = assetName; return assetName; }
        }
        if (_names.Count == 0)
        {
            var collection = _world.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (collection is not null)
                foreach (var entry in collection.SpawnableNameToPrefabGuidDictionary) _names[entry.Value.GuidHash] = entry.Key;
        }
        if (_names.TryGetValue(hash, out var name)) return name;
        return _names[hash] = "";
    }
    private static unsafe string AssetName(ConvertedAssetData data)
    {
        // FixedString.ToString and generic NativeHashMap out-value wrappers can
        // use unavailable IL2CPP generic instantiations. Decode a bounded copy.
        var name = data.AssetName;
        var length = name.utf8LengthInBytes;
        if (length == 0 || length > 126 || Unsafe.SizeOf<Unity.Collections.FixedString128Bytes>() < length + 2) return "";
        var text = Encoding.UTF8.GetString((byte*)&name + 2, length);
        return Regex.IsMatch(text, "^[A-Za-z0-9_]+$") ? text : "";
    }
    public void Prune(double now) => _cache.Prune(now);
}
