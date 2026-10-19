using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Wardrobe.Ui;

namespace Wardrobe;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/wardrobe";

    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly WindowSystem windows = new("Wardrobe");
    private readonly App app;
    private readonly MainWindow mainWindow;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IDataManager data, ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;

        app = new App(pluginInterface, data, textures);
        mainWindow = new MainWindow(app);
        windows.AddWindow(mainWindow);

        commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Wardrobe window.",
        });

        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
    }

    private void OnCommand(string command, string args) => mainWindow.Toggle();

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        commands.RemoveHandler(CommandName);
        windows.RemoveAllWindows();
        app.Dispose();
    }
}
