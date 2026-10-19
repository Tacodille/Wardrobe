using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wardrobe.Core;

namespace Wardrobe.Ui;

internal static class Colours
{
    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.8f, 0.35f, 1f);
    public static readonly Vector4 Bad = new(0.95f, 0.45f, 0.45f, 1f);
    public static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
}

internal static class Shared
{
    public static readonly (Area Area, string Label)[] Slots =
    [
        (Area.Head, "Head"),
        (Area.Body, "Body"),
        (Area.Hands, "Hands"),
        (Area.Legs, "Legs"),
        (Area.Feet, "Feet"),
        (Area.Ears, "Earrings"),
        (Area.Neck, "Necklace"),
        (Area.Wrists, "Bracelet"),
        (Area.RFinger, "Ring (right)"),
        (Area.LFinger, "Ring (left)"),
        (Area.MainHand, "Main hand"),
        (Area.OffHand, "Off hand"),
        (Area.Hair, "Hair"),
        (Area.Face, "Face"),
        (Area.Eyes, "Eyes"),
        (Area.Tail, "Tail / ears"),
        (Area.Skin, "Skin & tattoos"),
    ];

    public static string Label(Area area)
    {
        foreach (var (a, l) in Slots)
            if (a == area)
                return l;
        return area.ToString();
    }

    /// <summary>Draws an image file scaled down to fit a width. Returns false if the image isn't ready yet.</summary>
    public static bool Image(App app, string? path, float maxWidth)
    {
        if (path == null)
            return false;
        try
        {
            var wrap = app.Textures.GetFromFile(path).GetWrapOrEmpty();
            if (wrap.Width <= 0 || wrap.Height <= 0)
                return false;
            var scale = System.Math.Min(1f, maxWidth / wrap.Width);
            ImGui.Image(wrap.Handle, new Vector2(wrap.Width * scale, wrap.Height * scale));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
