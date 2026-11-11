using System;
using UnityEngine;

namespace UOGame.UI
{
    /// <summary>
    /// Text from the player's own UO files (Cliloc.enu in their UO folder): item names and the lines that describe
    /// things, as UO words them. Read once on first use; when the file isn't there every lookup returns null and
    /// callers fall back to the game's own words.
    /// </summary>
    public static class GameText
    {
        private static UOClient.Data.Cliloc _cliloc;
        private static bool _tried;

        private static UOClient.Data.Cliloc File
        {
            get
            {
                if (_cliloc == null && !_tried)
                {
                    _tried = true;
                    try
                    {
                        string path = System.IO.Path.Combine(UOClient.ClientSettings.DataFolder, "Cliloc.enu");
                        if (System.IO.File.Exists(path))
                        {
                            _cliloc = new UOClient.Data.Cliloc(path);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Game] Couldn't read cliloc ({e.Message}).");
                    }
                }

                return _cliloc;
            }
        }

        /// <summary>A text by number, or null.</summary>
        public static string Get(int number)
        {
            string text = number > 0 ? File?.Get(number) : null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>A text with its ~1_val~ (and further, tab-separated) arguments filled in, or null.</summary>
        public static string Format(int number, string args)
        {
            if (number <= 0 || File?.Get(number) == null)
            {
                return null;
            }

            string text = File.Format(number, args ?? string.Empty);
            return string.IsNullOrWhiteSpace(text) ? null : StripTags(text).Trim();
        }

        /// <summary>Cliloc texts carry HTML-ish tags (&lt;br&gt;, &lt;basefont&gt;): plain text for our labels.</summary>
        private static string StripTags(string text) => System.Text.RegularExpressions.Regex.Replace(text.Replace("<br>", "\n").Replace("<BR>", "\n"), "<[^>]*>", string.Empty);

        /// <summary>Capitalised (UO's lines are mostly lower case).</summary>
        public static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
