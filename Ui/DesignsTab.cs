using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Wardrobe.Ui;

/// <summary>Your Glamourer designs as tabs you can hover to preview, click to pin and apply, like the in-game glamour plates.</summary>
public sealed class DesignsTab
{
    private readonly App app;
    private List<DesignInfo> designs = [];
    private bool loaded;
    private string filter = string.Empty;
    private string newName = string.Empty;
    private Guid? selected;

    public DesignsTab(App app) => this.app = app;

    public PreviewRequest? Draw()
    {
        PreviewRequest? hovered = null;
        if (!loaded)
        {
            designs = app.Ipc.Designs();
            loaded = true;
        }

        if (ImGui.Button("Reload designs"))
            designs = app.Ipc.Designs();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##designfilter", "Search designs", ref filter, 64);
        ImGui.SameLine();
        if (ImGui.Button("Open Glamourer"))
            app.Ipc.OpenGlamourer();
        ImGui.Separator();

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##designlist", new Vector2(avail.X * 0.6f, avail.Y - 70), true))
        {
            foreach (var d in designs.Where(d => filter.Length == 0 || d.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.Selectable($"{d.Path}###{d.Id}", selected == d.Id))
                {
                    selected = d.Id;
                    app.Preview.Pin(DesignRequest(d));
                }

                if (ImGui.IsItemHovered())
                    hovered = DesignRequest(d);
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##designdetail", new Vector2(0, avail.Y - 70), true))
        {
            var d = designs.FirstOrDefault(x => x.Id == selected);
            if (d == null)
            {
                ImGui.TextWrapped("Hover a design to preview it on your character. Click to pin it, then Apply to keep it.");
            }
            else
            {
                ImGui.TextWrapped(d.Name);
                ImGui.TextDisabled(d.Path);
                if (ImGui.Button("Apply"))
                    app.Message = app.Preview.Commit(DesignRequest(d), app.Catalog);
            }
        }

        ImGui.EndChild();

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##savedesign", "Name for the current look", ref newName, 64);
        ImGui.SameLine();
        ImGui.BeginDisabled(newName.Trim().Length == 0);
        if (ImGui.Button("Save current look as a design"))
        {
            var id = app.Ipc.SaveDesign(newName.Trim());
            app.Message = id != null ? $"Saved design '{newName.Trim()}'." : "Glamourer could not save the design.";
            designs = app.Ipc.Designs();
        }

        ImGui.EndDisabled();
        ImGui.TextDisabled("A design saves what you are wearing and your appearance. It does not remember Penumbra mod settings unless you link them in Glamourer.");
        return hovered;
    }

    private static PreviewRequest DesignRequest(DesignInfo d) => new()
    {
        Id = $"design|{d.Id}",
        Label = $"design {d.Name}",
        Design = d.Id,
    };
}
