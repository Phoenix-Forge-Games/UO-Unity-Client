using UnityEngine;
using UOClient.Buildings.Synty;

namespace UOClient.Content
{
    /// <summary>
    /// Every asset the World scene needs from the paid art packs (Synty, BK Pure Nature), gathered
    /// into one object so a release build can ship it inside the encrypted content bundle instead of
    /// the player's readable data files. The release pipeline fills it from <see cref="WorldView"/>'s
    /// own fields at build time; field names match WorldView's so the copy is by property path.
    /// It is never saved in the repository.
    /// </summary>
    public sealed class ContentPack : ScriptableObject
    {
        [SerializeField] private Material _worldMaterial;
        [SerializeField] private Material _terrainMaterial;
        [SerializeField] private Material _waterMaterial;
        [SerializeField] private TerrainLayer[] _landLayers = new TerrainLayer[0];
        [SerializeField] private VegetationCatalog _vegetation;
        [SerializeField] private WallKit _wallKit;
        [SerializeField] private PropCatalog _props;
        [SerializeField] private GameObject[] _humanoidPrefabs = new GameObject[0];
        [SerializeField] private RuntimeAnimatorController _humanoidAnimator;
        [SerializeField] private SidekickWardrobe _wardrobe;
        [Tooltip("Kit prefabs and materials the house generator looks up by name.")]
        [SerializeField] private SyntyAssetRegistry _kit;

        public Material WorldMaterial => _worldMaterial;
        public Material TerrainMaterial => _terrainMaterial;
        public Material WaterMaterial => _waterMaterial;
        public TerrainLayer[] LandLayers => _landLayers;
        public VegetationCatalog Vegetation => _vegetation;
        public WallKit WallKit => _wallKit;
        public PropCatalog Props => _props;
        public GameObject[] HumanoidPrefabs => _humanoidPrefabs;
        public RuntimeAnimatorController HumanoidAnimator => _humanoidAnimator;
        public SidekickWardrobe Wardrobe => _wardrobe;
        public SyntyAssetRegistry Kit => _kit;

        /// <summary>The WorldView fields a release build moves into the pack (and clears in the scene).</summary>
        public static readonly string[] WorldViewFields =
        {
            "_worldMaterial", "_terrainMaterial", "_waterMaterial", "_landLayers", "_vegetation",
            "_wallKit", "_props", "_humanoidPrefabs", "_humanoidAnimator", "_wardrobe", "_grass", "_creatures", "_boatCatalog",
        };
    }
}
