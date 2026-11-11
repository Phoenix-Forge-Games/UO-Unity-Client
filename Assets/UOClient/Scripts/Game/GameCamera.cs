using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// Third-person orbit camera on the local player, MMO style: hold Look (right mouse) and move the mouse to turn it,
    /// scroll to zoom. Movement is relative to where it looks (<see cref="Yaw"/>). It pulls in when a wall or the ground
    /// is between it and the player.
    /// </summary>
    public sealed class GameCamera : MonoBehaviour
    {
        [SerializeField] private float _height = 1.7f;
        [SerializeField] private float _sensitivity = 0.15f;
        [SerializeField] private float _minPitch = -35f;
        [SerializeField] private float _maxPitch = 75f;
        [SerializeField] private float _minDistance = 1.5f;
        [SerializeField] private float _maxDistance = 18f;
        [SerializeField] private float _zoomStep = 0.12f;

        /// <summary>The camera's heading (degrees about Y): forward for movement.</summary>
        public static float Yaw { get; private set; }

        private float _pitch = 20f;
        private float _distance = 7f;
        private const float Radius = 0.25f;

        private void LateUpdate()
        {
            PlayerMotor player = PlayerMotor.Local;
            if (player == null)
            {
                return;
            }

            if (!GameInput.Typing && GameInput.Controls.Gameplay.LookHold.IsPressed())
            {
                Vector2 look = GameInput.Controls.Gameplay.Look.ReadValue<Vector2>() * _sensitivity;
                Yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, _minPitch, _maxPitch);
            }

            float scroll = GameInput.Controls.Gameplay.Zoom.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f && !GameInput.IsPointerOverUI(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>()))
            {
                _distance = Mathf.Clamp(_distance * (1f - Mathf.Sign(scroll) * _zoomStep), _minDistance, _maxDistance);
            }

            // Follow the smoothed model rather than the raw networked position.
            Transform followed = player.NetworkObject.GetGraphicalObject() ?? player.transform;
            Vector3 focus = followed.position + Vector3.up * _height;
            Quaternion rotation = Quaternion.Euler(_pitch, Yaw, 0f);
            Vector3 back = rotation * Vector3.back;
            float distance = _distance;
            if (Physics.SphereCast(focus, Radius, back, out RaycastHit hit, _distance, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(player.transform))
            {
                distance = Mathf.Max(hit.distance, 0.3f);
            }

            transform.SetPositionAndRotation(focus + back * distance, rotation);
        }
    }
}
