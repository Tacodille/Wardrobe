using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Newtonsoft.Json.Linq;
using Penumbra.Api.Enums;
using Wardrobe.Core;
using GlamApi = Glamourer.Api.IpcSubscribers;
using PenApi = Penumbra.Api.IpcSubscribers;

namespace Wardrobe;

public sealed class DesignInfo
{
    public Guid Id;
    public string Name = string.Empty;
    public string Path = string.Empty;
}

/// <summary>Defensive wrappers over the Penumbra and Glamourer IPC. Every call can fail if the other plugin is missing or reloading.</summary>
public sealed class Ipc : IDisposable
{
    public const string Source = "Wardrobe";
    public const string TempModTag = "Wardrobe Material Preview";
    public const int TempModPriority = 900;
    public const int PlayerIndex = 0;

    private readonly PenApi.ApiVersion penVersion;
    private readonly PenApi.GetModDirectory penModDir;
    private readonly PenApi.GetModList penModList;
    private readonly PenApi.GetCollection penCollection;
    private readonly PenApi.GetCurrentModSettings penSettings;
    private readonly PenApi.SetTemporaryModSettingsPlayer penTempSet;
    private readonly PenApi.RemoveTemporaryModSettingsPlayer penTempRemove;
    private readonly PenApi.TrySetMod penTrySetMod;
    private readonly PenApi.TrySetModPriority penTrySetPriority;
    private readonly PenApi.TrySetModSettings penTrySetSettings;
    private readonly PenApi.RedrawObject penRedraw;
    private readonly PenApi.ResolvePlayerPath penResolve;
    private readonly PenApi.AddTemporaryMod penAddTemp;
    private readonly PenApi.RemoveTemporaryMod penRemoveTemp;
    private readonly PenApi.AddMod penAddMod;
    private readonly PenApi.ReloadMod penReloadMod;
    private readonly GlamApi.ApiVersion glamVersion;
    private readonly GlamApi.GetState glamState;
    private readonly GlamApi.GetStateBase64 glamStateB64;
    private readonly GlamApi.ApplyState glamApplyState;
    private readonly GlamApi.SetItem glamSetItem;
    private readonly GlamApi.GetDesignListExtended glamDesigns;
    private readonly GlamApi.ApplyDesign glamApplyDesign;
    private readonly GlamApi.AddDesign glamAddDesign;
    private readonly GlamApi.OpenMainWindow glamOpen;
    private readonly IDisposable penSettingChanged;
    private readonly IDisposable penInitialised;
    private readonly IDisposable glamInitialised;

    /// <summary>Set whenever something we show may be stale.</summary>
    public volatile bool Dirty = true;

    public Ipc(IDalamudPluginInterface pi)
    {
        penVersion = new PenApi.ApiVersion(pi);
        penModDir = new PenApi.GetModDirectory(pi);
        penModList = new PenApi.GetModList(pi);
        penCollection = new PenApi.GetCollection(pi);
        penSettings = new PenApi.GetCurrentModSettings(pi);
        penTempSet = new PenApi.SetTemporaryModSettingsPlayer(pi);
        penTempRemove = new PenApi.RemoveTemporaryModSettingsPlayer(pi);
        penTrySetMod = new PenApi.TrySetMod(pi);
        penTrySetPriority = new PenApi.TrySetModPriority(pi);
        penTrySetSettings = new PenApi.TrySetModSettings(pi);
        penRedraw = new PenApi.RedrawObject(pi);
        penResolve = new PenApi.ResolvePlayerPath(pi);
        penAddTemp = new PenApi.AddTemporaryMod(pi);
        penRemoveTemp = new PenApi.RemoveTemporaryMod(pi);
        penAddMod = new PenApi.AddMod(pi);
        penReloadMod = new PenApi.ReloadMod(pi);
        glamVersion = new GlamApi.ApiVersion(pi);
        glamState = new GlamApi.GetState(pi);
        glamStateB64 = new GlamApi.GetStateBase64(pi);
        glamApplyState = new GlamApi.ApplyState(pi);
        glamSetItem = new GlamApi.SetItem(pi);
        glamDesigns = new GlamApi.GetDesignListExtended(pi);
        glamApplyDesign = new GlamApi.ApplyDesign(pi);
        glamAddDesign = new GlamApi.AddDesign(pi);
        glamOpen = new GlamApi.OpenMainWindow(pi);

        penSettingChanged = PenApi.ModSettingChanged.Subscriber(pi, (change, _, _, _) =>
        {
            // Our own previews raise these too; reacting to them would refresh the whole catalogue on every hover.
            if (change is not (ModSettingChange.TemporarySetting or ModSettingChange.TemporaryMod))
                Dirty = true;
        });
        penInitialised = PenApi.Initialized.Subscriber(pi, () => Dirty = true);
        glamInitialised = GlamApi.Initialized.Subscriber(pi, () => Dirty = true);
    }

    // ---------------------------------------------------------------- status

    public string PenumbraVersion()
    {
        try { var v = penVersion.Invoke(); return $"{v.Breaking}.{v.Features}"; }
        catch (Exception e) { return $"not reachable ({e.GetType().Name})"; }
    }

    public string GlamourerVersion()
    {
        try { var v = glamVersion.Invoke(); return $"{v.Major}.{v.Minor}"; }
        catch (Exception e) { return $"not reachable ({e.GetType().Name})"; }
    }

    public string? ModDirectory()
    {
        try { return penModDir.Invoke(); }
        catch { return null; }
    }

    // ------------------------------------------------------------- penumbra: read

    /// <summary>The collection that applies to the local player.</summary>
    public (Guid Id, string Name)? CurrentCollection()
    {
        try
        {
            var c = penCollection.Invoke(ApiCollectionType.Current);
            return c.HasValue ? (c.Value.Id, c.Value.Name) : null;
        }
        catch { return null; }
    }

    /// <summary>Every mod's directory and display name with its state in the collection.</summary>
    public List<(string Directory, string Name, Selection Sel, bool Inherited)> ReadMods(Guid collection)
    {
        var rows = new List<(string, string, Selection, bool)>();
        try
        {
            foreach (var (dir, name) in penModList.Invoke())
            {
                var sel = new Selection();
                var inherited = false;
                var (ec, settings) = penSettings.Invoke(collection, dir, name, false);
                if (ec == PenumbraApiEc.Success && settings.HasValue)
                {
                    sel.Enabled = settings.Value.Item1;
                    sel.Priority = settings.Value.Item2;
                    sel.Options = settings.Value.Item3;
                    inherited = settings.Value.Item4;
                }

                rows.Add((dir, name, sel, inherited));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Could not read the Penumbra mod list.");
        }

        rows.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>The file Penumbra serves to the player for a game path, or the path itself when unmodded.</summary>
    public string? ResolvePath(string gamePath)
    {
        try { return penResolve.Invoke(gamePath); }
        catch { return null; }
    }

    // ------------------------------------------------------------ penumbra: write

    public bool SetEnabled(Guid collection, string dir, string name, bool enabled)
        => Ok(() => penTrySetMod.Invoke(collection, dir, enabled, name)); // the wrapper names this parameter "inherit"; the IPC sets the enabled state

    public bool SetPriority(Guid collection, string dir, string name, int priority)
        => Ok(() => penTrySetPriority.Invoke(collection, dir, priority, name));

    public bool SetOptions(Guid collection, string dir, string name, string group, IReadOnlyList<string> options)
        => Ok(() => penTrySetSettings.Invoke(collection, dir, group, options, name));

    /// <summary>Applies settings only for the local player, without saving them. Remove with <see cref="RemoveTemporary"/>.</summary>
    public bool SetTemporary(string dir, string name, bool enabled, int priority, Dictionary<string, List<string>> options)
    {
        var map = options.ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value);
        return Ok(() => penTempSet.Invoke(PlayerIndex, dir, false, enabled, priority, map, Source, 0, name));
    }

    public bool RemoveTemporary(string dir, string name)
        => Ok(() => penTempRemove.Invoke(PlayerIndex, dir, 0, name), nothingChangedIsOk: true);

    public void Redraw()
    {
        try { penRedraw.Invoke(PlayerIndex, RedrawType.Redraw); }
        catch (Exception e) { Plugin.Log.Warning(e, "Redraw failed."); }
    }

    public bool AddTemporaryFiles(Guid collection, Dictionary<string, string> gameToFile)
    {
        RemoveTemporaryFiles(collection);
        return Ok(() => penAddTemp.Invoke(TempModTag, collection, gameToFile, string.Empty, TempModPriority));
    }

    public void RemoveTemporaryFiles(Guid collection)
        => Ok(() => penRemoveTemp.Invoke(TempModTag, collection, TempModPriority), nothingChangedIsOk: true);

    /// <summary>Tells Penumbra to load (or reload) a mod folder that was just written.</summary>
    public bool LoadOrReloadMod(string dir, string name)
    {
        var reload = Ok(() => penReloadMod.Invoke(dir, name), nothingChangedIsOk: true);
        return reload || Ok(() => penAddMod.Invoke(dir));
    }

    private static bool Ok(Func<PenumbraApiEc> call, bool nothingChangedIsOk = false, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        try
        {
            var ec = call();
            var good = ec == PenumbraApiEc.Success || (nothingChangedIsOk && ec == PenumbraApiEc.NothingChanged);
            if (!good)
                Plugin.Log.Warning("Penumbra {Caller} returned {Code}", caller, ec);
            return good;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Penumbra IPC call failed.");
            return false;
        }
    }

    // ------------------------------------------------------------- glamourer

    public JObject? PlayerState()
    {
        try
        {
            var (ec, state) = glamState.Invoke(PlayerIndex, 0);
            return ec == GlamourerApiEc.Success ? state : null;
        }
        catch { return null; }
    }

    public string? PlayerStateBase64()
    {
        try
        {
            var (ec, state) = glamStateB64.Invoke(PlayerIndex, 0);
            if (ec != GlamourerApiEc.Success)
                Plugin.Log.Warning("Glamourer GetStateBase64 returned {Code}", ec);
            return ec == GlamourerApiEc.Success ? state : null;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Glamourer GetStateBase64 failed.");
            return null;
        }
    }

    public bool ApplyState(object state, bool equipment = true, bool customization = true, bool once = true)
    {
        var flags = once ? ApplyFlag.Once : 0;
        if (equipment) flags |= ApplyFlag.Equipment;
        if (customization) flags |= ApplyFlag.Customization;
        try
        {
            var ec = state is JObject json
                ? glamApplyState.Invoke(json, PlayerIndex, 0, flags)
                : glamApplyState.Invoke((string)state, PlayerIndex, 0, flags);
            if (ec != GlamourerApiEc.Success)
                Plugin.Log.Warning("Glamourer ApplyState returned {Code}", ec);
            return ec == GlamourerApiEc.Success;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Glamourer ApplyState failed."); return false; }
    }

    /// <summary>Equips an item. With <paramref name="once"/> the change is a preview that automation may replace later.</summary>
    public bool SetItem(Area area, ulong itemId, byte stain, byte stain2, bool once)
    {
        var slot = ToApiSlot(area);
        if (slot == null)
            return false;
        // Dalamud's IPC turns a byte[] into a base64 string, which Glamourer cannot read back as a list. A List<byte> is sent as an array.
        var flags = ApplyFlag.Equipment | (once ? ApplyFlag.Once : 0);
        try
        {
            var ec = glamSetItem.Invoke(PlayerIndex, slot.Value, itemId, new List<byte> { stain, stain2 }, 0, flags);
            if (ec != GlamourerApiEc.Success)
                Plugin.Log.Warning("Glamourer SetItem({Area}, item {Item}) returned {Code}", area, itemId, ec);
            return ec == GlamourerApiEc.Success;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Glamourer SetItem failed."); return false; }
    }

    public List<DesignInfo> Designs()
    {
        try
        {
            return glamDesigns.Invoke()
                .Select(d => new DesignInfo { Id = d.Key, Name = d.Value.Item1, Path = d.Value.Item2 })
                .OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return []; }
    }

    public bool ApplyDesign(Guid id, bool once)
    {
        var flags = ApplyFlag.Equipment | ApplyFlag.Customization | (once ? ApplyFlag.Once : 0);
        try
        {
            var ec = glamApplyDesign.Invoke(id, PlayerIndex, 0, flags);
            if (ec != GlamourerApiEc.Success)
                Plugin.Log.Warning("Glamourer ApplyDesign returned {Code}", ec);
            return ec == GlamourerApiEc.Success;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Glamourer ApplyDesign failed."); return false; }
    }

    public Guid? SaveDesign(string name)
    {
        var state = PlayerStateBase64();
        if (state == null)
            return null;
        try
        {
            var ec = glamAddDesign.Invoke(state, name, out var id);
            return ec == GlamourerApiEc.Success ? id : null;
        }
        catch { return null; }
    }

    public void OpenGlamourer()
    {
        try { glamOpen.Invoke(true); }
        catch { /* not available */ }
    }

    private static ApiEquipSlot? ToApiSlot(Area area) => area switch
    {
        Area.Head => ApiEquipSlot.Head,
        Area.Body => ApiEquipSlot.Body,
        Area.Hands => ApiEquipSlot.Hands,
        Area.Legs => ApiEquipSlot.Legs,
        Area.Feet => ApiEquipSlot.Feet,
        Area.Ears => ApiEquipSlot.Ears,
        Area.Neck => ApiEquipSlot.Neck,
        Area.Wrists => ApiEquipSlot.Wrists,
        Area.RFinger => ApiEquipSlot.RFinger,
        Area.LFinger => ApiEquipSlot.LFinger,
        Area.MainHand => ApiEquipSlot.MainHand,
        Area.OffHand => ApiEquipSlot.OffHand,
        _ => null,
    };

    public void Dispose()
    {
        penSettingChanged.Dispose();
        penInitialised.Dispose();
        glamInitialised.Dispose();
    }
}
