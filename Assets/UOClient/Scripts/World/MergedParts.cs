using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// The pieces a merged mesh was made from, kept on its object while the world baker runs
    /// (<see cref="MergedPrefabBuilder.KeepParts"/>) so the bake can store the pieces rather than the merged mesh.
    /// </summary>
    public sealed class MergedParts : MonoBehaviour
    {
        public readonly List<CombineInstance> Parts = new List<CombineInstance>();
        public Material Material;
    }
}
