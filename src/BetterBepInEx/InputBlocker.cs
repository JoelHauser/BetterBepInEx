using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>
    /// Keeps the raid player still while the menu is open. The stock menu only calls
    /// Input.ResetInputAxes from OnGUI, which runs after the game has already read the frame's
    /// input, so the camera turns and the player walks while you drag a slider.
    ///
    /// EFT has a flag for exactly this: <c>GamePlayerOwner.SetIgnoreInput(bool)</c>, which every
    /// in-raid screen (inventory, map, menu) sets when it opens. While it is set, the owner's
    /// TranslateAxes and command handling return on their first line. InventoryWalker's notes have
    /// the IL reading. The flag is a private static bool, so its current value is read through the
    /// one field the public setter stores to, and put back as it was when the menu closes.
    ///
    /// Outside a raid the flag is read by nothing, so setting it there is harmless.
    /// </summary>
    internal sealed class InputBlocker
    {
        private const string GamePlayerOwnerName = "EFT.GamePlayerOwner";
        private const string SetIgnoreInputName = "SetIgnoreInput";

        private const string PlayerOwnerName = "EFT.PlayerOwner";
        private const string IgnoredCommandsName = "ignoredCommands";

        // What the mouse buttons start: fire, aim, and the two grenade throws. Only the starts:
        // the matching ends (EndShooting...) always go through, so a held trigger can't stick.
        private static readonly string[] ClickCommandNames = { "ToggleShooting", "ToggleAlternativeShooting", "TryHighThrow", "TryLowThrow" };

        private Action<bool> _set;
        private FieldInfo _flag;
        private bool _blocking;
        private bool _previous;

        private IList _clickCommands;
        private MethodInfo _ignoredAdd;
        private MethodInfo _ignoredRemove;
        private MethodInfo _ignoredContains;
        private object _ignored;
        private readonly List<object> _clicksAdded = new List<object>();
        private bool _holdingClicks;

        private static object _escapeCommand;
        private static float _swallowEscapeUntil;

        public bool Available => _set != null;

        /// <summary>
        /// Esc closes the menu, and the game must not see that press too: in raid it opens the
        /// exit menu, in the main menu it steps back a screen. EFT reads keys in its Update, before
        /// the menu's OnGUI, and delivers them up to a physics step later, so the press is dropped
        /// in InputManager.DispatchInput while the menu is up and for a moment after it closes.
        /// </summary>
        public static void SwallowEscape(float seconds)
        {
            _swallowEscapeUntil = Mathf.Max(_swallowEscapeUntil, Time.realtimeSinceStartup + seconds);
        }

        public static void PatchEscape(Harmony harmony, ManualLogSource log)
        {
            Type manager = AccessTools.TypeByName("EFT.InputSystem.InputManager");
            Type command = AccessTools.TypeByName("EFT.InputSystem.ECommand");
            MethodInfo dispatch = manager == null ? null : AccessTools.Method(manager, "DispatchInput");
            if (dispatch == null || command == null || !Enum.IsDefined(command, "Escape"))
            {
                log.LogWarning("Could not find EFT.InputSystem.InputManager.DispatchInput; Esc closes the menu but the game sees it too.");
                return;
            }
            _escapeCommand = Enum.Parse(command, "Escape");
            try
            {
                harmony.Patch(dispatch, prefix: new HarmonyMethod(typeof(InputBlocker), nameof(DropEscape)));
            }
            catch (Exception e)
            {
                log.LogWarning("Could not patch InputManager.DispatchInput; Esc closes the menu but the game sees it too: " + e.Message);
            }
        }

        private static void DropEscape(object __0)
        {
            if (Time.realtimeSinceStartup < _swallowEscapeUntil && __0 is IList commands && commands.Count > 0)
            {
                while (commands.Contains(_escapeCommand))
                {
                    commands.Remove(_escapeCommand);
                }
            }
        }

        public bool Resolve(ManualLogSource log)
        {
            Type owner = AccessTools.TypeByName(GamePlayerOwnerName);
            MethodInfo setter = owner == null ? null : AccessTools.Method(owner, SetIgnoreInputName, new[] { typeof(bool) });
            if (setter == null || !setter.IsStatic)
            {
                log.LogWarning("Could not find " + GamePlayerOwnerName + "." + SetIgnoreInputName
                               + "(bool); the menu will not hold the raid player still.");
                return false;
            }

            FieldInfo found = null;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(setter))
            {
                if (instruction.opcode != OpCodes.Stsfld)
                {
                    continue;
                }
                if (found != null || !(instruction.operand is FieldInfo field) || field.FieldType != typeof(bool))
                {
                    found = null;
                    break;
                }
                found = field;
            }
            if (found == null)
            {
                log.LogWarning(GamePlayerOwnerName + "." + SetIgnoreInputName
                               + " no longer stores to exactly one static bool; the menu will not hold the raid player still.");
                return false;
            }

            _flag = found;
            _set = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), setter);
            ResolveClickCommands(log);
            return true;
        }

        /// <summary>
        /// PlayerOwner.TranslateCommand drops any command in its static <c>ignoredCommands</c> set
        /// before the hands see it, which is how the faded menu keeps a click on it from firing.
        /// </summary>
        private void ResolveClickCommands(ManualLogSource log)
        {
            Type owner = AccessTools.TypeByName(PlayerOwnerName);
            Type command = AccessTools.TypeByName("EFT.InputSystem.ECommand");
            FieldInfo field = owner == null ? null : AccessTools.Field(owner, IgnoredCommandsName);
            object set = field == null || !field.IsStatic ? null : field.GetValue(null);
            if (set == null || command == null)
            {
                log.LogWarning("Could not find " + PlayerOwnerName + "." + IgnoredCommandsName
                               + "; a click on the faded menu may also fire the weapon.");
                return;
            }
            IList commands = new List<object>();
            foreach (string name in ClickCommandNames)
            {
                if (Enum.IsDefined(command, name))
                {
                    commands.Add(Enum.Parse(command, name));
                }
            }
            Type setType = set.GetType();
            _ignoredAdd = AccessTools.Method(setType, "Add", new[] { command });
            _ignoredRemove = AccessTools.Method(setType, "Remove", new[] { command });
            _ignoredContains = AccessTools.Method(setType, "Contains", new[] { command });
            if (_ignoredAdd == null || _ignoredRemove == null || _ignoredContains == null || commands.Count == 0)
            {
                log.LogWarning(PlayerOwnerName + "." + IgnoredCommandsName
                               + " is not a set of commands; a click on the faded menu may also fire the weapon.");
                return;
            }
            _ignored = set;
            _clickCommands = commands;
        }

        /// <summary>
        /// Called every frame while the menu is open. Re-asserts the flag, because a screen change
        /// underneath (the raid ending, say) can clear it.
        /// </summary>
        public void Hold()
        {
            if (_set == null)
            {
                return;
            }
            if (!_blocking)
            {
                _previous = (bool)_flag.GetValue(null);
                _blocking = true;
            }
            _set(true);
        }

        public void Release()
        {
            if (_set == null || !_blocking)
            {
                return;
            }
            _blocking = false;
            _set(_previous);
        }

        /// <summary>
        /// While the pointer is over the faded menu: the game reads the mouse button before the
        /// menu sees the click, so without this the click that wakes the menu also fires. Only the
        /// mouse commands are dropped; looking, moving and keys carry on. Commands someone else
        /// already put in the set are left there on release.
        /// </summary>
        public void HoldClicks()
        {
            if (_ignored == null || _holdingClicks)
            {
                return;
            }
            _holdingClicks = true;
            foreach (object command in _clickCommands)
            {
                if (!(bool)_ignoredContains.Invoke(_ignored, new[] { command }))
                {
                    _ignoredAdd.Invoke(_ignored, new[] { command });
                    _clicksAdded.Add(command);
                }
            }
        }

        public void ReleaseClicks()
        {
            if (!_holdingClicks)
            {
                return;
            }
            _holdingClicks = false;
            foreach (object command in _clicksAdded)
            {
                _ignoredRemove.Invoke(_ignored, new[] { command });
            }
            _clicksAdded.Clear();
        }
    }
}
