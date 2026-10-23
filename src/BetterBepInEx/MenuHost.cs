using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>
    /// Draws the menu. A component of its own, enabled only while the menu is open: Unity runs
    /// IMGUI for every enabled behaviour that has an OnGUI, a Layout and a Repaint pass every
    /// frame, even when it draws nothing. On the plugin itself that cost would run all game long.
    /// </summary>
    internal sealed class MenuHost : MonoBehaviour
    {
        internal MenuWindow Menu;

        private void OnGUI()
        {
            Menu?.OnGUI();
        }
    }
}
