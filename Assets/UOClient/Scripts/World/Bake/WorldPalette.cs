using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Every mesh and material a baked world refers to, by index. Chunk files store indices into these lists, so the
    /// lists only ever grow: an entry is never removed or moved once a chunk may point at it. The palette is our
    /// content and ships with the game; the chunks are made on the player's machine from their UO files.
    /// A mesh or material that isn't here (made at runtime, or a palette from an older build) is written into the
    /// chunk itself instead.
    /// </summary>
    [CreateAssetMenu(menuName = "UOClient/World Palette")]
    public sealed class WorldPalette : ScriptableObject
    {
        public List<Mesh> Meshes = new List<Mesh>();
        public List<Material> Materials = new List<Material>();

        /// <summary>Effect prefabs (particle systems such as candle flames), placed whole rather than recorded.</summary>
        public List<GameObject> Effects = new List<GameObject>();

        private Dictionary<Mesh, int> _meshIndex;
        private Dictionary<Material, int> _materialIndex;

        /// <summary>
        /// Set while baking in the editor: says whether an object is a project asset, which may then be added to the
        /// palette. Unset (a player build), the palette is fixed and anything missing is written inline.
        /// </summary>
        public static System.Func<Object, bool> CanAdd { get; set; }

        /// <summary>Set while baking in the editor: finds an effect prefab by name, to add to the palette.</summary>
        public static System.Func<string, GameObject> FindEffect { get; set; }

        /// <summary>Entries added since load (the editor saves the asset when this is set).</summary>
        public bool Grew { get; set; }

        public int IndexOf(Mesh mesh) => Find(mesh, Meshes, ref _meshIndex);

        public int IndexOf(Material material) => Find(material, Materials, ref _materialIndex);

        private int Find<T>(T item, List<T> list, ref Dictionary<T, int> index) where T : Object
        {
            if (item == null)
            {
                return -1;
            }

            if (index == null)
            {
                index = new Dictionary<T, int>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !index.ContainsKey(list[i]))
                    {
                        index[list[i]] = i;
                    }
                }
            }

            if (index.TryGetValue(item, out int found))
            {
                return found;
            }

            if (CanAdd == null || !CanAdd(item))
            {
                return -1;
            }

            list.Add(item);
            index[item] = list.Count - 1;
            Grew = true;
            return list.Count - 1;
        }

        /// <summary>The effect prefab an object was made from, by the object's name ("(Clone)" ignored), or -1.</summary>
        public int EffectIndex(string objectName)
        {
            // Instances are named "X(Clone)", and copies nested in other prefabs "X (1)".
            string name = System.Text.RegularExpressions.Regex.Replace(objectName.Replace("(Clone)", ""), @"\s*\(\d+\)$", "").Trim();
            for (int i = 0; i < Effects.Count; i++)
            {
                if (Effects[i] != null && Effects[i].name == name)
                {
                    return i;
                }
            }

            GameObject found = FindEffect?.Invoke(name);
            if (found == null)
            {
                return -1;
            }

            Effects.Add(found);
            Grew = true;
            return Effects.Count - 1;
        }

        public GameObject Effect(int i) => i >= 0 && i < Effects.Count ? Effects[i] : null;

        public Mesh Mesh(int i) => i >= 0 && i < Meshes.Count ? Meshes[i] : null;

        public Material Material(int i) => i >= 0 && i < Materials.Count ? Materials[i] : null;
    }
}
