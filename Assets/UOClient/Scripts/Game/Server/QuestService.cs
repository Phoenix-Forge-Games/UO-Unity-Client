using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Quests (Content/Quests.json: ServUO's Mondain's Legacy quests). Talk to a quest-giver: it offers the first of its
    /// quests you may take (a window with its story, objectives and rewards to accept or refuse), tells you how far you
    /// are with one you're on, or, once it's done, takes what it asked for and rewards you (and offers the next quest of
    /// a chain). Objectives: slay creatures (yours or your pets' kills, in a region when it says), obtain items (in your
    /// backpack when you return), deliver items (given to you at the start; hand them to the named quest-giver) and the
    /// New Haven apprentice training (reach a skill). Quests whose objectives aren't in the game yet (escorts, questions,
    /// custom ones) aren't offered. Your quests are on the Quests tab.
    /// </summary>
    public sealed class QuestService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _quests;
        [SerializeField] private TextAsset _regions;

        public static QuestService Instance { get; private set; }

        /// <summary>Client side: a quest window to show.</summary>
        public static event Action<QuestView> Shown;

        private const float Reach = 6f;
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<int, (string Quest, int Npc)> _offers = new Dictionary<int, (string, int)>();
        private readonly Dictionary<string, List<(int Facet, RectInt Rect)>> _regionRects = new Dictionary<string, List<(int, RectInt)>>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            Instance = this;
            QuestRules.Load(_quests);
            if (_quests != null)
            {
                Protocol.AddDataFile(_quests);
                global::UOGame.Content.LiveContent.Register(_quests, QuestRules.Load);
            }

            foreach (JToken r in _regions != null ? JObject.Parse(_regions.text)["Regions"] as JArray ?? new JArray() : new JArray())
            {
                string name = (string)r["Name"];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (!_regionRects.TryGetValue(name, out var list))
                {
                    _regionRects[name] = list = new List<(int, RectInt)>();
                }

                foreach (JToken rect in r["Rects"] as JArray ?? new JArray())
                {
                    list.Add(((int)r["Facet"], new RectInt((int)rect["X"], (int)rect["Y"], (int)rect["W"], (int)rect["H"])));
                }
            }

            _network.ServerManager.RegisterBroadcast<QuestAction>(OnAction);
            _network.ClientManager.RegisterBroadcast<QuestView>((view, channel) => Shown?.Invoke(view));
            if (QuestRules.Loaded)
            {
                int runs = 0;
                foreach (JObject q in QuestRules.Quests.Values)
                {
                    runs += QuestRules.Runs(q) ? 1 : 0;
                }

                Debug.Log($"[Game] Quests: {QuestRules.Quests.Count} ({runs} with objectives the game runs), {QuestRules.Questers.Count} quest-givers");
            }
        }

        private bool InRegion(string region, Vector3 position)
        {
            if (string.IsNullOrEmpty(region) || !_regionRects.TryGetValue(region, out var rects))
            {
                return string.IsNullOrEmpty(region);
            }

            Vector2Int t = WorldSpace.ToTile(position);
            foreach ((int facet, RectInt r) in rects)
            {
                if (facet == 1 && t.x >= r.x && t.y >= r.y && t.x < r.xMax && t.y < r.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        private void Send(NetworkConnection connection, byte kind, int npc, JObject json) =>
            _network.ServerManager.Broadcast(connection, new QuestView { Kind = kind, Npc = npc, Json = json.ToString(Newtonsoft.Json.Formatting.None) });

        // ------------------------------------------------------------------ requests

        private void OnAction(NetworkConnection connection, QuestAction request, Channel channel)
        {
            if (!QuestRules.Loaded || !ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            character.Quests ??= new List<AccountStore.QuestState>();
            character.QuestsDone ??= new List<AccountStore.QuestDone>();
            switch (request.Action)
            {
                case 0:
                    Talk(connection, character, body, request.Npc, Tell);
                    break;
                case 1:
                    Accept(connection, character, body, request.Quest, Tell);
                    break;
                case 2:
                    _offers.Remove(character.Id);
                    break;
                case 3:
                    if (character.Quests.RemoveAll(q => q.Quest == request.Quest) > 0)
                    {
                        Tell("You resign from the quest.");
                    }

                    ShowLog(connection, character);
                    break;
                case 4:
                    ShowLog(connection, character);
                    break;
            }
        }

        private static NpcMobile Npc(int id)
        {
            foreach (Combatant c in Combatant.All)
            {
                if (c != null && c.ObjectId == id)
                {
                    return c.GetComponent<NpcMobile>();
                }
            }

            return null;
        }

        /// <summary>Talking to a quest-giver: a delivery for them, a quest of theirs done or under way, or a new one.</summary>
        private void Talk(NetworkConnection connection, AccountStore.Character character, Combatant body, int npcId, Action<string> tell)
        {
            NpcMobile npc = Npc(npcId);
            if (npc == null || npc.Def == null || (npc.transform.position - body.transform.position).magnitude > Reach)
            {
                tell("You are too far away.");
                return;
            }

            string giver = npc.Def.Type;

            // A delivery for them.
            foreach (AccountStore.QuestState state in character.Quests.ToArray())
            {
                JObject q = Quest(state.Quest);
                foreach (JToken o in q?["Objectives"] as JArray ?? new JArray())
                {
                    if ((string)o["Kind"] == "Deliver" && (string)o["Receiver"] == giver &&
                        CraftService.Count(character, (string)o["Type"]) >= ((int?)o["Amount"] ?? 1))
                    {
                        CraftService.Take(character, (string)o["Type"], (int?)o["Amount"] ?? 1);
                        Finish(connection, character, body, q, state, npcId, tell);
                        return;
                    }
                }
            }

            // One of theirs, done or under way.
            foreach (AccountStore.QuestState state in character.Quests.ToArray())
            {
                JObject q = Quest(state.Quest);
                if (q == null || state.Giver != giver && !OfferedBy(q, giver))
                {
                    continue;
                }

                if (Complete(character, q, state))
                {
                    TakeObtained(character, q);
                    Finish(connection, character, body, q, state, npcId, tell);
                    return;
                }

                if (!HasDelivery(q))
                {
                    Send(connection, 1, npcId, View(character, q, state, npc.Name));
                    return;
                }
            }

            // Something new: one of their quests you may take, at random (QuestHelper.RandomQuest).
            JObject quester = QuestRules.Questers.TryGetValue(giver, out JObject qr) ? qr : null;
            var offerable = new List<JObject>();
            foreach (JToken t in quester?["Quests"] as JArray ?? new JArray())
            {
                JObject q = Quest((string)t);
                if (q != null && CanOffer(character, q))
                {
                    offerable.Add(q);
                }
            }

            if (offerable.Count > 0)
            {
                JObject q = offerable[_random.Next(offerable.Count)];
                _offers[character.Id] = ((string)q["Type"], npcId);
                Send(connection, 0, npcId, View(character, q, null, npc.Name));
                return;
            }

            tell(quester?["Quests"] is JArray list && list.Count > 0 ? "I have no further tasks for thee just now." : "Greetings.");
        }

        private static JObject Quest(string type) => type != null && QuestRules.Quests.TryGetValue(type, out JObject q) ? q : null;

        private static bool OfferedBy(JObject q, string giver)
        {
            foreach (JToken t in q["OfferedBy"] as JArray ?? new JArray())
            {
                if ((string)t == giver)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDelivery(JObject q)
        {
            foreach (JToken o in q["Objectives"] as JArray ?? new JArray())
            {
                if ((string)o["Kind"] == "Deliver")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>BaseQuest.CanOffer and the giver's rules: one the game can run, not under way, not done (once) or
        /// still resting, its skill and quest conditions met, and not a later step of a chain (those follow on).</summary>
        private static bool CanOffer(AccountStore.Character c, JObject q)
        {
            string type = (string)q["Type"];
            if (!QuestRules.Runs(q) || c.Quests.Exists(s => s.Quest == type) || q["PrecededBy"] != null && q["PrecededBy"].Type != JTokenType.Null)
            {
                return false;
            }

            AccountStore.QuestDone done = c.QuestsDone.Find(d => d.Quest == type);
            if (done != null)
            {
                if ((bool?)q["DoneOnce"] == true)
                {
                    return false;
                }

                double delay = (double?)q["RestartDelay"] ?? 0;
                if (delay > 0 && (DateTime.UtcNow - new DateTime(done.When, DateTimeKind.Utc)).TotalSeconds < delay)
                {
                    return false;
                }
            }

            foreach (JToken rule in q["CanOffer"] as JArray ?? new JArray())
            {
                switch ((string)rule["Kind"])
                {
                    case "SkillBelow":
                        if (CharacterService.SkillOf(c, (string)rule["Skill"]) >= (double)rule["Value"])
                        {
                            return false;
                        }

                        break;
                    case "SkillAtLeast":
                        if (CharacterService.SkillOf(c, (string)rule["Skill"]) < (double)rule["Value"])
                        {
                            return false;
                        }

                        break;
                    case "NotOnQuest":
                        if (c.Quests.Exists(s => s.Quest == (string)rule["Quest"]))
                        {
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        private void Accept(NetworkConnection connection, AccountStore.Character character, Combatant body, string quest, Action<string> tell)
        {
            if (!_offers.TryGetValue(character.Id, out var offer) || offer.Quest != quest)
            {
                return;
            }

            _offers.Remove(character.Id);
            JObject q = Quest(quest);
            NpcMobile npc = Npc(offer.Npc);
            if (q == null || character.Quests.Exists(s => s.Quest == quest))
            {
                return;
            }

            var state = new AccountStore.QuestState
            {
                Quest = quest, Giver = npc != null && npc.Def != null ? npc.Def.Type : "", Started = DateTime.UtcNow.Ticks,
                Progress = new List<int>(new int[((JArray)q["Objectives"]).Count]),
            };
            character.Quests.Add(state);

            // A delivery: what's to be delivered is given now (DeliverObjective).
            foreach (JToken o in q["Objectives"])
            {
                if ((string)o["Kind"] == "Deliver")
                {
                    ItemService.GiveToBackpack(character, (string)o["Type"], (int?)o["Amount"] ?? 1);
                }
            }

            ItemService.Instance.Resend(connection, character);
            tell("You accept the quest.");
        }

        // ------------------------------------------------------------------ progress

        /// <summary>Server: a creature died (by a player, or a player's pet): slay objectives move on.</summary>
        public void OnKill(Combatant killer, NpcMobile victim)
        {
            if (killer == null || victim?.Def == null || !QuestRules.Loaded)
            {
                return;
            }

            NetworkConnection owner = killer.Owner;
            if ((owner == null || !owner.IsValid) && killer.GetComponent<NpcMobile>() is NpcMobile pet && pet.Master != null)
            {
                owner = pet.Master.Owner; // a pet's kill is its master's
            }

            if (owner == null || !owner.IsValid || !ItemService.Instance.TryCarrier(owner, out AccountStore.Character c, out _) || c.Quests == null)
            {
                return;
            }

            foreach (AccountStore.QuestState state in c.Quests)
            {
                JObject q = Quest(state.Quest);
                var objectives = q?["Objectives"] as JArray;
                for (int i = 0; objectives != null && i < objectives.Count; i++)
                {
                    JToken o = objectives[i];
                    if ((string)o["Kind"] != "Slay" || i >= state.Progress.Count || state.Progress[i] >= ((int?)o["Amount"] ?? 1))
                    {
                        continue;
                    }

                    bool kind = false;
                    foreach (JToken t in o["Creatures"] as JArray ?? new JArray())
                    {
                        kind |= (string)t == victim.Def.Type;
                    }

                    if (kind && InRegion((string)o["Region"], victim.transform.position))
                    {
                        state.Progress[i]++;
                        int amount = (int?)o["Amount"] ?? 1;
                        ItemService.Instance.Tell(owner, state.Progress[i] >= amount
                            ? $"You have slain all you were asked to ({Name(o["Name"])})."
                            : $"Slain: {state.Progress[i]} of {amount} ({Name(o["Name"])}).");
                    }
                }
            }
        }

        private static string Name(JToken t) => t == null ? "" : t.Type == JTokenType.Integer ? $"#{(int)t}" : (string)t;

        /// <summary>How far an objective is: kills counted, items in the backpack, skill reached.</summary>
        private static int Progress(AccountStore.Character c, JToken o, AccountStore.QuestState state, int index)
        {
            switch ((string)o["Kind"])
            {
                case "Slay": return state != null && index < state.Progress.Count ? state.Progress[index] : 0;
                case "Obtain": return CraftService.Count(c, (string)o["Type"]);
                case "Deliver": return 0;
                case "Apprentice": return (int)CharacterService.SkillOf(c, (string)o["Skill"]);
                default: return 0;
            }
        }

        private static int Needed(JToken o) => (string)o["Kind"] == "Apprentice" ? (int?)o["Cap"] ?? 0 : (int?)o["Amount"] ?? 1;

        private static bool Complete(AccountStore.Character c, JObject q, AccountStore.QuestState state)
        {
            var objectives = (JArray)q["Objectives"];
            bool all = (bool?)q["AllObjectives"] != false, any = false, every = true;
            for (int i = 0; i < objectives.Count; i++)
            {
                bool done = (string)objectives[i]["Kind"] != "Deliver" && Progress(c, objectives[i], state, i) >= Needed(objectives[i]);
                any |= done;
                every &= done;
            }

            return all ? every : any;
        }

        private static void TakeObtained(AccountStore.Character c, JObject q)
        {
            foreach (JToken o in q["Objectives"])
            {
                if ((string)o["Kind"] == "Obtain")
                {
                    CraftService.Take(c, (string)o["Type"], (int?)o["Amount"] ?? 1);
                }
            }
        }

        /// <summary>A quest done: its rewards (items and gold), remembered for restarts, and the chain's next quest offered.</summary>
        private void Finish(NetworkConnection connection, AccountStore.Character character, Combatant body, JObject q, AccountStore.QuestState state, int npcId, Action<string> tell)
        {
            character.Quests.Remove(state);
            string type = (string)q["Type"];
            character.QuestsDone.RemoveAll(d => d.Quest == type);
            character.QuestsDone.Add(new AccountStore.QuestDone { Quest = type, When = DateTime.UtcNow.Ticks });
            foreach (JToken r in q["Rewards"] as JArray ?? new JArray())
            {
                string kind = (string)r["Kind"];
                if ((kind == "Item" || kind == "Gold") && !string.IsNullOrEmpty((string)r["Type"]))
                {
                    if (!ItemService.GiveToBackpack(character, (string)r["Type"], (int?)r["Amount"] ?? 1))
                    {
                        ItemService.GiveToBank(character, (string)r["Type"], (int?)r["Amount"] ?? 1);
                        tell("Your backpack is full: a reward went to your bank box.");
                    }
                }
            }

            ItemService.Instance.Resend(connection, character);
            NpcMobile npc = Npc(npcId);
            Send(connection, 2, npcId, View(character, q, null, npc != null ? npc.Name : ""));
            SkillService.Instance?.MarkDirty(body);

            string next = (string)q["NextQuest"];
            JObject nextQuest = Quest(next);
            if (nextQuest != null && QuestRules.Runs(nextQuest) && !character.Quests.Exists(s => s.Quest == next))
            {
                _offers[character.Id] = (next, npcId); // the chain goes on
                Send(connection, 0, npcId, View(character, nextQuest, null, npc != null ? npc.Name : ""));
            }
        }

        // ------------------------------------------------------------------ what the window shows

        private static JObject View(AccountStore.Character c, JObject q, AccountStore.QuestState state, string giver)
        {
            var objectives = new JArray();
            var list = (JArray)q["Objectives"];
            for (int i = 0; i < list.Count; i++)
            {
                JToken o = list[i];
                objectives.Add(new JObject
                {
                    ["Kind"] = o["Kind"], ["Name"] = o["Name"], ["Region"] = o["Region"], ["Skill"] = o["Skill"], ["Receiver"] = o["ReceiverName"] ?? o["Receiver"],
                    ["Have"] = Math.Min(Progress(c, o, state, i), Needed(o)), ["Need"] = Needed(o),
                });
            }

            var rewards = new JArray();
            foreach (JToken r in q["Rewards"] as JArray ?? new JArray())
            {
                rewards.Add(new JObject { ["Name"] = r["Name"], ["Amount"] = r["Amount"] });
            }

            return new JObject
            {
                ["Quest"] = q["Type"], ["Giver"] = giver, ["Title"] = q["Title"], ["Description"] = q["Description"], ["Uncomplete"] = q["Uncomplete"],
                ["Complete"] = q["Complete"], ["TitleText"] = q["Texts"]?["Title"], ["Objectives"] = objectives, ["Rewards"] = rewards,
            };
        }

        private void ShowLog(NetworkConnection connection, AccountStore.Character character)
        {
            var quests = new JArray();
            foreach (AccountStore.QuestState state in character.Quests)
            {
                JObject q = Quest(state.Quest);
                if (q != null)
                {
                    quests.Add(View(character, q, state, ""));
                }
            }

            Send(connection, 3, 0, new JObject { ["Quests"] = quests });
        }
    }
}
