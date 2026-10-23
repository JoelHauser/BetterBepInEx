# BetterBepInEx

A replacement for SPT's F12 mod settings menu. Same hotkey, same settings, a much better window.

## What you get

- **Sidebar of mods** with favorites and a filter box.
- **Search every setting** across all mods, or just the mod you're on.
- **See what you changed**: changed settings are marked and listed on one page. Reset one, a
  whole mod, or everything.
- **Undo and redo** with Ctrl+Z / Ctrl+Y.
- **Keybinds page** listing every mod's keys, with keys that several mods share at the top.
- **Smooth sliders and scrolling**, a resizable window, and text that scales with your screen.
- **Show advanced** in the title bar for hidden and advanced settings.
- **Holds you still in raid** while the menu is open.

Mods with their own custom settings controls keep working.

## Install

Unzip into your SPT folder, so `BetterBepInEx.dll` lands in `BepInEx\plugins`. Client only.

To get the stock menu back, turn off **Use the new menu** on BetterBepInEx's page.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder>
```
