<!--
  The mod-page description. Paste everything below this comment into the listing.

  This is not the README: the README is for the repository, this is for someone deciding
  whether to download. When a release changes what a player sees, update both.

  The Forge shows the word "Tarkov" as "T*****", so say "EFT".

  Images are served from the repository (docs/images), so they show once pushed to main.

  Short-description field:
    "A better F12 menu: every mod in a sidebar, search across all settings, see what you changed, undo, and screenshot your config."
-->

# BetterBepInEx

A better F12 menu. Same key, same settings, in a window that's actually nice to use.

![The BetterBepInEx menu](https://raw.githubusercontent.com/JoelHauser/BetterBepInEx/main/docs/images/menu.png)

## What you get

- **Every mod in a sidebar.** Favorites at the top, a filter box, and sorting by name, author,
  most changed, most settings or recently opened.
- **Search everything.** Find any setting across all your mods as you type, or search inside the
  mod you're on.
- **See what you changed.** Changed settings are marked with a bar, and the Changed page lists
  every one across all mods. Reset one setting, a whole mod, or everything.
- **Undo and redo** with Ctrl+Z and Ctrl+Y.
- **Keybinds page.** Every mod's keys in one list. Keys that more than one mod uses are shown
  first, so you can spot clashes.
- **Screenshot your settings.** Someone asks for your config? Open the mod, click **Screenshot**,
  and paste it into Discord with Ctrl+V. Your changes are marked, with their defaults.
- **Show advanced** reveals the settings mods hide or mark as advanced.
- **Keep playing with it open.** Click outside the menu to fade it and get your mouse and keys back.
  Click it to come back. While it's in front during a raid, it holds you still, like the inventory.
- **Make it yours.** Five themes (Dark, Black for OLED, Grey, High contrast, Tan), any accent
  color, text size and compact rows.
- **Smooth sliders and scrolling**, a resizable window, and sizing that fits 1080p, 1440p,
  ultrawide and 4K.

![Keybinds page](https://raw.githubusercontent.com/JoelHauser/BetterBepInEx/main/docs/images/keybinds.png)

### Screenshot example

One click on a mod's page makes this, ready to paste:

![A settings screenshot made with the Screenshot button](https://raw.githubusercontent.com/JoelHauser/BetterBepInEx/main/docs/images/settings-screenshot.png)

## Install

Unzip into your SPT folder, so `BetterBepInEx.dll` lands in `BepInEx\plugins`. Press F12 as
usual. Client only, no server part.

For SPT 4.1.x.

## Good to know

- Mods with their own custom settings controls keep working.
- Want the old menu back? Turn off **Use the new menu** on BetterBepInEx's page. It switches
  straight away.
- Screenshots are also saved in `BepInEx\BetterBepInEx\Screenshots`.
- Mods that only restyle the stock F12 window have nothing to restyle while this menu is on.

## Uninstall

Delete `BepInEx\plugins\BetterBepInEx.dll`. The stock F12 menu comes back.
