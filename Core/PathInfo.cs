using System.Text.RegularExpressions;

namespace Wardrobe.Core;

/// <summary>The part of the character a game file belongs to.</summary>
public enum Area
{
    Head, Body, Hands, Legs, Feet,
    Ears, Neck, Wrists, RFinger, LFinger,
    MainHand, OffHand,
    Glasses,
    Hair, Face, Eyes, Skin, Tail, VieraEars,
    Animation, Common, Vfx, Other,
}

public readonly record struct PathInfo(Area Area, int SetId, int Variant, int RaceCode, bool IsMaterial)
{
    public bool IsEquipment => Area is >= Area.Head and <= Area.LFinger;
    public bool IsWeapon => Area is Area.MainHand or Area.OffHand;
    public bool IsAppearance => Area is >= Area.Hair and <= Area.VieraEars;

    private static readonly Regex Equip = new(@"^chara/equipment/e(\d{4})/(?:model|material|texture)/(?:.*/)?([^/]+)$", RegexOptions.Compiled);
    private static readonly Regex Accessory = new(@"^chara/accessory/a(\d{4})/(?:model|material|texture)/(?:.*/)?([^/]+)$", RegexOptions.Compiled);
    private static readonly Regex Weapon = new(@"^chara/weapon/w(\d{4})/obj/body/b(\d{4})/", RegexOptions.Compiled);
    private static readonly Regex Human = new(@"^chara/human/c(\d{4})/obj/(hair|face|body|tail|zear)/[a-z](\d{4})/", RegexOptions.Compiled);
    private static readonly Regex EquipSuffix = new(@"_(met|top|glv|dwn|sho|gls)(?=[_.])", RegexOptions.Compiled);
    private static readonly Regex AccessorySuffix = new(@"_(ear|nek|wrs|rir|ril)(?=[_.])", RegexOptions.Compiled);
    private static readonly Regex IrisFile = new(@"_iri[_.]", RegexOptions.Compiled);
    private static readonly Regex VariantDir = new(@"/material/v(\d{4})/", RegexOptions.Compiled);
    private static readonly Regex VariantPrefix = new(@"/texture/v(\d{2})_", RegexOptions.Compiled);
    private static readonly Regex RaceInName = new(@"c(\d{4})[ea]\d{4}", RegexOptions.Compiled);

    public static string Normalise(string path) => path.Replace('\\', '/').Trim().ToLowerInvariant();

    public static PathInfo Classify(string gamePath)
    {
        var p = Normalise(gamePath);

        var m = Equip.Match(p);
        if (m.Success)
        {
            var suffix = EquipSuffix.Match(m.Groups[2].Value);
            if (suffix.Success)
                return new PathInfo(suffix.Groups[1].Value switch
                {
                    "met" => Area.Head,
                    "top" => Area.Body,
                    "glv" => Area.Hands,
                    "dwn" => Area.Legs,
                    "gls" => Area.Glasses,
                    _ => Area.Feet,
                }, int.Parse(m.Groups[1].Value), VariantOf(p), RaceIn(m.Groups[2].Value), p.Contains("/material/"));
            return new PathInfo(Area.Other, int.Parse(m.Groups[1].Value), 0, 0, false);
        }

        m = Accessory.Match(p);
        if (m.Success)
        {
            var suffix = AccessorySuffix.Match(m.Groups[2].Value);
            if (suffix.Success)
                return new PathInfo(suffix.Groups[1].Value switch
                {
                    "ear" => Area.Ears,
                    "nek" => Area.Neck,
                    "wrs" => Area.Wrists,
                    "rir" => Area.RFinger,
                    _ => Area.LFinger,
                }, int.Parse(m.Groups[1].Value), VariantOf(p), RaceIn(m.Groups[2].Value), p.Contains("/material/"));
            return new PathInfo(Area.Other, int.Parse(m.Groups[1].Value), 0, 0, false);
        }

        m = Weapon.Match(p);
        if (m.Success)
            return new PathInfo(Area.MainHand, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0, p.Contains("/material/"));

        m = Human.Match(p);
        if (m.Success)
        {
            var area = m.Groups[2].Value switch
            {
                "hair" => Area.Hair,
                "face" => IrisFile.IsMatch(p) ? Area.Eyes : Area.Face,
                "body" => Area.Skin,
                "tail" => Area.Tail,
                _ => Area.VieraEars,
            };
            return new PathInfo(area, int.Parse(m.Groups[3].Value), 0, int.Parse(m.Groups[1].Value), p.Contains("/material/"));
        }

        if (p.StartsWith("chara/human/") && p.Contains("/animation/"))
            return new PathInfo(Area.Animation, 0, 0, 0, false);
        if (p.StartsWith("chara/action/"))
            return new PathInfo(Area.Animation, 0, 0, 0, false);
        if (p.StartsWith("vfx/") || p.Contains("/vfx/"))
            return new PathInfo(Area.Vfx, 0, 0, 0, false);
        if (p.StartsWith("chara/common/texture/eye/"))
            return new PathInfo(Area.Eyes, 0, 0, 0, false);
        if (p.StartsWith("chara/common/"))
            return new PathInfo(Area.Common, 0, 0, 0, false);
        return new PathInfo(Area.Other, 0, 0, 0, false);
    }

    private static int VariantOf(string p)
    {
        var d = VariantDir.Match(p);
        if (d.Success)
            return int.Parse(d.Groups[1].Value);
        var t = VariantPrefix.Match(p);
        return t.Success ? int.Parse(t.Groups[1].Value) : 0;
    }

    private static int RaceIn(string fileName)
    {
        var m = RaceInName.Match(fileName);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>
    /// The 4 digit race and gender code used in model paths. Race: 1 Hyur, 2 Elezen, 3 Lalafell, 4 Miqo'te, 5 Roegadyn, 6 Au Ra,
    /// 7 Hrothgar, 8 Viera. Clan only matters for Hyur (1 Midlander, 2 Highlander). Gender: 0 male, 1 female.
    /// </summary>
    public static int RaceCodeFor(int race, int clan, int gender)
    {
        var male = race switch
        {
            1 => clan == 2 ? 301 : 101,
            2 => 501,
            3 => 1101,
            4 => 701,
            5 => 901,
            6 => 1301,
            7 => 1501,
            8 => 1701,
            _ => 101,
        };
        return male + (gender == 1 ? 100 : 0);
    }

    /// <summary>
    /// The race code of the body (skin) files. Midlander, Miqo'te and Elezen share the Midlander body (c0101 or c0201), which is why
    /// a Miqo'te's tattoo mods live under c0101 while their face files live under c0701.
    /// </summary>
    public static int BodyRaceCodeFor(int race, int clan, int gender)
    {
        var male = race switch
        {
            1 => clan == 2 ? 301 : 101,
            2 => 101,
            3 => 1101,
            4 => 101,
            5 => 901,
            6 => 1301,
            7 => 1501,
            8 => 1701,
            _ => 101,
        };
        return male + (gender == 1 ? 100 : 0);
    }

    public static string Suffix(Area area) => area switch
    {
        Area.Head => "met",
        Area.Body => "top",
        Area.Hands => "glv",
        Area.Legs => "dwn",
        Area.Feet => "sho",
        Area.Glasses => "gls",
        Area.Ears => "ear",
        Area.Neck => "nek",
        Area.Wrists => "wrs",
        Area.RFinger => "rir",
        Area.LFinger => "ril",
        _ => string.Empty,
    };

    /// <summary>The main model and the first material of an equipped item, for asking Penumbra which file is being served.</summary>
    public static (string Model, string Material)? ItemPaths(Area area, int setId, int variant, int raceCode)
    {
        var suffix = Suffix(area);
        if (suffix.Length == 0)
            return null;
        var kind = area is Area.Ears or Area.Neck or Area.Wrists or Area.RFinger or Area.LFinger ? "accessory" : "equipment";
        var letter = kind == "accessory" ? "a" : "e";
        var set = setId.ToString("D4");
        var race = raceCode.ToString("D4");
        var model = $"chara/{kind}/{letter}{set}/model/c{race}{letter}{set}_{suffix}.mdl";
        var material = $"chara/{kind}/{letter}{set}/material/v{variant:D4}/mt_c{race}{letter}{set}_{suffix}_a.mtrl";
        return (model, material);
    }
}
