using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Combat;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// What happened, kept: the combat log, the chat window's Combat tab (your hits and misses, what hit you, deaths
    /// nearby, every skill and stat change and every other message from the server), the stacked messages in the middle of the screen (several at
    /// once, so a swing that raises two skills shows both), and the skill watch (the skills you tagged in the Skills
    /// tab, live, in a see-through panel on the right). Also the Attack hotbar entry.
    /// </summary>
    public sealed partial class ItemHud
    {
        /// <summary>Hotbar id of the Attack entry: start fighting your target (as R does).</summary>
        private const int AttackSlot = 90002;

        private const float NoticeSeconds = 4f;
        private const int NoticeLines = 3;

        private VisualElement _watch;
        private readonly List<(string Text, float Until)> _notices = new List<(string, float)>();
        private readonly HashSet<int> _tagged = new HashSet<int>();
        private string _tagOwner;
        private readonly Dictionary<Combatant, bool> _deadSeen = new Dictionary<Combatant, bool>();
        private float _nextDeathCheck;
        private bool _logHooked;

        private void BuildLog()
        {
            _watch = new VisualElement { pickingMode = PickingMode.Ignore };
            _watch.AddToClassList("skill-watch");
            _watch.style.display = DisplayStyle.None;
            _root.Add(_watch);

            if (!_logHooked)
            {
                _logHooked = true;
                CombatService.Swing += LogSwing;
                SkillService.Changed += _ => RefreshWatch();
            }
        }

        private void OnDisableLog()
        {
            if (_logHooked)
            {
                _logHooked = false;
                CombatService.Swing -= LogSwing;
            }
        }

        /// <summary>Every frame: deaths nearby, and the watch follows whoever you're playing.</summary>
        private void UpdateLog()
        {
            if (_hotbarOwner != _tagOwner)
            {
                LoadTags();
            }

            if (Time.unscaledTime < _nextDeathCheck)
            {
                return;
            }

            _nextDeathCheck = Time.unscaledTime + 0.25f;
            Combatant me = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<Combatant>() : null;
            foreach (Combatant c in Combatant.Seen)
            {
                if (c == null || me != null && (c.transform.position - me.transform.position).sqrMagnitude > 40f * 40f)
                {
                    continue;
                }

                bool dead = c.Dead.Value;
                if (_deadSeen.TryGetValue(c, out bool was) && dead && !was)
                {
                    AddLog(c == me ? "You die." : $"{NameOf(c)} dies.", "death");
                }

                _deadSeen[c] = dead;
            }
        }

        // ------------------------------------------------------------------ the log

        /// <summary>A line in the combat log: the chat window's Combat tab (GameHud).</summary>
        private static void AddLog(string text, string kind) => GameHud.AddCombat(text, kind);

        /// <summary>A message from the server: into the log, and stacked in the middle of the screen.</summary>
        private void LogNotice(string text)
        {
            bool skill = text.Contains("skill in") || text.Contains("has increased") || text.Contains("has decreased");
            AddLog(text, skill ? "skill" : "system");
        }

        private void LogSwing(SwingMessage m)
        {
            Combatant me = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<Combatant>() : null;
            if (me == null)
            {
                return;
            }

            Combatant attacker = Find(m.AttackerId), target = Find(m.TargetId);
            if (m.AttackerId == me.ObjectId)
            {
                AddLog(m.Amount > 0 ? $"You hit {NameOf(target)} for {m.Amount}." : $"You miss {NameOf(target)}.", m.Amount > 0 ? "hit" : "miss");
            }
            else if (m.TargetId == me.ObjectId)
            {
                AddLog(m.Amount > 0 ? $"{NameOf(attacker)} hits you for {m.Amount}." : $"{NameOf(attacker)} misses you.", m.Amount > 0 ? "hurt" : "miss");
            }
        }

        private static Combatant Find(int id)
        {
            foreach (Combatant c in Combatant.Seen)
            {
                if (c != null && c.ObjectId == id)
                {
                    return c;
                }
            }

            return null;
        }

        private static string NameOf(Combatant c)
        {
            if (c == null)
            {
                return "something";
            }

            PlayerIdentity id = c.GetComponent<PlayerIdentity>();
            if (id != null && !string.IsNullOrEmpty(id.Name.Value))
            {
                return id.Name.Value;
            }

            return c.TryGetComponent(out INamed named) && !string.IsNullOrEmpty(named.DisplayName) ? named.DisplayName : "something";
        }

        /// <summary>The middle-of-screen messages: the last few, each for a few seconds, newest at the bottom.</summary>
        private void StackNotice(string text)
        {
            _notices.RemoveAll(n => n.Until <= Time.unscaledTime);
            _notices.Add((text, Time.unscaledTime + NoticeSeconds));
            while (_notices.Count > NoticeLines)
            {
                _notices.RemoveAt(0);
            }

            var lines = new List<string>();
            foreach ((string t, float _) in _notices)
            {
                lines.Add(t);
            }

            ControlPrompts.Set(_notice, string.Join("\n", lines));
        }

        // ------------------------------------------------------------------ the skill watch

        private bool IsTagged(int skill) => _tagged.Contains(skill);

        private void ToggleTag(int skill)
        {
            if (!_tagged.Remove(skill))
            {
                _tagged.Add(skill);
            }

            SaveTags();
            RefreshWatch();
            _skillsDirty = true;
        }

        private void LoadTags()
        {
            _tagOwner = _hotbarOwner;
            _tagged.Clear();
            string saved = string.Empty;
            try
            {
                saved = string.IsNullOrEmpty(_tagOwner) ? string.Empty : PlayerPrefs.GetString($"game.watch.{_tagOwner}", string.Empty);
            }
            catch (Exception)
            {
                // no preference store: nothing tagged
            }

            foreach (string part in saved.Split(','))
            {
                if (int.TryParse(part, out int id))
                {
                    _tagged.Add(id);
                }
            }

            RefreshWatch();
        }

        private void SaveTags()
        {
            if (!string.IsNullOrEmpty(_tagOwner))
            {
                PlayerPrefs.SetString($"game.watch.{_tagOwner}", string.Join(",", _tagged));
            }
        }

        /// <summary>The see-through panel: each tagged skill and its value, kept up to date.</summary>
        private void RefreshWatch()
        {
            if (_watch == null)
            {
                return;
            }

            _watch.Clear();
            var values = new Dictionary<int, float>();
            foreach (SkillView v in SkillService.Mine.Skills ?? Array.Empty<SkillView>())
            {
                values[v.Skill] = v.Value / 10f;
            }

            foreach (Content.SkillDef def in Content.CreationRules.Skills)
            {
                if (!_tagged.Contains(def.Id))
                {
                    continue;
                }

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("skill-watch-row");
                var name = new Label(def.Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("skill-watch-name");
                var value = new Label((values.TryGetValue(def.Id, out float v) ? v : 0f).ToString("0.0")) { pickingMode = PickingMode.Ignore };
                value.AddToClassList("skill-watch-value");
                row.Add(name);
                row.Add(value);
                _watch.Add(row);
            }

            _watch.style.display = _watch.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ Attack

        /// <summary>First on the spells page: Attack, to use or drag onto the hotbar.</summary>
        private void AddAttackRow()
        {
            var row = new VisualElement();
            row.AddToClassList("skill-row");
            var name = new Label("Attack") { tooltip = "Start fighting your target (R). Drag onto the hotbar." };
            name.AddToClassList("skill-name");
            var what = new Label("melee, your target");
            what.AddToClassList("skill-cap");
            var use = new Button(AttackTarget) { text = "Use" };
            use.AddToClassList("spell-button");
            row.Add(name);
            row.Add(what);
            row.Add(use);
            name.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0)
                {
                    _draggingSpell = AttackSlot;
                    _dragGhost.text = "Attack";
                    _dragGhost.style.display = DisplayStyle.Flex;
                    _dragGhost.style.left = e.position.x + 12;
                    _dragGhost.style.top = e.position.y + 12;
                }
            });
            _spellsPage.Add(row);
        }

        /// <summary>The Attack entry (hotbar or spell book): start fighting your target, as R does.</summary>
        private void AttackTarget()
        {
            Combatant target = CombatHud.Targeted;
            if (target == null)
            {
                ItemService.ShowNotice("You have no target. Click one, or press Tab.");
                return;
            }

            _network.ClientManager.Broadcast(new TargetRequest { ObjectId = target.ObjectId, Attack = true });
        }
    }
}
