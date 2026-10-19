using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Wardrobe.Core;

namespace Wardrobe;

/// <summary>What to put on the character as a preview (or to apply for real).</summary>
public sealed class PreviewRequest
{
    public string Id = string.Empty;
    public string Label = string.Empty;
    public ModState? Mod;
    public Dictionary<string, List<string>>? Options;
    public int? Priority;

    /// <summary>The priority used for the temporary preview only. Applying uses <see cref="Priority"/>.</summary>
    public int? PreviewPriority;
    public Area Area;
    public ItemInfo? Item;

    /// <summary>Other enabled mods that replace the same model or material files; Apply switches them off.</summary>
    public List<ModState>? TurnOff;
    public (string Key, int Value)? Customize;
    public Guid? Design;
}

/// <summary>
/// Puts a temporary look on the player (Penumbra temporary settings plus Glamourer's Once flag) and takes it off again.
/// Nothing a preview does is saved: reverting removes the temporary settings and restores the items.
/// </summary>
public sealed class PreviewController
{
    private sealed record Original(ulong ItemId, byte Stain, byte Stain2);

    private sealed class Undo
    {
        public string Label = string.Empty;
        public string? Dir;
        public string? Name;
        public bool WasEnabled;
        public int WasPriority;
        public Dictionary<string, List<string>> WasOptions = new();
        public Area? Area;
        public Original? Item;
        public List<(string Dir, string Name, bool Enabled, int Priority, Dictionary<string, List<string>> Options)> Others = new();
    }

    private readonly Ipc ipc;
    private readonly Configuration config;
    private readonly Dictionary<Area, Original> originals = new();
    private readonly HashSet<(string Dir, string Name)> touchedMods = new();
    private readonly List<Undo> undo = new();

    private string? snapshotBase64;
    private JObject? snapshot;
    private PreviewRequest? active;
    private PreviewRequest? pending;
    private PreviewRequest? pinned;
    private DateTime pendingSince;
    private DateTime lastWanted;
    private bool touchedCustomize;

    public PreviewController(Ipc ipc, Configuration config)
    {
        this.ipc = ipc;
        this.config = config;
    }

    public PreviewRequest? Active => active;
    public PreviewRequest? Pinned => pinned;
    public bool CanUndo => undo.Count > 0;
    public string LastMessage { get; private set; } = string.Empty;

    // ---------------------------------------------------------------- driving

    /// <summary>Call once a frame with whatever the mouse is over. Hover previews are debounced so sweeping the list does not spam redraws.</summary>
    public void Update(PreviewRequest? hovered)
    {
        var wanted = pinned ?? (config.LiveHover ? hovered : null);
        var now = DateTime.UtcNow;

        if (wanted != null)
        {
            lastWanted = now;
            if (pending?.Id != wanted.Id)
            {
                pending = wanted;
                pendingSince = now;
            }

            if (active?.Id != wanted.Id && (now - pendingSince).TotalMilliseconds >= config.HoverDelayMs)
                Show(wanted);
        }
        else
        {
            pending = null;
            if (active != null && (now - lastWanted).TotalMilliseconds >= config.RevertDelayMs)
                Revert();
        }
    }

    public void Pin(PreviewRequest request)
    {
        pinned = request;
        Show(request);
    }

    public void Unpin() => pinned = null;

    // ----------------------------------------------------------------- preview

    private void Show(PreviewRequest r)
    {
        if (snapshotBase64 == null)
        {
            snapshotBase64 = ipc.PlayerStateBase64();
            snapshot = ipc.PlayerState();
            if (snapshotBase64 == null)
            {
                LastMessage = "Glamourer has no state for your character yet.";
                Plugin.Log.Warning("Preview skipped: no Glamourer state for the player.");
                active = r; // back off instead of retrying every frame; hover another row to try again
                return;
            }
        }

        // Anything the previous preview changed that this one does not change goes back first.
        var keepDirs = r.Mod != null ? new HashSet<string> { r.Mod.Def.Directory } : new HashSet<string>();
        foreach (var m in touchedMods.Where(m => !keepDirs.Contains(m.Dir)).ToList())
        {
            ipc.RemoveTemporary(m.Dir, m.Name);
            touchedMods.Remove(m);
        }

        if (active?.Area != null && active.Item != null && active.Area != r.Area)
            RestoreSlot(active.Area);

        var needsRedraw = false;
        var failures = new List<string>();
        Plugin.Log.Information("Preview Show: {Label} (area {Area}, item {Item}, customize {Customize}, mod {Mod}, preview priority {Priority})",
            r.Label, r.Area, r.Item?.Id, r.Customize, r.Mod?.Def.Directory, r.PreviewPriority);

        if (r.Mod != null)
        {
            var options = r.Options ?? r.Mod.Sel.Options;
            var priority = Math.Max(r.PreviewPriority ?? r.Priority ?? r.Mod.Sel.Priority, r.Mod.Sel.Priority);
            if (ipc.SetTemporary(r.Mod.Def.Directory, r.Mod.Name, true, priority, options))
            {
                touchedMods.Add((r.Mod.Def.Directory, r.Mod.Name));
                needsRedraw = true;
            }
            else
            {
                failures.Add("Penumbra refused the temporary settings");
            }
        }

        if (r.Item != null)
        {
            var (stain, stain2) = StainsOf(r.Area);
            if (!originals.ContainsKey(r.Area))
                originals[r.Area] = OriginalOf(r.Area);
            if (!ipc.SetItem(r.Area, r.Item.Id, stain, stain2, once: true))
                failures.Add("Glamourer refused the item");
        }

        if (r.Customize is { } c && snapshot != null)
        {
            var edited = (JObject)snapshot.DeepClone();
            if (edited["Customize"]?[c.Key] is JObject node)
            {
                node["Value"] = c.Value;
                node["Apply"] = true;
                touchedCustomize = ipc.ApplyState(edited, equipment: false, customization: true, once: true);
                if (!touchedCustomize)
                {
                    failures.Add($"Glamourer would not apply {c.Key} {c.Value}");
                }
                else
                {
                    // Glamourer says "success" even for a value your race cannot use, then quietly keeps the old one. Check it stuck.
                    var actual = (int?)ipc.PlayerState()?["Customize"]?[c.Key]?["Value"];
                    if (actual != c.Value)
                        failures.Add($"your character can't use {c.Key} {c.Value} (Glamourer kept {actual?.ToString() ?? "its old value"}), so only the mod's files changed");
                }
            }
            else
            {
                failures.Add($"Glamourer's state has no '{c.Key}' entry to change");
            }
        }

        if (r.Design is { } design && !ipc.ApplyDesign(design, once: true))
            failures.Add("Glamourer refused the design");

        if (needsRedraw)
            ipc.Redraw();

        active = r;
        LastMessage = failures.Count == 0 ? $"Previewing {r.Label}" : $"Preview problem: {string.Join("; ", failures)}. See the Dalamud log.";
        Plugin.Log.Information("Preview result: {Message}", LastMessage);
    }

    public void Revert()
    {
        if (snapshotBase64 == null && touchedMods.Count == 0)
        {
            active = null;
            pinned = null;
            return;
        }

        Plugin.Log.Information("Preview Revert (was {Label})", active?.Label);
        foreach (var m in touchedMods)
            ipc.RemoveTemporary(m.Dir, m.Name);
        touchedMods.Clear();

        if (snapshotBase64 != null)
            ipc.ApplyState(snapshotBase64, equipment: true, customization: true, once: true);

        ipc.Redraw();
        Forget();
        LastMessage = "Preview reverted.";
    }

    private void Forget()
    {
        snapshotBase64 = null;
        snapshot = null;
        active = null;
        pending = null;
        pinned = null;
        touchedCustomize = false;
        originals.Clear();
    }

    private void RestoreSlot(Area area)
    {
        if (originals.TryGetValue(area, out var o))
        {
            ipc.SetItem(area, o.ItemId, o.Stain, o.Stain2, once: true);
            originals.Remove(area);
        }
    }

    // ------------------------------------------------------------------ commit

    /// <summary>Makes a request permanent: enable the mod, raise its priority, set its options, then equip the item for real.</summary>
    public string Commit(PreviewRequest r, Catalog catalog)
    {
        var collection = catalog.Collection;
        if (collection == null)
            return "No Penumbra collection found.";

        var record = new Undo { Label = r.Label };
        var ok = true;

        if (r.Mod != null)
        {
            var mod = r.Mod;
            record.Dir = mod.Def.Directory;
            record.Name = mod.Name;
            record.WasEnabled = mod.Sel.Enabled;
            record.WasPriority = mod.Sel.Priority;
            record.WasOptions = mod.Sel.Options.ToDictionary(k => k.Key, k => k.Value.ToList());

            ipc.RemoveTemporary(mod.Def.Directory, mod.Name);
            touchedMods.Remove((mod.Def.Directory, mod.Name));

            if (!mod.Sel.Enabled)
                ok &= ipc.SetEnabled(collection.Value.Id, mod.Def.Directory, mod.Name, true);
            if (r.Priority is { } p && p > mod.Sel.Priority)
                ok &= ipc.SetPriority(collection.Value.Id, mod.Def.Directory, mod.Name, p);
            if (r.Options != null)
                foreach (var (group, names) in r.Options)
                    if (!mod.Sel.Options.TryGetValue(group, out var current) || !current.OrderBy(x => x).SequenceEqual(names.OrderBy(x => x)))
                        ok &= ipc.SetOptions(collection.Value.Id, mod.Def.Directory, mod.Name, group, names);
        }

        if (r.TurnOff != null)
        {
            foreach (var other in r.TurnOff.Where(o => o.Sel.Enabled && o.Def.Directory != r.Mod?.Def.Directory))
            {
                record.Others.Add((other.Def.Directory, other.Name, true, other.Sel.Priority, other.Sel.Options.ToDictionary(k => k.Key, k => k.Value.ToList())));
                ok &= ipc.SetEnabled(collection.Value.Id, other.Def.Directory, other.Name, false);
            }
        }

        if (r.Item != null)
        {
            record.Area = r.Area;
            record.Item = originals.TryGetValue(r.Area, out var o) ? o : OriginalOf(r.Area);
            var (stain, stain2) = StainsOf(r.Area);
            ok &= ipc.SetItem(r.Area, r.Item.Id, stain, stain2, once: false);
        }

        if (r.Customize is { } c && snapshot != null)
        {
            var edited = (JObject)snapshot.DeepClone();
            if (edited["Customize"]?[c.Key] is JObject node)
            {
                node["Value"] = c.Value;
                node["Apply"] = true;
                ok &= ipc.ApplyState(edited, equipment: false, customization: true, once: false);
            }
        }

        if (r.Design is { } d)
            ok &= ipc.ApplyDesign(d, once: false);

        ipc.Redraw();
        undo.Add(record);
        Forget();
        ipc.Dirty = true;
        var turnedOff = record.Others.Count > 0 ? $" Turned off {string.Join(", ", record.Others.Select(o => o.Name))}." : string.Empty;
        return ok ? $"Applied {r.Label}.{turnedOff}" : $"Applied {r.Label}, but at least one step failed. See the Dalamud log.{turnedOff}";
    }

    /// <summary>Switches a mod on in your collection without equipping anything. Can be undone with Undo last apply.</summary>
    public string EnableOnly(ModState mod, Catalog catalog)
    {
        if (catalog.Collection == null)
            return "No Penumbra collection found.";
        if (mod.Sel.Enabled)
            return $"{mod.Name} is already on.";

        var record = new Undo
        {
            Label = $"turning on {mod.Name}",
            Dir = mod.Def.Directory,
            Name = mod.Name,
            WasEnabled = false,
            WasPriority = mod.Sel.Priority,
            WasOptions = mod.Sel.Options.ToDictionary(k => k.Key, k => k.Value.ToList()),
        };

        // Drop any temporary preview of this mod first so the real setting is what you see.
        ipc.RemoveTemporary(mod.Def.Directory, mod.Name);
        touchedMods.Remove((mod.Def.Directory, mod.Name));

        if (!ipc.SetEnabled(catalog.Collection.Value.Id, mod.Def.Directory, mod.Name, true))
            return $"Penumbra would not turn on {mod.Name}. See the Dalamud log.";

        undo.Add(record);
        ipc.Redraw();
        ipc.Dirty = true;
        return $"Turned on {mod.Name}.";
    }

    public string UndoLast(Catalog catalog)
    {
        if (undo.Count == 0 || catalog.Collection == null)
            return "Nothing to undo.";

        var u = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Restore(u, catalog);
        return $"Undid {u.Label}.";
    }

    /// <summary>
    /// The second click on something you applied: stop previewing it and reverse the last apply or turn-on of that mod, including any
    /// mods the apply switched off.
    /// </summary>
    public string Unapply(ModState mod, Catalog catalog)
    {
        var hadPreview = active != null;
        if (hadPreview)
            Revert();

        var index = undo.FindLastIndex(u => u.Dir == mod.Def.Directory);
        if (index < 0)
            return hadPreview ? $"Stopped previewing {mod.Name}." : $"Nothing from this session to undo for {mod.Name}. To switch it off, use Turn this mod off.";

        var u = undo[index];
        undo.RemoveAt(index);
        Restore(u, catalog);
        return $"Unapplied {mod.Name}.";
    }

    /// <summary>Switches a mod off in your collection. Can be undone with Undo last apply.</summary>
    public string TurnOff(ModState mod, Catalog catalog)
    {
        if (catalog.Collection == null)
            return "No Penumbra collection found.";
        if (!mod.Sel.Enabled)
            return $"{mod.Name} is already off.";

        var record = new Undo
        {
            Label = $"turning off {mod.Name}",
            Dir = mod.Def.Directory,
            Name = mod.Name,
            WasEnabled = true,
            WasPriority = mod.Sel.Priority,
            WasOptions = mod.Sel.Options.ToDictionary(k => k.Key, k => k.Value.ToList()),
        };
        if (!ipc.SetEnabled(catalog.Collection.Value.Id, mod.Def.Directory, mod.Name, false))
            return $"Penumbra would not turn off {mod.Name}. See the Dalamud log.";

        undo.Add(record);
        ipc.Redraw();
        ipc.Dirty = true;
        return $"Turned off {mod.Name}.";
    }

    private void Restore(Undo u, Catalog catalog)
    {
        if (catalog.Collection == null)
            return;
        var id = catalog.Collection.Value.Id;
        if (u.Dir != null && u.Name != null)
        {
            ipc.SetEnabled(id, u.Dir, u.Name, u.WasEnabled);
            ipc.SetPriority(id, u.Dir, u.Name, u.WasPriority);
            foreach (var (group, names) in u.WasOptions)
                ipc.SetOptions(id, u.Dir, u.Name, group, names);
        }

        foreach (var (dir, name, enabled, priority, options) in u.Others)
        {
            ipc.SetEnabled(id, dir, name, enabled);
            ipc.SetPriority(id, dir, name, priority);
            foreach (var (group, names) in options)
                ipc.SetOptions(id, dir, name, group, names);
        }

        if (u.Area != null && u.Item != null)
            ipc.SetItem(u.Area.Value, u.Item.ItemId, u.Item.Stain, u.Item.Stain2, once: false);

        ipc.Redraw();
        ipc.Dirty = true;
    }

    // ----------------------------------------------------------------- helpers

    private (byte, byte) StainsOf(Area area)
    {
        var o = originals.TryGetValue(area, out var x) ? x : OriginalOf(area);
        return (o.Stain, o.Stain2);
    }

    private Original OriginalOf(Area area)
    {
        var json = snapshot ?? ipc.PlayerState();
        if (json?["Equipment"]?[area.ToString()] is JObject e)
            return new Original((ulong?)e["ItemId"] ?? 0, (byte)((int?)e["Stain"] ?? 0), (byte)((int?)e["Stain2"] ?? 0));
        return new Original(0, 0, 0);
    }
}
