using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// The pair of wings on a gargoyle's back: folded by default, opened (Spread) and beaten (Flap) by whatever moves
    /// the gargoyle. Both wings share one controller; the left is a mirror of the right.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class GargoyleWings : MonoBehaviour
    {
        private static readonly int SpreadParameter = Animator.StringToHash("Spread");
        private static readonly int FlapParameter = Animator.StringToHash("Flap");
        private static readonly int HoverParameter = Animator.StringToHash("Hover");

        private Animator[] _animators = System.Array.Empty<Animator>();
        private bool _spread, _flap;

        public void Bind(params Animator[] animators) => _animators = animators;

        /// <summary>Folded wings lean this far forward onto the back and narrow a little; open wings don't.</summary>
        /// <summary>Share of the body's lean the wings cancel, and how far the wing pitches nose-down on the downstroke (pronation) and back up on the upstroke.</summary>
        private const float CounterLean = 0.9f, Pronation = 0f, BrakeTilt = 38f;
        private const float RestTilt = 30f, RestNarrow = 0.8f, SpreadOut = 0f, SpreadRoll = 0f, DownRoll = 0f;
        private Transform _model;
        private (Transform Mount, float Side, Quaternion Base, Vector3 Scale, Vector3 Position)[] _mounts = System.Array.Empty<(Transform, float, Quaternion, Vector3, Vector3)>();
        private float _rest = 1f, _down, _phase;

        /// <summary>
        /// How far the body is pitched forward in flight (degrees). The wings turn back against it, so their flat stays
        /// near level (lifting) with only a slight nose-down, the way a bird's body and wings are set.
        /// </summary>
        public float Lean { get; set; }

        /// <summary>0..1: air braking, the wings turned to face the airflow (leading edge up) to dump speed.</summary>
        public float Brake { get; set; }

        /// <summary>Beat phase 0..1 while the wings flap (the body rocks with it).</summary>
        public float Phase => _phase;
        public bool Beating => _flap;

        /// <summary>The wing roots (and which side each is on), so they can settle against the back while folded.</summary>
        public void Mount(Transform model, params (Transform Mount, float Side)[] mounts)
        {
            _model = model;
            _mounts = new (Transform, float, Quaternion, Vector3, Vector3)[mounts.Length];
            _roots = new Transform[mounts.Length];
            _pins = new Vector3[mounts.Length];
            for (int i = 0; i < mounts.Length; i++)
            {
                _mounts[i] = (mounts[i].Mount, mounts[i].Side, mounts[i].Mount.localRotation, mounts[i].Mount.localScale, mounts[i].Mount.localPosition);
                // The wing's root joint is what has to stay on the back: tilting and folding turn the wing about it.
                _roots[i] = FindRoot(mounts[i].Mount);
                _pins[i] = _roots[i] != null && mounts[i].Mount.parent != null ? mounts[i].Mount.parent.InverseTransformPoint(_roots[i].position) : mounts[i].Mount.localPosition;
            }
        }

        private Transform[] _roots = System.Array.Empty<Transform>();
        private Vector3[] _pins = System.Array.Empty<Vector3>();

        /// <summary>The joint the wing hangs from (the start of its arm bone).</summary>
        public static Transform FindRoot(Transform wing)
        {
            foreach (Transform t in wing.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Wing_Upper")
                {
                    return t;
                }
            }

            return null;
        }

        private void LateUpdate()
        {
            if (_model == null)
            {
                return;
            }

            // The two wings start their beat whenever each one's controller gets there, so they can drift half a beat apart:
            // pull the second onto the first whenever they're in the same loop.
            if (_animators.Length > 1 && _animators[0] != null && _animators[1] != null)
            {
                AnimatorStateInfo first = _animators[0].GetCurrentAnimatorStateInfo(0), second = _animators[1].GetCurrentAnimatorStateInfo(0);
                if (first.fullPathHash == second.fullPathHash && (first.IsName("Flapping") || first.IsName("Hovering")))
                {
                    float gap = Mathf.Repeat(second.normalizedTime - first.normalizedTime + 0.5f, 1f) - 0.5f;
                    if (Mathf.Abs(gap) > 0.02f)
                    {
                        _animators[1].Play(first.fullPathHash, 0, first.normalizedTime);
                    }
                }
            }

            // On the downstroke the wings swing in over each other; lean them outward as they come down.
            float down = 0f;
            _phase = 0f;
            if (_flap && _animators.Length > 0)
            {
                AnimatorStateInfo state = _animators[0].GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Flapping") || state.IsName("Hovering"))
                {
                    _phase = state.normalizedTime % 1f;
                    down = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * _phase));
                }
            }

            _down = Mathf.Lerp(_down, down, 1f - Mathf.Exp(-20f * Time.deltaTime));
            _rest = Mathf.MoveTowards(_rest, _spread ? 0f : 1f, Time.deltaTime * 2.5f);
            for (int m = 0; m < _mounts.Length; m++)
            {
                (Transform mount, float side, Quaternion baseRotation, Vector3 baseScale, Vector3 basePosition) = _mounts[m];
                if (mount == null || mount.parent == null)
                {
                    continue;
                }

                // Rotations about the body's own right and forward axes, expressed in the spine's frame.
                Quaternion toParent = Quaternion.Inverse(mount.parent.rotation);
                float beat = _flap ? Pronation * Mathf.Sin(2f * Mathf.PI * _phase) : 0f;
                float openTilt = (beat - CounterLean * Lean - BrakeTilt * Brake) * (1f - _rest);
                Quaternion tilt = Quaternion.AngleAxis(RestTilt * _rest + openTilt, toParent * _model.right);
                // Open wings also lean outward at the top, so the beat's ends don't cross over each other.
                float trim = 2.5f * Mathf.Sin(Time.time * 2.2f + (side > 0 ? 0f : 1.7f)); // small balancing corrections, a little different each side
                Quaternion roll = Quaternion.AngleAxis(side * (SpreadRoll + DownRoll * _down + trim) * (1f - _rest), toParent * _model.forward);
                mount.localRotation = roll * tilt * baseRotation;
                // Open wings reach inward past the spine, so they stand further out than folded ones or they cross.
                mount.position = mount.parent.TransformPoint(basePosition) + _model.right * side * SpreadOut * (1f - _rest);
                mount.localScale = new Vector3(baseScale.x * Mathf.Lerp(1f, RestNarrow, _rest), baseScale.y, baseScale.z);
                if (_roots[m] != null)
                {
                    // Pin the root joint to its spot on the back, whatever the wing's turned or narrowed by.
                    mount.position += mount.parent.TransformPoint(_pins[m]) + _model.right * side * SpreadOut * (1f - _rest) - _roots[m].position;
                }
            }
        }

        /// <summary>Wings open (gliding or flying) rather than folded against the back.</summary>
        public bool Spread
        {
            get => _spread;
            set
            {
                _spread = value;
                foreach (Animator a in _animators)
                {
                    a.SetBool(SpreadParameter, value);
                }
            }
        }

        /// <summary>How fast the wings beat (1 = the clip's own pace).</summary>
        public float Rate
        {
            set
            {
                foreach (Animator a in _animators)
                {
                    a.speed = value;
                }
            }
        }

        /// <summary>The hovering beat (the biggest arc, tips swung down past the hips) rather than the forward-flight one.</summary>
        public bool Hover
        {
            set
            {
                foreach (Animator a in _animators)
                {
                    a.SetBool(HoverParameter, value);
                }
            }
        }

        /// <summary>Wings beating (only while spread).</summary>
        public bool Flap
        {
            get => _flap;
            set
            {
                _flap = value;
                foreach (Animator a in _animators)
                {
                    a.SetBool(FlapParameter, value);
                }
            }
        }
    }
}
