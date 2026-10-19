using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Wardrobe;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool EnabledOnly { get; set; } = true;
    public bool LiveHover { get; set; } = true;
    public int HoverDelayMs { get; set; } = 250;
    public int RevertDelayMs { get; set; } = 400;
    public bool ShowHoverImage { get; set; } = true;

    /// <summary>Clicking a mod that is switched off turns it on in your collection (Undo last apply reverses it).</summary>
    public bool ClickEnables { get; set; } = true;

    /// <summary>Applying something switches off other enabled mods that replace the same model or material files.</summary>
    public bool TurnOffConflicting { get; set; } = true;

    /// <summary>Mod folders that applying something must never switch off, such as a mark remover, make-up or tattoos.</summary>
    public System.Collections.Generic.List<string> NeverTurnOff { get; set; } = new();

    /// <summary>Previews run at least this high so they show over other mods. Raised further if a conflicting mod is higher.</summary>
    public int PreviewPriority { get; set; } = 99;

    /// <summary>Preview images for mods that ship none, named after the mod's folder (for example "My Mod.png").</summary>
    public string PreviewFolder { get; set; } = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Wardrobe", "previews");

    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
