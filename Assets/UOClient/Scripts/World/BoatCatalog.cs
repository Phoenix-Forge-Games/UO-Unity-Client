using System;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Which ship model draws each UO boat multi (Synty POLYGON Pirate ships). Every boat type has four multi IDs,
    /// one per heading (north, east, south, west). Built by Tools/Boats/BoatCatalog.eval.cs.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Boat Catalog")]
    public sealed class BoatCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Name;
            [Tooltip("First multi ID (heading north); the next three are east, south and west.")]
            public int NorthId;
            public GameObject Prefab;
            [Tooltip("How far under the water the keel sits, as a fraction of the hull length.")]
            public float Draft = 0.04f;
        }

        public Entry[] Entries = new Entry[0];

        /// <summary>The entry for a multi ID and its heading (0 north, 1 east, 2 south, 3 west), or null.</summary>
        public Entry Find(int multiId, out int heading)
        {
            heading = 0;
            foreach (Entry entry in Entries)
            {
                if (entry?.Prefab != null && multiId >= entry.NorthId && multiId < entry.NorthId + 4)
                {
                    heading = multiId - entry.NorthId;
                    return entry;
                }
            }

            return null;
        }
    }
}
