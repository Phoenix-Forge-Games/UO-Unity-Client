using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// The body of a flying gargoyle, posed over whatever the animator plays: pitched forward from the pelvis, a leg trailing
    /// and the other knee drawn up, one arm reaching ahead and the other swept back, head up to look where it's going; it
    /// hangs loosely when hovering. The chest and legs move a little with the wing beat. Bone axes (Sidekick): local Z
    /// bends a joint forward (a leg back, an arm up) for the left side and the spine, the other way on the right arm.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class GargoyleFlightPose : MonoBehaviour
    {
        /// <summary>0 on the ground, 1 flying; and how far into a cruise the pose is (0 hover … 1 prone).</summary>
        public float Fly { get; set; }
        public float Cruise { get; set; }
        /// <summary>Wing beat phase 0..1 (from <see cref="GargoyleWings"/>), 0 when not beating.</summary>
        public float Phase { get; set; }
        public bool Beating { get; set; }
        /// <summary>Descending straight down: legs reach for the ground and the body flares upright.</summary>
        public float Brake { get; set; }

        /// <summary>How deep into a crouch the body is (0 standing … 1 full), for the take-off wind-up and the landing impact.</summary>
        public float Crouch { get; private set; }
        /// <summary>Seconds since take-off began (large when not taking off); the first strong beat is while this is small.</summary>
        public float TakeoffTime { get; private set; } = 99f;

        private const float WindUp = 0.30f, Spring = 0.22f, Impact = 0.55f;
        private bool _was;
        private float _landing = 99f;
        /// <summary>Seconds since touchdown (large while flying or long after).</summary>
        public float LandingTime => _landing;

        /// <summary>Where the body pitches from (model space, metres up): about the pelvis.</summary>
        public static readonly Vector3 Pivot = new Vector3(0f, 1.0f, 0f);

        private float _w, _c;
        private Transform _spine1, _spine2, _spine3, _neck, _head, _thighL, _thighR, _calfL, _calfR, _armL, _armR, _footL, _footR;

        private void Awake()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "spine_01": _spine1 = t; break;
                    case "spine_02": _spine2 = t; break;
                    case "spine_03": _spine3 = t; break;
                    case "neck_01": _neck = t; break;
                    case "head": _head = t; break;
                    case "thigh_l": _thighL = t; break;
                    case "thigh_r": _thighR = t; break;
                    case "calf_l": _calfL = t; break;
                    case "calf_r": _calfR = t; break;
                    case "upperarm_l": _armL = t; break;
                    case "upperarm_r": _armR = t; break;
                    case "foot_l": _footL = t; break;
                    case "foot_r": _footR = t; break;
                }
            }
        }

        private static void Bend(Transform bone, float degrees)
        {
            if (bone != null)
            {
                bone.localRotation *= Quaternion.AngleAxis(degrees, Vector3.forward);
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private void LateUpdate()
        {
            bool flying = Fly > 0.5f;
            if (flying && !_was)
            {
                TakeoffTime = 0f;
            }
            else if (!flying && _was)
            {
                _landing = 0f;
            }

            _was = flying;
            TakeoffTime += Time.deltaTime;
            _landing += Time.deltaTime;

            // Crouch: loads before the take-off, releases in the spring; absorbs the touchdown on landing.
            float crouch = 0f;
            if (flying && TakeoffTime < WindUp + Spring)
            {
                crouch = TakeoffTime < WindUp ? Smooth(TakeoffTime / WindUp) : 1f - Smooth((TakeoffTime - WindUp) / Spring);
            }
            else if (!flying && _landing < Impact)
            {
                crouch = 0.85f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_landing / Impact));
            }

            Crouch = crouch;
            // The airborne pose comes in as the spring finishes.
            _w = Mathf.MoveTowards(_w, flying && TakeoffTime > WindUp ? 1f : 0f, Time.deltaTime * (flying ? 3.5f : 4f));
            _c = Mathf.Lerp(_c, Cruise, 1f - Mathf.Exp(-4f * Time.deltaTime));
            if (_w <= 0.001f && crouch <= 0.001f)
            {
                return;
            }

            float w = _w, c = _c, b = Brake * w;
            float beat = Beating ? Mathf.Sin(2f * Mathf.PI * Phase) : 0f;
            float lag = Beating ? Mathf.Sin(2f * Mathf.PI * Phase - 0.9f) : 0f;
            // Spine: a gentle arch back, rocking with the wing beat; bowed forward in a crouch.
            Bend(_spine1, w * (4f * c) + w * 2f * beat + crouch * 12f);
            Bend(_spine2, w * (-6f * c) + w * 4f * beat * (0.4f + c) + crouch * 14f);
            if (_spine2 != null)
            {
                // Hovering takes constant small corrections at the chest.
                _spine2.localRotation *= Quaternion.AngleAxis(w * (1f - c) * 2.5f * Mathf.Sin(Time.time * 1.7f), Vector3.up);
            }

            Bend(_spine3, w * (-4f * c) + crouch * 8f);
            // Head and neck lift to look ahead against the pitch (and up out of a crouch).
            Bend(_neck, w * (-22f * c - 4f) - crouch * 20f);
            Bend(_head, w * (-14f * c) - crouch * 12f);
            // Legs trail back from the hips, knees slightly bent; both dangle loosely when hovering; reach down to brake;
            // deep knee and hip bend in a crouch.
            Bend(_thighL, w * (8f + 22f * c) + w * 5f * lag - b * 38f - crouch * 78f);
            Bend(_calfL, w * (14f + 14f * c) + w * 8f * lag + b * 12f + crouch * 105f);
            Bend(_thighR, w * (8f + 22f * c) + w * 4f * lag - b * 30f - crouch * 72f);
            Bend(_calfR, (w * (16f + 20f * c) + w * 8f * lag) * (1f - b * 0.8f) + crouch * 100f);
            // Arms: close to the torso, swept a little back (the right arm's axis runs the other way);
            // both swing forward and out to balance a crouch.
            Bend(_armL, w * (-6f - 10f * c) + w * 4f * beat + crouch * 28f);
            Bend(_armR, w * (-6f + 10f * c) + w * 3f * beat - crouch * 28f);
        }
    }
}
