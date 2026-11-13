using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// Footsteps for one walker (a player, an NPC or a creature): a step each stride while it moves, near the camera.
    /// What it sounds like, most fitting first (Sounds.json step.*):
    ///   creatures: their own kind's steps (step.orc, step.troll...), else the ground's, else step.plain;
    ///   people:    the ground's (step.gravel, step.leaves, step.sand, step.water, step.metal), else step.plain.
    /// The ground is read under the feet: the terrain texture there (each land layer folded into the five ground
    /// sounds there are), or the name of the floor's mesh and material. Paving, wooden and stone floors have no
    /// ground sound of their own, so they get the plain steps.
    /// </summary>
    public sealed class Footsteps
    {
        /// <summary>Only walkers this near the camera are heard stepping (metres).</summary>
        private const float HearRange = 28f;

        private float _travelled;

        /// <summary>The sound the last step played (step.sand...), for checking by hand.</summary>
        public static string LastStep { get; private set; }

        /// <summary>
        /// Call each frame. `speed`: ground speed (m/s); `scale`: body size against a person's (strides grow with
        /// it); `family`: the creature's sound family, null for people; `water`: wading or swimming.
        /// </summary>
        public void Update(Vector3 feet, float speed, float scale, string family, bool water)
        {
            if (speed < 0.3f)
            {
                _travelled = 0.6f; // the first step comes soon after setting off
                return;
            }

            float stride = Mathf.Clamp(scale, 0.35f, 3f) * Mathf.Lerp(0.75f, 1.9f, Mathf.InverseLerp(1.5f, PlayerMotor.RunSpeed, speed));
            _travelled += speed * Time.deltaTime / stride;
            if (_travelled < 1f)
            {
                return;
            }

            _travelled -= Mathf.Floor(_travelled);
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - feet).sqrMagnitude > HearRange * HearRange)
            {
                return;
            }

            string ground = water ? "water" : Ground(feet);
            LastStep = GameAudio.PlayFirst(feet + Vector3.up * 0.1f,
                family != null ? "step." + family : null,
                ground != null ? "step." + ground : null,
                "step.plain");
        }

        /// <summary>The ground sound for what's underfoot: gravel, leaves, sand, water, metal, or null (plain).</summary>
        public static string Ground(Vector3 feet)
        {
            // A thin ray can slip between two meshes (a roof's ridge, floorboards' seams): then a narrow sphere.
            Vector3 from = feet + Vector3.up * 0.5f;
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !Physics.SphereCast(from, 0.2f, Vector3.down, out hit, 1.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            if (hit.collider is TerrainCollider)
            {
                Terrain terrain = hit.collider.GetComponent<Terrain>();
                return terrain != null ? OfName(LayerAt(terrain, hit.point), true) : null;
            }

            Renderer r = hit.collider.GetComponentInParent<Renderer>();
            string name = hit.collider.name + " " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty);
            return OfName(name, false);
        }

        /// <summary>The name of the terrain layer that covers this point most.</summary>
        private static string LayerAt(Terrain terrain, Vector3 point)
        {
            TerrainData data = terrain.terrainData;
            if (data == null || data.alphamapLayers == 0)
            {
                return null;
            }

            Vector3 local = point - terrain.transform.position;
            int ax = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int az = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(ax, az, 1, 1);
            int best = 0;
            for (int i = 1; i < data.alphamapLayers; i++)
            {
                if (weights[0, 0, i] > weights[0, 0, best])
                {
                    best = i;
                }
            }

            TerrainLayer[] layers = data.terrainLayers;
            return best < layers.Length && layers[best] != null ? layers[best].name : null;
        }

        /// <summary>
        /// Every ground folded into the five sounds there are. The land layers (WorldContent): SandUnderwater and
        /// Moss (swamp) splash; SandBeach, Snow and the muds (dirt, furrows) are soft sand; Gravel (rock), Mud_rubble
        /// (cave) and LavaDry crunch; the grasses rustle. Cobbles, flagstones, brick and terrace stone: plain.
        /// </summary>
        private static string OfName(string name, bool land)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            name = name.ToLowerInvariant();
            if (Has(name, "underwater", "water", "swamp", "bog") || land && Has(name, "moss")) return "water"; // Moss is the swamp layer; a mossy roof isn't
            if (Has(name, "metal", "iron", "steel", "grate", "anvil", "chain")) return "metal";
            if (Has(name, "rubble", "gravel", "lava", "pebble", "scree")) return "gravel";
            if (Has(name, "sand", "snow", "mud", "dirt", "soil", "furrow")) return "sand";
            if (Has(name, "grass", "leaf", "leaves", "hay", "straw", "forest", "jungle")) return "leaves";
            return null;
        }

        private static bool Has(string name, params string[] words)
        {
            foreach (string w in words)
            {
                if (name.Contains(w))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
