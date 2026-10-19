using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Wardrobe.Core;

namespace Wardrobe;

public sealed class ModState
{
    public ModDef Def = null!;
    public string Name = string.Empty;
    public Selection Sel = new();
    public bool Inherited;
}

/// <summary>One mod's footprint on one slot and model set, e.g. "Agony Necklace Set changes neck set 143".</summary>
public sealed class SlotEntry
{
    public ModState Mod = null!;
    public Area Area;
    public int SetId;
    public List<ItemInfo> Items = new();
    public List<(string? Group, string? Option, bool Active)> Sources = new();
    public int FileCount;

    /// <summary>Every game path this entry's options can supply, whether or not they are switched on right now.</summary>
    public HashSet<string> Paths { get; } = new();

    /// <summary>For hair, face and tail mods: the Glamourer customize key and value this mod belongs to, when Penumbra tells us.</summary>
    public List<(string Key, int Value)> Customize = new();

    public bool ActiveNow => Mod.Sel.Enabled && Sources.Any(s => s.Active);

    /// <summary>Set when the same mod has several entries in one slot (tail shapes, hair variants), so each row says which one it is.</summary>
    public bool NeedsDetail;

    /// <summary>True when every variant comes from the same options, so only the model number tells them apart.</summary>
    public bool SetOnly;

    /// <summary>Which options supply this variant, e.g. "CHANTILLY: Ears + tail", plus the model set number.</summary>
    public string Detail
    {
        get
        {
            if (SetOnly)
                return Area switch
                {
                    Area.Tail => $"tail shape {SetId}",
                    Area.Hair => $"hairstyle {SetId}",
                    Area.Face => $"face {SetId}",
                    Area.VieraEars => $"ear shape {SetId}",
                    _ => $"set {SetId}",
                };

            var names = Sources
                .Where(s => s.Option != null)
                .Select(s => s.Group != null && s.Group != s.Option ? $"{s.Group}: {s.Option}" : s.Option!)
                .Distinct()
                .ToList();
            var text = names.Count switch
            {
                0 => string.Empty,
                <= 2 => string.Join(" / ", names),
                _ => string.Join(" / ", names.Take(2)) + $" +{names.Count - 2}",
            };
            return text.Length > 0 ? $"{text}  #{SetId}" : $"#{SetId}";
        }
    }

    public string Title => NeedsDetail ? $"{BaseTitle}   [{Detail}]" : BaseTitle;

    private string BaseTitle => Items.Count > 0 ? $"{Mod.Name}  ->  {Items[0].Name}" : Area is Area.Skin or Area.Eyes or Area.Hair or Area.Face or Area.Tail or Area.VieraEars ? Mod.Name : $"{Mod.Name}  (set {SetId})";
}

/// <summary>Everything Wardrobe knows about your mods right now: folders, states, who wins which file, and per-slot entries.</summary>
public sealed class Catalog
{
    private readonly Dictionary<string, (DateTime Stamp, ModDef? Def)> cache = new();
    private readonly Dictionary<string, (List<(string Winner, int Count, int Priority, bool Tie)> LosesTo, int Winning, int Total)> standingCache = new();
    private readonly Ipc ipc;
    private readonly Items items;

    public List<ModState> Mods { get; private set; } = [];
    public Dictionary<string, PathOutcome> Outcomes { get; private set; } = new();
    public List<SlotEntry> Entries { get; private set; } = [];
    public (Guid Id, string Name)? Collection { get; private set; }
    public string? ModRoot { get; private set; }

    /// <summary>A folder of fallback preview images named after each mod's folder.</summary>
    public string? PreviewFolder { get; set; }
    public int RaceCode { get; private set; } = 101;
    public int BodyCode { get; private set; } = 101;

    public Catalog(Ipc ipc, Items items)
    {
        this.ipc = ipc;
        this.items = items;
    }

    public void Refresh(Newtonsoft.Json.Linq.JObject? playerState)
    {
        Collection = ipc.CurrentCollection();
        ModRoot = ipc.ModDirectory();
        (RaceCode, BodyCode) = RaceFrom(playerState);
        Mods = [];
        standingCache.Clear();
        if (Collection == null || ModRoot == null)
        {
            Outcomes = new();
            Entries = [];
            return;
        }

        foreach (var (dir, name, sel, inherited) in ipc.ReadMods(Collection.Value.Id))
        {
            var def = Load(ModRoot, dir);
            if (def != null)
            {
                def.PreviewImage ??= FallbackPreview(dir);
                Mods.Add(new ModState { Def = def, Name = name, Sel = sel, Inherited = inherited });
            }
        }

        Outcomes = Resolver.Build(Mods.Select(m => (m.Def, m.Sel)));
        Entries = BuildEntries();
    }

    private string? FallbackPreview(string dir)
    {
        if (string.IsNullOrEmpty(PreviewFolder) || !Directory.Exists(PreviewFolder))
            return null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var path = Path.Combine(PreviewFolder, dir + ext);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private ModDef? Load(string root, string dir)
    {
        var meta = Path.Combine(root, dir, "meta.json");
        if (!File.Exists(meta))
            return null;
        var stamp = File.GetLastWriteTimeUtc(meta);
        if (cache.TryGetValue(dir, out var c) && c.Stamp == stamp)
            return c.Def;
        var def = ModLoader.Load(root, dir);
        cache[dir] = (stamp, def);
        return def;
    }

    private static (int Race, int Body) RaceFrom(Newtonsoft.Json.Linq.JObject? state)
    {
        if (state?["Customize"] is not JObject c)
            return (101, 101);
        int V(string k) => (int?)c[k]?["Value"] ?? 0;
        var race = V("Race");
        return race == 0 ? (101, 101) : (PathInfo.RaceCodeFor(race, V("Clan"), V("Gender")), PathInfo.BodyRaceCodeFor(race, V("Clan"), V("Gender")));
    }

    private List<SlotEntry> BuildEntries()
    {
        var result = new List<SlotEntry>();
        foreach (var mod in Mods)
        {
            var active = Resolver.ActiveSources(mod.Def, mod.Sel)
                .Select(s => s.Label).ToHashSet();

            var found = new Dictionary<(Area, int), SlotEntry>();
            var skinAnyRace = 0;

            void Add(string? group, string? option, ContainerDef c, string label)
            {
                var isActive = active.Contains(label);
                foreach (var path in c.Files.Keys.Concat(c.Swaps.Keys))
                {
                    var info = PathInfo.Classify(path);
                    if (info.Area is Area.Other or Area.Animation or Area.Common or Area.Vfx)
                        continue;
                    if (info.Area == Area.Skin)
                        skinAnyRace++;
                    if (info.IsAppearance && info.RaceCode != 0 && !RaceMatches(info))
                        continue;
                    var entry = Get(info.Area, info.Area is Area.Skin or Area.Eyes ? 0 : info.SetId);
                    entry.FileCount++;
                    entry.Paths.Add(path);
                    Note(entry, group, option, isActive);
                }

                foreach (var imc in c.Imc)
                {
                    var area = ImcArea(imc);
                    if (area == null)
                        continue;
                    Note(Get(area.Value, imc.PrimaryId), group, option, isActive);
                }
            }

            SlotEntry Get(Area area, int set)
            {
                if (!found.TryGetValue((area, set), out var e))
                    found[(area, set)] = e = new SlotEntry { Mod = mod, Area = area, SetId = set };
                return e;
            }

            static void Note(SlotEntry e, string? group, string? option, bool isActive)
            {
                var i = e.Sources.FindIndex(s => s.Group == group && s.Option == option);
                if (i < 0)
                    e.Sources.Add((group, option, isActive));
            }

            Add(null, null, mod.Def.Default, "default");
            foreach (var g in mod.Def.Groups)
            {
                if (g.Type == "Imc" && g.Imc != null)
                {
                    var area = ImcArea(g.Imc);
                    if (area != null)
                        foreach (var o in g.Options)
                        {
                            var entry = Get(area.Value, g.Imc.PrimaryId);
                            var picked = mod.Sel.Options.TryGetValue(g.Name, out var p) && p.Contains(o.Name);
                            Note(entry, g.Name, o.Name, picked);
                        }

                    continue;
                }

                foreach (var o in g.Options)
                    Add(g.Name, o.Name, o.Container, $"{g.Name}:{o.Name}");
                for (var i = 0; i < g.Containers.Count; i++)
                    Add(g.Name, null, g.Containers[i], $"{g.Name}:combo{i}");
            }

            // A body mod (The Body SE and friends) ships far more skin files than clothing, plus bare-body gear models for the
            // equipment slots. Those gear models belong with the skin, so the slot lists are not full of it.
            var gearFiles = found.Where(kv => kv.Key.Item1 <= Area.LFinger).Sum(kv => kv.Value.FileCount);
            if (skinAnyRace >= 10 && skinAnyRace > gearFiles)
            {
                var hasSkin = found.TryGetValue((Area.Skin, 0), out var skin);
                foreach (var key in found.Keys.Where(k => k.Item1 <= Area.LFinger).ToList())
                {
                    if (hasSkin)
                    {
                        foreach (var src in found[key].Sources)
                            Note(skin!, src.Group, src.Option, src.Active);
                        skin!.FileCount += found[key].FileCount;
                        skin.Paths.UnionWith(found[key].Paths);
                    }

                    found.Remove(key);
                }
            }

            foreach (var group in found.Values.GroupBy(e => e.Area))
                if (group.Count() > 1)
                {
                    var signatures = group.Select(e => string.Join("|", e.Sources.Select(x => $"{x.Group}:{x.Option}").OrderBy(x => x))).Distinct().Count();
                    foreach (var e in group)
                    {
                        e.NeedsDetail = true;
                        e.SetOnly = signatures == 1;
                    }
                }

            foreach (var e in found.Values)
            {
                if (e.Area is >= Area.Head and <= Area.OffHand)
                    foreach (var item in items.ForSet(e.Area, e.SetId))
                        e.Items.Add(item);
                result.Add(e);
            }
        }

        return result;
    }

    private bool RaceMatches(PathInfo info) => info.RaceCode == (info.Area == Area.Skin ? BodyCode : RaceCode);

    public static Area? ImcArea(ImcId id)
    {
        if (!id.ObjectType.Equals("Equipment", StringComparison.OrdinalIgnoreCase) && !id.ObjectType.Equals("Accessory", StringComparison.OrdinalIgnoreCase))
            return null;
        return id.EquipSlot switch
        {
            "Head" => Area.Head,
            "Body" => Area.Body,
            "Hands" => Area.Hands,
            "Legs" => Area.Legs,
            "Feet" => Area.Feet,
            "Ears" => Area.Ears,
            "Neck" => Area.Neck,
            "Wrists" => Area.Wrists,
            "RFinger" => Area.RFinger,
            "LFinger" => Area.LFinger,
            _ => null,
        };
    }

    // ------------------------------------------------------------------ queries

    public IEnumerable<SlotEntry> For(Area area, bool enabledOnly, string filter)
    {
        var q = Entries.Where(e => e.Area == area || (area is Area.RFinger or Area.LFinger && e.Area is Area.RFinger or Area.LFinger));
        if (enabledOnly)
            q = q.Where(e => e.ActiveNow);
        if (filter.Length > 0)
            q = q.Where(e => e.Mod.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || e.Items.Any(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        return q.OrderBy(e => e.Mod.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For an entry: how many of its files it would win, and who beats it on the rest. Works for mods that are off too, as if they
    /// were enabled at their stored priority.
    /// </summary>
    public (List<(string Winner, int Count, int Priority, bool Tie)> LosesTo, int Winning, int Total) Standing(SlotEntry e)
    {
        var cacheKey = $"{e.Mod.Def.Directory}|{e.Area}|{e.SetId}";
        if (standingCache.TryGetValue(cacheKey, out var hit))
            return hit;

        var losses = new Dictionary<string, (int Count, int Pri, bool Tie)>();
        var winning = 0;
        var total = 0;
        foreach (var path in e.Paths)
        {
            total++;
            var top = TopOther(path, e.Mod.Def.Directory);
            if (top == null || e.Mod.Sel.Priority > top.ModPriority)
            {
                winning++;
                continue;
            }

            var tie = top.ModPriority == e.Mod.Sel.Priority;
            losses.TryGetValue(top.Mod.Name, out var cur);
            losses[top.Mod.Name] = (cur.Count + 1, top.ModPriority, tie);
        }

        var result = (losses.Select(k => (k.Key, k.Value.Count, k.Value.Pri, k.Value.Tie)).OrderByDescending(x => x.Count).ToList(), winning, total);
        standingCache[cacheKey] = result;
        return result;
    }

    private Claim? TopOther(string path, string modDirectory)
    {
        if (!Outcomes.TryGetValue(path, out var outcome))
            return null;
        return outcome.Claims.Where(c => c.Mod.Directory != modDirectory).OrderByDescending(c => c.ModPriority).FirstOrDefault();
    }

    /// <summary>
    /// Other enabled mods that replace the same model or material files as this entry, which makes them alternatives to it rather than
    /// add-ons (two face sculpts, two versions of the same necklace). Texture-only overlays such as tattoos are not alternatives.
    /// Skin is excluded because layering several skin mods is normal.
    /// </summary>
    public List<ModState> Alternatives(SlotEntry e)
    {
        var found = new Dictionary<string, ModState>();
        if (e.Area == Area.Skin)
            return [];
        foreach (var path in e.Paths.Where(p => p.EndsWith(".mdl", StringComparison.Ordinal) || p.EndsWith(".mtrl", StringComparison.Ordinal)))
        {
            if (!Outcomes.TryGetValue(path, out var outcome))
                continue;
            foreach (var c in outcome.Claims.Where(c => c.Mod.Directory != e.Mod.Def.Directory))
            {
                var mod = Mods.FirstOrDefault(m => m.Def.Directory == c.Mod.Directory);
                if (mod != null && mod.Sel.Enabled)
                    found[mod.Def.Directory] = mod;
            }
        }

        return found.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The priority this mod needs to beat every other enabled mod on all of its files in this slot.</summary>
    public int PriorityToWin(SlotEntry e)
    {
        var top = e.Mod.Sel.Priority;
        foreach (var path in e.Paths)
        {
            var other = TopOther(path, e.Mod.Def.Directory);
            if (other != null)
                top = Math.Max(top, other.ModPriority + 1);
        }

        return top;
    }

    /// <summary>The material game paths a slot is using right now, with the mod that wins each.</summary>
    public IEnumerable<PathOutcome> MaterialsFor(Area area)
        => Outcomes.Values
            .Where(o => o.GamePath.EndsWith(".mtrl", StringComparison.Ordinal))
            .Where(o => o.Info.Area == area && (o.Info.RaceCode == 0 || o.Info.RaceCode == (area == Area.Skin ? BodyCode : RaceCode)))
            .OrderBy(o => o.GamePath, StringComparer.Ordinal);

    // -------------------------------------------------------- appearance (hair, face...)

    /// <summary>
    /// The Glamourer customize value an appearance entry stands for. The model number in the file path is the value itself for
    /// hair (h0113 is hairstyle 113) and tails (t0003 is tail shape 3). Faces repeat per clan, so f0101 is face 1 of the second clan.
    /// </summary>
    public static (string Key, int Value)? CustomizeFor(SlotEntry e) => e.Area switch
    {
        Area.Hair => ("Hairstyle", e.SetId),
        Area.Tail => ("TailShape", e.SetId),
        Area.Face => ("Face", e.SetId >= 100 ? e.SetId - 100 : e.SetId),
        _ => null,
    };
}
