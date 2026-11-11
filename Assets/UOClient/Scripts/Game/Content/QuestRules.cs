using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// Quests as data (Content/Quests.json, migrated by Tools/Data/export_servuo_quests.py from ServUO's Mondain's
    /// Legacy quest engine), the same on server and client: each quest's texts (cliloc numbers or words), objectives,
    /// rewards, chain and restart rules, and each quest-giver's quests.
    /// </summary>
    public static class QuestRules
    {
        public static readonly Dictionary<string, JObject> Quests = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public static readonly Dictionary<string, JObject> Questers = new Dictionary<string, JObject>(StringComparer.Ordinal);

        /// <summary>The objective kinds the game runs so far; a quest with any other kind isn't offered yet.</summary>
        public static readonly HashSet<string> Supported = new HashSet<string> { "Slay", "Obtain", "Deliver", "Apprentice" };

        public static bool Loaded => Quests.Count > 0;

        public static void Load(TextAsset file)
        {
            Quests.Clear();
            Questers.Clear();
            _receivers.Clear();
            if (file == null)
            {
                return;
            }

            JObject root = JObject.Parse(file.text);
            foreach (JToken q in root["Quests"] as JArray ?? new JArray())
            {
                if (q is JObject o && (string)o["Type"] != null)
                {
                    Quests[(string)o["Type"]] = o;
                }
            }

            foreach (JToken q in root["Questers"] as JArray ?? new JArray())
            {
                if (q is JObject o && (string)o["Type"] != null)
                {
                    Questers[(string)o["Type"]] = o;
                }
            }
        }

        public static bool IsQuester(string creatureType) => creatureType != null && Questers.TryGetValue(creatureType, out JObject q) &&
                                                            ((q["Quests"] as JArray)?.Count > 0 || ReceivesDeliveries(creatureType));

        private static readonly Dictionary<string, bool> _receivers = new Dictionary<string, bool>();

        /// <summary>Does anyone deliver to this quest-giver?</summary>
        public static bool ReceivesDeliveries(string type)
        {
            if (_receivers.TryGetValue(type, out bool known))
            {
                return known;
            }

            bool found = false;
            foreach (JObject q in Quests.Values)
            {
                foreach (JToken o in q["Objectives"] as JArray ?? new JArray())
                {
                    found |= (string)o["Kind"] == "Deliver" && (string)o["Receiver"] == type;
                }
            }

            _receivers[type] = found;
            return found;
        }

        /// <summary>Can the game run every objective of this quest?</summary>
        public static bool Runs(JObject quest)
        {
            foreach (JToken o in quest["Objectives"] as JArray ?? new JArray())
            {
                if (!Supported.Contains((string)o["Kind"]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
