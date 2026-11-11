using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UOClient;
using UOClient.Content;

namespace UOGame.UI
{
    /// <summary>
    /// Text that names a control, shown with the player's own key or mouse button as an icon:
    /// <c>"Press {Use} to open"</c> shows "Press [E] to open" with the E key's icon, and follows the binding when the
    /// player rebinds it. Placeholders are action names in the UOControls actions asset, bare (<c>{Use}</c>, any map)
    /// or with their map (<c>{Build/Rotate}</c>); a composite (Move) shows each of its keys. Anything else in braces is
    /// left as text.
    ///
    /// Icons are the prompt icons in the secured art (<see cref="SecureArtAddresses.PromptIcon"/>). Until one has
    /// loaded, or when the build has none, the key's name shows in a small key cap, so a prompt always reads.
    /// Not for the hotbar, which shows its own number keys.
    /// </summary>
    public static class ControlPrompts
    {
        public const float DefaultIconSize = 22f;

        private static readonly Regex Token = new Regex(@"\{([A-Za-z][A-Za-z0-9]*(?:/[A-Za-z][A-Za-z0-9]*)?)\}");
        private static readonly HashSet<VisualElement> Live = new HashSet<VisualElement>();
        private static bool _hooked;

        private sealed class State
        {
            public string Message;
            public float IconSize;
            public Color? Tint;
            public string Built;
        }

        /// <summary>Whether a message has any control placeholders.</summary>
        public static bool HasControls(string message) => !string.IsNullOrEmpty(message) && Token.IsMatch(message);

        /// <summary>
        /// A new element showing <paramref name="message"/>: words and control icons in a wrapping row. Give it the
        /// class a Label would have had (text colour and size are inherited by its words).
        /// </summary>
        public static VisualElement Create(string message, float iconSize = DefaultIconSize, Color? tint = null)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("control-prompt");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.userData = new State { IconSize = iconSize };
            row.RegisterCallback<AttachToPanelEvent>(_ => Live.Add(row));
            row.RegisterCallback<DetachFromPanelEvent>(_ => Live.Remove(row));
            Set(row, message, tint);
            return row;
        }

        /// <summary>Changes what a <see cref="Create"/>d element says (cheap when it's the same message).</summary>
        public static void Set(VisualElement row, string message, Color? tint = null)
        {
            if (row == null || !(row.userData is State state))
            {
                return;
            }

            Hook();
            state.Message = message ?? string.Empty;
            state.Tint = tint;
            string key = state.Message + "|" + (tint.HasValue ? ColorUtility.ToHtmlStringRGBA(tint.Value) : string.Empty) + "|" + BindingStamp(state.Message);
            if (key == state.Built)
            {
                return;
            }

            state.Built = key;
            Build(row, state);
        }

        /// <summary>The message as plain text, each placeholder replaced by its key's name ("Press E to open").</summary>
        public static string Plain(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return Token.Replace(message, m =>
            {
                List<Bound> controls = Resolve(m.Groups[1].Value, out bool found);
                if (!found)
                {
                    return m.Value;
                }

                if (controls.Count == 0)
                {
                    return "(unbound)";
                }

                var names = new List<string>();
                foreach (Bound c in controls)
                {
                    names.Add(c.Display);
                }

                return string.Join("/", names);
            });
        }

        // ---------------------------------------------------------------- building

        private static void Build(VisualElement row, State state)
        {
            row.Clear();
            string message = state.Message;
            int at = 0;
            foreach (Match m in Token.Matches(message))
            {
                AddWords(row, message.Substring(at, m.Index - at), state.Tint);
                List<Bound> controls = Resolve(m.Groups[1].Value, out bool found);
                if (!found)
                {
                    AddWords(row, m.Value, state.Tint);
                }
                else if (controls.Count == 0)
                {
                    AddWords(row, "(unbound)", state.Tint);
                }
                else
                {
                    foreach (Bound control in controls)
                    {
                        row.Add(KeyIcon(control, state.IconSize));
                    }
                }

                at = m.Index + m.Length;
            }

            AddWords(row, message.Substring(at), state.Tint);
        }

        /// <summary>One Label per word so the row wraps like text; a newline starts a new line.</summary>
        private static void AddWords(VisualElement row, string text, Color? tint)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string[] lines = text.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                if (l > 0)
                {
                    var br = new VisualElement { pickingMode = PickingMode.Ignore };
                    br.style.flexBasis = new Length(100, LengthUnit.Percent);
                    br.style.height = 0;
                    row.Add(br);
                }

                foreach (string word in lines[l].Split(' '))
                {
                    if (word.Length == 0)
                    {
                        continue;
                    }

                    var label = new Label(word) { pickingMode = PickingMode.Ignore, enableRichText = true };
                    label.style.marginLeft = 0;
                    label.style.marginTop = 0;
                    label.style.marginBottom = 0;
                    label.style.marginRight = 4;
                    label.style.paddingLeft = 0;
                    label.style.paddingRight = 0;
                    label.style.paddingTop = 0;
                    label.style.paddingBottom = 0;
                    if (tint.HasValue)
                    {
                        label.style.color = tint.Value;
                    }

                    row.Add(label);
                }
            }
        }

        /// <summary>A key's icon, with its name in a key cap until (or unless) the icon loads.</summary>
        private static VisualElement KeyIcon(Bound control, float size)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore, name = "key-" + control.Control };
            icon.AddToClassList("control-icon");
            icon.style.minWidth = size;
            icon.style.height = size;
            icon.style.marginRight = 4;
            icon.style.justifyContent = Justify.Center;
            icon.style.alignItems = Align.Center;
            icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);

            var cap = new Label(control.Display) { pickingMode = PickingMode.Ignore };
            cap.style.fontSize = Mathf.Max(10f, size * 0.55f);
            cap.style.unityFontStyleAndWeight = FontStyle.Bold;
            cap.style.color = new Color(0.1f, 0.09f, 0.08f);
            cap.style.backgroundColor = new Color(0.93f, 0.9f, 0.84f);
            cap.style.borderTopLeftRadius = cap.style.borderTopRightRadius = cap.style.borderBottomLeftRadius = cap.style.borderBottomRightRadius = 3;
            cap.style.paddingLeft = cap.style.paddingRight = 4;
            cap.style.paddingTop = cap.style.paddingBottom = 0;
            cap.style.marginLeft = cap.style.marginRight = cap.style.marginTop = cap.style.marginBottom = 0;
            cap.style.height = size - 2;
            cap.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.Add(cap);

            string address = SecureArtAddresses.PromptIcon(control.Device, control.Control);
            SecureArt.Load<Texture2D>(address, texture =>
            {
                if (texture == null)
                {
                    return; // no icon in this build: the key cap stays
                }

                icon.style.backgroundImage = new StyleBackground(texture);
                // Wide keys (Space, Shift) are drawn wider in their square image; let them take what they need.
                icon.style.width = size * (control.Display.Length > 2 ? 1.6f : 1f);
                cap.style.display = DisplayStyle.None;
            });
            return icon;
        }

        // ---------------------------------------------------------------- bindings

        private readonly struct Bound
        {
            public readonly string Device, Control, Display;

            public Bound(string device, string control, string display)
            {
                Device = device;
                Control = control;
                Display = display;
            }
        }

        /// <summary>The keyboard/mouse controls an action is bound to now (the first binding, or a composite's parts).</summary>
        private static List<Bound> Resolve(string actionName, out bool found)
        {
            var result = new List<Bound>();
            InputAction action = GameInput.Controls.asset.FindAction(actionName, false);
            found = action != null;
            if (action == null)
            {
                return result;
            }

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding b = bindings[i];
                if (b.isComposite)
                {
                    var parts = new List<Bound>();
                    for (int p = i + 1; p < bindings.Count && bindings[p].isPartOfComposite; p++)
                    {
                        if (TryControl(bindings[p].effectivePath, out Bound c))
                        {
                            parts.Add(c);
                        }
                    }

                    if (parts.Count == 4 && parts.TrueForAll(c => c.Device == "keyboard" && "wasd".Contains(c.Control)) &&
                        parts.Exists(c => c.Control == "w") && parts.Exists(c => c.Control == "a") && parts.Exists(c => c.Control == "s") && parts.Exists(c => c.Control == "d"))
                    {
                        result.Add(new Bound("keyboard", "wasd", "WASD")); // one icon for the usual four
                        return result;
                    }

                    if (parts.Count > 0)
                    {
                        return parts;
                    }

                    continue;
                }

                if (!b.isPartOfComposite && TryControl(b.effectivePath, out Bound control))
                {
                    result.Add(control);
                    return result;
                }
            }

            return result;
        }

        /// <summary>A keyboard or mouse control from a binding path ("&lt;Keyboard&gt;/leftShift", "&lt;Mouse&gt;/scroll/y").</summary>
        private static bool TryControl(string path, out Bound control)
        {
            control = default;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string layout = InputControlPath.TryGetDeviceLayout(path);
            if (layout != "Keyboard" && layout != "Mouse")
            {
                return false; // the prompt icons are keyboard and mouse only
            }

            string[] parts = path.Split('/');
            if (parts.Length < 2 || parts[1].Length == 0)
            {
                return false;
            }

            string display = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (layout == "Mouse")
            {
                display = parts[1] == "leftButton" ? "LMB" : parts[1] == "rightButton" ? "RMB" : parts[1] == "middleButton" ? "MMB" : display;
            }

            control = new Bound(layout.ToLowerInvariant(), parts[1], string.IsNullOrEmpty(display) ? parts[1] : display);
            return true;
        }

        /// <summary>What the placeholders in a message are bound to, so a rebind makes the message rebuild.</summary>
        private static string BindingStamp(string message)
        {
            if (!HasControls(message))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (Match m in Token.Matches(message))
            {
                foreach (Bound c in Resolve(m.Groups[1].Value, out _))
                {
                    sb.Append(c.Device).Append(':').Append(c.Control).Append(',');
                }

                sb.Append(';');
            }

            return sb.ToString();
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            InputSystem.onActionChange += OnActionChange;
        }

        private static void OnActionChange(object _, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged)
            {
                Refresh();
            }
        }

        /// <summary>Rebuilds every shown prompt whose keys changed (after a rebind or a reset).</summary>
        public static void Refresh()
        {
            foreach (VisualElement row in new List<VisualElement>(Live))
            {
                if (row.userData is State state)
                {
                    Set(row, state.Message, state.Tint);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            Live.Clear();
            InputSystem.onActionChange -= OnActionChange;
            _hooked = false;
        }
    }
}
