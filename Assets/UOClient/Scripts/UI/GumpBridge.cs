using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Server gumps are never drawn (Docs/GameplayDesign.md). The few the game can't do without are recognised here
    /// by what they say and answered from the client's own windows:
    ///  - the shard's death window: respawn at your spawn point, or stay a ghost;
    ///  - the shard's update notes: the What's new window;
    ///  - suppressed: the publish news and the shard's "Commands" button;
    ///  - resurrection (a healer, an ankh, paid resurrection): "Return to life?";
    /// Everything else is noted once in chat and logged (layout and text) so we can see what it was.
    /// </summary>
    public sealed class GumpBridge
    {
        private const int ResurrectionTitle = 1011022;   // <center>Resurrection</center>
        private const int PaidResurrection = 1060017;    // Wishing to rejoin the living, are you? ...

        private const int NewsDontShowAgain = 1158386; // the publish-news gump's "Close and do not show this version again"

        private readonly HudServices _hud;
        private readonly ConfirmWindow _confirm;
        private readonly WhatsNewWindow _whatsNew;
        private readonly Action<string> _note;
        private readonly HashSet<uint> _noted = new HashSet<uint>();

        public GumpBridge(HudServices hud, Action<string> note)
        {
            _hud = hud;
            _note = note;
            _confirm = new ConfirmWindow(hud);
            _whatsNew = new WhatsNewWindow(hud);
            hud.World.ServerGumpReceived += OnGump;
            foreach (ServerGump early in hud.World.UnclaimedGumps)
            {
                OnGump(early);
            }

            hud.World.UnclaimedGumps.Clear();
        }

        public HudWindow Window => _confirm;
        public HudWindow WhatsNew => _whatsNew;

        public void Dispose() => _hud.World.ServerGumpReceived -= OnGump;

        private void OnGump(ServerGump gump)
        {
            var clilocs = new HashSet<int>(gump.Clilocs());
            WorldState world = _hud.World;

            // Suppressed: the publish news, and the shard's always-on "Commands" button (paused; its features get
            // their own client pathways as we go through them).
            if (clilocs.Contains(NewsDontShowAgain) || gump.Lines.Length == 1 && gump.Lines[0] == "Commands")
            {
                return;
            }

            // The shard's update notes (Scripts/Custom/WhatsNew.cs).
            if (gump.Lines.Length >= 2)
            {
                string heading = Plain(gump.Lines[0]);
                if (heading.StartsWith("What's changed since you left") || heading.StartsWith("What's new on"))
                {
                    _whatsNew.Show(heading, WhatsNewWindow.Parse(gump.Lines[1]), () => world.ReplyGump(gump, 0));
                    return;
                }
            }

            // The shard's own death window (SeriousFun respawn): button 1 stays a ghost, button 2 respawns.
            if (gump.Lines.Length >= 4 && Plain(gump.Lines[0]).StartsWith("You have died"))
            {
                _confirm.Ask("You have died", Plain(gump.Lines[1]),
                    Plain(gump.Lines[3]), () => world.ReplyGump(gump, 2),
                    Plain(gump.Lines[2]), () => world.ReplyGump(gump, 1));
                return;
            }

            if (clilocs.Contains(PaidResurrection))
            {
                string fee = gump.Lines.FirstOrDefault(l => l.Any(char.IsDigit)) ?? "a fee";
                _confirm.Ask("Resurrection",
                    $"Wishing to rejoin the living? The healer will restore your body for {fee} gold, taken from your bank.",
                    "Pay and return to life", () => world.ReplyGump(gump, 2, 1),
                    "Stay dead", () => world.ReplyGump(gump, 2, 2));
                return;
            }

            if (clilocs.Contains(ResurrectionTitle))
            {
                int message = clilocs.FirstOrDefault(n => n > ResurrectionTitle && n < ResurrectionTitle + 20);
                string text = message != 0 ? world.Cliloc?.Get(message) : null;
                _confirm.Ask("Resurrection", Plain(text) ?? "Do you wish to be resurrected?",
                    "Return to life", () => world.ReplyGump(gump, 1),
                    "Not now", () => world.ReplyGump(gump, 0));
                return;
            }

            if (_noted.Add(gump.GumpId))
            {
                _note($"(The server opened a window this client doesn't have yet: gump 0x{gump.GumpId:X}.)");
                Debug.Log($"[UOClient] Unhandled gump 0x{gump.GumpId:X} from 0x{gump.Serial:X}: clilocs [{string.Join(", ", clilocs)}], " +
                          $"text [{string.Join(" | ", gump.Lines.Take(12))}], layout {gump.Layout.Substring(0, Math.Min(600, gump.Layout.Length))}");
            }
        }

        /// <summary>Cliloc text without its HTML tags.</summary>
        private static string Plain(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            bool tag = false;
            foreach (char c in html.Replace("<br>", "\n").Replace("<BR>", "\n"))
            {
                if (c == '<') tag = true;
                else if (c == '>') tag = false;
                else if (!tag) sb.Append(c);
            }

            return sb.ToString().Trim();
        }
    }

    /// <summary>A small question window with two answers (yes / no style), in the client's own style.</summary>
    public sealed class ConfirmWindow : HudWindow
    {
        private readonly Label _text;
        private readonly Button _yes, _no;
        private Action _onYes, _onNo;

        public ConfirmWindow(HudServices hud) : base(hud, string.Empty, "confirm-window", 460, 260)
        {
            _text = MakeLabel(string.Empty, "npc-said");
            Body.Add(_text);
            var row = new VisualElement();
            row.AddToClassList("npc-options");
            _yes = MakeButton(string.Empty, () => Answer(true), "npc-option");
            _no = MakeButton(string.Empty, () => Answer(false), "npc-option");
            row.Add(_yes);
            row.Add(_no);
            Body.Add(row);
        }

        protected override Vector2 DefaultPosition => new Vector2(730, 300);

        public void Ask(string title, string text, string yes, Action onYes, string no, Action onNo)
        {
            if (IsOpen)
            {
                Answer(false); // a new question replaces an unanswered one
            }

            Title.text = title;
            _text.text = text;
            _yes.text = yes;
            _no.text = no;
            _onYes = onYes;
            _onNo = onNo;
            Open();
        }

        private void Answer(bool yes)
        {
            Action action = yes ? _onYes : _onNo;
            _onYes = _onNo = null;
            Close();
            action?.Invoke();
        }

        /// <summary>Closing the window (×, Esc) counts as "no".</summary>
        protected override void OnClose()
        {
            Action no = _onNo;
            _onYes = _onNo = null;
            no?.Invoke();
        }
    }
}
