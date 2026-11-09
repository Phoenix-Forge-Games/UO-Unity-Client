using UnityEngine;

namespace UOClient.Buildings
{
    /// <summary>
    /// Picks one roof colourway for the whole building, once, and then never changes it.
    ///
    /// THE REQUIREMENT IS STABILITY, NOT RANDOMNESS. The same placed building must look the same every
    /// session, after every reload, on every client. So the choice is NOT made at runtime — nothing here
    /// calls UnityEngine.Random, and nothing derives the colour from a world position, an instance id, a
    /// spawn order or a clock, all of which are stable within one session and different in the next.
    ///
    /// HOW IT SURVIVES A RELOAD. <see cref="Seed"/> is a serialized field, written ONCE when the building is
    /// placed and saved with the scene or prefab instance. Reload reads the same integer back, the same
    /// deterministic mix turns it into the same index, and the same material is applied. There is no state
    /// anywhere else to disagree with it.
    ///
    /// HOW IT IS IDENTICAL ON EVERY CLIENT. Because the seed is *authored data shipped inside the asset*, not
    /// a roll. Every client loads the same scene or prefab and therefore the same seed, so no replication is
    /// needed for a placed building — the cheapest possible network answer. The one rule that keeps it true:
    /// a building SPAWNED at runtime must have its seed assigned by the server and replicated before Awake
    /// (or baked into the spawned prefab). A client that invents its own seed is the only way this breaks,
    /// which is why <see cref="EnsureSeed"/> takes the seed as an argument rather than generating one.
    ///
    /// WHY A SEED AND NOT THE MATERIAL DIRECTLY. Every instance of one prefab shares that prefab's materials,
    /// so baking the choice only into the material would make every house on the street the same colour. The
    /// seed is per-placement, so a street varies while each individual building stays put.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingRoofColour : MonoBehaviour
    {
        [Tooltip("Per-placement identity. Written once when the building is placed and then serialized. " +
                 "Zero means 'not yet placed' — see EnsureSeed.")]
        public int Seed;

        [Tooltip("The roof colourways available, in a FIXED order. Reordering this changes what every " +
                 "already-placed building looks like, so treat it as append-only.")]
        public Material[] Palette;

        [Tooltip("The groups this searches. Scoping the search by group keeps a roof repaint from ever " +
                 "reaching into another building or into the interior.")]
        public Transform[] RoofGroups;

        [Tooltip("Piece-name prefixes that count as roof. Empty means every renderer in the groups. This is " +
                 "needed because a roof group is not purely roof: a flat roof's group also holds its " +
                 "Parapet, which is a WALL and must keep the wall colourway.")]
        public string[] NameMatch;

        /// <summary>Which palette entry this building uses. Pure function of the seed.</summary>
        public int Index => Palette == null || Palette.Length == 0 ? -1 : (int)(Mix((uint)Seed) % (uint)Palette.Length);

        /// <summary>The chosen material, or null when unconfigured.</summary>
        public Material Chosen
        {
            get { int i = Index; return i < 0 ? null : Palette[i]; }
        }

        /// <summary>
        /// Assign the seed if this building has never been placed. Idempotent — a second call on an
        /// already-seeded building does nothing, which is what makes "chosen once, then fixed" true even if
        /// a placement pass runs twice.
        /// </summary>
        public bool EnsureSeed(int seed)
        {
            if (Seed != 0) return false;
            Seed = seed == 0 ? 1 : seed;   // 0 is reserved for "unplaced", so never store it as a value
            Apply();
            return true;
        }

        /// <summary>
        /// Force a different colourway. Exists for authoring and tests — the game never calls this, because
        /// the whole point is that a placed roof does not change.
        /// </summary>
        public void OverrideSeed(int seed)
        {
            Seed = seed == 0 ? 1 : seed;
            Apply();
        }

        private void Awake() => Apply();

        /// <summary>
        /// Paint every roof renderer with the chosen material. Uniform across the whole roof by construction:
        /// one material, applied to every slot of every renderer in the roof groups.
        /// </summary>
        public void Apply()
        {
            var mat = Chosen;
            if (mat == null || RoofGroups == null) return;

            foreach (var g in RoofGroups)
            {
                if (g == null) continue;
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!Matches(r.transform.name)) continue;
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        // Glass is never part of a colourway — the same carve-out SyntyKit.Repaint makes.
                        if (mats[i] != null && mats[i].name.StartsWith("Glass")) continue;
                        if (mats[i] == mat) continue;
                        mats[i] = mat;
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
        }

        /// <summary>
        /// The roof colourways, in a FIXED order — append only, never reorder.
        ///
        /// These are TINTED variants of the stock Synty atlas material rather than entries from the pack's
        /// Alts family, and that is a measured decision, not a preference. Sampling the atlas at the roof
        /// tile's own triangle-centroid UVs across all TWELVE Alts variants gave a maximum pairwise colour
        /// separation of 0.0409, and the best-separated five had a minimum separation of 0.0157 — i.e. the
        /// tile roof is one colour no matter which colourway is applied. That is the same dead end the
        /// noble-house pass hit with paving, which measured 0.0000 across all five and is the control this
        /// method was validated against. The Alts family recolours WALLS.
        ///
        /// (Thatch is the exception — it does vary strongly across the atlas, 0.0901 to 0.3828. A palette
        /// that only worked on thatched buildings would still be a broken feature on tiled ones, so the tint
        /// route is used for both and gives one consistent mechanism.)
        /// </summary>
        public static readonly string[] PalettePaths =
        {
            "Assets/Content/Buildings/Materials/Roof_Slate.mat",
            "Assets/Content/Buildings/Materials/Roof_Terracotta.mat",
            "Assets/Content/Buildings/Materials/Roof_Moss.mat",
            "Assets/Content/Buildings/Materials/Roof_Lead.mat",
            "Assets/Content/Buildings/Materials/Roof_Straw.mat",
        };

        /// <summary>Whether a piece name counts as roof. No filter configured means "everything in the groups".</summary>
        public bool Matches(string pieceName)
        {
            if (NameMatch == null || NameMatch.Length == 0) return true;
            foreach (var m in NameMatch)
                if (!string.IsNullOrEmpty(m) && pieceName.StartsWith(m)) return true;
            return false;
        }

        /// <summary>
        /// A deterministic integer mix (a finalizer from the MurmurHash3 family). Deliberately NOT
        /// string.GetHashCode or Random: those are free to differ between runtimes and Unity versions, and a
        /// colour that changes when the engine updates is exactly the bug this class exists to prevent.
        /// </summary>
        public static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Build a placement seed from a stable identity — a name and a grid position, say.</summary>
        public static int SeedFrom(string identity, int salt = 0)
        {
            uint h = 2166136261u;
            if (identity != null)
                foreach (char c in identity) { h ^= c; h *= 16777619u; }
            h ^= (uint)salt;
            int s = (int)Mix(h);
            return s == 0 ? 1 : s;
        }
    }
}
