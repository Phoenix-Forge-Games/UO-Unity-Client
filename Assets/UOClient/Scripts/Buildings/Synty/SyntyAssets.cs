using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// Name-to-asset lookup for the building generator, which addresses every kit piece and material by
    /// its bare name. Player builds resolve through a <see cref="SyntyAssetRegistry"/> (assign
    /// <see cref="Registry"/> before building); in the editor, names the registry lacks fall back to an
    /// AssetDatabase search so new pieces work before the registry is refreshed.
    /// </summary>
    public static class SyntyAssets
    {
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static SyntyAssetRegistry _registry;

        public static SyntyAssetRegistry Registry
        {
            get => _registry;
            set
            {
                _registry = value;
                Prefabs.Clear();
                Materials.Clear();
                if (value == null)
                {
                    return;
                }

                foreach (GameObject prefab in value.Prefabs)
                {
                    if (prefab != null)
                    {
                        Prefabs[prefab.name] = prefab;
                    }
                }

                foreach (Material material in value.Materials)
                {
                    if (material != null)
                    {
                        Materials[material.name] = material;
                    }
                }
            }
        }

        public static GameObject Prefab(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (Prefabs.TryGetValue(name, out GameObject prefab))
            {
                return prefab;
            }

#if UNITY_EDITOR
            prefab = FindInEditor<GameObject>(name, "t:Prefab");
            Prefabs[name] = prefab;
#endif
            return prefab;
        }

        public static Material Material(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (Materials.TryGetValue(name, out Material material))
            {
                return material;
            }

#if UNITY_EDITOR
            material = FindInEditor<Material>(name, "t:Material");
            Materials[name] = material;
#endif
            return material;
        }

#if UNITY_EDITOR
        private static T FindInEditor<T>(string name, string filter) where T : Object
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(name + " " + filter))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                {
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }

            return null;
        }
#endif
    }

    /// <summary>
    /// Names of the generated wall-skin materials some wall sets ask for. The generator looks them up by
    /// name; until those atlases exist in this project, the wall sets fall back to the kit's own material.
    /// </summary>
    public static class SyntyWallTextures
    {
        public const string BrickMaterialPath = "SB_SyntyWall_Brick.mat";
        public const string WoodMaterialPath = "SB_SyntyWall_WoodBoard.mat";
        public const string GreyBrickMaterialPath = "SB_SyntyWall_BrickGrey.mat";
    }
}
