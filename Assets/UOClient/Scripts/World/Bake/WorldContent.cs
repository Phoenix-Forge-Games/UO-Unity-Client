using UnityEngine;
using UnityEngine.VFX;

namespace UOClient.Bake
{
    /// <summary>
    /// The art the world is generated and drawn with: what the World scene's view holds, as one asset, so the
    /// baker (which runs without a scene or a login) and the baked-world view use the same set.
    /// </summary>
    [CreateAssetMenu(menuName = "UOClient/World Content")]
    public sealed class WorldContent : ScriptableObject
    {
        public Material WorldMaterial;
        public Material TerrainMaterial;
        public Material WaterMaterial;

        [Tooltip("One terrain layer per natural LandKind, in enum order.")]
        public TerrainLayer[] LandLayers;

        public VegetationCatalog Vegetation;
        public WallKit WallKit;
        public PropCatalog Props;

        [Tooltip("VFX Graph grass grown on the terrain (Grass.vfx); empty for none.")]
        public VisualEffectAsset Grass;

        [Tooltip("Rebuild detected UO houses whole with the Synty building generator.")]
        public bool GenerateHouses = true;
    }
}
