using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOClient.Data;
using UOGame.Content;

namespace UOGame.UI
{
    /// <summary>
    /// Choosing where a new character begins: a map of Britannia with the starting cities marked; click a city.
    /// The map is our own picture, drawn on this machine from the map data (each spot coloured by its ground: sea,
    /// grass, forest, sand, snow, rock, towns, shaded by height), never UO's art. Drawn once, in the background.
    /// </summary>
    public sealed class StartCityMap : VisualElement
    {
        /// <summary>The mainland part of Trammel (tiles); the dungeons lie east of it.</summary>
        private const int MapWidth = 5120, MapHeight = 4096;
        /// <summary>Tiles per map pixel.</summary>
        private const int Step = 8;

        private static Texture2D _texture;
        private static Task<Color32[]> _drawing;

        private readonly VisualElement _picture;
        private readonly Label _caption;
        private readonly Dictionary<int, VisualElement> _markers = new Dictionary<int, VisualElement>();
        private readonly Action<int> _chose;

        public StartCityMap(Action<int> chose)
        {
            _chose = chose;
            AddToClassList("city-map");
            _picture = new VisualElement();
            _picture.AddToClassList("city-map-picture");
            // Keep the map's shape (5:4) whatever width the panel gives it.
            _picture.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float height = Mathf.Round(e.newRect.width * MapHeight / MapWidth);
                if (e.newRect.width > 0f && Mathf.Abs(_picture.resolvedStyle.height - height) > 1f)
                {
                    _picture.style.height = height;
                }
            });
            Add(_picture);
            _caption = new Label();
            _caption.AddToClassList("city-map-caption");
            Add(_caption);
            Draw();
        }

        /// <summary>Put a marker on each starting city (Creation.json StartCities).</summary>
        public void Build()
        {
            foreach (VisualElement m in _markers.Values)
            {
                m.RemoveFromHierarchy();
            }

            _markers.Clear();
            for (int i = 0; i < CreationRules.StartCities.Count; i++)
            {
                int index = i;
                CreationRules.StartCity city = CreationRules.StartCities[i];
                var marker = new VisualElement { tooltip = city.Place };
                marker.AddToClassList("city-marker");
                marker.style.left = Length.Percent(100f * city.X / MapWidth);
                marker.style.top = Length.Percent(100f * city.Y / MapHeight);
                var dot = new VisualElement();
                dot.AddToClassList("city-marker-dot");
                marker.Add(dot);
                var name = new Label(city.Name);
                name.AddToClassList("city-marker-name");
                marker.Add(name);
                marker.RegisterCallback<ClickEvent>(_ => _chose(index));
                _markers[i] = marker;
                _picture.Add(marker);
            }
        }

        /// <summary>Show this city as the chosen one.</summary>
        public void Show(int index)
        {
            foreach (KeyValuePair<int, VisualElement> pair in _markers)
            {
                pair.Value.EnableInClassList("city-marker-chosen", pair.Key == index);
            }

            CreationRules.StartCity city = index >= 0 && index < CreationRules.StartCities.Count ? CreationRules.StartCities[index] : null;
            _caption.text = city != null ? $"{city.Name}: {city.Place}" : string.Empty;
        }

        // ------------------------------------------------------------------ the picture

        private void Draw()
        {
            if (_texture != null)
            {
                _picture.style.backgroundImage = new StyleBackground(_texture);
                return;
            }

            if (_drawing == null)
            {
                TileData tiles = UoData.Tiles;
                if (tiles == null)
                {
                    return; // no UO data: the markers alone, on the dark background
                }

                if (!LandClassifier.IsInitialized)
                {
                    LandClassifier.Initialize(tiles);
                }

                string folder = ClientSettings.DataFolder;
                _drawing = Task.Run(() => Colours(folder));
            }

            schedule.Execute(() =>
            {
                if (_texture == null && _drawing.IsCompleted && !_drawing.IsFaulted)
                {
                    _texture = new Texture2D(MapWidth / Step, MapHeight / Step, TextureFormat.RGBA32, false)
                    {
                        name = "Start city map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                    };
                    _texture.SetPixels32(_drawing.Result);
                    _texture.Apply(false, true);
                }

                if (_texture != null)
                {
                    _picture.style.backgroundImage = new StyleBackground(_texture);
                }
            }).Every(150).Until(() => _texture != null || (_drawing != null && _drawing.IsFaulted));
        }

        /// <summary>The map's pixels (bottom row first, as a texture wants them): ground colour, shaded by slope.</summary>
        private static Color32[] Colours(string folder)
        {
            int w = MapWidth / Step, h = MapHeight / Step;
            var pixels = new Color32[w * h];
            var z = new int[w * h];
            using (var map = new MapFacet(folder, 1))
            {
                for (int py = 0; py < h; py++)
                {
                    for (int px = 0; px < w; px++)
                    {
                        LandCell cell = map.GetLand(px * Step + Step / 2, py * Step + Step / 2);
                        int i = (h - 1 - py) * w + px;
                        pixels[i] = Ground(LandClassifier.Get(cell.TileId));
                        z[i] = cell.Z;
                    }
                }
            }

            // Light from the north-west: a slope facing it is lighter, one facing away darker (not on the sea).
            for (int py = 1; py < h; py++)
            {
                for (int px = 1; px < w; px++)
                {
                    int i = (h - 1 - py) * w + px;
                    int nw = (h - py) * w + px - 1;
                    float shade = Mathf.Clamp(1f + (z[nw] - z[i]) * -0.025f, 0.7f, 1.25f);
                    Color32 c = pixels[i];
                    pixels[i] = new Color32((byte)Mathf.Min(255, c.r * shade), (byte)Mathf.Min(255, c.g * shade), (byte)Mathf.Min(255, c.b * shade), 255);
                }
            }

            return pixels;
        }

        private static Color32 Ground(LandKind kind) => kind switch
        {
            LandKind.Underwater => new Color32(38, 66, 96, 255),
            LandKind.Grass => new Color32(92, 118, 58, 255),
            LandKind.Forest => new Color32(52, 84, 44, 255),
            LandKind.Jungle => new Color32(46, 92, 52, 255),
            LandKind.Furrows => new Color32(118, 100, 64, 255),
            LandKind.Sand => new Color32(196, 176, 126, 255),
            LandKind.Snow => new Color32(222, 226, 230, 255),
            LandKind.Rock or LandKind.Unnamed => new Color32(112, 104, 94, 255), // unnamed land is mostly under mountain rock
            LandKind.Cave => new Color32(70, 64, 58, 255),
            LandKind.Lava => new Color32(140, 52, 30, 255),
            LandKind.Swamp => new Color32(70, 82, 52, 255),
            LandKind.Dirt => new Color32(122, 102, 70, 255),
            LandKind.Town or LandKind.Cobblestones or LandKind.Flagstones or LandKind.BrickPaving or LandKind.Terrace => new Color32(150, 138, 118, 255),
            _ => new Color32(24, 22, 20, 255),
        };
    }
}
