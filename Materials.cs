using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Wardrobe.Core;

namespace Wardrobe;

/// <summary>A material being edited: where it came from, the parsed file, and the unedited bytes for reset.</summary>
public sealed class MaterialEdit
{
    public string GamePath = string.Empty;
    public string SourceMod = string.Empty;
    public string SourceFile = string.Empty;
    public MtrlFile File = null!;
    public byte[] Original = [];
    public bool Changed;
}

/// <summary>
/// Edits shader constants (colours, emission) in a mod's material without touching the mod. A preview is a Penumbra temporary mod
/// pointing the game path at an edited copy. Saving writes the copy into a separate "Wardrobe Overrides" mod.
/// </summary>
public sealed class MaterialService
{
    public const string OverrideModDir = "Wardrobe Overrides";

    private readonly Ipc ipc;
    private readonly Catalog catalog;
    private readonly string dataDir;
    private readonly string overridesIndex;
    private readonly Dictionary<string, string> previewing = new();
    private Dictionary<string, string> saved = new();

    public MaterialService(Ipc ipc, Catalog catalog, string dataDir)
    {
        this.ipc = ipc;
        this.catalog = catalog;
        this.dataDir = dataDir;
        overridesIndex = Path.Combine(dataDir, "overrides.json");
        Directory.CreateDirectory(Path.Combine(dataDir, "preview"));
        try
        {
            if (File.Exists(overridesIndex))
                saved = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(overridesIndex)) ?? new();
        }
        catch { saved = new(); }
    }

    public IReadOnlyDictionary<string, string> Saved => saved;
    public bool IsPreviewing => previewing.Count > 0;

    /// <summary>Opens the material that currently wins a game path, if it is a loose file inside a mod.</summary>
    public (MaterialEdit? Edit, string Error) Open(PathOutcome outcome)
    {
        var w = outcome.Winner;
        if (w.File == null)
            return (null, w.SwapTarget != null ? "The winning mod swaps this path to another game file, so there is no file to edit." : "No file to edit.");
        var full = Path.Combine(w.Mod.Folder, w.File.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            return (null, $"The mod's file is missing: {full}");

        var bytes = File.ReadAllBytes(full);
        var parsed = MtrlFile.Parse(bytes);
        if (parsed == null)
            return (null, "That material file is in a format this editor can't read.");
        return (new MaterialEdit
        {
            GamePath = outcome.GamePath,
            SourceMod = w.Mod.Name,
            SourceFile = full,
            File = parsed,
            Original = bytes,
        }, string.Empty);
    }

    public void Reset(MaterialEdit edit)
    {
        var fresh = MtrlFile.Parse(edit.Original);
        if (fresh != null)
            edit.File = fresh;
        edit.Changed = false;
    }

    /// <summary>Shows the edited material on your character without saving anything.</summary>
    public bool Preview(MaterialEdit edit)
    {
        if (catalog.Collection == null)
            return false;
        var temp = Path.Combine(dataDir, "preview", Hash(edit.GamePath) + ".mtrl");
        File.WriteAllBytes(temp, edit.File.ToBytes());
        previewing[edit.GamePath] = temp;
        var ok = ipc.AddTemporaryFiles(catalog.Collection.Value.Id, new Dictionary<string, string>(previewing));
        ipc.Redraw();
        return ok;
    }

    public void ClearPreview()
    {
        if (previewing.Count == 0 || catalog.Collection == null)
            return;
        ipc.RemoveTemporaryFiles(catalog.Collection.Value.Id);
        previewing.Clear();
        ipc.Redraw();
    }

    /// <summary>Writes the edited material into the Wardrobe Overrides mod, enables it and lifts it above everything else.</summary>
    public string Save(MaterialEdit edit)
    {
        if (catalog.ModRoot == null || catalog.Collection == null)
            return "Penumbra's mod folder isn't known yet.";

        try
        {
            var modFolder = Path.Combine(catalog.ModRoot, OverrideModDir);
            var filesFolder = Path.Combine(modFolder, "files");
            Directory.CreateDirectory(filesFolder);

            var fileName = $"{Hash(edit.GamePath)}_{Path.GetFileName(edit.GamePath)}";
            File.WriteAllBytes(Path.Combine(filesFolder, fileName), edit.File.ToBytes());
            saved[edit.GamePath] = "files\\" + fileName;
            WriteOverrideMeta(modFolder);
            File.WriteAllText(overridesIndex, JsonConvert.SerializeObject(saved, Formatting.Indented));

            var loaded = ipc.LoadOrReloadMod(OverrideModDir, OverrideModDir);
            var id = catalog.Collection.Value.Id;
            var enabled = ipc.SetEnabled(id, OverrideModDir, OverrideModDir, true);
            var prio = ipc.SetPriority(id, OverrideModDir, OverrideModDir, 1000);

            ClearPreview();
            ipc.Redraw();
            ipc.Dirty = true;
            return loaded && enabled && prio
                ? $"Saved to the '{OverrideModDir}' mod (priority 1000)."
                : $"Wrote the file to '{OverrideModDir}', but Penumbra did not confirm loading it (loaded {loaded}, enabled {enabled}, priority {prio}). Check Penumbra's mod list.";
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Saving the material override failed.");
            return "Saving failed: " + e.Message;
        }
    }

    /// <summary>Removes one saved override from the Wardrobe Overrides mod.</summary>
    public string Remove(string gamePath)
    {
        if (catalog.ModRoot == null || !saved.TryGetValue(gamePath, out var rel))
            return "Nothing to remove.";
        try
        {
            var modFolder = Path.Combine(catalog.ModRoot, OverrideModDir);
            var file = Path.Combine(modFolder, rel.Replace('\\', Path.DirectorySeparatorChar));
            saved.Remove(gamePath);
            if (File.Exists(file))
                File.Delete(file);
            WriteOverrideMeta(modFolder);
            File.WriteAllText(overridesIndex, JsonConvert.SerializeObject(saved, Formatting.Indented));
            ipc.LoadOrReloadMod(OverrideModDir, OverrideModDir);
            ipc.Redraw();
            ipc.Dirty = true;
            return "Removed.";
        }
        catch (Exception e)
        {
            return "Removing failed: " + e.Message;
        }
    }

    private void WriteOverrideMeta(string modFolder)
    {
        var files = new JObject();
        foreach (var (path, rel) in saved)
            files[path] = rel;
        var meta = new JObject
        {
            ["FileVersion"] = 4,
            ["Identifier"] = Guid.NewGuid().ToString(),
            ["Name"] = OverrideModDir,
            ["Author"] = "Wardrobe",
            ["Description"] = "Material colour and emission edits saved by the Wardrobe plugin. Safe to delete if you no longer want them.",
            ["Version"] = "1.0.0",
            ["DefaultData"] = new JObject
            {
                ["Files"] = files,
                ["FileSwaps"] = new JObject(),
                ["Manipulations"] = new JArray(),
            },
        };
        File.WriteAllText(Path.Combine(modFolder, "meta.json"), meta.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..10].ToLowerInvariant();
}
