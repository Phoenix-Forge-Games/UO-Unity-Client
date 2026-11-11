using System;
using UnityEngine;

namespace UOClient
{
    /// <summary>How a prop is sized and turned to fit the UO tiles it stands for.</summary>
    public enum PropFit
    {
        /// <summary>One prop per tile at the prefab's own (real-world) size, shrunk only if it overflows the tile.</summary>
        Natural,
        /// <summary>A row of same-named tiles (a table, a counter, a bed) becomes one prop stretched along it.</summary>
        Group,
        /// <summary>Lies along the tile edge its UO art stands on, like a wall (fences, railings).</summary>
        Edge,
        /// <summary>
        /// Stretched to fill the tile (times <see cref="PropCatalog.Entry.Footprint"/>) and exactly the UO tile's
        /// height: raised stone and wood platforms, stair steps, posts and piers, which are solid geometry.
        /// </summary>
        Block,
        /// <summary>
        /// A stair step: a flight scaled to one tile and the tile's rise, climbing toward the higher
        /// neighbouring surface, so a run of UO stair tiles reads as one staircase.
        /// </summary>
        Stair,
        /// <summary>A low wall along the tile edge its art stands on, reaching down to the ground (planter fronts, garden walls).</summary>
        LowWall,
        /// <summary>Hung flat against the wall beside its tile, its bottom at the tile's Z (banners, paintings).</summary>
        Hang,
        /// <summary>
        /// Mounted on the wall beside its tile and sticking straight out from it, just under the wall's top: a
        /// shop's sign bracket (UO's "signpost", which the shop sign hangs from). The prefab's pivot is its wall
        /// plate and its arm runs along local -x (Synty's sign brackets).
        /// </summary>
        Bracket,
    }

    /// <summary>
    /// Synty props for UO's furniture and clutter, matched by the UO tile name (tiledata), for both map statics
    /// and server items. Anything not matched stays a placeholder box. Editable in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Prop Catalog", fileName = "PropCatalog")]
    public sealed class PropCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Lower-case fragments of the UO tile name; any one matches. A fragment starting with '=' must be the whole name.")]
            public string[] Names = new string[0];
            [Tooltip("Lower-case fragments that rule a name out (e.g. 'chest of drawers' for chests).")]
            public string[] Exclude = new string[0];
            public GameObject[] Prefabs = new GameObject[0];
            public PropFit Fit = PropFit.Natural;
            [Tooltip("Turn the prop's back to a wall beside its tile (shelves, wardrobes, cabinets).")]
            public bool AgainstWall;
            [Tooltip("Turn the prop to face a table or counter beside its tile (chairs, stools); falls back to the wall rule.")]
            public bool FaceTable;
            [Tooltip("Block fit: share of the tile the block covers (1 = the whole tile; posts are thinner).")]
            [Range(0.1f, 1f)]
            public float Footprint = 1f;
            [Tooltip("Only tiles at most this tall (UO Z units) match; 0 = any height. Short walls become low walls this way.")]
            public int MaxHeight;
        }

        public Entry[] Entries = new Entry[0];

        /// <summary>The first entry whose names match (and whose height limit the tile is within), or null.</summary>
        public Entry Match(string tileName, int height = 0)
        {
            if (string.IsNullOrEmpty(tileName))
            {
                return null;
            }

            string name = tileName.ToLowerInvariant();
            foreach (Entry entry in Entries)
            {
                if (entry.Prefabs == null || entry.Prefabs.Length == 0 || !Any(name, entry.Names) || Any(name, entry.Exclude) ||
                    (entry.MaxHeight > 0 && height > entry.MaxHeight))
                {
                    continue;
                }

                return entry;
            }

            return null;
        }

        private static bool Any(string name, string[] fragments)
        {
            if (fragments == null)
            {
                return false;
            }

            foreach (string fragment in fragments)
            {
                if (string.IsNullOrEmpty(fragment))
                {
                    continue;
                }

                if (fragment[0] == '=' ? name == fragment.Substring(1) : name.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
