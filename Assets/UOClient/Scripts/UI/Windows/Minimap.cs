using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The minimap (top right, M toggles): an orthographic camera straight above the player renders our own 3D
    /// world (no UO art) a few times a second, north up and without fog. On top: the player's arrow, dots for
    /// mobiles in notoriety colours, and the names of towns, dungeons and other places (Resources/Places.json,
    /// generated from the shard's regions by Tools/Data/make_places.py). +/- zoom.
    /// </summary>
    public sealed class Minimap
    {
        private const int TextureSize = 384;
        private const float PanelSize = 280f;
        private const float RenderSeconds = 0.2f;
        private static readonly float[] Zooms = { 20f, 35f, 60f, 100f, 160f };

        [Serializable] private sealed class Place { public string Name; public string Kind; public int Facet; public int X; public int Y; }
        [Serializable] private sealed class PlaceList { public Place[] Places; }

        private readonly HudServices _hud;
        private readonly VisualElement _root, _map, _overlay;
        private readonly Label _arrow, _caption;
        private readonly Camera _camera;
        private readonly RenderTexture _texture;
        private readonly Place[] _places;
        private readonly List<Label> _placeLabels = new List<Label>();
        private readonly List<VisualElement> _dots = new List<VisualElement>();
        private int _zoom = 1;
        private float _nextRender;

        public Minimap(HudServices hud, VisualElement hudRoot)
        {
            _hud = hud;
            _texture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32) { name = "Minimap" };
            var go = new GameObject("Minimap Camera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            _camera.cullingMask = ~((1 << PaperdollStage.Layer) | (1 << LayerMask.NameToLayer("UI")));
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 260f;
            _camera.targetTexture = _texture;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // looking down, north (+Z) up

            TextAsset json = Resources.Load<TextAsset>("Places");
            _places = json != null ? JsonUtility.FromJson<PlaceList>(json.text).Places ?? new Place[0] : new Place[0];

            _root = new VisualElement();
            _root.AddToClassList("minimap");
            _map = new VisualElement { pickingMode = PickingMode.Ignore };
            _map.AddToClassList("minimap-map");
            _map.style.backgroundImage = Background.FromRenderTexture(_texture);
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.AddToClassList("minimap-overlay");
            _map.Add(_overlay);
            _arrow = new Label("▲") { pickingMode = PickingMode.Ignore };
            _arrow.AddToClassList("minimap-arrow");
            _map.Add(_arrow);
            _root.Add(_map);

            var bar = new VisualElement();
            bar.AddToClassList("minimap-bar");
            _caption = new Label { pickingMode = PickingMode.Ignore };
            _caption.AddToClassList("minimap-caption");
            bar.Add(_caption);
            var zoomIn = new Button(() => Zoom(-1)) { text = "+" };
            var zoomOut = new Button(() => Zoom(+1)) { text = "−" };
            zoomIn.AddToClassList("qty-button");
            zoomOut.AddToClassList("qty-button");
            bar.Add(zoomOut);
            bar.Add(zoomIn);
            _root.Add(bar);
            hudRoot.Add(_root);
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set => _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Zoom(int step) => _zoom = Mathf.Clamp(_zoom + step, 0, Zooms.Length - 1);

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_camera.gameObject);
            _texture.Release();
            UnityEngine.Object.Destroy(_texture);
            _root.RemoveFromHierarchy();
        }

        public void Update()
        {
            WorldState world = _hud.World;
            MobileView view = world.Player != null ? WorldView.Current?.Entities.Get(world.Player.Serial) : null;
            if (!Visible || view == null)
            {
                return;
            }

            float half = Zooms[_zoom];
            Vector3 centre = view.transform.position;
            if (Time.unscaledTime >= _nextRender)
            {
                _nextRender = Time.unscaledTime + RenderSeconds;
                _camera.orthographicSize = half;
                _camera.transform.position = centre + Vector3.up * 150f;
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false; // the map is seen from far above; fog would wash it out
                _camera.Render();
                RenderSettings.fog = fog;
            }

            Mobile player = world.Player;
            _arrow.style.rotate = new Rotate(DirectionUtil.Yaw(player.Facing));
            float pixelsPerMetre = PanelSize / (2f * half);
            Vector2 ToPanel(int x, int y) => new Vector2(
                PanelSize * 0.5f + (x - player.X) * WorldSpace.TileSize * pixelsPerMetre,
                PanelSize * 0.5f + (y - player.Y) * WorldSpace.TileSize * pixelsPerMetre);
            bool Inside(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= PanelSize && p.y <= PanelSize;

            // Mobiles.
            int dot = 0;
            foreach (Mobile mobile in world.Mobiles.Values)
            {
                if (mobile == player)
                {
                    continue;
                }

                Vector2 p = ToPanel(mobile.X, mobile.Y);
                if (!Inside(p))
                {
                    continue;
                }

                if (dot == _dots.Count)
                {
                    var d = new VisualElement { pickingMode = PickingMode.Ignore };
                    d.AddToClassList("minimap-dot");
                    _overlay.Add(d);
                    _dots.Add(d);
                }

                VisualElement e = _dots[dot++];
                e.style.display = DisplayStyle.Flex;
                e.style.left = p.x;
                e.style.top = p.y;
                e.style.backgroundColor = HudController.NotorietyColour(mobile.Notoriety);
            }

            for (int i = dot; i < _dots.Count; i++)
            {
                _dots[i].style.display = DisplayStyle.None;
            }

            // Place names on this facet.
            int label = 0;
            string nearest = null;
            int nearestDistance = int.MaxValue;
            foreach (Place place in _places)
            {
                if (place.Facet != world.Facet)
                {
                    continue;
                }

                int distance = Math.Max(Math.Abs(place.X - player.X), Math.Abs(place.Y - player.Y));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = place.Name;
                }

                Vector2 p = ToPanel(place.X, place.Y);
                if (!Inside(p))
                {
                    continue;
                }

                if (label == _placeLabels.Count)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.AddToClassList("minimap-place");
                    _overlay.Add(l);
                    _placeLabels.Add(l);
                }

                Label t = _placeLabels[label++];
                t.text = place.Name;
                t.style.display = DisplayStyle.Flex;
                t.style.left = p.x;
                t.style.top = p.y;
                t.EnableInClassList("minimap-town", place.Kind == "town");
                t.EnableInClassList("minimap-dungeon", place.Kind == "dungeon");
            }

            for (int i = label; i < _placeLabels.Count; i++)
            {
                _placeLabels[i].style.display = DisplayStyle.None;
            }

            string near = nearest != null && nearestDistance < 150 ? (nearestDistance < 60 ? nearest : "near " + nearest) : "wilderness";
            _caption.text = $"{near} · {player.X}, {player.Y}";
        }
    }
}
