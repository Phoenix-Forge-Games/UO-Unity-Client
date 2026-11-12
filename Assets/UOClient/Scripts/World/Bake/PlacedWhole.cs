using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Marks a prefab the bake keeps whole, like an effect: a chunk records only which prefab and where (it must be in
    /// the palette's effects, found by name while baking in the editor), and loading the chunk places the prefab
    /// itself. So editing the prefab changes every copy in the world without baking again. Light fixtures carry it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacedWhole : MonoBehaviour
    {
    }
}
