using System.Linq;
using Dalamud.Bindings.ImGui;

namespace Wardrobe.Ui;

public sealed class StatusTab
{
    private readonly App app;

    public StatusTab(App app) => this.app = app;

    public void Draw()
    {
        ImGui.Text($"Penumbra IPC:   {app.Ipc.PenumbraVersion()}   (built against 5.19)");
        ImGui.Text($"Glamourer IPC:  {app.Ipc.GlamourerVersion()}   (built against 1.8)");
        ImGui.Separator();
        ImGui.Text($"Mod folder:  {app.Catalog.ModRoot ?? "unknown"}");
        ImGui.Text($"Your collection:  {(app.Catalog.Collection.HasValue ? app.Catalog.Collection.Value.Name : "unknown")}");
        ImGui.Text($"Mods read:  {app.Catalog.Mods.Count}  ({app.Catalog.Mods.Count(m => m.Sel.Enabled)} enabled)");
        ImGui.Text($"Files claimed by enabled mods:  {app.Catalog.Outcomes.Count}  ({app.Catalog.Outcomes.Values.Count(o => o.Claims.Count > 1)} contested)");
        ImGui.Text($"Vanilla items indexed:  {app.Items.Count}");
        ImGui.Text($"Your race code:  c{app.Catalog.RaceCode:D4}   (body files: c{app.Catalog.BodyCode:D4})");
        ImGui.Separator();

        var hover = app.Config.LiveHover;
        if (ImGui.Checkbox("Preview on hover", ref hover))
        {
            app.Config.LiveHover = hover;
            app.SaveConfig();
        }

        var clickEnables = app.Config.ClickEnables;
        if (ImGui.Checkbox("Clicking a mod that is off turns it on", ref clickEnables))
        {
            app.Config.ClickEnables = clickEnables;
            app.SaveConfig();
        }

        var cleanUp = app.Config.TurnOffConflicting;
        if (ImGui.Checkbox("Applying turns off other mods that replace the same model or material", ref cleanUp))
        {
            app.Config.TurnOffConflicting = cleanUp;
            app.SaveConfig();
        }

        if (app.Config.NeverTurnOff.Count > 0 && ImGui.TreeNode($"Mods Apply will never switch off ({app.Config.NeverTurnOff.Count})"))
        {
            foreach (var dir in app.Config.NeverTurnOff.ToList())
            {
                var name = app.Catalog.Mods.FirstOrDefault(m => m.Def.Directory == dir)?.Name ?? dir;
                ImGui.PushID(dir);
                ImGui.BulletText(name);
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                {
                    app.Config.NeverTurnOff.Remove(dir);
                    app.SaveConfig();
                }

                ImGui.PopID();
            }

            ImGui.TreePop();
        }

        var image = app.Config.ShowHoverImage;
        if (ImGui.Checkbox("Show mod preview images", ref image))
        {
            app.Config.ShowHoverImage = image;
            app.SaveConfig();
        }

        var delay = app.Config.HoverDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Hover delay (ms)", ref delay, 0, 1000))
        {
            app.Config.HoverDelayMs = delay;
            app.SaveConfig();
        }

        var prio = app.Config.PreviewPriority;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Preview priority", ref prio, 0, 500))
        {
            app.Config.PreviewPriority = prio;
            app.SaveConfig();
        }

        ImGui.TextDisabled("Previews run at least this high so they show over other mods. Applying does not use it.");

        ImGui.Separator();
        ImGui.TextWrapped("Everything here is read-only until you press Apply or Save. Previews use Penumbra temporary settings and Glamourer's 'once' flag, and are removed when you revert, close this window or unload the plugin.");
    }
}
