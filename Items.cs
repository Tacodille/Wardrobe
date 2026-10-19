using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Wardrobe.Core;

namespace Wardrobe;

public sealed class ItemInfo
{
    public uint Id;
    public string Name = string.Empty;
    public Area Area;
    public int SetId;
    public int Variant;
    public int Level;

    public override string ToString() => Name;
}

/// <summary>A lookup of vanilla items by the model set they use, built once from the game's item sheet.</summary>
public sealed class Items
{
    private readonly Dictionary<(Area, int), List<ItemInfo>> bySet = new();
    private readonly Dictionary<Area, List<ItemInfo>> byArea = new();
    private readonly Dictionary<uint, ItemInfo> byId = new();
    private readonly Dictionary<uint, ItemInfo> glassesById = new();
    private readonly Dictionary<int, List<ItemInfo>> glassesBySet = new();

    public Items(IDataManager data)
    {
        try
        {
            var sheet = data.GetExcelSheet<Item>();
            foreach (var item in sheet)
            {
                if (item.EquipSlotCategory.RowId == 0 || item.ModelMain == 0)
                    continue;
                var name = item.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var esc = item.EquipSlotCategory.Value;
                var areas = new List<Area>();
                if (esc.MainHand != 0) areas.Add(Area.MainHand);
                if (esc.OffHand != 0) areas.Add(Area.OffHand);
                if (esc.Head != 0) areas.Add(Area.Head);
                if (esc.Body != 0) areas.Add(Area.Body);
                if (esc.Gloves != 0) areas.Add(Area.Hands);
                if (esc.Legs != 0) areas.Add(Area.Legs);
                if (esc.Feet != 0) areas.Add(Area.Feet);
                if (esc.Ears != 0) areas.Add(Area.Ears);
                if (esc.Neck != 0) areas.Add(Area.Neck);
                if (esc.Wrists != 0) areas.Add(Area.Wrists);
                if (esc.FingerR != 0) areas.Add(Area.RFinger);
                if (esc.FingerL != 0) areas.Add(Area.LFinger);

                foreach (var area in areas)
                {
                    var weapon = area is Area.MainHand or Area.OffHand;
                    var mm = item.ModelMain;
                    var info = new ItemInfo
                    {
                        Id = item.RowId,
                        Name = name,
                        Area = area,
                        SetId = (int)(mm & 0xFFFF),
                        Variant = weapon ? (int)((mm >> 32) & 0xFFFF) : (int)((mm >> 16) & 0xFF),
                        Level = item.LevelEquip,
                    };
                    if (weapon)
                        info.Variant = (int)((mm >> 16) & 0xFFFF);

                    Add(bySet, (area, info.SetId), info);
                    if (!byArea.TryGetValue(area, out var list))
                        byArea[area] = list = new List<ItemInfo>();
                    list.Add(info);
                    byId.TryAdd(info.Id, info);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Could not build the item index.");
        }

        try
        {
            // Facewear (glasses, monocles, auras) is its own sheet. Its model id is the equipment set the mod files use.
            foreach (var g in data.GetExcelSheet<Glasses>())
            {
                var name = g.Singular.ToString();
                if (g.RowId == 0 || string.IsNullOrWhiteSpace(name))
                    continue;
                var info = new ItemInfo
                {
                    Id = g.RowId,
                    Name = name,
                    Area = Area.Glasses,
                    SetId = (int)(g.Model & 0xFFFF),
                    Variant = (int)((g.Model >> 16) & 0xFF),
                };
                glassesById[info.Id] = info;
                if (!glassesBySet.TryGetValue(info.SetId, out var list))
                    glassesBySet[info.SetId] = list = new List<ItemInfo>();
                list.Add(info);
                if (!byArea.TryGetValue(Area.Glasses, out var all))
                    byArea[Area.Glasses] = all = new List<ItemInfo>();
                all.Add(info);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Could not build the facewear index.");
        }
    }

    public int Count => byId.Count + glassesById.Count;

    public ItemInfo? GlassesById(ulong id) => id <= uint.MaxValue && glassesById.TryGetValue((uint)id, out var i) ? i : null;

    public IReadOnlyList<ItemInfo> GlassesForSet(int setId) => glassesBySet.TryGetValue(setId, out var l) ? l : [];

    public ItemInfo? ById(ulong id) => id <= uint.MaxValue && byId.TryGetValue((uint)id, out var i) ? i : null;

    /// <summary>Items that use a given model set in a slot. A ring item fits both finger slots.</summary>
    public IReadOnlyList<ItemInfo> ForSet(Area area, int setId)
        => area == Area.Glasses ? GlassesForSet(setId) : bySet.TryGetValue((area, setId), out var l) ? l : [];

    public IEnumerable<ItemInfo> ForArea(Area area, string filter)
    {
        if (!byArea.TryGetValue(area, out var list))
            return [];
        return list
            .Where(i => filter.Length == 0 || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static void Add(Dictionary<(Area, int), List<ItemInfo>> map, (Area, int) key, ItemInfo item)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<ItemInfo>();
        list.Add(item);
    }
}
