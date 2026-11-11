using System;
using UnityEngine;

namespace UOClient
{
    /// <summary>What a Sidekick outfit (or a UO item) is made of; UO items pick outfits of the same kind.</summary>
    public enum OutfitKind
    {
        Cloth,
        Fine,
        Leather,
        Studded,
        Ring,
        Chain,
        Plate,
        Bone,
        Robe,
    }

    /// <summary>What an outfit's head attachment is, so UO helmets and hats find a matching one.</summary>
    public enum HeadKind
    {
        None,
        Hat,
        Hood,
        Cap,
        OpenHelm,
        ClosedHelm,
        Crown,
    }

    /// <summary>How a held item sits in the hand.</summary>
    public enum Grip
    {
        /// <summary>Right hand, pointing forward and a little up out of the fist (swords, axes, maces, daggers).</summary>
        Blade,
        /// <summary>Right hand, carried level and pointing forward, held at the middle (staffs, spears, polearms, tools).</summary>
        Upright,
        /// <summary>Left forearm, facing front-left.</summary>
        Shield,
        /// <summary>Left hand, level, string up toward the armpit (bows).</summary>
        Bow,
    }

    /// <summary>
    /// The Sidekick parts the client dresses humanoids from: a naked human, face variants, hair and beard
    /// styles keyed by UO graphic, and whole outfits tagged by what they're made of. Filled from the
    /// imported Sidekick packs; <see cref="SidekickDresser"/> turns a mobile's equipment into a model.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Sidekick Wardrobe")]
    public sealed class SidekickWardrobe : ScriptableObject
    {
        [Serializable]
        public sealed class Outfit
        {
            [Tooltip("Pack, outfit and number, e.g. FANT_KNGT_01.")]
            public string Id;
            public OutfitKind Kind;
            [Tooltip("What its head attachment is, if it has one.")]
            public HeadKind Head;
            [Tooltip("Its hips part hangs to the knees or lower (robes, long skirts, tabards).")]
            public bool LongHips;
            [Tooltip("Its back attachment is a cape or cloak (not a quiver, pack or shield).")]
            public bool Cape;
            [Tooltip("Made for elves: elf bodies prefer these, humans skip them.")]
            public bool Elven;
            public GameObject[] Parts;
        }

        [Serializable]
        public sealed class Style
        {
            [Tooltip("The UO hair or beard graphic.")]
            public ushort Graphic;
            [Tooltip("Sidekick parts for it (hair, or facial hair).")]
            public GameObject[] Parts;
        }

        [Tooltip("SK_BaseModel: the rig and humanoid avatar every part is bound to.")]
        public GameObject BaseModel;
        [Tooltip("A Sidekick material; each character gets a copy with its own colour map.")]
        public Material Material;
        [Tooltip("Colour maps of the ready-made characters, used as starting palettes.")]
        public Texture2D[] Schemes;
        [Tooltip("A complete unclothed human: head, eyes, teeth, tongue and body parts.")]
        public GameObject[] Body;
        [Tooltip("Nose, ear and eyebrow variants, picked per character.")]
        public GameObject[] Faces;
        [Tooltip("A complete unclothed elf, for elf bodies (605-608).")]
        public GameObject[] ElfBody;
        [Tooltip("Elf nose and ear variants.")]
        public GameObject[] ElfFaces;
        [Serializable]
        public sealed class Held
        {
            [Tooltip("Words in the UO item name, separated by |; the first entry that matches wins.")]
            public string Names;
            [Tooltip("Words that rule an item out (|-separated).")]
            public string Exclude;
            public Grip Grip;
            [Tooltip("Slides the item along its own axes so the fist closes on its handle (metres; +Y towards the blade).")]
            public Vector3 Offset;
            [Tooltip("Extra turn in the hand (degrees, the item's own axes), for a model that sits differently.")]
            public Vector3 Turn;
            [Tooltip("Models to pick from, the same one for every copy of an item.")]
            public GameObject[] Prefabs;
        }

        [Header("Gargoyles")]
        [Tooltip("The rigged wing (Wing_Gargoyle_Rig); skinned to the Sidekick skin cells so skin hue tints it. One is placed per side, the left mirrored.")]
        public GameObject GargoyleWing;
        [Tooltip("Drives the wing: Spread (open) and Flap (beating) bools.")]
        public RuntimeAnimatorController GargoyleWingController;
        [Tooltip("Wing size next to the Synty gargoyle it came from (a 2.2 m monster) for a 1.8 m Sidekick.")]
        public float GargoyleWingScale = 0.85f;

        public Style[] Hair;
        public Style[] Beards;
        public Outfit[] Outfits;
        [Tooltip("Weapons, shields and tools held in the hands (UO layers 1 and 2), matched by item name.")]
        public Held[] Weapons;
    }
}
