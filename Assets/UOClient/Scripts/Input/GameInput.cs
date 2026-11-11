using UnityEngine;
using UnityEngine.InputSystem;

namespace UOClient
{
    /// <summary>
    /// The one <see cref="UOControls"/> instance (Input/UOControls.inputactions). All game input reads from it, never
    /// from devices directly. While the player types in chat the Gameplay map is off, so letters don't move them.
    /// </summary>
    public static class GameInput
    {
        private static UOControls _controls;

        public static UOControls Controls
        {
            get
            {
                if (_controls == null)
                {
                    _controls = new UOControls();
                    string overrides = GameSettings.Current.BindingOverrides;
                    if (!string.IsNullOrEmpty(overrides))
                    {
                        _controls.asset.LoadBindingOverridesFromJson(overrides);
                    }

                    _controls.Gameplay.Enable();
                    _controls.UI.Enable();
                }

                return _controls;
            }
        }

        public static UOControls.GameplayActions Gameplay => Controls.Gameplay;

        /// <summary>Saves the current key bindings (after a rebind or a reset) with the settings.</summary>
        public static void SaveBindings()
        {
            GameSettings.Current.BindingOverrides = Controls.asset.SaveBindingOverridesAsJson();
            GameSettings.Current.Save();
        }

        /// <summary>
        /// Set by the HUD: whether a screen position (pixels, origin bottom-left) is over a UI panel. Clicks and
        /// scrolling there belong to the UI, not the world or the camera.
        /// </summary>
        public static System.Func<Vector2, bool> PointerOverUI { get; set; }

        public static bool IsPointerOverUI(Vector2 screen) => PointerOverUI != null && PointerOverUI(screen);

        /// <summary>True while a text field (chat) has the keyboard.</summary>
        public static bool Typing { get; private set; }

        public static void SetTyping(bool typing)
        {
            Typing = typing;
            if (typing)
            {
                Controls.Gameplay.Disable();
            }
            else
            {
                Controls.Gameplay.Enable();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            _controls?.Dispose();
            _controls = null;
            Typing = false;
            PointerOverUI = null;
        }
    }
}
