using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UOClient
{
    /// <summary>
    /// The shard's update notes ("What's changed since you left" on login, "What's new on SeriousFun" with [news]):
    /// dated sections of bullet points, newest first. Today the notes arrive inside the server's WhatsNew gump
    /// (Scripts/Custom/WhatsNew.cs) and are read out of it; the window itself only takes a title and sections,
    /// so our own server can feed it directly later.
    /// </summary>
    public sealed class WhatsNewWindow : HudWindow
    {
        public sealed class Section
        {
            public string When;
            public readonly List<string> Points = new List<string>();
        }

        private readonly ScrollView _list;
        private Action _closed;

        public WhatsNewWindow(HudServices hud) : base(hud, "What's new", "whats-new", 620, 560)
        {
            _list = new ScrollView();
            _list.AddToClassList("list-scroll");
            Body.Add(_list);
            var footer = new VisualElement();
            footer.AddToClassList("menu-row");
            Label hint = MakeLabel("Say [news to see this again. Details are in the Adventurer's Guide.", "empty-note");
            hint.style.flexShrink = 1;
            hint.style.flexGrow = 1;
            footer.Add(hint);
            footer.Add(MakeButton("Close", Close, "hud-button"));
            Body.Add(footer);
        }

        protected override Vector2 DefaultPosition => new Vector2(650, 140);

        public void Show(string title, IList<Section> sections, Action closed)
        {
            _closed?.Invoke();
            _closed = closed;
            Title.text = title;
            _list.Clear();
            foreach (Section section in sections)
            {
                _list.Add(MakeLabel(section.When, "stats-heading"));
                foreach (string point in section.Points)
                {
                    Label line = MakeLabel("•  " + point, "news-point");
                    line.enableRichText = true;
                    _list.Add(line);
                }
            }

            _list.scrollOffset = Vector2.zero;
            Open();
        }

        protected override void OnClose()
        {
            Action closed = _closed;
            _closed = null;
            closed?.Invoke();
        }

        /// <summary>
        /// Reads the notes out of the WhatsNew gump's HTML: "&lt;b&gt;date&lt;/b&gt;&lt;BR&gt;" headings, then
        /// "- point&lt;BR&gt;" lines. Bold stays bold; other markup is dropped.
        /// </summary>
        public static List<Section> Parse(string html)
        {
            var sections = new List<Section>();
            foreach (string raw in html.Replace("<br>", "<BR>").Split(new[] { "<BR>" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = Clean(raw).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    if (sections.Count == 0)
                    {
                        sections.Add(new Section { When = string.Empty });
                    }

                    sections[sections.Count - 1].Points.Add(line.Substring(2));
                }
                else
                {
                    sections.Add(new Section { When = line.Replace("<b>", string.Empty).Replace("</b>", string.Empty) });
                }
            }

            return sections;
        }

        /// <summary>Keeps &lt;b&gt; / &lt;i&gt; (UI Toolkit rich text); drops BASEFONT, CENTER and the rest.</summary>
        private static string Clean(string html)
        {
            var sb = new System.Text.StringBuilder(html.Length);
            int i = 0;
            while (i < html.Length)
            {
                if (html[i] == '<')
                {
                    int end = html.IndexOf('>', i);
                    if (end < 0)
                    {
                        break;
                    }

                    string tag = html.Substring(i, end - i + 1).ToLowerInvariant();
                    if (tag == "<b>" || tag == "</b>" || tag == "<i>" || tag == "</i>")
                    {
                        sb.Append(tag);
                    }

                    i = end + 1;
                }
                else
                {
                    sb.Append(html[i++]);
                }
            }

            return sb.ToString().Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
        }
    }
}
