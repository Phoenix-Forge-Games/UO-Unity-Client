using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// Builds the model for a UO body under a parent, on clients: the creature catalog's model for the body, or the
    /// Synty human for human bodies (and, for now, bodies the catalog doesn't have yet). Used by living creatures and
    /// their corpses alike. Colliders are stripped (the actor's controller is its collision) and the model is set up
    /// for <see cref="ActorCulling"/>.
    /// </summary>
    public static class ActorModel
    {
        public static bool IsHumanBody(int body) => body is 400 or 401 or 605 or 606 or 666 or 667;

        public static GameObject Build(int body, Transform parent, CreatureCatalog catalog, GameObject human,
            RuntimeAnimatorController humanAnimator, out Animator animator) =>
            Build(body, parent, catalog, human, humanAnimator, out animator, out _, default);

        /// <param name="isHuman">Whether the model built is the human one (a body the catalog lacks gets it too).</param>
        public static GameObject Build(int body, Transform parent, CreatureCatalog catalog, GameObject human,
            RuntimeAnimatorController humanAnimator, out Animator animator, out bool isHuman, Look look)
        {
            animator = null;
            GameObject model;
            CreatureCatalog.Entry entry = catalog != null && !IsHumanBody(body) ? catalog.Find((ushort)body) : null;
            bool placeholder = entry == null && !IsHumanBody(body) && catalog != null;
            isHuman = entry == null && !placeholder;
            SidekickDresser dresser = isHuman && look.IsSet ? GameLook.Dresser : null;
            if (entry != null)
            {
                model = CreatureCatalog.Spawn(entry, parent, out animator);
            }
            else if (placeholder)
            {
                model = BuildPlaceholder(body, parent);
            }
            else if (dresser != null)
            {
                // A human dressed in what the server says they wear.
                model = dresser.Build(Looks.ToMobile(look, Server.ItemService.Catalog), parent);
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                {
                    Object.Destroy(c);
                }

                animator = model.GetComponentInChildren<Animator>();
            }
            else if (human == null)
            {
                // No human model in this build (the paid art is missing, see Docs/SecureArt.md): the placeholder capsule.
                model = BuildPlaceholder(body, parent);
                isHuman = false;
            }
            else
            {
                model = Object.Instantiate(human, parent, false);
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                {
                    Object.Destroy(c);
                }

                animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = humanAnimator;
            }

            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            ActorCulling.Attach(model, animator);
            return model;
        }

        /// <summary>
        /// A magenta capsule standing in for a body the creature catalog has no model for, so what is still missing
        /// can be seen in the world (see Docs/MissingVisuals.md). Its colour varies a little per body so groups read apart.
        /// </summary>
        private static GameObject BuildPlaceholder(int body, Transform parent)
        {
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Missing model (body " + body + ")";
            Object.Destroy(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(parent, false);
            capsule.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
            capsule.transform.localPosition = Vector3.up * 0.5f;
            float hue = 0.78f + (body % 7) * 0.03f;
            Color colour = Color.HSVToRGB(hue % 1f, 0.8f, 1f);
            var block = new MaterialPropertyBlock();
            Renderer r = capsule.GetComponent<Renderer>();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", colour);
            block.SetColor("_Color", colour);
            r.SetPropertyBlock(block);
            return capsule;
        }

        /// <summary>A model as above for a human with a look (players, NPCs).</summary>
        public static GameObject Build(int body, Transform parent, CreatureCatalog catalog, GameObject human,
            RuntimeAnimatorController humanAnimator, Look look, out Animator animator)
        {
            return Build(body, parent, catalog, human, humanAnimator, out animator, out _, look);
        }

        /// <summary>
        /// Half the model's width across its body (metres): how far it stands off the ground lying on its side or
        /// back. From the meshes' own bounds, since a skinned renderer's bounds don't follow a rotated model.
        /// </summary>
        public static float HalfWidth(GameObject model)
        {
            float half = 0f;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    Vector3 scale = r.transform.lossyScale;
                    half = Mathf.Max(half, Mathf.Min(mesh.bounds.extents.x * Mathf.Abs(scale.x), 0.6f));
                }
            }

            return half;
        }

        /// <summary>World-space bounds of everything the model draws (false when it draws nothing).</summary>
        public static bool Bounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return any;
        }
    }
}
