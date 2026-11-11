using System;
using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Which model draws each non-humanoid UO body (creatures, animals, monsters): the prefab, how tall it stands, an
    /// optional tint (for one model standing in for several bodies, e.g. bear colours) and an optional animator.
    /// Bodies with no entry keep the placeholder capsule. See Docs/CreatureMapping.md for where the models come from.
    /// </summary>
    [CreateAssetMenu(menuName = "UO Client/Creature Catalog")]
    public sealed class CreatureCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("What this is, for people (e.g. \"wolves\").")]
            public string Name;
            [Tooltip("UO body IDs drawn with this model.")]
            public ushort[] Bodies;
            public GameObject Prefab;
            [Tooltip("Height (metres) to scale the model to; 0 keeps its own size.")]
            public float Height;
            [Tooltip("Multiplied into the model's materials; white keeps them as they are.")]
            public Color Tint = Color.white;
            [Tooltip("Optional: drives the model (Speed: 0 idle, 1 walk, 2 run).")]
            public RuntimeAnimatorController Animator;
            [Tooltip("Child objects (by name) to hide, e.g. a tail on a model standing in for a tailless creature.")]
            public string[] Hide = new string[0];
        }

        public Entry[] Entries = new Entry[0];

        [NonSerialized] private Dictionary<ushort, Entry> _byBody;

        public Entry Find(ushort body)
        {
            if (_byBody == null)
            {
                _byBody = new Dictionary<ushort, Entry>();
                foreach (Entry entry in Entries)
                {
                    if (entry?.Prefab == null || entry.Bodies == null)
                    {
                        continue;
                    }

                    foreach (ushort b in entry.Bodies)
                    {
                        _byBody[b] = entry;
                    }
                }
            }

            return _byBody.TryGetValue(body, out Entry found) ? found : null;
        }

        /// <summary>Builds a creature's model under `parent`: scaled to its height, tinted, with its animator.</summary>
        public static GameObject Spawn(Entry entry, Transform parent, out Animator animator)
        {
            GameObject model = UnityEngine.Object.Instantiate(entry.Prefab, parent, false);
            model.name = "Creature " + entry.Name;
            animator = model.GetComponentInChildren<Animator>();
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.Destroy(c);
            }

            if (entry.Hide != null && entry.Hide.Length > 0)
            {
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (Array.IndexOf(entry.Hide, t.name) >= 0)
                    {
                        t.gameObject.SetActive(false);
                    }
                }
            }

            if (entry.Height > 0f)
            {
                Bounds b = default;
                bool any = false;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
                }

                if (any && b.size.y > 0.01f)
                {
                    model.transform.localScale *= entry.Height / b.size.y;
                }
            }

            if (entry.Tint != Color.white)
            {
                var block = new MaterialPropertyBlock();
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    r.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", entry.Tint);
                    block.SetColor("_Color", entry.Tint);
                    r.SetPropertyBlock(block);
                }
            }

            if (entry.Animator != null)
            {
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = entry.Animator;
                animator.applyRootMotion = false; // the server owns position
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            return model;
        }
    }
}
