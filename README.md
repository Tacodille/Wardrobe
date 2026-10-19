# Wardrobe

A Dalamud plugin for FFXIV that puts Penumbra mods and Glamourer looks in one window.

Pick a slot, see every mod that fits it, hover to preview on your own character, click to pin, and apply. It works through Penumbra's and Glamourer's own IPC, so it does not replace either of them.

**This is early test software.** It has been used on one setup. Expect rough edges and please report what breaks.

## What it does

- **Wardrobe tab.** Slots on the left, with what you are wearing. The middle list shows every mod that touches the slot (head to rings, weapons, hair, face, eyes, tail, skin and tattoos), plus vanilla items. Hover to preview, click to pin, click again to take it back off.
- **Options.** Each mod's option groups are shown and can be changed before you apply. Mods with one option per variant (tail shapes, for example) preview each variant separately.
- **Priority.** Previews run at a high temporary priority so they show over other mods. The panel shows which mods beat a mod on its files, and **Make this win** raises its priority just enough.
- **Apply and undo.** Apply turns the mod on in your collection and equips the item. It can also switch off other mods that replace the same model or material files, with a tick box per mod and an always-keep-on list. **Undo last apply** reverses it.
- **Designs tab.** Your Glamourer designs. Hover to preview, apply, or save the current look as a new design.
- **Materials tab.** Edit shader colours and emission for the materials your mods actually use (skin glow, iris colour, and so on), with a live preview. Saving writes a copy into a separate mod called `Wardrobe Overrides`. It never edits the original mod.
- **Diagnostics tab.** What Glamourer says you are wearing, which mod Penumbra serves each model from, and every file that more than one enabled mod wants.

Mod preview images come from the mod folder. For mods that ship none, drop a PNG named after the mod's folder into `%USERPROFILE%\Wardrobe\previews`.

## Requirements

- XIVLauncher with Dalamud (API level 15 at the time of writing)
- Penumbra and Glamourer installed and working
- A Windows PC

## Install

**From the plugin list (recommended):**

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this address, click **+**, then **Save changes and close**:

   `https://raw.githubusercontent.com/Tacodille/Wardrobe/main/repo.json`

3. Type `/xlplugins`, search for **Wardrobe** under **All Plugins**, and install it.
4. Type `/wardrobe` to open the window.

Updates show up in the plugin list like any other plugin. Custom repositories are not reviewed by the Dalamud team, so Dalamud shows its usual third-party warning.

**Or as a dev plugin:** download `latest.zip` from the [latest release](../../releases/latest), unzip it somewhere permanent, add the full path to `Wardrobe.dll` under **Dev Plugin Locations** in the same Experimental tab, then enable it under `/xlplugins`, **Dev Tools**, **Installed Dev Plugins**.

## How it behaves

- Hovering and pinning only use Penumbra temporary settings and Glamourer's "once" flag. Closing the window, pressing **Revert preview**, or unloading the plugin removes them.
- Clicking a mod that is switched off turns it on in your collection (this can be turned off in the Status tab, and Undo reverses it).
- **Apply** changes your Penumbra collection: it can enable mods, change options and priorities, and switch other mods off. Check the yellow list above the Apply button first.
- The plugin writes to disk in two places only: its own config, and the `Wardrobe Overrides` mod when you save a material.

## Known limits

- Glasses and other bonus items are not supported yet.
- The character select screen cannot be changed. Neither Penumbra nor Glamourer can touch it.
- Hair, face and tail previews set the style number from the model path. If your race cannot use that style, the top bar says so and only the mod's files change.
- Saved material overrides are written in Penumbra's current mod format. If Penumbra does not load the new mod, the file is still in your mod folder.

## Build from source

You need the .NET 10 SDK and a Dalamud dev install (the one XIVLauncher creates).

```
dotnet build -c Release
```

The output is `bin/Release/Wardrobe.dll`. `python tools/release.py` builds it, packages `latest.zip` and refreshes `repo.json`; add `--publish` to push and attach it to a GitHub release.

## A note on the game's rules

Plugins and mods are not allowed under the FFXIV terms of service, and that includes Dalamud, Penumbra and Glamourer. Use this at your own risk.

## Licence and credits

MIT, see `LICENSE`. Penumbra and Glamourer are by Ottermandias and contributors. This plugin talks to them through the published `Penumbra.Api` (MIT) and `Glamourer.Api` packages and is not affiliated with either project.
