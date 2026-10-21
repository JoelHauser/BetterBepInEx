# F12 Overhaul

A replacement for SPT's F12 mod settings menu. Same hotkey, same settings, a much better window.

> **Early version, not yet tested in game.** 0.2.0 is the first build with the current look.
> If something looks wrong, turn off **Use the new menu** on F12 Overhaul's page to get the
> stock menu back instantly.

## What you get

- **Sidebar of mods** instead of one endless list. Favorites pin to the top, a filter box finds a
  mod by name, and code names like `Tyfon.UIFixes` read as "UI Fixes".
- **Search every setting** across all mods: names, sections and descriptions.
- **Filters**: Changed, Toggles, Numbers, Lists, Keybinds, Text, with counts.
- **See what you changed**: changed settings are marked, counted per mod, and listed together
  on one page. Reset one, a whole mod, or everything.
- **Undo and redo** with Ctrl+Z / Ctrl+Y, including whole profile loads.
- **Keybinds page** listing every mod's keys, with keys that several mods share at the top.
  Binding Ctrl+H works whichever key you let go of first.
- **Profiles**: save all your settings under a name and switch between them. Copy one mod's
  settings, or all of them, as text to share.
- **Readable at any resolution**: sharp text that scales with your screen, a resizable window,
  descriptions shown under each setting.
- **Holds you still in raid** while the menu is open, like the inventory does.
- **Opens instantly**: settings are read once while the game loads, not on every F12 press.

Mods that draw their own custom settings controls keep working. Those controls are drawn by
the stock menu's code inside this window.

## Install

Unzip into your SPT folder, so the DLL lands in `BepInEx\plugins`. Client only; nothing touches
your profile or the server.

If you use acidphantasm's Configuration Manager add-on (window scale and resize), you can remove
it. This replaces the window it changes.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder>
```

Builds against your install's BepInEx, the stock ConfigurationManager and Unity modules, runs the
tests, and writes the release zip to `releases\`.
