using System;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// The modular pieces UO walls are built from. Panels are one tile (1.25 m) wide and are fitted
    /// to their tile edge; their pivot and size are read from the meshes, so any wall-shaped prefab works.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Wall Kit", fileName = "WallKit")]
    public sealed class WallKit : ScriptableObject
    {
        public enum Style
        {
            Plaster,
            Timber,
            Stone,
        }

        [Header("Panels (one tile wide; variants are picked per tile)")]
        public GameObject[] PlasterPanels = Array.Empty<GameObject>();
        public GameObject[] TimberPanels = Array.Empty<GameObject>();
        [Tooltip("Stone panels are stacked to reach the wall's height instead of being stretched.")]
        public GameObject[] StonePanels = Array.Empty<GameObject>();
        public GameObject[] WindowPanels = Array.Empty<GameObject>();

        [Header("Posts and blocks")]
        public GameObject TimberPost;
        public GameObject StonePost;
        [Tooltip("Fills a whole tile, for thick castle walls.")]
        public GameObject StoneBlock;
        [Tooltip("Post footprint in metres; posts are scaled to this width.")]
        public float PostWidth = 0.35f;

        [Header("Roofs (slope pieces run downhill along local -X; caps run along local Z)")]
        public GameObject TileRoofSlope;
        public GameObject TileRoofCap;
        public GameObject ThatchRoofSlope;
        public GameObject ThatchRoofCap;

        private static readonly string[] ThatchWords = { "thatch", "hay", "palm", "leaf", "leafed", "bark", "frond" };

        /// <summary>True for thatch-like UO roof names; everything else uses clay tiles.</summary>
        public static bool IsThatch(string name) => Contains(name, ThatchWords);

        private static readonly string[] StoneWords = { "stone", "brick", "marble", "rock", "granite", "sandstone", "cave" };
        private static readonly string[] TimberWords = { "wood", "log", "timber", "palisade", "fence", "plank" };

        public static Style StyleFor(string name)
        {
            if (Contains(name, StoneWords))
            {
                return Style.Stone;
            }

            return Contains(name, TimberWords) ? Style.Timber : Style.Plaster;
        }

        public GameObject[] PanelsFor(Style style, bool window)
        {
            if (window && WindowPanels.Length > 0)
            {
                return WindowPanels;
            }

            switch (style)
            {
                case Style.Stone: return StonePanels.Length > 0 ? StonePanels : PlasterPanels;
                case Style.Timber: return TimberPanels.Length > 0 ? TimberPanels : PlasterPanels;
                default: return PlasterPanels;
            }
        }

        public GameObject PostFor(Style style) => style == Style.Stone && StonePost != null ? StonePost : TimberPost;

        private static bool Contains(string name, string[] words)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (string word in words)
            {
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
