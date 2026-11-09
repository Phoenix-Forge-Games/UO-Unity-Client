using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// The kit prefabs and materials the building generator may ask for by name. In its own file
    /// because Unity can only serialize a ScriptableObject whose file name matches its class.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Synty Asset Registry", fileName = "SyntyAssetRegistry")]
    public sealed class SyntyAssetRegistry : ScriptableObject
    {
        public GameObject[] Prefabs = new GameObject[0];
        public Material[] Materials = new Material[0];
    }
}
