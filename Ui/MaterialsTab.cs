using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wardrobe.Core;

namespace Wardrobe.Ui;

/// <summary>Colour and emission editing for the materials your mods actually use, without opening Penumbra's Material Editor.</summary>
public sealed class MaterialsTab
{
    private readonly App app;
    private int areaIndex;
    private string? selectedPath;
    private MaterialEdit? edit;
    private string error = string.Empty;
    private string note = string.Empty;

    private static readonly Area[] Areas =
    [
        Area.Skin, Area.Face, Area.Eyes, Area.Hair, Area.Tail, Area.Head, Area.Body, Area.Hands, Area.Legs, Area.Feet,
        Area.Ears, Area.Neck, Area.Wrists, Area.RFinger, Area.LFinger,
    ];

    public MaterialsTab(App app) => this.app = app;

    public void Draw()
    {
        ImGui.TextWrapped("Pick a slot, pick one of its materials, and change its colours. Previews are temporary. Save writes a copy into a separate mod called 'Wardrobe Overrides' and never edits the original mod.");
        ImGui.Separator();

        var labels = Areas.Select(Shared.Label).ToArray();
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Slot", ref areaIndex, labels, labels.Length))
        {
            selectedPath = null;
            edit = null;
            app.Materials.ClearPreview();
        }

        var paths = app.Catalog.MaterialsFor(Areas[areaIndex]).ToList();
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##matlist", new Vector2(avail.X * 0.42f, avail.Y), true))
        {
            if (paths.Count == 0)
                ImGui.TextDisabled("No enabled mod replaces a material for this slot.");

            foreach (var o in paths)
            {
                var name = System.IO.Path.GetFileName(o.GamePath);
                var label = $"{name}  -  {o.Winner.Mod.Name}";
                if (ImGui.Selectable($"{label}###{o.GamePath}", selectedPath == o.GamePath))
                {
                    app.Materials.ClearPreview();
                    selectedPath = o.GamePath;
                    (edit, error) = app.Materials.Open(o);
                    note = string.Empty;
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(o.GamePath + (o.Claims.Count > 1 ? $"\n{o.Claims.Count} mods claim this file; the winner is shown." : string.Empty));
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##matedit", new Vector2(0, avail.Y), true))
            DrawEditor();
        ImGui.EndChild();
    }

    private void DrawEditor()
    {
        if (edit == null)
        {
            ImGui.TextWrapped(error.Length > 0 ? error : "Pick a material on the left.");
            DrawSaved();
            return;
        }

        ImGui.TextWrapped(System.IO.Path.GetFileName(edit.GamePath));
        ImGui.TextDisabled($"From {edit.SourceMod}   shader {edit.File.ShaderName}");

        if (edit.File.ColourTableBytes > 0)
        {
            ImGui.TextColored(Colours.Warn, "This material colours itself through a colour table.");
            ImGui.TextWrapped("Equipment like this is dyed per row, which Glamourer's Advanced Dyes already does well. Use the button below.");
            if (ImGui.Button("Open Glamourer"))
                app.Ipc.OpenGlamourer();
        }

        var constants = edit.File.Constants;
        if (constants.Count == 0)
        {
            ImGui.TextDisabled("This material has no editable shader constants.");
        }
        else
        {
            ImGui.Separator();
            foreach (var c in constants)
                DrawConstant(c);
        }

        ImGui.Separator();
        if (ImGui.Button("Reset to the mod's values"))
        {
            app.Materials.Reset(edit);
            app.Materials.ClearPreview();
            note = "Reset.";
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!edit.Changed);
        if (ImGui.Button("Save"))
            note = app.Materials.Save(edit);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Writes this material into the 'Wardrobe Overrides' mod at priority 1000, above every other mod.");

        if (note.Length > 0)
            ImGui.TextWrapped(note);

        DrawSaved();
    }

    private void DrawConstant(MtrlConstant c)
    {
        ImGui.PushID((int)c.Id);
        var isColour = c.Name.Contains("Color", StringComparison.OrdinalIgnoreCase) && c.Values.Length is 3 or 4;
        var changed = false;
        var released = false;

        if (isColour && c.Values.Length >= 3)
        {
            var v = new Vector3(c.Values[0], c.Values[1], c.Values[2]);
            ImGui.SetNextItemWidth(220);
            if (ImGui.ColorEdit3(c.Name, ref v, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.Hdr))
            {
                var values = (float[])c.Values.Clone();
                values[0] = v.X;
                values[1] = v.Y;
                values[2] = v.Z;
                changed = edit!.File.Set(c.Id, values);
            }

            released = ImGui.IsItemDeactivatedAfterEdit();
        }
        else
        {
            var values = (float[])c.Values.Clone();
            for (var i = 0; i < values.Length; i++)
            {
                ImGui.SetNextItemWidth(80);
                var f = values[i];
                if (ImGui.DragFloat($"##{i}", ref f, 0.01f))
                {
                    values[i] = f;
                    changed = true;
                }

                released |= ImGui.IsItemDeactivatedAfterEdit();
                ImGui.SameLine();
            }

            ImGui.Text(c.Name);
            if (changed)
                edit!.File.Set(c.Id, values);
        }

        if (changed)
            edit!.Changed = true;
        if (released && edit!.Changed)
            note = app.Materials.Preview(edit) ? "Previewing (not saved)." : "Could not preview. See the Dalamud log.";
        ImGui.PopID();
    }

    private void DrawSaved()
    {
        if (app.Materials.Saved.Count == 0)
            return;
        ImGui.Separator();
        if (ImGui.CollapsingHeader($"Saved overrides ({app.Materials.Saved.Count})"))
        {
            foreach (var path in app.Materials.Saved.Keys.ToList())
            {
                ImGui.PushID(path);
                ImGui.TextWrapped(System.IO.Path.GetFileName(path));
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                    note = app.Materials.Remove(path);
                ImGui.PopID();
            }
        }
    }
}
