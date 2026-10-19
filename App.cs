using System;
using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

namespace Wardrobe;

/// <summary>The shared services the windows draw from.</summary>
public sealed class App : IDisposable
{
    public IDalamudPluginInterface PluginInterface { get; }
    public ITextureProvider Textures { get; }
    public Configuration Config { get; }
    public Ipc Ipc { get; }
    public Items Items { get; }
    public Catalog Catalog { get; }
    public PreviewController Preview { get; }
    public MaterialService Materials { get; }

    /// <summary>Glamourer's state for you, as of the last refresh.</summary>
    public JObject? State { get; private set; }
    public string Message { get; set; } = string.Empty;
    private bool loggedState;

    public App(IDalamudPluginInterface pi, IDataManager data, ITextureProvider textures)
    {
        PluginInterface = pi;
        Textures = textures;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Ipc = new Ipc(pi);
        Items = new Items(data);
        Catalog = new Catalog(Ipc, Items) { PreviewFolder = Config.PreviewFolder };
        Preview = new PreviewController(Ipc, Config);

        var dir = Path.Combine(pi.GetPluginConfigDirectory());
        Directory.CreateDirectory(dir);
        Materials = new MaterialService(Ipc, Catalog, dir);
    }

    public void Refresh()
    {
        State = Ipc.PlayerState();
        if (!loggedState && State != null)
        {
            loggedState = true;
            var text = State.ToString(Newtonsoft.Json.Formatting.None);
            Plugin.Log.Information("Glamourer state sample: {State}", text.Length > 6000 ? text[..6000] : text);
        }

        Catalog.Refresh(State);
        Ipc.Dirty = false;
    }

    public void SaveConfig() => Config.Save(PluginInterface);

    public void Dispose()
    {
        try
        {
            Preview.Revert();
            Materials.ClearPreview();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Cleanup on unload failed.");
        }

        Ipc.Dispose();
    }
}
