using System.Collections.Generic;
using UnityEngine;
using UOClient.Bake;

namespace UOGame
{
    /// <summary>
    /// The baked world in the game scene: the server keeps ground loaded under every player, a client under its own.
    /// (In a hosted game both happen in one process and share the loaded chunks.)
    /// </summary>
    public sealed class GameWorld : MonoBehaviour
    {
        [SerializeField] private BakedWorldView _view;
        /// <summary>Content/World.json: the clock, light, swimming and falling.</summary>
        [SerializeField] private TextAsset _worldRules;

        private static GameWorld _current;
        private static readonly List<Transform> Followed = new List<Transform>();

        private void Awake()
        {
            _current = this;
            Content.WorldRules.Load(_worldRules);
            if (_worldRules != null)
            {
                Net.Protocol.AddDataFile(_worldRules);
                global::UOGame.Content.LiveContent.Register(_worldRules, Content.WorldRules.Load);
            }

            if (!Application.isBatchMode && GetComponent<DayNight>() == null)
            {
                gameObject.AddComponent<DayNight>(); // a client: the sun, sky and light follow UO's clock
            }

            if (GetComponent<Server.NavMeshService>() == null)
            {
                gameObject.AddComponent<Server.NavMeshService>(); // the server's navmesh, built as chunks load
            }

            if (!Application.isBatchMode && GetComponent<WorldLights>() == null)
            {
                gameObject.AddComponent<WorldLights>(); // lamps and torches in the world light up at night
            }

            if (_view != null && string.IsNullOrEmpty(_view.Folder))
            {
                _view.Folder = WorldFolder;
            }
        }

        private void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        /// <summary>Where the baked chunks are: next to the project in the editor, next to the game in a build.</summary>
        public static string WorldFolder =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "WorldData"));

        /// <summary>Keep the world loaded around this character.</summary>
        public static void Follow(Transform character)
        {
            if (!Followed.Contains(character))
            {
                Followed.Add(character);
            }
        }

        public static void Unfollow(Transform character) => Followed.Remove(character);

        /// <summary>Is the ground under this point loaded? Characters wait (no gravity) until it is.</summary>
        public static bool IsReadyAt(Vector3 position) =>
            _current == null || _current._view == null || _current._view.IsLoadedAt(position);

        /// <summary>Chunks loaded each way for the View distance setting (Near … Very far); a chunk is 80 m.</summary>
        private static readonly int[] ViewChunks = { 3, 4, 5, 6 };
        private int _fogRadius;

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            int radius = ViewChunks[Mathf.Clamp(UOClient.GameSettings.Current.ViewDistance, 0, ViewChunks.Length - 1)];
            _view.RadiusChunks = radius;
            if (radius != _fogRadius && !Application.isBatchMode)
            {
                // Haze that is total just inside the edge of what's loaded, so the world fades out rather than ending.
                _fogRadius = radius;
                float reach = radius * 80f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = reach * 0.45f;
                RenderSettings.fogEndDistance = reach * 0.95f;
            }

            _view.Targets.Clear();
            Followed.RemoveAll(t => t == null);
            _view.Targets.AddRange(Followed);
        }
    }
}
