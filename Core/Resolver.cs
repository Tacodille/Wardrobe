using System;
using System.Collections.Generic;
using System.Linq;

namespace Wardrobe.Core;

/// <summary>One mod's claim on a game path in its current state.</summary>
public sealed class Claim
{
    public ModDef Mod = null!;
    public int ModPriority;
    /// <summary>Where inside the mod this claim comes from, e.g. "BASE:-" or "default".</summary>
    public string Label = string.Empty;
    /// <summary>(group priority, option priority). Higher wins inside one mod. The default section ranks last.</summary>
    public (int Group, int Option) Inner;
    public string? File;
    public string? SwapTarget;

    public bool IsImc;
}

public sealed class PathOutcome
{
    public string GamePath = string.Empty;
    public PathInfo Info;
    public List<Claim> Claims = new();
    public Claim Winner = null!;
    /// <summary>True when the best two mods have the same priority, so Penumbra's choice depends on load order.</summary>
    public bool Tie;
}

public static class Resolver
{
    /// <summary>What one mod contributes right now, as (label, inner priority, container) triples.</summary>
    public static IEnumerable<(string Label, (int, int) Inner, ContainerDef Container, GroupDef? Group, OptionDef? Option)> ActiveSources(ModDef mod, Selection sel)
    {
        yield return ("default", (int.MinValue, int.MinValue), mod.Default, null, null);

        foreach (var g in mod.Groups)
        {
            sel.Options.TryGetValue(g.Name, out var picked);
            picked ??= DefaultNames(g);

            switch (g.Type)
            {
                case "Single":
                case "Multi":
                    foreach (var o in g.Options.Where(o => picked.Contains(o.Name)))
                        yield return ($"{g.Name}:{o.Name}", (g.Priority, o.Priority), o.Container, g, o);
                    break;
                case "Imc":
                    {
                        var on = g.Options.Where(o => picked.Contains(o.Name)).ToList();
                        if (on.Count > 0 && g.Imc != null)
                        {
                            var shell = new ContainerDef();
                            shell.Imc.Add(g.Imc);
                            yield return ($"{g.Name}:{string.Join("+", on.Select(o => o.Name))}", (g.Priority, 0), shell, g, on[0]);
                        }

                        break;
                    }
                case "Combining":
                    {
                        var index = 0;
                        for (var i = 0; i < g.Options.Count; i++)
                            if (picked.Contains(g.Options[i].Name))
                                index |= 1 << i;
                        if (index < g.Containers.Count)
                            yield return ($"{g.Name}:combo{index}", (g.Priority, 0), g.Containers[index], g, null);
                        break;
                    }
            }
        }
    }

    public static List<string> DefaultNames(GroupDef g)
    {
        var names = new List<string>();
        switch (g.Type)
        {
            case "Single":
                if (g.DefaultSettings < (ulong)g.Options.Count)
                    names.Add(g.Options[(int)g.DefaultSettings].Name);
                break;
            default:
                for (var i = 0; i < g.Options.Count && i < 64; i++)
                    if ((g.DefaultSettings >> i & 1) != 0)
                        names.Add(g.Options[i].Name);
                break;
        }

        return names;
    }

    /// <summary>
    /// Builds every game path claimed by the enabled mods, with Penumbra's rules: one file wins per path, a strictly higher
    /// priority wins, and within a mod higher group and option priority win.
    /// </summary>
    public static Dictionary<string, PathOutcome> Build(IEnumerable<(ModDef Mod, Selection Sel)> mods)
    {
        var result = new Dictionary<string, PathOutcome>();
        foreach (var (mod, sel) in mods)
        {
            if (!sel.Enabled)
                continue;

            var best = new Dictionary<string, Claim>();
            foreach (var (label, inner, container, _, _) in ActiveSources(mod, sel))
            {
                foreach (var (path, file) in container.Files)
                    Offer(best, mod, sel, label, inner, path, file, null, false);
                foreach (var (path, target) in container.Swaps)
                    Offer(best, mod, sel, label, inner, path, null, target, false);
                foreach (var imc in container.Imc)
                    Offer(best, mod, sel, label, inner, ImcKey(imc), null, null, true);
            }

            foreach (var (path, claim) in best)
            {
                if (!result.TryGetValue(path, out var outcome))
                    result[path] = outcome = new PathOutcome { GamePath = path, Info = PathInfo.Classify(path) };
                outcome.Claims.Add(claim);
            }
        }

        foreach (var o in result.Values)
        {
            o.Claims.Sort((a, b) => b.ModPriority.CompareTo(a.ModPriority));
            o.Winner = o.Claims[0];
            o.Tie = o.Claims.Count > 1 && o.Claims[1].ModPriority == o.Claims[0].ModPriority;
        }

        return result;
    }

    public static string ImcKey(ImcId i) => $"imc:{i.ObjectType}:{i.PrimaryId}:{i.SecondaryId}:v{i.Variant}:{i.EquipSlot}:{i.BodySlot}".ToLowerInvariant();

    private static void Offer(Dictionary<string, Claim> best, ModDef mod, Selection sel, string label, (int, int) inner, string path, string? file, string? swap, bool imc)
    {
        if (best.TryGetValue(path, out var existing) && existing.Inner.CompareTo(inner) >= 0)
            return;
        best[path] = new Claim
        {
            Mod = mod,
            ModPriority = sel.Priority,
            Label = label,
            Inner = inner,
            File = file,
            SwapTarget = swap,
            IsImc = imc,
        };
    }

    private static int CompareTo(this (int Group, int Option) a, (int, int) b)
    {
        var c = a.Group.CompareTo(b.Item1);
        return c != 0 ? c : a.Option.CompareTo(b.Item2);
    }
}
