using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wardrobe.Core;

namespace Wardrobe.Ui;

/// <summary>Slots on the left, what fits the slot in the middle, details and options on the right.</summary>
public sealed class WardrobeTab
{
    private readonly App app;

    private Area area = Area.Body;
    private string filter = string.Empty;
    private string vanillaFilter = string.Empty;
    private string? selectedKey;
    private int itemChoice;
    private string designName = string.Empty;
    private readonly Dictionary<string, bool> rivalOff = new();

    private readonly Dictionary<string, Dictionary<string, List<string>>> pendingOptions = new();
    private readonly Dictionary<string, int> pendingPriority = new();

    public WardrobeTab(App app) => this.app = app;

    public PreviewRequest? Draw()
    {
        PreviewRequest? hovered = null;
        var avail = ImGui.GetContentRegionAvail();

        if (ImGui.BeginChild("##slots", new Vector2(210, avail.Y), true))
            DrawSlots();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##choices", new Vector2(avail.X * 0.38f, avail.Y), true))
            hovered = DrawChoices();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##details", new Vector2(0, avail.Y), true))
            DrawDetails();
        ImGui.EndChild();

        return hovered;
    }

    // ------------------------------------------------------------------ slots

    private void DrawSlots()
    {
        foreach (var (a, label) in Shared.Slots)
        {
            if (ImGui.Selectable($"{label}###slot{a}", area == a))
            {
                area = a;
                selectedKey = null;
                itemChoice = 0;
                app.Preview.Unpin();
            }

            ImGui.SameLine(100);
            ImGui.TextDisabled(Truncate(Equipped(a), 18));
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Glasses: use Glamourer for now.");
    }

    private string Equipped(Area a)
    {
        var state = app.State;
        if (state == null)
            return string.Empty;
        try
        {
            if (a <= Area.OffHand && state["Equipment"]?[a.ToString()]?["ItemId"] is { } id)
            {
                var item = app.Items.ById((ulong)id);
                var raw = (ulong)id;
                return raw == 0 ? "(empty)" : item?.Name ?? $"id {raw}";
            }

            var key = a switch { Area.Hair => "Hairstyle", Area.Face => "Face", Area.Tail => "TailShape", _ => null };
            if (key != null && state["Customize"]?[key]?["Value"] is { } v)
                return $"#{(int)v}";
        }
        catch
        {
            // fall through
        }

        return string.Empty;
    }

    // ----------------------------------------------------------------- choices

    private PreviewRequest? DrawChoices()
    {
        PreviewRequest? hovered = null;
        ImGui.Text(Shared.Label(area));
        ImGui.SameLine();
        var enabledOnly = app.Config.EnabledOnly;
        if (ImGui.Checkbox("Enabled mods only", ref enabledOnly))
        {
            app.Config.EnabledOnly = enabledOnly;
            app.SaveConfig();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search mods or items", ref filter, 128);
        ImGui.Separator();

        var entries = app.Catalog.For(area, app.Config.EnabledOnly, filter).ToList();
        ImGui.TextDisabled($"{entries.Count} mod{(entries.Count == 1 ? string.Empty : "s")} for this slot");

        foreach (var e in entries)
        {
            var key = KeyOf(e);
            var standing = app.Catalog.Standing(e);
            var colour = !e.ActiveNow ? Colours.Dim
                : standing.Total > 0 && standing.Winning == 0 ? Colours.Bad
                : standing.Winning < standing.Total ? Colours.Warn
                : Colours.Good;

            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            var clicked = ImGui.Selectable($"{e.Title}###{key}", selectedKey == key);
            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
            {
                hovered = BuildRequest(e, Math.Min(selectedKey == key ? itemChoice : 0, Math.Max(e.Items.Count - 1, 0)));
                DrawHoverTooltip(e, standing);
            }

            if (clicked && selectedKey == key)
            {
                // A second click on the same row takes it back off.
                app.Message = app.Preview.Unapply(e.Mod, app.Catalog);
                selectedKey = null;
            }
            else if (clicked)
            {
                selectedKey = key;
                itemChoice = 0;
                if (app.Config.ClickEnables && !e.Mod.Sel.Enabled)
                    app.Message = app.Preview.EnableOnly(e.Mod, app.Catalog);
                var req = BuildRequest(e, 0);
                if (req != null)
                    app.Preview.Pin(req);
            }
        }

        if (area <= Area.OffHand)
        {
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Vanilla items"))
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##vfilter", "Search items", ref vanillaFilter, 128);
                var shown = 0;
                foreach (var item in app.Items.ForArea(area, vanillaFilter))
                {
                    if (++shown > 150)
                    {
                        ImGui.TextDisabled("(refine the search to see more)");
                        break;
                    }

                    var key = $"vanilla|{item.Id}";
                    if (ImGui.Selectable($"{item.Name}###{key}", selectedKey == key))
                    {
                        selectedKey = key;
                        app.Preview.Pin(VanillaRequest(item));
                    }

                    if (ImGui.IsItemHovered())
                        hovered = VanillaRequest(item);
                }
            }
        }

        return hovered;
    }

    private void DrawHoverTooltip(SlotEntry e, (List<(string Winner, int Count, int Priority, bool Tie)> LosesTo, int Winning, int Total) standing)
    {
        ImGui.BeginTooltip();
        ImGui.Text(e.Mod.Name);
        ImGui.TextDisabled($"{(e.Mod.Sel.Enabled ? "Enabled" : "Off")}, priority {e.Mod.Sel.Priority}");
        if (standing.Total > 0)
            ImGui.Text($"Wins {standing.Winning} of {standing.Total} files for this slot");
        var needed = app.Catalog.PriorityToWin(e);
        ImGui.TextDisabled(needed > e.Mod.Sel.Priority
            ? $"Needs priority {needed} to be seen over everything else. The preview runs at {Math.Max(app.Config.PreviewPriority, needed)}."
            : "Already high enough to be seen.");
        if (app.Config.ShowHoverImage)
            Shared.Image(app, e.Mod.Def.PreviewImage, 420);
        ImGui.EndTooltip();
    }

    // ----------------------------------------------------------------- details

    private void DrawDetails()
    {
        if (selectedKey == null)
        {
            ImGui.TextWrapped("Hover a mod or item to preview it on your character. Click one to pin the preview and see its options. Click it again to take it back off.");
            return;
        }

        if (selectedKey.StartsWith("vanilla|"))
        {
            DrawVanillaDetails();
            return;
        }

        var entry = app.Catalog.For(area, false, string.Empty).FirstOrDefault(e => KeyOf(e) == selectedKey);
        if (entry == null)
        {
            ImGui.TextDisabled("That entry is no longer available.");
            return;
        }

        var request = BuildRequest(entry, itemChoice);
        var mod = entry.Mod;

        ImGui.TextWrapped(mod.Name);
        ImGui.TextDisabled($"{mod.Def.Author ?? "unknown author"}  v{mod.Def.Version ?? "?"}    {(mod.Sel.Enabled ? "enabled" : "OFF")}, priority {mod.Sel.Priority}");

        if (entry.Items.Count > 1)
        {
            var names = entry.Items.Select(i => $"{i.Name} (v{i.Variant})").ToArray();
            if (ImGui.Combo("Equip as", ref itemChoice, names, names.Length) && request != null)
            {
                itemChoice = Math.Clamp(itemChoice, 0, entry.Items.Count - 1);
                request = BuildRequest(entry, itemChoice);
                if (request != null)
                    app.Preview.Pin(request);
            }
        }
        else if (entry.Items.Count == 0 && area <= Area.OffHand)
        {
            ImGui.TextColored(Colours.Warn, "No vanilla item uses this model set, so Glamourer can't equip it by itself.");
        }

        if (area is Area.Hair or Area.Face or Area.Tail && request?.Customize == null)
            ImGui.TextColored(Colours.Warn, "Penumbra didn't say which style number this mod replaces. Apply turns the mod on; pick the style in Glamourer.");

        if (!mod.Sel.Enabled && ImGui.Button("Turn this mod on"))
            app.Message = app.Preview.EnableOnly(mod, app.Catalog);
        if (mod.Sel.Enabled && ImGui.Button("Turn this mod off"))
            app.Message = app.Preview.TurnOff(mod, app.Catalog);

        DrawRivals(entry);

        DrawStanding(entry);
        ImGui.Separator();
        DrawOptions(entry);
        ImGui.Separator();

        var disabled = request == null;
        ImGui.BeginDisabled(disabled);
        if (ImGui.Button("Apply"))
            app.Message = app.Preview.Commit(request!, app.Catalog);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Turns the mod on in your collection with these options and equips the item for real.");

        ImGui.SameLine();
        var win = app.Catalog.PriorityToWin(entry);
        var pending = pendingPriority.GetValueOrDefault(mod.Def.Directory, mod.Sel.Priority);
        ImGui.BeginDisabled(pending >= win);
        if (ImGui.Button($"Make this win (priority {win})"))
        {
            pendingPriority[mod.Def.Directory] = win;
            var r = BuildRequest(entry, itemChoice);
            if (r != null)
                app.Preview.Pin(r);
        }

        ImGui.EndDisabled();

        ImGui.Spacing();
        if (app.Config.ShowHoverImage)
            Shared.Image(app, mod.Def.PreviewImage, ImGui.GetContentRegionAvail().X - 8);

        ImGui.Separator();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##designname", "Design name", ref designName, 64);
        ImGui.SameLine();
        ImGui.BeginDisabled(designName.Trim().Length == 0);
        if (ImGui.Button("Save look as Glamourer design"))
        {
            var id = app.Ipc.SaveDesign(designName.Trim());
            app.Message = id != null ? $"Saved design '{designName.Trim()}'." : "Glamourer could not save the design.";
        }

        ImGui.EndDisabled();
        ImGui.TextDisabled("Apply first, then save, so the design includes the change.");
    }

    private void DrawVanillaDetails()
    {
        var id = uint.Parse(selectedKey!.Split('|')[1]);
        var item = app.Items.ById(id);
        ImGui.TextWrapped(item?.Name ?? $"Item {id}");
        if (item == null)
            return;

        ImGui.TextDisabled($"Model set {item.SetId}, variant {item.Variant}");
        var claims = app.Catalog.Outcomes.Values
            .Where(o => o.Info.Area == item.Area && o.Info.SetId == item.SetId)
            .Select(o => o.Winner.Mod.Name).Distinct().ToList();
        if (claims.Count > 0)
            ImGui.TextColored(Colours.Warn, "Mods change this item's files: " + string.Join(", ", claims));

        if (ImGui.Button("Apply"))
            app.Message = app.Preview.Commit(VanillaRequest(item), app.Catalog);
    }

    /// <summary>The mods Apply would switch off for this entry, after your tick boxes and the always-keep-on list.</summary>
    private List<ModState>? RivalsToTurnOff(SlotEntry e)
    {
        if (!app.Config.TurnOffConflicting)
            return null;
        return app.Catalog.Alternatives(e)
            .Where(r => !app.Config.NeverTurnOff.Contains(r.Def.Directory))
            .Where(r => !rivalOff.TryGetValue(r.Def.Directory, out var off) || off)
            .ToList();
    }

    /// <summary>One tick box per mod that replaces the same model or material files, so you decide what Apply switches off.</summary>
    private void DrawRivals(SlotEntry entry)
    {
        if (!app.Config.TurnOffConflicting)
            return;
        var rivals = app.Catalog.Alternatives(entry);
        if (rivals.Count == 0)
            return;

        ImGui.TextColored(Colours.Warn, "These replace the same model or material files:");
        var changed = false;
        foreach (var r in rivals)
        {
            ImGui.PushID(r.Def.Directory);
            var kept = app.Config.NeverTurnOff.Contains(r.Def.Directory);
            if (kept)
            {
                ImGui.BulletText($"{r.Name}  (always kept on)");
                ImGui.SameLine();
                if (ImGui.SmallButton("Allow turning off"))
                {
                    app.Config.NeverTurnOff.Remove(r.Def.Directory);
                    app.SaveConfig();
                    changed = true;
                }
            }
            else
            {
                var off = !rivalOff.TryGetValue(r.Def.Directory, out var v) || v;
                if (ImGui.Checkbox($"Turn off  {r.Name}", ref off))
                {
                    rivalOff[r.Def.Directory] = off;
                    changed = true;
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Always keep on"))
                {
                    app.Config.NeverTurnOff.Add(r.Def.Directory);
                    app.SaveConfig();
                    changed = true;
                }
            }

            ImGui.PopID();
        }

        if (changed)
        {
            var req = BuildRequest(entry, itemChoice);
            if (req != null)
                app.Preview.Pin(req);
        }
    }

    private void DrawStanding(SlotEntry e)
    {
        var s = app.Catalog.Standing(e);
        if (!e.Mod.Sel.Enabled)
            ImGui.TextColored(Colours.Dim, "This mod is off. Previewing uses temporary settings; Apply turns it on. The standing below is what it would be once enabled.");

        if (s.Total == 0)
        {
            ImGui.TextDisabled("No file conflicts to report for this slot.");
            return;
        }

        if (s.LosesTo.Count == 0)
        {
            ImGui.TextColored(Colours.Good, $"Wins all {s.Total} of its files for this slot.");
            return;
        }

        ImGui.TextColored(s.Winning == 0 ? Colours.Bad : Colours.Warn, $"Wins {s.Winning} of {s.Total} files for this slot. Overridden by:");
        foreach (var (winner, count, priority, tie) in s.LosesTo)
            ImGui.BulletText($"{winner}  ({count} file{(count == 1 ? string.Empty : "s")}, priority {priority}{(tie ? ", tie" : string.Empty)})");
    }

    private void DrawOptions(SlotEntry e)
    {
        var mod = e.Mod;
        var groups = mod.Def.Groups.Where(g => g.Options.Count > 0).ToList();
        if (groups.Count == 0)
        {
            ImGui.TextDisabled("This mod has no options.");
            return;
        }

        if (!ImGui.CollapsingHeader($"Options ({groups.Count} groups)", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var opts = OptionsFor(mod);
        var changed = false;
        foreach (var g in groups)
        {
            if (!opts.TryGetValue(g.Name, out var picked))
                opts[g.Name] = picked = Resolver.DefaultNames(g);

            ImGui.PushID(g.Name);
            ImGui.Text(g.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"[{g.Type}]");

            if (g.Type == "Single")
            {
                var current = Math.Max(0, g.Options.FindIndex(o => picked.Contains(o.Name)));
                var names = g.Options.Select(o => o.Name).ToArray();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##single", ref current, names, names.Length))
                {
                    opts[g.Name] = [names[current]];
                    changed = true;
                }
            }
            else
            {
                foreach (var o in g.Options)
                {
                    var on = picked.Contains(o.Name);
                    if (ImGui.Checkbox(o.Name, ref on))
                    {
                        if (on) picked.Add(o.Name); else picked.Remove(o.Name);
                        changed = true;
                    }

                    if (o.Description.Length > 0 && ImGui.IsItemHovered())
                        ImGui.SetTooltip(o.Description);
                }
            }

            ImGui.PopID();
        }

        if (changed)
        {
            var r = BuildRequest(e, itemChoice);
            if (r != null)
                app.Preview.Pin(r);
        }
    }

    // ----------------------------------------------------------------- requests

    private Dictionary<string, List<string>> OptionsFor(ModState mod)
    {
        if (!pendingOptions.TryGetValue(mod.Def.Directory, out var opts))
            pendingOptions[mod.Def.Directory] = opts = mod.Sel.Options.ToDictionary(k => k.Key, k => k.Value.ToList());
        return opts;
    }

    private PreviewRequest? BuildRequest(SlotEntry e, int itemIndex)
    {
        var opts = OptionsFor(e.Mod);
        var priority = pendingPriority.GetValueOrDefault(e.Mod.Def.Directory, e.Mod.Sel.Priority);
        var chosen = opts.ToDictionary(k => k.Key, k => k.Value.ToList());
        SwapVariant(chosen, e);
        var req = new PreviewRequest
        {
            Mod = e.Mod,
            Options = chosen,
            Priority = priority,
            TurnOff = RivalsToTurnOff(e),
            PreviewPriority = Math.Max(app.Config.PreviewPriority, app.Catalog.PriorityToWin(e)),
            Area = e.Area,
            Label = e.NeedsDetail ? $"{e.Mod.Name} [{e.Detail}]" : e.Mod.Name,
        };

        if (e.Items.Count > 0)
            req.Item = e.Items[Math.Clamp(itemIndex, 0, e.Items.Count - 1)];
        else if (e.Area is Area.Hair or Area.Face or Area.Tail)
        {
            var match = Catalog.CustomizeFor(e);
            if (match != null)
                req.Customize = match;
        }

        var optionKey = string.Join(";", req.Options.OrderBy(k => k.Key).Select(k => k.Key + "=" + string.Join(",", k.Value.OrderBy(x => x))));
        req.Id = $"{e.Mod.Def.Directory}|{e.Area}|{e.SetId}|{req.Item?.Id}|{req.Customize?.Value}|{priority}|{optionKey}";
        return req;
    }

    /// <summary>
    /// Mods like the Wheel give every style one option per variant ("Tail 1" to "Tail 8"). To show variant N, swap the variant options
    /// you currently have switched on in each group for this variant's option, so it appears in the style you picked.
    /// </summary>
    private void SwapVariant(Dictionary<string, List<string>> options, SlotEntry e)
    {
        var siblings = app.Catalog.Entries
            .Where(x => x.Mod.Def.Directory == e.Mod.Def.Directory && x.Area == e.Area && x.SetId != e.SetId)
            .ToList();
        if (siblings.Count == 0)
            return;

        var swapped = false;
        foreach (var byGroup in e.Sources.Where(s => s.Group != null && s.Option != null).GroupBy(s => s.Group!))
        {
            var group = e.Mod.Def.Groups.FirstOrDefault(g => g.Name == byGroup.Key);
            if (group == null)
                continue;
            var mine = byGroup.Select(s => s.Option!).Distinct().ToList();
            var theirs = siblings.SelectMany(x => x.Sources)
                .Where(s => s.Group == byGroup.Key && s.Option != null && !mine.Contains(s.Option))
                .Select(s => s.Option!).ToHashSet();

            if (!options.TryGetValue(byGroup.Key, out var picked))
                options[byGroup.Key] = picked = Resolver.DefaultNames(group);
            if (!picked.Any(theirs.Contains))
                continue;

            picked.RemoveAll(theirs.Contains);
            foreach (var o in group.Type == "Single" ? mine.Take(1) : mine)
                if (!picked.Contains(o))
                    picked.Add(o);
            swapped = true;
        }

        if (swapped)
            return;

        // No variant option is on in any group yet, so switch this one on in the first group that offers it.
        var first = e.Sources.FirstOrDefault(s => s.Group != null && s.Option != null);
        if (first.Group == null)
            return;
        var g0 = e.Mod.Def.Groups.First(g => g.Name == first.Group);
        if (!options.TryGetValue(g0.Name, out var list))
            options[g0.Name] = list = Resolver.DefaultNames(g0);
        if (g0.Type == "Single")
            list.Clear();
        if (!list.Contains(first.Option!))
            list.Add(first.Option!);
    }

    private static PreviewRequest VanillaRequest(ItemInfo item) => new()
    {
        Id = $"vanilla|{item.Id}",
        Label = item.Name,
        Area = item.Area,
        Item = item,
    };

    private string KeyOf(SlotEntry e) => $"{e.Mod.Def.Directory}|{e.Area}|{e.SetId}";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
