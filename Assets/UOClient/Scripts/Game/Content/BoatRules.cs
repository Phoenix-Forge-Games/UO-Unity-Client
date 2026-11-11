using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// Ships as data (Content/Boats.json, migrated by Tools/Data/export_servuo_boats.py), the same on server and client:
    /// each ship's deed, docked item, multi IDs and footprint per heading, and the sailing speeds.
    /// </summary>
    public static class BoatRules
    {
        public sealed class BoatDef
        {
            public int Index;
            public string Deed, Boat, Docked;
            public int Name, NorthId, Width, Length;
            /// <summary>Footprint per heading (north, east, south, west): tiles from the centre tile (minX, minY, maxX, maxY).</summary>
            public RectInt[] Bounds = new RectInt[4];
        }

        public static readonly List<BoatDef> Boats = new List<BoatDef>();
        public static int FastMs = 250, NormalMs = 500, SlowMs = 1000;
        public static float BoardReach = 8, ShoreSearch = 10, DeckLift = 0.3f;
        public static int CoastReach = 6, PlaceReach = 30;
        public static float Accel = 2f, Brake = 3f, ReverseShare = 0.35f, TurnScale = 400f, TurnMin = 10f, TurnMax = 45f, StillTurnShare = 0.3f;

        /// <summary>Full speed, metres a second: a tile every FastMs.</summary>
        public static float MaxSpeed => UOClient.WorldSpace.TileSize * 1000f / Mathf.Max(1, FastMs);

        /// <summary>How fast a ship turns at speed (degrees a second): smaller ships turn quicker.</summary>
        public static float TurnRate(BoatDef def) => Mathf.Clamp(TurnScale / Mathf.Max(1, def.Length), TurnMin, TurnMax);

        public static void Load(TextAsset boats)
        {
            Boats.Clear();
            if (boats == null)
            {
                return;
            }

            JObject root = JObject.Parse(boats.text);
            FastMs = (int?)root["Speeds"]?["FastMs"] ?? FastMs;
            NormalMs = (int?)root["Speeds"]?["NormalMs"] ?? NormalMs;
            SlowMs = (int?)root["Speeds"]?["SlowMs"] ?? SlowMs;
            CoastReach = (int?)root["Rules"]?["CoastReach"] ?? CoastReach;
            JToken s = root["Sailing"];
            Accel = (float?)s?["Accel"] ?? Accel;
            Brake = (float?)s?["Brake"] ?? Brake;
            ReverseShare = (float?)s?["ReverseShare"] ?? ReverseShare;
            TurnScale = (float?)s?["TurnScale"] ?? TurnScale;
            TurnMin = (float?)s?["TurnMin"] ?? TurnMin;
            TurnMax = (float?)s?["TurnMax"] ?? TurnMax;
            StillTurnShare = (float?)s?["StillTurnShare"] ?? StillTurnShare;
            PlaceReach = (int?)root["Rules"]?["PlaceReach"] ?? PlaceReach;
            BoardReach = (float?)root["Rules"]?["BoardReach"] ?? BoardReach;
            ShoreSearch = (float?)root["Rules"]?["ShoreSearch"] ?? ShoreSearch;
            DeckLift = (float?)root["Rules"]?["DeckLift"] ?? DeckLift;
            foreach (JToken b in root["Boats"] as JArray ?? new JArray())
            {
                var def = new BoatDef
                {
                    Index = Boats.Count, Deed = (string)b["Deed"], Boat = (string)b["Boat"], Docked = (string)b["Docked"],
                    Name = (int?)b["Name"] ?? 0, NorthId = (int)b["NorthId"], Width = (int)b["Width"], Length = (int)b["Length"],
                };
                var bounds = b["Bounds"] as JArray;
                for (int h = 0; h < 4 && bounds != null && h < bounds.Count; h++)
                {
                    var r = (JArray)bounds[h];
                    def.Bounds[h] = new RectInt((int)r[0], (int)r[1], (int)r[2] - (int)r[0], (int)r[3] - (int)r[1]);
                }

                Boats.Add(def);
            }
        }

        public static BoatDef ByItem(string type) => Boats.Find(b => b.Deed == type || b.Docked == type);
        public static BoatDef Get(int index) => index >= 0 && index < Boats.Count ? Boats[index] : null;

        /// <summary>A heading's direction in the world: 0 north (+Z), 1 east (+X), 2 south, 3 west.</summary>
        public static Vector3 Direction(int heading)
        {
            switch (((heading % 4) + 4) % 4)
            {
                case 0: return Vector3.forward;
                case 1: return Vector3.right;
                case 2: return Vector3.back;
                default: return Vector3.left;
            }
        }
    }
}
