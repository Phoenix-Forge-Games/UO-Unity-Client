using System.Collections.Generic;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// The world's light fixtures (Resources/World/RoomLights, set in the inspector): which fixture prefabs
    /// (<see cref="LampFixture"/>, Assets/Synty/UOLit/Fixtures) furnish rooms, doors and streets, and for every lamp prop
    /// where its flames are (the fixtures were made from these; baked props lit by name use them too). A point light shines from the middle of a fixture's flames (the tips
    /// of its candles' wicks, the inside of a lantern's glass, the bowl of a torch), and each flame is a particle effect
    /// there, never a modelled one: <see cref="LightFixtures"/> puts them on.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Game/Room Light Kit")]
    public sealed class RoomLightKit : ScriptableObject
    {
        public enum FlameKind
        {
            Candle,  // a candle's wick: a small flame
            Lantern, // a flame behind glass: a candle flame, a little bigger
            Torch,   // an open torch or brazier bowl: fire
        }

        [System.Serializable]
        public sealed class Fixture
        {
            [Tooltip("The lamp prop (its name also matches baked world props: name or name(Clone)).")]
            public GameObject Prefab;

            [Tooltip("Each flame, in the prop's own space (metres from its origin): a wick tip, a lantern's middle, a torch's bowl.")]
            public Vector3[] Flames = new Vector3[0];

            public FlameKind Kind;

            [Tooltip("Size of its flame effects (1 = as made).")]
            public float FlameScale = 1f;
        }

        [Header("Flames")]
        public GameObject CandleFlame;
        public GameObject TorchFlame;

        [Header("Rooms")]
        [Tooltip("Fixtures for tables: one is set on the table nearest the room's middle (base at the origin).")]
        public GameObject[] TableLights = new GameObject[0];

        [Tooltip("Fixture stood in a free corner of a room with no table.")]
        public GameObject CornerLight;

        [Header("Outside")]
        [Tooltip("Fixture hung beside every outside door: its origin on the wall, the lantern out along +Z.")]
        public GameObject DoorLantern;

        [Tooltip("Street lamp fixture: its origin at the foot of the post, the lantern hanging out along +Z.")]
        public GameObject StreetLamp;

        [System.Serializable]
        public struct Anchor
        {
            [Tooltip("Where the street lamp stands (world metres; its foot is set on the ground).")]
            public Vector3 Position;

            [Tooltip("Which way its lantern hangs out (degrees about up; 0 = world +Z).")]
            public float Yaw;
        }

        [Header("Street lamps (seeded by the bake)")]
        [Tooltip("Housing.json and Regions.json: which land is road, and the named towns (when the bake runs without the game's rules loaded).")]
        public TextAsset Housing;
        public TextAsset Regions;

        [Tooltip("Named regions that are cities (densest lamps); other named towns are villages.")]
        public string[] Cities = { "Britain", "Trinsic", "Vesper", "Minoc", "Jhelom", "Moonglow", "Nujel'm", "Magincia", "Serpent's Hold" };

        [Tooltip("At most one street lamp in each square this many tiles across (a divisor of the 64-tile chunk): cities, villages, the wilds.")]
        public int CitySpacing = 8;
        public int VillageSpacing = 16;
        public int WildSpacing = 64;

        [Tooltip("Street lamps placed exactly (the one behind the menu's door shot); the square round each takes no other.")]
        public Anchor[] Anchors = new Anchor[0];

        [Tooltip("Map statics the bake leaves out, by Synty prop name prefix: UO's own lamps, replaced by the seeded ones.")]
        public string[] DropProps = { "SM_Prop_Street_Lamp", "SM_Prop_Torch_01" };

        [Header("Every lamp prop's flames")]
        public List<Fixture> Fixtures = new List<Fixture>();

        /// <summary>The fixture for a prop by its (instance) name, if its flames are known.</summary>
        public Fixture Find(string name)
        {
            if (name.EndsWith("(Clone)", System.StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
            }

            foreach (Fixture f in Fixtures)
            {
                if (f.Prefab != null && f.Prefab.name == name)
                {
                    return f;
                }
            }

            return null;
        }
    }
}
