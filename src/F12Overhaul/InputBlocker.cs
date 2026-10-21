using System;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;

namespace F12Overhaul
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

        private Action<bool> _set;
        private FieldInfo _flag;
        private bool _blocking;
        private bool _previous;

        public bool Available => _set != null;

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
            return true;
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
    }
}
