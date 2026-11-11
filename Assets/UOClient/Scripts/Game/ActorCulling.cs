using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// Keeps an actor's model (a creature, NPC or other player) cheap when it can't be seen:
    /// <list type="bullet">
    /// <item>off-screen it isn't animated or skinned (the animator culls completely, skins skip off-screen updates);</item>
    /// <item>beyond the draw distance it isn't drawn or animated at all, coming back a little nearer than it went
    /// (so walking at the edge doesn't flicker);</item>
    /// <item>past the animation distance it still draws but animates at a quarter rate.</item>
    /// </list>
    /// The server still runs every actor; this is only how much the client spends showing it.
    /// </summary>
    public sealed class ActorCulling : MonoBehaviour
    {
        public const float DrawDistance = 90f;
        public const float FullRateDistance = 35f;
        private const float Margin = 8f;
        private const int SlowEvery = 4;

        private Renderer[] _renderers;
        private Animator _animator;
        private bool _drawn = true;
        private int _frameOffset;

        /// <summary>Set the model up for culling (call once it's built).</summary>
        public static ActorCulling Attach(GameObject model, Animator animator)
        {
            SkinCompactor.Apply(model);
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = false;
            }

            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
            }

            ActorCulling culling = model.GetComponent<ActorCulling>() ?? model.AddComponent<ActorCulling>();
            culling._renderers = model.GetComponentsInChildren<Renderer>(true);
            culling._animator = animator;
            culling._frameOffset = Random.Range(0, SlowEvery);
            return culling;
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            float distance = Vector3.Distance(camera.transform.position, transform.position);
            bool drawn = _drawn ? distance <= DrawDistance + Margin : distance <= DrawDistance;
            // Re-applied now and then too: when hosting, FishNet shows and hides the host's renderers by observer range.
            if (drawn != _drawn || (Time.frameCount + _frameOffset) % 15 == 0)
            {
                _drawn = drawn;
                foreach (Renderer r in _renderers)
                {
                    if (r != null)
                    {
                        r.enabled = drawn;
                    }
                }
            }

            if (_animator == null)
            {
                return;
            }

            // Far but drawn: animate every few frames, catching up the time skipped.
            bool slow = distance > FullRateDistance;
            _animator.enabled = drawn && !slow;
            if (drawn && slow && (Time.frameCount + _frameOffset) % SlowEvery == 0)
            {
                _animator.Update(Time.deltaTime * SlowEvery);
            }
        }
    }
}
