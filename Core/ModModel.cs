using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Wardrobe.Core;

/// <summary>An Imc manipulation identifier, enough to say which vanilla item's slot a mod touches.</summary>
public sealed record ImcId(string ObjectType, int PrimaryId, int SecondaryId, int Variant, string EquipSlot, string BodySlot);

/// <summary>Files, file swaps and Imc identifiers that switch on together (a default section, an option, or a combining container).</summary>
public sealed class ContainerDef
{
    /// <summary>Game path (lower case, forward slashes) to relative file path in the mod folder.</summary>
    public Dictionary<string, string> Files { get; } = new();
    /// <summary>Game path to game path.</summary>
    public Dictionary<string, string> Swaps { get; } = new();
    public List<ImcId> Imc { get; } = new();
    public int OtherManipulations;

    public bool IsEmpty => Files.Count == 0 && Swaps.Count == 0 && Imc.Count == 0 && OtherManipulations == 0;
}

public sealed class OptionDef
{
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Priority;
    public int AttributeMask;
    public ContainerDef Container = new();
}

public sealed class GroupDef
{
    public string Name = string.Empty;
    public string Type = string.Empty;
    public string Description = string.Empty;
    public int Priority;
    public ulong DefaultSettings;
    public List<OptionDef> Options = new();
    /// <summary>Combining groups only: one container per combination (bit i set means option i is on).</summary>
    public List<ContainerDef> Containers = new();
    public ImcId? Imc;
}

public sealed class ModDef
{
    public string Directory = string.Empty;
    public string Name = string.Empty;
    public string? Author;
    public string? Version;
    public string Folder = string.Empty;
    public ContainerDef Default = new();
    public List<GroupDef> Groups = new();
    public string? PreviewImage;
}

/// <summary>A mod's state in one collection: what Penumbra's IPC reports.</summary>
public sealed class Selection
{
    public bool Enabled;
    public int Priority;
    /// <summary>Group name to the names of the selected options.</summary>
    public Dictionary<string, List<string>> Options { get; set; } = new();
}

public static class ModLoader
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Reads a mod folder (Penumbra meta format 4, with a fallback for the older split files).</summary>
    public static ModDef? Load(string modRoot, string directory)
    {
        var folder = Path.Combine(modRoot, directory);
        var metaPath = Path.Combine(folder, "meta.json");
        if (!File.Exists(metaPath))
            return null;

        try
        {
            var meta = JObject.Parse(File.ReadAllText(metaPath));
            var mod = new ModDef
            {
                Directory = directory,
                Folder = folder,
                Name = (string?)meta["Name"] ?? directory,
                Author = (string?)meta["Author"],
                Version = (string?)meta["Version"],
            };

            if (meta["DefaultData"] is JObject dd)
                ReadContainer(dd, mod.Default);
            else if (File.Exists(Path.Combine(folder, "default_mod.json")))
                ReadContainer(JObject.Parse(File.ReadAllText(Path.Combine(folder, "default_mod.json"))), mod.Default);

            if (meta["Groups"] is JArray groups)
            {
                foreach (var g in groups.OfType<JObject>())
                    mod.Groups.Add(ReadGroup(g));
            }
            else
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(folder, "group_*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    mod.Groups.Add(ReadGroup(JObject.Parse(File.ReadAllText(file))));
            }

            mod.PreviewImage = FindPreview(folder, (string?)meta["Image"]);
            return mod;
        }
        catch
        {
            return null;
        }
    }

    private static GroupDef ReadGroup(JObject g)
    {
        var group = new GroupDef
        {
            Name = (string?)g["Name"] ?? "?",
            Type = (string?)g["Type"] ?? "?",
            Description = (string?)g["Description"] ?? string.Empty,
            Priority = (int?)g["Priority"] ?? 0,
            DefaultSettings = (ulong?)g["DefaultSettings"] ?? 0,
        };

        if (g["Identifier"] is JObject id)
            group.Imc = ReadImcId(id);

        if (g["Options"] is JArray options)
        {
            foreach (var o in options.OfType<JObject>())
            {
                var opt = new OptionDef
                {
                    Name = (string?)o["Name"] ?? "?",
                    Description = (string?)o["Description"] ?? string.Empty,
                    Priority = (int?)o["Priority"] ?? 0,
                    AttributeMask = (int?)o["AttributeMask"] ?? 0,
                };
                ReadContainer(o, opt.Container);
                group.Options.Add(opt);
            }
        }

        if (g["Containers"] is JArray containers)
        {
            foreach (var c in containers.OfType<JObject>())
            {
                var def = new ContainerDef();
                ReadContainer(c, def);
                group.Containers.Add(def);
            }
        }

        return group;
    }

    private static void ReadContainer(JObject c, ContainerDef into)
    {
        if (c["Files"] is JObject files)
            foreach (var p in files.Properties())
                into.Files[PathInfo.Normalise(p.Name)] = (string?)p.Value ?? string.Empty;

        if (c["FileSwaps"] is JObject swaps)
            foreach (var p in swaps.Properties())
                into.Swaps[PathInfo.Normalise(p.Name)] = PathInfo.Normalise((string?)p.Value ?? string.Empty);

        if (c["Manipulations"] is JArray manips)
        {
            foreach (var m in manips.OfType<JObject>())
            {
                if ((string?)m["Type"] == "Imc" && m["Manipulation"] is JObject body)
                    into.Imc.Add(ReadImcId(body));
                else
                    into.OtherManipulations++;
            }
        }
    }

    private static ImcId ReadImcId(JObject o) => new(
        (string?)o["ObjectType"] ?? string.Empty,
        (int?)o["PrimaryId"] ?? 0,
        (int?)o["SecondaryId"] ?? 0,
        (int?)o["Variant"] ?? 0,
        (string?)o["EquipSlot"] ?? string.Empty,
        (string?)o["BodySlot"] ?? string.Empty);

    private static string? FindPreview(string folder, string? declared)
    {
        if (!string.IsNullOrWhiteSpace(declared))
        {
            var p = Path.Combine(folder, declared.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(p))
                return p;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(folder))
            if (Array.IndexOf(ImageExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                return file;

        return null;
    }
}
