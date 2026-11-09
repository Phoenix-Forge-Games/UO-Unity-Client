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
            SyntyBuilding spec = HouseSpecBuilder.Build(house);
            if (spec == null)
            {
                return null;
            }

            SyntyBuildingGenerator.DressingEnabled = false;
            SyntyBuildingGenerator.SquashPlainHalves = true;
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
