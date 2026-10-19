using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Wardrobe.Ui;

public sealed class MainWindow : Window
{
    private readonly App app;
    private readonly WardrobeTab wardrobe;
    private readonly DesignsTab designs;
    private readonly DiagnosticsTab diagnostics;
    private readonly MaterialsTab materials;
    private readonly StatusTab status;
    private readonly ImportTab import;

    public MainWindow(App app) : base("Wardrobe###WardrobeMain")
    {
        this.app = app;
        wardrobe = new WardrobeTab(app);
        designs = new DesignsTab(app);
        diagnostics = new DiagnosticsTab(app);
        materials = new MaterialsTab(app);
        status = new StatusTab(app);
        import = new ImportTab(app);

        Size = new Vector2(1000, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen() => app.Ipc.Dirty = true;

    public override void OnClose()
    {
        // Closing the window must never leave a preview on your character.
        app.Preview.Revert();
        app.Materials.ClearPreview();
    }

    public override void Draw()
    {
        if (app.Ipc.Dirty)
            app.Refresh();

        PreviewRequest? hovered = null;

        DrawTopBar();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##wardrobe-tabs"))
        {
            if (ImGui.BeginTabItem("Wardrobe"))
            {
                hovered = wardrobe.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Designs"))
            {
                hovered = designs.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Materials"))
            {
                materials.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Import"))
            {
                import.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Diagnostics"))
            {
                diagnostics.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Status"))
            {
                status.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        app.Preview.Update(hovered);
    }

    private void DrawTopBar()
    {
        var previewing = app.Preview.Active != null;
        if (previewing)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.25f, 0.2f, 1f));
        if (ImGui.Button("Revert preview"))
            app.Preview.Revert();
        if (previewing)
            ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.BeginDisabled(!app.Preview.CanUndo);
        if (ImGui.Button("Undo last apply"))
            app.Message = app.Preview.UndoLast(app.Catalog);
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
            app.Refresh();

        ImGui.SameLine();
        ImGui.TextColored(previewing ? Colours.Warn : Colours.Dim,
            previewing ? $"Previewing: {app.Preview.Active!.Label}{(app.Preview.Pinned != null ? "  (pinned)" : string.Empty)}" : "No preview active");

        var shown = !string.IsNullOrEmpty(app.Message) ? app.Message : app.Preview.LastMessage;
        if (!string.IsNullOrEmpty(shown))
        {
            ImGui.SameLine();
            ImGui.TextDisabled("   " + shown);
        }
    }
}
