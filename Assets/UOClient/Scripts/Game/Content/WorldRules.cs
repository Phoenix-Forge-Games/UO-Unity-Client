using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// The world's rules as data (Content/World.json), the same on server and client: UO's clock (a day is two real
    /// hours, later the further east you are), its light levels by hour and in dungeons, Night Sight's length, and this
    /// game's swimming and falling.
    /// </summary>
    public static class WorldRules
    {
        public static DateTime WorldStart = new DateTime(1997, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        public static double SecondsPerMinute = 5;
        public static int FacetMinutes = 320, TilesPerMinute = 16;
        public static int DayLevel, NightLevel = 12, DungeonLevel = 26, Darkest = 30;
        public static int NightFrom, NightTo = 4, DawnTo = 6, DuskFrom = 22;
        public static int NightSightMin = 15, NightSightMax = 25;
        /// <summary>The sky's day: sunrise and sunset (UO hours) and the sun's height at midday (degrees).</summary>
        public static float SunRise = 6f, SunSet = 20f, SunMaxElevation = 62f;
        public static float SwimDepth = 1.25f, SwimRun = 3.5f, SwimWalk = 2f, SwimRise = 4f;
        public static float FallSafe = 5f, FallPercentPerMetre = 4.5f;

        public static void Load(TextAsset world)
        {
            if (world == null)
            {
                return;
            }

            JObject root = JObject.Parse(world.text);
            JToken c = root["Clock"], l = root["Light"], s = root["Swim"], f = root["Fall"];
            if (c?["WorldStart"] != null)
            {
                WorldStart = ((DateTime)c["WorldStart"]).ToUniversalTime();
            }

            SecondsPerMinute = (double?)c?["SecondsPerMinute"] ?? SecondsPerMinute;
            FacetMinutes = (int?)c?["FacetMinutes"] ?? FacetMinutes;
            TilesPerMinute = Math.Max(1, (int?)c?["TilesPerMinute"] ?? TilesPerMinute);
            DayLevel = (int?)l?["Day"] ?? DayLevel;
            NightLevel = (int?)l?["Night"] ?? NightLevel;
            DungeonLevel = (int?)l?["Dungeon"] ?? DungeonLevel;
            Darkest = (int?)l?["Darkest"] ?? Darkest;
            NightFrom = (int?)l?["NightFrom"] ?? NightFrom;
            NightTo = (int?)l?["NightTo"] ?? NightTo;
            DawnTo = (int?)l?["DawnTo"] ?? DawnTo;
            DuskFrom = (int?)l?["DuskFrom"] ?? DuskFrom;
            if (root["NightSight"]?["Minutes"] is JArray ns && ns.Count == 2)
            {
                NightSightMin = (int)ns[0];
                NightSightMax = (int)ns[1];
            }

            JToken sun = root["Sun"];
            SunRise = (float?)sun?["Rise"] ?? SunRise;
            SunSet = Mathf.Max(SunRise + 1f, (float?)sun?["Set"] ?? SunSet);
            SunMaxElevation = (float?)sun?["MaxElevation"] ?? SunMaxElevation;

            SwimDepth = (float?)s?["Depth"] ?? SwimDepth;
            SwimRun = (float?)s?["RunSpeed"] ?? SwimRun;
            SwimWalk = (float?)s?["WalkSpeed"] ?? SwimWalk;
            SwimRise = (float?)s?["Rise"] ?? SwimRise;
            FallSafe = (float?)f?["SafeMetres"] ?? FallSafe;
            FallPercentPerMetre = (float?)f?["PercentPerMetre"] ?? FallPercentPerMetre;

            JToken lights = root["Lights"];
            MaxLitProps = (int?)lights?["MaxLit"] ?? MaxLitProps;
            PropLights.Clear();
            foreach (JToken p in lights?["Props"] as JArray ?? new JArray())
            {
                PropLights.Add(new PropLight
                {
                    Match = (string)p["Match"] ?? "",
                    Range = (float?)p["Range"] ?? 6f,
                    Intensity = (float?)p["Intensity"] ?? 2f,
                    Flicker = (float?)p["Flicker"] ?? 0f,
                    Top = (float?)p["Top"] ?? 0.2f,
                });
            }

            JToken indoor = lights?["Indoor"];
            IndoorLight = new PropLight
            {
                Match = "",
                Range = (float?)indoor?["MaxRange"] ?? 9f,
                Intensity = (float?)indoor?["Intensity"] ?? 1.8f,
                Flicker = (float?)indoor?["Flicker"] ?? 0.6f,
                Top = (float?)indoor?["Height"] ?? 1.9f,
            };
            IndoorDayShare = (float?)indoor?["DayShare"] ?? IndoorDayShare;

            JToken w = root["Weather"];
            WeatherPeriodMinutes = Math.Max(1, (int?)w?["PeriodMinutes"] ?? WeatherPeriodMinutes);
            WeatherBlendSeconds = Mathf.Max(0.1f, (float?)w?["BlendSeconds"] ?? WeatherBlendSeconds);
            WeatherSeed = (int?)w?["Seed"] ?? WeatherSeed;
            WeatherForce = (string)w?["Force"] ?? "";
            Weathers.Clear();
            foreach (JProperty k in (w?["Kinds"] as JObject ?? new JObject()).Properties())
            {
                JToken v = k.Value;
                Weathers.Add(new Weather
                {
                    Name = k.Name,
                    Chance = (float?)v["Chance"] ?? 0f,
                    Clouds = (float?)v["Clouds"] ?? 0f,
                    CloudDark = (float?)v["CloudDark"] ?? 0f,
                    Fog = (float?)v["Fog"] ?? 0f,
                    Rain = (float?)v["Rain"] ?? 0f,
                    Snow = (float?)v["Snow"] ?? 0f,
                    Wind = (float?)v["Wind"] ?? 0f,
                    Lightning = (float?)v["Lightning"] ?? 0f,
                    Light = (float?)v["Light"] ?? 1f,
                });
            }
        }

        // ------------------------------------------------------------------ lights in the world (World.json Lights)

        /// <summary>A kind of lamp prop in the baked world and the light it gives (Top: metres below its top).</summary>
        public sealed class PropLight
        {
            public string Match;
            public float Range, Intensity, Flicker, Top;
        }

        public static readonly List<PropLight> PropLights = new List<PropLight>();
        public static int MaxLitProps = 24;
        /// <summary>The light in every room of every building (Range: the most it reaches; Top: height above the floor).</summary>
        public static PropLight IndoorLight = new PropLight { Match = "", Range = 9f, Intensity = 1.8f, Flicker = 0.6f, Top = 1.9f };
        /// <summary>How bright room lights are by day, as a share of their night brightness (interiors are dim then too).</summary>
        public static float IndoorDayShare = 0.4f;

        // ------------------------------------------------------------------ weather (World.json Weather)

        /// <summary>A kind of weather (and, blended, the weather now): every value 0..1.</summary>
        public sealed class Weather
        {
            public string Name;
            public float Chance, Clouds, CloudDark, Fog, Rain, Snow, Wind, Lightning, Light = 1f;

            public static Weather Lerp(Weather a, Weather b, float t) => new Weather
            {
                Name = t < 0.5f ? a.Name : b.Name,
                Clouds = Mathf.Lerp(a.Clouds, b.Clouds, t),
                CloudDark = Mathf.Lerp(a.CloudDark, b.CloudDark, t),
                Fog = Mathf.Lerp(a.Fog, b.Fog, t),
                Rain = Mathf.Lerp(a.Rain, b.Rain, t),
                Snow = Mathf.Lerp(a.Snow, b.Snow, t),
                Wind = Mathf.Lerp(a.Wind, b.Wind, t),
                Lightning = Mathf.Lerp(a.Lightning, b.Lightning, t),
                Light = Mathf.Lerp(a.Light, b.Light, t),
            };
        }

        public static readonly List<Weather> Weathers = new List<Weather>();
        public static int WeatherPeriodMinutes = 120, WeatherSeed = 1997;
        public static float WeatherBlendSeconds = 90f;
        public static string WeatherForce = "";

        private static readonly Weather ClearSky = new Weather { Name = "Clear", Clouds = 0.1f, Wind = 0.2f, Light = 1f };

        /// <summary>The kind for one period: picked by chance from the period's number alone (the same everywhere).</summary>
        public static Weather WeatherOfPeriod(long period)
        {
            if (!string.IsNullOrEmpty(WeatherForce))
            {
                return Weathers.Find(k => string.Equals(k.Name, WeatherForce, StringComparison.OrdinalIgnoreCase)) ?? ClearSky;
            }

            float total = 0f;
            foreach (Weather k in Weathers)
            {
                total += Mathf.Max(0f, k.Chance);
            }

            if (total <= 0f)
            {
                return ClearSky;
            }

            // A small integer hash of the period and seed, as a fraction 0..1.
            ulong x = unchecked((ulong)period * 0x9E3779B97F4A7C15UL ^ (ulong)WeatherSeed * 0xBF58476D1CE4E5B9UL);
            x ^= x >> 31;
            x = unchecked(x * 0x94D049BB133111EBUL);
            x ^= x >> 29;
            float roll = (float)((x >> 11) * (1.0 / (1UL << 53))) * total;
            foreach (Weather k in Weathers)
            {
                roll -= Mathf.Max(0f, k.Chance);
                if (roll < 0f)
                {
                    return k;
                }
            }

            return Weathers[Weathers.Count - 1];
        }

        /// <summary>The weather now: this period's kind, changing from the last one over BlendSeconds.</summary>
        public static Weather WeatherAt(DateTime utc)
        {
            double minutes = (utc - WorldStart).TotalSeconds / SecondsPerMinute;
            long period = (long)Math.Floor(minutes / WeatherPeriodMinutes);
            double secondsIn = (minutes - period * (double)WeatherPeriodMinutes) * SecondsPerMinute;
            Weather now = WeatherOfPeriod(period);
            if (!string.IsNullOrEmpty(WeatherForce) || secondsIn >= WeatherBlendSeconds)
            {
                return now;
            }

            return Weather.Lerp(WeatherOfPeriod(period - 1), now, Mathf.SmoothStep(0f, 1f, (float)(secondsIn / WeatherBlendSeconds)));
        }

        /// <summary>
        /// Where the sun is at an hour: its height above the horizon and its compass bearing (degrees; 90 east, 180
        /// south, 270 west). By day it climbs from the east to SunMaxElevation at midday and sets in the west; by night it
        /// carries on round under the world, back to the east for the next sunrise.
        /// </summary>
        public static (float Elevation, float Azimuth) SunAt(double hour)
        {
            float h = (float)hour, dayLength = SunSet - SunRise;
            float a; // 0 at sunrise, 180 at sunset, 360 at the next sunrise
            if (h >= SunRise && h <= SunSet)
            {
                a = (h - SunRise) / dayLength * 180f;
            }
            else
            {
                float since = h > SunSet ? h - SunSet : h + 24f - SunSet;
                a = 180f + since / (24f - dayLength) * 180f;
            }

            float rad = a * Mathf.Deg2Rad;
            return (Mathf.Sin(rad) * SunMaxElevation, 90f + a); // the bearing turns east, south, west, north
        }

        /// <summary>The moon's phase now (0 new, 0.5 full): UO's eight-day cycle.</summary>
        public static float MoonPhase(DateTime utc) =>
            (float)((utc - WorldStart).TotalSeconds / SecondsPerMinute / 1440.0 / 8.0 % 1.0);

        /// <summary>UO's time of day at a tile (Clock.GetTime): hours as a fraction (0..24).</summary>
        public static double HourAt(int facet, int tileX) => HourAt(DateTime.UtcNow, facet, tileX);

        public static double HourAt(DateTime utc, int facet, int tileX)
        {
            double minutes = (utc - WorldStart).TotalSeconds / SecondsPerMinute + facet * FacetMinutes + tileX / TilesPerMinute;
            return minutes / 60.0 % 24.0;
        }

        /// <summary>UO's light level for an hour (LightCycle.ComputeLevelFor): night, a two-hour dawn, day, a
        /// two-hour dusk.</summary>
        public static float LevelAt(double hour)
        {
            if (hour < NightTo && hour >= NightFrom)
            {
                return NightLevel;
            }

            if (hour < DawnTo)
            {
                return Mathf.Lerp(NightLevel, DayLevel, (float)((hour - NightTo) / (DawnTo - NightTo)));
            }

            if (hour < DuskFrom)
            {
                return DayLevel;
            }

            return Mathf.Lerp(DayLevel, NightLevel, (float)((hour - DuskFrom) / (24 - DuskFrom)));
        }

        /// <summary>A clock reading: "3:15 PM".</summary>
        public static string Clock(double hour)
        {
            int h = (int)hour, m = (int)((hour - h) * 60);
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
        }
    }
}
