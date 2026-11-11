namespace UOClient.Bake
{
    /// <summary>
    /// A baked chunk file (<c>{cx}_{cy}.chunk</c>, one per 64x64 tiles = 80 m square), raw-deflate compressed:
    /// <code>
    /// header   magic "UOWC", version, facet, cx, cy, palette meshes / materials / effects the chunk needs
    /// strings  count, then each (names, tags, component types, component JSON)
    /// meshes   count, then each inline mesh: name, vertices, optional normals / tangents / colours,
    ///          uv0-3 (dimension 0 = absent), submeshes (topology, indices)
    /// terrain  present?, position, size, heights (res x res floats, Unity's 0-1),
    ///          land kinds of its layers, splat (res x res x layers bytes), hole bits, grass-free bits, water mesh
    /// nodes    count, then each in depth-first order (a parent before its children):
    ///          parent (-1: a root, stored in world space), name, stable id, layer, active, tag,
    ///          position, rotation, scale, then its components (tag byte + data, see the constants)
    /// </code>
    /// Mesh references: 0 and up = palette index, -1 = none, -2 and down = inline mesh (-2 is the first).
    /// Material references: palette index or -1.
    /// Stable ids hash the facet, the chunk, and each object's name and place among same-named siblings, so the same
    /// UO files bake the same ids; the edit layer and the server address world objects by them.
    /// </summary>
    public static class ChunkFormat
    {
        public const uint Magic = 0x43574F55; // "UOWC"
        public const int Version = 2;
        public const int ChunkTiles = TerrainStreamer.ChunkTiles;
        public const int BlocksPerChunk = ChunkTiles / 8;

        public const byte Draw = 1;
        public const byte Parts = 2;
        public const byte Box = 3;
        public const byte Sphere = 4;
        public const byte Capsule = 5;
        public const byte MeshCol = 6;
        public const byte Lod = 7;
        public const byte LightC = 8;
        public const byte Probe = 9;
        public const byte Behaviour = 10;
        /// <summary>A palette effect prefab placed at this object (its own components and children aren't recorded).</summary>
        public const byte Effect = 11;

        /// <summary>An object's id as JsonUtility writes it in references ("instanceID").</summary>
        public static ulong Id(UnityEngine.Object o) => UnityEngine.EntityId.ToULong(o.GetEntityId());

        public static string FileName(int cx, int cy) => $"{cx}_{cy}.chunk";

        /// <summary>FNV-1a, 64-bit, over a value.</summary>
        public static ulong Hash(ulong seed, long value)
        {
            ulong h = seed;
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(value >> (i * 8));
                h *= 1099511628211UL;
            }

            return h;
        }

        public static ulong Hash(ulong seed, string value)
        {
            ulong h = seed;
            foreach (char c in value)
            {
                h ^= c & 0xFFu;
                h *= 1099511628211UL;
                h ^= (uint)c >> 8;
                h *= 1099511628211UL;
            }

            return h;
        }
    }
}
