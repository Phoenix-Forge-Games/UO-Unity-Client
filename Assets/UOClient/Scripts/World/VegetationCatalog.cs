using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Which 3D prefabs stand in for each kind of UO vegetation in each biome, and how big they
    /// should be. Each prefab's natural height is stored beside it so placement can scale every
    /// species to <see cref="Entry.TargetHeight"/> without measuring at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Vegetation Catalog", fileName = "VegetationCatalog")]
    public sealed class VegetationCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public VegetationKind Kind;
            public Biome Biome;
            public GameObject[] Prefabs = Array.Empty<GameObject>();
            [Tooltip("Natural height in metres of each prefab, same order as Prefabs.")]
            public float[] NaturalHeights = Array.Empty<float>();
            [Tooltip("Placed height range in metres.")]
            public Vector2 TargetHeight = new Vector2(1f, 1f);
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private Dictionary<(VegetationKind, Biome), Entry> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// Finds the entry for a kind in a biome, falling back to the temperate entry for that kind.
        /// Returns null when nothing is mapped (the placeholder box is drawn instead).
        /// </summary>
        public Entry Find(VegetationKind kind, Biome biome)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<(VegetationKind, Biome), Entry>();
                foreach (Entry entry in _entries)
                {
                    if (entry.Prefabs != null && entry.Prefabs.Length > 0)
                    {
                        _lookup[(entry.Kind, entry.Biome)] = entry;
                    }
                }
            }

            if (_lookup.TryGetValue((kind, biome), out Entry found))
            {
                return found;
            }

            return _lookup.TryGetValue((kind, Biome.Temperate), out found) ? found : null;
        }

        private void OnValidate() => _lookup = null;
    }
}
