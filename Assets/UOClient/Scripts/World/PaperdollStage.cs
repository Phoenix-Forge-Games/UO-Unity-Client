using System.Collections.Generic;
using UnityEngine;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The paperdoll's picture: a copy of the player's dressed model, idling far below the world on its own layer,
    /// filmed by its own camera into a render texture the character window shows. Rebuilt when the player's
    /// equipment changes. The camera only renders while the paperdoll is showing.
    /// </summary>
    public sealed class PaperdollStage
    {
        public const int Layer = 31;
        private static readonly Vector3 StagePosition = new Vector3(0f, -2000f, 0f);

        public RenderTexture Texture { get; }

        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly Light _light;
        private GameObject _model;
        private string _signature;
        private float _rebuildAt = -1f;
        private float _turn;

        public PaperdollStage()
        {
            Texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32) { name = "Paperdoll", antiAliasing = 4 };
            _root = new GameObject("Paperdoll Stage");
            _root.transform.position = StagePosition;
            Object.DontDestroyOnLoad(_root);

            var cameraObject = new GameObject("Paperdoll Camera");
            cameraObject.transform.SetParent(_root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.05f, 3.4f);
            cameraObject.transform.LookAt(_root.transform.position + Vector3.up * 0.95f);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.fieldOfView = 34f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;
            _camera.targetTexture = Texture;
            _camera.enabled = false;

            var lightObject = new GameObject("Paperdoll Light");
            lightObject.transform.SetParent(_root.transform, false);
            lightObject.transform.localPosition = new Vector3(1.2f, 2.4f, 2.4f);
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 8f;
            _light.intensity = 3f;
            _light.cullingMask = 1 << Layer;
        }

        public bool Showing
        {
            get => _camera.enabled;
            set
            {
                _camera.enabled = value;
                if (value)
                {
                    _rebuildAt = Time.unscaledTime; // catch up on anything that changed while hidden
                }
            }
        }

        /// <summary>Spin the doll (degrees), e.g. from dragging on it.</summary>
        public void Turn(float degrees) => _turn += degrees;

        public void Update(WorldState world, EntityViews entities)
        {
            if (world?.Player == null || entities == null)
            {
                return;
            }

            Update(Signature(world.Player), entities.Get(world.Player.Serial)?.transform);
        }

        /// <summary>
        /// Show a copy of what's under <paramref name="source"/> (a character's model), rebuilt whenever
        /// <paramref name="signature"/> (what they wear, how they look) changes.
        /// </summary>
        public void Update(string signature, Transform source)
        {
            if (source == null)
            {
                return;
            }

            if (signature != _signature)
            {
                _signature = signature;
                _rebuildAt = Time.unscaledTime + 0.3f; // let the model re-dress first
            }

            if (_rebuildAt >= 0f && Time.unscaledTime >= _rebuildAt && Showing)
            {
                _rebuildAt = -1f;
                Rebuild(source);
            }

            if (_model != null)
            {
                _model.transform.localRotation = Quaternion.Euler(0f, _turn, 0f);
            }
        }

        private static string Signature(Mobile player)
        {
            var parts = new List<string> { player.Body.ToString(), player.Hue.ToString() };
            foreach (KeyValuePair<byte, EquippedItem> pair in player.Equipment)
            {
                parts.Add($"{pair.Key}:{pair.Value.Serial:X}:{pair.Value.Graphic:X}:{pair.Value.Hue:X}");
            }

            parts.Sort();
            return string.Join("|", parts);
        }

        private void Rebuild(Transform view)
        {
            if (_model != null)
            {
                Object.Destroy(_model);
                _model = null;
            }

            if (view == null)
            {
                return;
            }

            _model = new GameObject("Doll");
            _model.transform.SetParent(_root.transform, false);
            foreach (Transform child in view.transform)
            {
                GameObject copy = Object.Instantiate(child.gameObject, _model.transform, false);
                copy.transform.localPosition = child.localPosition;
                copy.transform.localRotation = child.localRotation;
            }

            foreach (Transform t in _model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = Layer;
            }

            // The doll is always drawn: drop anything that hides models by distance (the stage is far below the
            // world) and make sure every renderer is on.
            foreach (MonoBehaviour behaviour in _model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == "ActorCulling")
                {
                    Object.Destroy(behaviour);
                }
            }

            foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
            }

            foreach (Animator animator in _model.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.SetFloat("Speed", 0f);
            }
        }

        public void Dispose()
        {
            Object.Destroy(_root);
            Texture.Release();
            Object.Destroy(Texture);
        }
    }
}
