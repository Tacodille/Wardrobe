using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json.Linq;

namespace Wardrobe.Ui;

/// <summary>Finds mod packs sitting in your mod folder or Downloads that Penumbra has not imported yet, and imports them through Penumbra.</summary>
public sealed class ImportTab
{
    private sealed class Pack
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public bool Known;
        public bool Queued;
        public long Size;
        public DateTime Written;
    }

    private readonly App app;
    private readonly Dictionary<string, string> nameCache = new();
    private List<Pack> packs = [];
    private bool scanned;
    private bool showKnown;
    private string filter = string.Empty;
    private string note = string.Empty;

    public ImportTab(App app) => this.app = app;

    public void Draw()
    {
        if (!scanned || app.Ipc.Dirty)
            Scan();

        ImGui.TextWrapped("Packs (.pmp and .ttmp2 files) in your mod folder and Downloads. Penumbra only uses a mod once it has imported the pack, and moving the file does not do that. Import asks Penumbra to do it for you.");
        if (ImGui.Button("Rescan"))
            Scan();
        ImGui.SameLine();
        var newOnes = packs.Where(p => !p.Known && !p.Queued).ToList();
        ImGui.BeginDisabled(newOnes.Count == 0);
        if (ImGui.Button($"Import all new ({newOnes.Count})"))
            foreach (var p in newOnes)
                Import(p);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Checkbox("Show ones Penumbra already has", ref showKnown);
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##importfilter", "Filter by name", ref filter, 64);
        if (note.Length > 0)
            ImGui.TextDisabled(note);
        ImGui.Separator();

        if (!ImGui.BeginChild("##importlist", new Vector2(0, 0), true))
        {
            ImGui.EndChild();
            return;
        }

        var shown = packs
            .Where(p => showKnown || !p.Known)
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(p.Path).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Known).ThenByDescending(p => p.Written)
            .ToList();
        if (shown.Count == 0)
            ImGui.TextDisabled(packs.Count == 0 ? "No packs found." : "Nothing new. Every pack found is already in Penumbra.");

        foreach (var p in shown)
        {
            ImGui.PushID(p.Path);
            var status = p.Queued ? "import queued" : p.Known ? "already in Penumbra" : "new";
            ImGui.TextColored(p.Known ? Colours.Dim : p.Queued ? Colours.Warn : Colours.Good, p.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"  {System.IO.Path.GetFileName(p.Path)}  ({p.Size / 1_048_576.0:0.#} MB, {status})");
            if (!p.Queued)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(p.Known ? "Import again" : "Import"))
                    Import(p);
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void Import(Pack p)
    {
        if (app.Ipc.InstallMod(p.Path))
        {
            p.Queued = true;
            note = $"Penumbra is importing {p.Name}. It will appear in the mod list when it finishes.";
            app.Ipc.Dirty = true;
        }
        else
        {
            note = $"Penumbra would not start importing {p.Name}. See the Dalamud log.";
        }
    }

    private void Scan()
    {
        scanned = true;
        var known = new HashSet<string>();
        foreach (var m in app.Catalog.Mods)
        {
            known.Add(Norm(m.Name));
            known.Add(Norm(m.Def.Directory));
        }

        var folders = new List<string>();
        if (app.Catalog.ModRoot != null)
            folders.Add(app.Catalog.ModRoot);
        var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
            folders.Add(downloads);

        var queued = packs.Where(p => p.Queued).Select(p => p.Path).ToHashSet();
        var result = new List<Pack>();
        foreach (var folder in folders.Distinct())
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".pmp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttmp2", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttmp", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                var info = new FileInfo(f);
                var name = PackName(info);
                var isKnown = known.Contains(Norm(name)) || known.Contains(Norm(System.IO.Path.GetFileNameWithoutExtension(f)));
                result.Add(new Pack { Path = f, Name = name, Known = isKnown, Queued = queued.Contains(f) && !isKnown, Size = info.Length, Written = info.LastWriteTimeUtc });
            }
        }

        packs = result;
    }

    /// <summary>The mod's own name from inside the pack, so "BM Spectral Steps.pmp" matches the mod "[BM] Spectral Steps".</summary>
    private string PackName(FileInfo file)
    {
        var key = file.FullName + "|" + file.LastWriteTimeUtc.Ticks;
        if (nameCache.TryGetValue(key, out var cached))
            return cached;

        var name = System.IO.Path.GetFileNameWithoutExtension(file.Name);
        try
        {
            using var zip = ZipFile.OpenRead(file.FullName);
            var entry = file.Extension.Equals(".pmp", StringComparison.OrdinalIgnoreCase)
                ? zip.GetEntry("meta.json")
                : zip.GetEntry("TTMPL.mpl");
            if (entry != null)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd().TrimStart('﻿');
                // A TexTools list file can be several JSON lines. The first is the pack's header.
                var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
                var json = JObject.Parse(file.Extension.Equals(".pmp", StringComparison.OrdinalIgnoreCase) ? text : first);
                var packName = (string?)json["Name"];
                if (!string.IsNullOrWhiteSpace(packName))
                    name = packName;
            }
        }
        catch
        {
            // not a readable archive; keep the file name
        }

        nameCache[key] = name;
        return name;
    }

    private static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
