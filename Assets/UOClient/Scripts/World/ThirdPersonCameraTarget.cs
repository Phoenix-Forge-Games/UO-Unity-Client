using Unity.Cinemachine;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// The pivot a Cinemachine Third Person Follow camera tracks. It follows the player's
    /// position at shoulder height but keeps its own yaw and pitch, so the character's 8-way
    /// facing snaps never jerk the camera. Hold the right mouse button to look around (the
    /// cursor stays free otherwise for clicking the world); scroll zooms the camera arm.
    /// </summary>
    public sealed class ThirdPersonCameraTarget : MonoBehaviour
    {
        [SerializeField] private CinemachineThirdPersonFollow _follow;
        [SerializeField] private float _height = 1.6f;
        [SerializeField] private float _lookSensitivity = 0.15f;
        [SerializeField] private float _minPitch = -30f;
        [SerializeField] private float _maxPitch = 70f;
        [SerializeField] private float _minDistance = 1.5f;
        [SerializeField] private float _maxDistance = 12f;
        [SerializeField] private float _zoomStep = 0.12f;

        private float _yaw;
        private float _pitch = 15f;

        /// <summary>The player view to follow.</summary>
        public Transform Target { get; set; }

        /// <summary>Camera heading in degrees; 0 looks north (Unity +Z). Movement is relative to it.</summary>
        public float Yaw => _yaw;

        /// <summary>Jumps to the target and faces the same way it does.</summary>
        public void SnapToTarget()
        {
            if (Target == null)
            {
                return;
            }

            _yaw = Target.eulerAngles.y;
            transform.SetPositionAndRotation(Target.position + Vector3.up * _height, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        private void LateUpdate()
        {
            ReadInput();
            if (Target != null)
            {
                transform.position = Target.position + Vector3.up * _height;
            }

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void ReadInput()
        {
            if (GameInput.Gameplay.LookHold.IsPressed())
            {
                Vector2 delta = GameInput.Gameplay.Look.ReadValue<Vector2>();
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, _minPitch, _maxPitch);
            }

            // Scrolling over the HUD (chat log) scrolls the UI, not the camera.
            float scroll = GameInput.IsPointerOverUI(GameInput.Gameplay.Point.ReadValue<Vector2>()) ? 0f : GameInput.Gameplay.Zoom.ReadValue<float>();
            if (_follow != null && Mathf.Abs(scroll) > 0.01f)
            {
                float factor = scroll > 0 ? 1f - _zoomStep : 1f + _zoomStep;
                _follow.CameraDistance = Mathf.Clamp(_follow.CameraDistance * factor, _minDistance, _maxDistance);
            }
        }
    }
}
