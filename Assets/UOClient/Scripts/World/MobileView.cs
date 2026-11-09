using UnityEngine;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The visible stand-in for one mobile. Glides toward the mobile's tile at walking or running
    /// pace, turns to face its direction, snaps when the gap is too large to be a step, and drives
    /// the locomotion Animator (when the model has one) from that movement.
    /// </summary>
    public sealed class MobileView : MonoBehaviour
    {
        private const float WalkMetresPerSecond = WorldSpace.TileSize / 0.4f;
        private const float RunMetresPerSecond = WorldSpace.TileSize / 0.2f;
        private const float SnapDistance = 4f * WorldSpace.TileSize;
        private const float TurnDegreesPerSecond = 720f;

        // Steps arrive one tile at a time, so treat the mobile as still moving for a little longer
        // than one step interval; otherwise the animation flickers to idle between steps.
        private const float WalkHoldSeconds = 0.5f;
        private const float RunHoldSeconds = 0.3f;

        // Animator Speed values, matching the AC_UOHumanoid transitions.
        private const float IdleSpeed = 0f;
        private const float WalkSpeed = 1f;
        private const float RunSpeed = 2f;
        private const float SpeedDampSeconds = 0.08f;

        // UO walking (2.5 tiles/s) and running (5 tiles/s) are brisker than the clips' natural
        // pace, so play them faster to keep the feet planted.
        private const float WalkPlaybackRate = 1.6f;
        private const float RunPlaybackRate = 1.25f;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        private Mobile _mobile;
        private Animator _animator;
        private Vector3 _target;
        private Quaternion _targetRotation;
        private float _lastStepTime = -10f;

        public Mobile Mobile => _mobile;

        public void Bind(Mobile mobile, Animator animator)
        {
            _mobile = mobile;
            _animator = animator;
            Refresh();
            Snap();
        }

        /// <summary>Swaps the Animator driven by movement (the model was rebuilt with new clothes).</summary>
        public void SetAnimator(Animator animator) => _animator = animator;

        /// <summary>Re-reads position and facing from the mobile.</summary>
        public void Refresh()
        {
            Vector3 target = WorldSpace.TileCenter(_mobile.X, _mobile.Y, _mobile.Z);
            if ((target - _target).sqrMagnitude > 0.0001f)
            {
                _lastStepTime = Time.time;
            }

            _target = target;
            _targetRotation = Quaternion.Euler(0f, DirectionUtil.Yaw(_mobile.Facing), 0f);
        }

        public void Snap()
        {
            transform.SetPositionAndRotation(_target, _targetRotation);
            _lastStepTime = -10f;
        }

        private void Update()
        {
            if (_mobile == null)
            {
                return;
            }

            Vector3 position = transform.position;
            if ((position - _target).sqrMagnitude > SnapDistance * SnapDistance)
            {
                Snap();
            }
            else
            {
                float speed = (_mobile.Running ? RunMetresPerSecond : WalkMetresPerSecond) * 1.15f;
                transform.position = Vector3.MoveTowards(position, _target, speed * Time.deltaTime);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    _targetRotation,
                    TurnDegreesPerSecond * Time.deltaTime
                );
            }

            UpdateAnimator();
        }

        private void UpdateAnimator()
        {
            if (_animator == null)
            {
                return;
            }

            float hold = _mobile.Running ? RunHoldSeconds : WalkHoldSeconds;
            bool moving = Time.time - _lastStepTime < hold;
            float speed = moving ? (_mobile.Running ? RunSpeed : WalkSpeed) : IdleSpeed;

            _animator.SetFloat(SpeedParameter, speed, SpeedDampSeconds, Time.deltaTime);
            _animator.speed = moving ? (_mobile.Running ? RunPlaybackRate : WalkPlaybackRate) : 1f;
        }
    }
}
