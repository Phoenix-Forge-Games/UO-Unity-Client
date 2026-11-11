using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Makes a mobile or item view clickable: a trigger collider fitted to its model, found by the targeting raycast.
    /// It's fitted from the meshes' own bounds (skinned renderer bounds are padded and would catch clicks well
    /// around the model). Humanoid rigs get an upright capsule (their bind pose is a T-pose, too wide for a box);
    /// everything else gets a box. It refits when the model changes (re-dressed, swapped).
    /// </summary>
    public sealed class Pickable : MonoBehaviour
    {
        private const float RefitCheckSeconds = 0.5f;

        public uint Serial;

        private Collider _collider;
        private int _rendererCount = -1;
        private float _nextCheck;
        private static readonly List<Renderer> Renderers = new List<Renderer>();

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + RefitCheckSeconds;
            GetComponentsInChildren(Renderers);
            int count = 0;
            foreach (Renderer r in Renderers)
            {
                if (r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return; // hidden (an unseen ghost): keep the collider as it is; MobileView switches it off
            }

            if (count != _rendererCount || _collider == null)
            {
                _rendererCount = count;
                Fit();
            }
        }

        private void Fit()
        {
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer r in Renderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer)
                {
                    continue;
                }

                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            if (!any)
            {
                return;
            }

            Animator animator = GetComponentInChildren<Animator>();
            bool humanoid = animator != null && animator.avatar != null && animator.avatar.isHuman;
            if (humanoid)
            {
                if (!(_collider is CapsuleCollider capsule))
                {
                    DestroyCollider();
                    capsule = gameObject.AddComponent<CapsuleCollider>();
                    _collider = capsule;
                }

                float height = Mathf.Max(0.3f, bounds.max.y - Mathf.Min(0f, bounds.min.y));
                capsule.direction = 1;
                capsule.height = height;
                capsule.radius = Mathf.Clamp(height * 0.16f, 0.15f, 1.2f);
                capsule.center = new Vector3(0f, height * 0.5f, 0f);
            }
            else
            {
                if (!(_collider is BoxCollider box))
                {
                    DestroyCollider();
                    box = gameObject.AddComponent<BoxCollider>();
                    _collider = box;
                }

                Vector3 size = Vector3.Max(bounds.size, new Vector3(0.25f, 0.25f, 0.25f));
                box.size = size;
                box.center = bounds.center;
            }

            _collider.isTrigger = true;
        }

        private void DestroyCollider()
        {
            if (_collider != null)
            {
                Destroy(_collider);
                _collider = null;
            }
        }
    }
}
