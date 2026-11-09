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
        public Style[] Hair;
        public Style[] Beards;
        public Outfit[] Outfits;
    }
}
