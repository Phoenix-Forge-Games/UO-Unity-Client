using UnityEngine;
using UOClient.Buildings.Synty;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>Builds a detected UO house with the Synty building generator, placed where the house stands.</summary>
    public static class HouseBuilder
    {
        /// <summary>Layer the generated houses go on; the camera stops drawing it where the fog is total.</summary>
        public const string LayerName = "Buildings";

        /// <summary>
        /// The kit's transparent glass shader ignores fog, so distant windows glowed in the haze; generated
        /// houses use this see-through URP Lit glass instead, which fogs like the walls round it.
        /// </summary>
        private const string KitGlass = "Glass_01";
        private const string FoggedGlass = "UOWindowGlass";

        public static GameObject Build(HouseLayout house)
        {
            if (house.Castle)
            {
                return BuildCastle(house);
            }

            SyntyBuilding spec = HouseSpecBuilder.Build(house);
            if (spec == null)
            {
                return null;
            }

            SyntyBuildingGenerator.DressingEnabled = false;
            SyntyBuildingGenerator.ResetDoors();
            GameObject root;
            try
            {
                root = SyntyBuildingGenerator.Build(spec);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UOClient] House at {house.Anchor.X},{house.Anchor.Y} failed to generate: {e.Message}");
                return null;
            }

            root.transform.position = HouseSpecBuilder.RootPosition(house);
            SwapGlass(root);
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = layer;
                }
            }


            return root;
        }

        /// <summary>A castle-masonry building, from the castle kit rather than the house generator.</summary>
        /// <summary>
        /// Hand-finished castles: a prefab here named <c>Castle_&lt;anchorX&gt;_&lt;anchorY&gt;</c> is placed instead of
        /// generating that castle (and its outworks). Its root is the castle's <see cref="HouseSpecBuilder.RootPosition"/>,
        /// exactly as the generator builds it, so an exported castle can be edited in place.
        /// </summary>
        public const string CastleOverrideFolder = "Assets/UOClient/Models/Castles";

        private static GameObject CastleOverride(HouseLayout house)
        {
#if UNITY_EDITOR
            string path = $"{CastleOverrideFolder}/Castle_{house.Anchor.X}_{house.Anchor.Y}.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                GameObject root = Object.Instantiate(prefab);
                root.name = prefab.name;
                return root;
            }
#endif
            return null;
        }

        private static GameObject BuildCastle(HouseLayout house)
        {
            GameObject manual = CastleOverride(house);
            if (manual != null)
            {
                manual.transform.position = HouseSpecBuilder.RootPosition(house);
                int overrideLayer = LayerMask.NameToLayer(LayerName);
                if (overrideLayer >= 0)
                {
                    foreach (Transform t in manual.GetComponentsInChildren<Transform>(true))
                    {
                        t.gameObject.layer = overrideLayer;
                    }
                }

                return manual;
            }

            SyntyCastle castle = HouseSpecBuilder.CastlePlan(house);
            if (castle == null)
            {
                return null;
            }

            GameObject root;
            try
            {
                root = SyntyCastleBuilder.Build(castle);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UOClient] Castle at {house.Anchor.X},{house.Anchor.Y} failed to generate: {e.Message}");
                return null;
            }

            root.transform.position = HouseSpecBuilder.RootPosition(house);
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = layer;
                }
            }

            return root;
        }

        private static void SwapGlass(GameObject root)
        {
            Material fogged = SyntyAssets.Material(FoggedGlass);
            if (fogged == null)
            {
                return;
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].name == KitGlass)
                    {
                        materials[i] = fogged;
                        changed = true;
                    }
                }

                if (changed)
                {
                    r.sharedMaterials = materials;
                }
            }
        }
    }
}
