using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UOGame.UI
{
    /// <summary>
    /// Picking a colour: a grid of swatches, one per UO hue in a range (skin tones, hair colours, a dye tub's
    /// colours), each drawn in that hue's own colour; click one to choose it. Never a slider. Works like an int
    /// field: value is the hue number, and RegisterValueChangedCallback hears changes.
    /// </summary>
    public sealed class HueSwatches : VisualElement, INotifyValueChanged<int>
    {
        /// <summary>Which of a hue's 32 shades (0 dark, 31 light) a swatch shows: a mid-light one, as it reads on skin and hair.</summary>
        private const int Shade = UOClient.Data.Hues.SwatchShade;

        private readonly Dictionary<int, VisualElement> _swatches = new Dictionary<int, VisualElement>();
        private int _value, _low, _high = -1;

        public HueSwatches(int low, int count)
        {
            AddToClassList("hue-swatches");
            SetRange(low, count);
        }

        public int lowValue => _low;
        public int highValue => _high;

        /// <summary>Show the hues low .. low + count - 1 (keeping the choice if it's still among them).</summary>
        public void SetRange(int low, int count)
        {
            int high = low + Mathf.Max(1, count) - 1;
            if (low == _low && high == _high)
            {
                return;
            }

            _low = low;
            _high = high;
            Clear();
            _swatches.Clear();
            UOClient.Data.Hues hues = UoData.Hues;
            for (int hue = low; hue <= high; hue++)
            {
                int chosen = hue;
                var swatch = new VisualElement { tooltip = $"Hue {hue}" };
                swatch.AddToClassList("hue-swatch");
                swatch.style.backgroundColor = (Color)(hues != null && hues.TryShade((ushort)hue, Shade, out byte r, out byte g, out byte b)
                    ? new Color32(r, g, b, 255) : new Color32(128, 128, 128, 255));
                swatch.RegisterCallback<ClickEvent>(_ => value = chosen);
                _swatches[hue] = swatch;
                Add(swatch);
            }

            SetValueWithoutNotify(Mathf.Clamp(_value, low, high));
        }

        public int value
        {
            get => _value;
            set
            {
                int clamped = Mathf.Clamp(value, _low, _high);
                if (clamped == _value)
                {
                    Mark();
                    return;
                }

                int previous = _value;
                SetValueWithoutNotify(clamped);
                using (ChangeEvent<int> e = ChangeEvent<int>.GetPooled(previous, clamped))
                {
                    e.target = this;
                    SendEvent(e);
                }
            }
        }

        public void SetValueWithoutNotify(int newValue)
        {
            _value = Mathf.Clamp(newValue, _low, _high);
            Mark();
        }

        private void Mark()
        {
            foreach (KeyValuePair<int, VisualElement> pair in _swatches)
            {
                pair.Value.EnableInClassList("hue-swatch-chosen", pair.Key == _value);
            }
        }
    }
}
