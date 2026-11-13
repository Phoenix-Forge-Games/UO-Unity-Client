using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Net;

namespace UOGame.UI
{
    /// <summary>
    /// Quests on the client (no server gumps): the quest window a quest-giver opens (an offer to accept or refuse, how far
    /// you are, or the quest done and its rewards) and the Quests tab (your quests, their progress, resign). Texts are the
    /// player's UO cliloc where the quest uses one.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _quest;
        private ScrollView _questsPage;

        private void OnEnableQuests() => Server.QuestService.Shown += ShowQuest;
        private void OnDisableQuests() => Server.QuestService.Shown -= ShowQuest;

        private void SendQuest(byte action, int npc = 0, string quest = null) =>
            _network.ClientManager.Broadcast(new QuestAction { Action = action, Npc = npc, Quest = quest ?? "" });

        /// <summary>A quest text: the cliloc's words (its markup and placeholders tidied), or the words themselves.</summary>
        private static string QuestText(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                return "";
            }

            string text = t.Type == JTokenType.Integer ? GameText.Get((int)t) ?? "" : (string)t;
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", "");
            text = Regex.Replace(text, @"~\d+_[A-Za-z_]+~", "");
            return text.Trim();
        }

        private static string ObjectiveLine(JToken o)
        {
            string name = QuestText(o["Name"]);
            int have = (int?)o["Have"] ?? 0, need = (int?)o["Need"] ?? 1;
            switch ((string)o["Kind"])
            {
                case "Slay": return $"Slay {need} {name}{(o["Region"]?.Type == JTokenType.String ? $" in {o["Region"]}" : "")}: {have} / {need}";
                case "Obtain": return $"Obtain {need} {name}: {have} / {need}";
                case "Deliver": return $"Deliver {name} to {QuestText(o["Receiver"])}";
                case "Apprentice": return $"Train {o["Skill"]} to {need}{(o["Region"]?.Type == JTokenType.String ? $" in {o["Region"]}" : "")}: {have} / {need}";
                default: return name;
            }
        }

        private void ShowQuest(QuestView view)
        {
            JObject j = string.IsNullOrEmpty(view.Json) ? new JObject() : JObject.Parse(view.Json);
            if (view.Kind == 3)
            {
                FillQuestLog(j);
                return;
            }

            if (_quest == null)
            {
                _quest = new Window(_layer, "Quest", 440, 520, new Vector2(480, 160));
            }

            string title = QuestText(j["Title"]);
            _quest.SetTitle(string.IsNullOrEmpty(title) ? QuestText(j["TitleText"]) : title);
            _quest.Body.Clear();
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            void Line(string text, bool heading = false)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var l = new Label(text) { style = { whiteSpace = WhiteSpace.Normal } };
                l.AddToClassList(heading ? "stats-heading" : "quest-text"); // light text on the dark window

                scroll.Add(l);
            }

            Line(view.Kind == 0 ? QuestText(j["Description"]) : view.Kind == 1 ? QuestText(j["Uncomplete"]) : QuestText(j["Complete"]));
            Line("Objectives", true);
            foreach (JToken o in j["Objectives"] as JArray ?? new JArray())
            {
                Line(ObjectiveLine(o));
            }

            Line(view.Kind == 2 ? "Rewards received" : "Rewards", true);
            foreach (JToken r in j["Rewards"] as JArray ?? new JArray())
            {
                int amount = (int?)r["Amount"] ?? 1;
                Line(amount > 1 ? $"{amount:N0} {QuestText(r["Name"])}" : QuestText(r["Name"]));
            }

            _quest.Body.Add(scroll);
            void B(string text, System.Action click)
            {
                var b = new Button(click) { text = text };
                b.AddToClassList("npc-option");
                _quest.Body.Add(b);
            }

            string quest = (string)j["Quest"];
            if (view.Kind == 0)
            {
                B("Accept", () =>
                {
                    SendQuest(1, view.Npc, quest);
                    _quest.Close();
                });
                B("Refuse", () =>
                {
                    SendQuest(2, view.Npc, quest);
                    _quest.Close();
                });
            }
            else
            {
                B("Close", () => _quest.Close());
            }

            _quest.Open();
        }

        /// <summary>The Quests tab of the character window.</summary>
        private void BuildQuestsPage()
        {
            _questsPage = new ScrollView();
            _questsPage.AddToClassList("tab-page");
            _equipment.Body.Add(_questsPage);
        }

        private void FillQuestLog(JObject j)
        {
            if (_questsPage == null)
            {
                return;
            }

            _questsPage.Clear();
            var quests = j["Quests"] as JArray ?? new JArray();
            if (quests.Count == 0)
            {
                _questsPage.Add(new Label("You are on no quests. Quest-givers stand in towns and wild places: talk to them.") { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }

            foreach (JToken q in quests)
            {
                string title = QuestText(q["Title"]);
                var heading = new Label(string.IsNullOrEmpty(title) ? QuestText(q["TitleText"]) : title);
                heading.AddToClassList("stats-heading");
                _questsPage.Add(heading);
                foreach (JToken o in q["Objectives"] as JArray ?? new JArray())
                {
                    _questsPage.Add(new Label(ObjectiveLine(o)) { style = { whiteSpace = WhiteSpace.Normal } });
                }

                string type = (string)q["Quest"];
                var resign = new Button(() => SendQuest(3, 0, type)) { text = "Resign" };
                resign.AddToClassList("spell-button");
                _questsPage.Add(resign);
            }
        }
    }
}
