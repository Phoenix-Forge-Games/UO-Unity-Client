using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Talking to an NPC (E on them): a dialogue window with everything the server offers for them, which in UO
    /// is the NPC's context menu (buy, sell, bank, train, quests, stable...), plus what they've said lately.
    /// Closes when you walk away.
    /// </summary>
    public sealed class NpcDialogue : HudWindow
    {
        private const int ReachTiles = 4;

        private readonly Label _said;
        private readonly VisualElement _options;
        private uint _npc;
        private bool _waiting;

        public NpcDialogue(HudServices hud) : base(hud, "", "npc-dialogue", 420, 380)
        {
            _said = MakeLabel(string.Empty, "npc-said");
            Body.Add(_said);
            _options = new VisualElement();
            _options.AddToClassList("npc-options");
            Body.Add(_options);
            hud.ClaimContextMenu = OnEntries;
            hud.World.Speech += OnSpeech;
        }

        protected override Vector2 DefaultPosition => new Vector2(760, 300);

        public uint Npc => _npc;

        public void Talk(uint npc)
        {
            WorldState world = Hud.World;
            _npc = npc;
            _waiting = true;
            Title.text = world.Mobiles.TryGetValue(npc, out Mobile mobile) && mobile.Name.Length > 0 ? mobile.Name : "…";
            _said.text = string.Empty;
            _options.Clear();
            _options.Add(MakeLabel("…", "empty-note"));
            Open();
            world.RequestContextMenu(npc);
        }

        protected override void OnClose()
        {
            _npc = 0;
            _waiting = false;
        }

        private bool OnEntries(uint serial, IReadOnlyList<ContextEntry> entries)
        {
            if (!_waiting || serial != _npc)
            {
                return false;
            }

            _waiting = false;
            _options.Clear();
            foreach (ContextEntry entry in entries)
            {
                ContextEntry chosen = entry;
                Button option = MakeButton(entry.Text, () => Choose(chosen), "npc-option");
                option.SetEnabled(!entry.Disabled);
                _options.Add(option);
            }

            _options.Add(MakeButton("Goodbye", Close, "npc-option"));
            return true;
        }

        private void Choose(ContextEntry entry)
        {
            Hud.World.ChooseContextEntry(_npc, entry.Index);
            // Most entries open something else (a shop, the bank, a gump); the dialogue stays for the next choice.
            uint npc = _npc;
            Root.schedule.Execute(() => { if (IsOpen && _npc == npc) Hud.World.RequestContextMenu(npc); _waiting = IsOpen; }).StartingIn(600);
        }

        private void OnSpeech(SpeechMessage message)
        {
            if (IsOpen && message.Serial == _npc && message.Type != MessageType.Label)
            {
                _said.text = message.Text;
            }
        }

        public override void Update()
        {
            WorldState world = Hud.World;
            if (_npc == 0 || !world.Mobiles.TryGetValue(_npc, out Mobile npc) || world.Player == null ||
                Mathf.Max(Mathf.Abs(npc.X - world.Player.X), Mathf.Abs(npc.Y - world.Player.Y)) > ReachTiles)
            {
                Close();
            }
        }
    }
}
