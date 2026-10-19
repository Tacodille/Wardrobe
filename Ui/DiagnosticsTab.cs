using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json.Linq;
using Wardrobe.Core;

namespace Wardrobe.Ui;

/// <summary>Answers "why isn't this showing?": what Glamourer says you wear, what Penumbra actually serves, and which mod wins.</summary>
public sealed class DiagnosticsTab
{
    private readonly App app;
    private readonly Dictionary<string, string?> resolved = new();
    private string modFilter = string.Empty;

    public DiagnosticsTab(App app) => this.app = app;

    public void Draw()
    {
        if (ImGui.Button("Re-check now"))
            resolved.Clear();
        ImGui.SameLine();
        ImGui.TextDisabled("Asks Penumbra which file it serves for each worn item.");

        ImGui.Separator();
        DrawWorn();
        ImGui.Spacing();
        DrawContested();
    }

    private void DrawWorn()
    {
        ImGui.Text("What you are wearing, and where each model comes from");
        var state = app.State;
        if (state?["Equipment"] is not JObject equipment)
        {
            ImGui.TextDisabled("Glamourer has no state for you yet.");
            return;
        }

        if (!ImGui.BeginTable("##worn", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
            return;
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Glamourer says");
        ImGui.TableSetupColumn("Penumbra serves the model from");
        ImGui.TableSetupColumn("Mods fighting over it");
        ImGui.TableHeadersRow();

        foreach (var (area, label) in Shared.Slots.Where(s => s.Area <= Area.LFinger))
        {
            if (equipment[area.ToString()] is not JObject e)
                continue;
            var id = (ulong?)e["ItemId"] ?? 0;
            var item = app.Items.ById(id);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(label);
            ImGui.TableNextColumn();
            ImGui.Text(id == 0 ? "(empty)" : item?.Name ?? $"id {id}");

            ImGui.TableNextColumn();
            var paths = item == null ? null : PathInfo.ItemPaths(area, item.SetId, item.Variant, app.Catalog.RaceCode);
            if (item == null || paths == null)
            {
                ImGui.TextDisabled("-");
                ImGui.TableNextColumn();
                continue;
            }

            var model = paths.Value.Model;
            if (!resolved.TryGetValue(model, out var file))
                resolved[model] = file = app.Ipc.ResolvePath(model);

            var served = Describe(file, model);
            ImGui.TextColored(served.StartsWith("game") ? Colours.Dim : Colours.Good, served);

            ImGui.TableNextColumn();
            var claims = app.Catalog.Outcomes
                .Where(kv => kv.Value.Info.Area == area && kv.Value.Info.SetId == item.SetId)
                .SelectMany(kv => kv.Value.Claims.Select(c => (c.Mod.Name, c.ModPriority)))
                .GroupBy(x => x.Name)
                .Select(g => $"{g.Key} ({g.First().ModPriority})")
                .ToList();
            if (claims.Count == 0)
                ImGui.TextDisabled("none");
            else
                ImGui.TextWrapped(string.Join(", ", claims));
        }

        ImGui.EndTable();
    }

    private string Describe(string? resolvedFile, string gamePath)
    {
        if (resolvedFile == null)
            return "(Penumbra did not answer)";
        if (string.Equals(resolvedFile.Replace('\\', '/'), gamePath, StringComparison.OrdinalIgnoreCase))
            return "game file (no mod)";

        var root = app.Catalog.ModRoot;
        if (root != null && resolvedFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var rest = resolvedFile[root.Length..].TrimStart('\\', '/');
            var dir = rest.Split('\\', '/')[0];
            var mod = app.Catalog.Mods.FirstOrDefault(m => m.Def.Directory == dir);
            return mod?.Name ?? dir;
        }

        return resolvedFile;
    }

    private void DrawContested()
    {
        ImGui.Text("Files more than one enabled mod wants");
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##contfilter", "Filter by mod or file", ref modFilter, 64);

        var rows = app.Catalog.Outcomes.Values
            .Where(o => o.Claims.Count > 1)
            .Where(o => modFilter.Length == 0
                || o.GamePath.Contains(modFilter, StringComparison.OrdinalIgnoreCase)
                || o.Claims.Any(c => c.Mod.Name.Contains(modFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(o => o.GamePath, StringComparer.Ordinal)
            .ToList();
        ImGui.TextDisabled($"{rows.Count} files. Only the top mod's file is used; the others are completely hidden on that file.");

        if (!ImGui.BeginChild("##contested", new Vector2(0, 0), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var o in rows)
        {
            var area = o.Info.Area;
            ImGui.PushID(o.GamePath);
            ImGui.TextColored(o.Tie ? Colours.Warn : Colours.Good, $"[{area}] {System.IO.Path.GetFileName(o.GamePath)}");
            ImGui.SameLine();
            ImGui.TextDisabled(o.Tie ? "  tie at the top, so load order decides" : string.Empty);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(o.GamePath);
            foreach (var c in o.Claims)
                ImGui.BulletText($"{(c == o.Winner ? "WINS " : "hidden ")} {c.Mod.Name}  (priority {c.ModPriority}, {c.Label})");
            ImGui.PopID();
        }

        ImGui.EndChild();
    }
}
