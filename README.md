# BetterBepInEx

A replacement for SPT's F12 mod settings menu. Same hotkey, same settings, a much better window.

> **Early version, not yet tested in game.** If something looks wrong, turn off **Use the new
> menu** on BetterBepInEx's page to get the stock menu back instantly.

## What you get

- **Sidebar of mods** instead of one endless list. Favorites pin to the top, a filter box finds a
  mod by name, and code names like `Tyfon.UIFixes` read as "UI Fixes".
- **Search every setting** across all mods: names, sections and descriptions.
- **Filters**: Changed, Toggles, Numbers, Lists, Keybinds, Text, with counts.
- **See what you changed**: changed settings are marked, counted per mod, and listed together
  on one page. Reset one, a whole mod, or everything.
- **Undo and redo** with Ctrl+Z / Ctrl+Y.
- **Keybinds page** listing every mod's keys, with keys that several mods share at the top.
  Binding Ctrl+H works whichever key you let go of first.
- **Copy and paste** a mod's settings as text, to share them or move them to another install.
- **Sized for your screen**: sharp text that scales with your resolution (1080p, 1440p,
  ultrawide, 4K), a resizable window that keeps its shape when the resolution changes, and
  descriptions shown under each setting.
- **Holds you still in raid** while the menu is open, like the inventory does.
- **Opens instantly**: settings are read once while the game loads, not on every F12 press.

Mods that draw their own custom settings controls keep working. Those controls are drawn by
the stock menu's code inside this window.

## Install

Unzip into your SPT folder, so `BetterBepInEx.dll` lands in `BepInEx\plugins`. Client only;
nothing touches your profile or the server.

If you use acidphantasm's Configuration Manager add-on (window scale and resize), you can remove
it. This replaces the window it changes.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder>
```

Builds against your install's BepInEx, the stock ConfigurationManager and Unity modules, runs the
tests, and writes the release zip to `releases\`.
