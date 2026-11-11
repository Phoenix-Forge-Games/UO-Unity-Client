using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// A post or pole placed above the ground (a house sign's post standing at the house's floor height, out over
    /// lower ground) is stretched down to whatever solid is below it — terrain or a building — keeping its top where
    /// it is, so it never floats. Re-checked for a few seconds, since the ground under it may stream in later.
    /// </summary>
    public sealed class GroundedPole : MonoBehaviour
    {
        private const float CheckSeconds = 0.5f;
        private const int Checks = 20;

        private int _checks;
        private float _next;

        private void Update()
        {
            if (_checks >= Checks || Time.time < _next)
            {
                return;
            }

            _next = Time.time + CheckSeconds;
            _checks++;
            if (!Bounds(out Bounds b))
            {
                return;
            }

            var from = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            float gap = b.min.y - hit.point.y;
            if (gap < 0.03f || b.size.y < 0.05f)
            {
                return;
            }

            float top = b.max.y;
            Vector3 scale = transform.localScale;
            transform.localScale = new Vector3(scale.x, scale.y * (b.size.y + gap) / b.size.y, scale.z);
            if (Bounds(out Bounds stretched))
            {
                transform.position += Vector3.up * (top - stretched.max.y);
            }
        }

        private bool Bounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
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
