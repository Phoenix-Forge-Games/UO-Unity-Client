using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The in-world HUD (Hud.uxml): player frame (hits / mana / stamina, war mode), target frame, chat (log and an
    /// input line opened with Enter), and over the world: name plates in notoriety colours, health bars, speech
    /// bubbles and damage numbers. Speech prefixes: "! " yell, "; " whisper, ": " emote.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    [RequireComponent(typeof(Targeting))]
    public sealed partial class HudController : MonoBehaviour
    {
        private const int PlateRangeTiles = 18;
        private const int MaxChatLines = 200;
        private const int MaxBubbles = 3;
        private const float DamageSeconds = 1.2f;
        private const float SightCheckSeconds = 0.15f;

        private sealed class Plate
        {
            public VisualElement Root, SpeechBox, Health, HealthFill;
            public Label Name;
            public readonly List<(Label Line, float Until)> Bubbles = new List<(Label, float)>();
            public Vector3 Anchor = Vector3.up * 2f;
            public float AnchorCheckedAt = -10f;
            /// <summary>Whether the camera can see the mobile (terrain or a building isn't in the way).</summary>
            public bool InSight = true;
            public float SightCheckedAt = -10f;
            public bool IsItem;
        }

        private sealed class DamageNumber
        {
            public Label Label;
            public uint Serial;
            public float Born;
        }

        private VisualElement _root, _overhead, _playerFrame, _targetFrame, _warBadge, _targetFighting;
        private Label _playerName, _targetName;
        private VisualElement _playerHits, _playerMana, _playerStamina, _targetHits;
        private ScrollView _chatLog;
        private TextField _chatInput;
        private float _chatOpenedAt;

        private WorldState _world;
        private Targeting _targeting;
        private readonly Dictionary<uint, Plate> _plates = new Dictionary<uint, Plate>();
        private readonly List<DamageNumber> _damage = new List<DamageNumber>();
        private readonly List<SpeechMessage> _pendingLog = new List<SpeechMessage>();
        private readonly List<uint> _scratch = new List<uint>();

        private void OnEnable() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);

        private void OnDisable() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            _root = root;
            _overhead = root.Q("overhead-layer");
            _playerFrame = root.Q("player-frame");
            _targetFrame = root.Q("target-frame");
            _warBadge = root.Q("war-badge");
            _targetFighting = root.Q("target-fighting");
            _playerName = root.Q<Label>("player-name");
            _targetName = root.Q<Label>("target-name");
            _playerHits = root.Q("player-hits");
            _playerMana = root.Q("player-mana");
            _playerStamina = root.Q("player-stamina");
            _targetHits = root.Q("target-hits");
            _chatLog = root.Q<ScrollView>("chat-log");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.RegisterCallback<KeyDownEvent>(OnChatKey, TrickleDown.TrickleDown);
            _chatInput.RegisterCallback<FocusOutEvent>(_ => CloseChat(false));
            _plates.Clear();
            _damage.Clear();
            foreach (SpeechMessage message in _pendingLog)
            {
                Log(message);
            }

            _pendingLog.Clear();
            BuildWindows();
        }

        private void Start()
        {
            _targeting = GetComponent<Targeting>();
            GameInput.PointerOverUI = IsOverUI;
            _world = UoClientRuntime.Instance != null ? UoClientRuntime.Instance.World : null;
            if (_world == null)
            {
                enabled = false;
                return;
            }

            _world.Speech += OnSpeech;
            _world.Damaged += OnDamaged;
            _world.MobileRemoved += OnRemoved;
            _world.ItemRemoved += OnRemoved;
            _targeting.TalkTo += serial => { BuildWindows(); _dialogue?.Talk(serial); };
            if (Camera.main != null)
            {
                Camera.main.cullingMask &= ~(1 << PaperdollStage.Layer); // the paperdoll's doll is only for its camera
            }

            BuildWindows();
        }

        private void OnDestroy()
        {
            DisposeWindows();
            if (_world != null)
            {
                _world.Speech -= OnSpeech;
                _world.Damaged -= OnDamaged;
                _world.MobileRemoved -= OnRemoved;
                _world.ItemRemoved -= OnRemoved;
            }

            if (GameInput.Typing)
            {
                GameInput.SetTyping(false);
            }
        }

        private void Update()
        {
            if (_root == null || _world?.Player == null)
            {
                return;
            }

            if (!GameInput.Typing && GameInput.Gameplay.Chat.WasPressedThisFrame())
            {
                OpenChat();
            }

            UpdateWindows();
            RefreshPlayerFrame();
            RefreshTargetFrame();
            UpdateOverhead();
        }

        // ---- Frames ----

        private void RefreshPlayerFrame()
        {
            Mobile player = _world.Player;
            _playerName.text = string.IsNullOrEmpty(player.Name) ? "You" : player.Name;
            SetBar(_playerHits, player.Hits, player.HitsMax);
            SetBar(_playerMana, player.Mana, player.ManaMax);
            SetBar(_playerStamina, player.Stamina, player.StaminaMax);
            _warBadge.EnableInClassList("hidden", !_world.WarMode);
        }

        private void RefreshTargetFrame()
        {
            uint serial = _targeting.Current;
            if (serial == 0)
            {
                _targetFrame.AddToClassList("hidden");
                return;
            }

            _targetFrame.RemoveFromClassList("hidden");
            if (_world.Mobiles.TryGetValue(serial, out Mobile mobile))
            {
                _targetName.text = string.IsNullOrEmpty(mobile.Name) ? "..." : mobile.Name;
                _targetName.style.color = NotorietyColour(mobile.Notoriety);
                _targetHits.style.display = DisplayStyle.Flex;
                float fraction = mobile.HealthFraction;
                SetBar(_targetHits, fraction < 0 ? 0 : Mathf.RoundToInt(fraction * 100f), fraction < 0 ? 0 : 100, "%");
                _targetFighting.EnableInClassList("hidden", _world.Combatant != serial);
            }
            else
            {
                _targetName.text = _plates.TryGetValue(serial, out Plate plate) && plate.Name.text.Length > 0 ? plate.Name.text : "Item";
                _targetName.style.color = new Color(0.88f, 0.86f, 0.8f);
                _targetHits.style.display = DisplayStyle.None;
                _targetFighting.AddToClassList("hidden");
            }
        }

        private static void SetBar(VisualElement bar, int current, int max, string suffix = null)
        {
            float fraction = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            bar.Q(className: "bar-fill").style.width = Length.Percent(fraction * 100f);
            bar.Q<Label>(className: "bar-text").text = max <= 0 ? "?" : suffix != null ? $"{current}{suffix}" : $"{current} / {max}";
        }

        private static bool IsHostile(Mobile mobile) =>
            mobile.Notoriety != Notoriety.Innocent && mobile.Notoriety != Notoriety.Ally && mobile.Notoriety != Notoriety.Invulnerable;

        public static Color NotorietyColour(Notoriety notoriety)
        {
            switch (notoriety)
            {
                case Notoriety.Innocent: return new Color(0.35f, 0.65f, 1f);
                case Notoriety.Ally: return new Color(0.35f, 0.9f, 0.35f);
                case Notoriety.Gray:
                case Notoriety.Criminal: return new Color(0.7f, 0.7f, 0.7f);
                case Notoriety.Enemy: return new Color(1f, 0.6f, 0.2f);
                case Notoriety.Murderer: return new Color(1f, 0.25f, 0.2f);
                case Notoriety.Invulnerable: return new Color(1f, 0.9f, 0.3f);
                default: return new Color(0.9f, 0.9f, 0.9f);
            }
        }

        // ---- Chat ----

        private void OpenChat()
        {
            _chatInput.RemoveFromClassList("hidden");
            _chatInput.value = string.Empty;
            _chatOpenedAt = Time.unscaledTime;
            GameInput.SetTyping(true);
            // After the field is laid out again (a hidden field can't take focus).
            _chatInput.schedule.Execute(() => _chatInput.Focus()).StartingIn(30);
        }

        private void CloseChat(bool keepFocus)
        {
            if (_chatInput == null)
            {
                return;
            }

            _chatInput.AddToClassList("hidden");
            if (!keepFocus)
            {
                _chatInput.Blur();
            }

            if (GameInput.Typing)
            {
                GameInput.SetTyping(false);
            }
        }

        private void OnChatKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopPropagation();
                string text = _chatInput.value?.Trim() ?? string.Empty;
                if (text.Length == 0 && Time.unscaledTime - _chatOpenedAt < 0.25f)
                {
                    return; // the Enter that opened the line
                }

                if (text.Length > 0)
                {
                    Send(text);
                }

                _chatInput.value = string.Empty;
                CloseChat(false);
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                _chatInput.value = string.Empty;
                CloseChat(false);
            }
        }

        private void Send(string text)
        {
            MessageType type = MessageType.Regular;
            if (text.StartsWith("! ")) { type = MessageType.Yell; text = text.Substring(2); }
            else if (text.StartsWith("; ")) { type = MessageType.Whisper; text = text.Substring(2); }
            else if (text.StartsWith(": ")) { type = MessageType.Emote; text = text.Substring(2); }

            _world.Say(text, type);
        }

        private void OnSpeech(SpeechMessage message)
        {
            if (_chatLog == null)
            {
                _pendingLog.Add(message);
                return;
            }

            Log(message);
            if (!message.IsSystem)
            {
                Bubble(message);
            }
        }

        private void Log(SpeechMessage message)
        {
            string text;
            if (message.IsSystem || string.IsNullOrEmpty(message.Name) || message.Type == MessageType.Label)
            {
                text = message.Text;
            }
            else if (message.Type == MessageType.Emote)
            {
                text = $"{message.Name} {message.Text}";
            }
            else
            {
                text = $"{message.Name}: {message.Text}";
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var line = new Label(text);
            line.AddToClassList("chat-line");
            line.style.color = ChatColour(message);
            _chatLog.Add(line);
            while (_chatLog.contentContainer.childCount > MaxChatLines)
            {
                _chatLog.contentContainer.RemoveAt(0);
            }

            _chatLog.schedule.Execute(() => _chatLog.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private static Color ChatColour(SpeechMessage message)
        {
            if (message.IsSystem)
            {
                return new Color(0.92f, 0.84f, 0.56f);
            }

            switch (message.Type)
            {
                case MessageType.Emote: return new Color(0.95f, 0.72f, 0.5f);
                case MessageType.Whisper: return new Color(0.65f, 0.65f, 0.85f);
                case MessageType.Yell: return new Color(1f, 0.5f, 0.42f);
                case MessageType.Label: return new Color(0.72f, 0.72f, 0.72f);
                case MessageType.Spell: return new Color(0.55f, 0.75f, 1f);
                case MessageType.Guild: return new Color(0.45f, 0.95f, 0.45f);
                case MessageType.Alliance: return new Color(0.4f, 0.9f, 0.85f);
                default: return new Color(0.96f, 0.95f, 0.92f);
            }
        }

        // ---- Over the world ----

        private void Bubble(SpeechMessage message)
        {
            if (message.Serial == 0 || message.Serial == 0xFFFFFFFF || _overhead == null)
            {
                return;
            }

            bool isItem = !_world.Mobiles.ContainsKey(message.Serial);
            if (isItem && !_world.Items.ContainsKey(message.Serial))
            {
                return;
            }

            Plate plate = GetPlate(message.Serial, isItem);
            if (message.Type == MessageType.Label && isItem)
            {
                plate.Name.text = message.Text; // an item's name
            }

            if (message.Type == MessageType.Label && !isItem)
            {
                return; // a mobile's name label: the plate shows it already
            }

            var line = new Label(message.Text);
            line.AddToClassList("plate-speech");
            line.style.color = ChatColour(message);
            plate.SpeechBox.Add(line);
            plate.Bubbles.Add((line, Time.unscaledTime + 4f + message.Text.Length * 0.05f));
            while (plate.Bubbles.Count > MaxBubbles)
            {
                plate.Bubbles[0].Line.RemoveFromHierarchy();
                plate.Bubbles.RemoveAt(0);
            }
        }

        private Plate GetPlate(uint serial, bool isItem)
        {
            if (_plates.TryGetValue(serial, out Plate plate))
            {
                return plate;
            }

            plate = new Plate { Root = new VisualElement(), SpeechBox = new VisualElement(), Name = new Label(), IsItem = isItem };
            plate.Root.AddToClassList("plate");
            plate.Root.pickingMode = PickingMode.Ignore;
            plate.SpeechBox.pickingMode = PickingMode.Ignore;
            plate.Name.AddToClassList("plate-name");
            plate.Name.pickingMode = PickingMode.Ignore;
            plate.Health = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.Health.AddToClassList("plate-health");
            plate.HealthFill = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.HealthFill.AddToClassList("plate-health-fill");
            plate.Health.Add(plate.HealthFill);
            plate.Root.Add(plate.SpeechBox);
            plate.Root.Add(plate.Name);
            plate.Root.Add(plate.Health);
            _overhead.Add(plate.Root);
            _plates[serial] = plate;
            return plate;
        }

        private void OnRemoved(uint serial)
        {
            if (_plates.TryGetValue(serial, out Plate plate))
            {
                plate.Root.RemoveFromHierarchy();
                _plates.Remove(serial);
            }
        }

        private void OnDamaged(uint serial, int amount)
        {
            if (_overhead == null || amount <= 0 || !GameSettings.Current.ShowDamageNumbers)
            {
                return;
            }

            var label = new Label(amount.ToString()) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("damage");
            if (serial == _world.Player.Serial)
            {
                label.style.color = new Color(1f, 0.85f, 0.3f);
            }

            _overhead.Add(label);
            _damage.Add(new DamageNumber { Label = label, Serial = serial, Born = Time.unscaledTime });
        }

        private void UpdateOverhead()
        {
            Camera camera = Camera.main;
            WorldView view = WorldView.Current;
            if (camera == null || view == null || _overhead.panel == null)
            {
                return;
            }

            Mobile player = _world.Player;
            float now = Time.unscaledTime;

            // Plates for every mobile in range (names asked for once), and items that are speaking.
            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                if (Mathf.Max(Mathf.Abs(mobile.X - player.X), Mathf.Abs(mobile.Y - player.Y)) > PlateRangeTiles ||
                    !MobileView.IsVisible(mobile))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(mobile.Name) && mobile != player)
                {
                    _world.RequestName(mobile.Serial);
                }

                GetPlate(mobile.Serial, false);
            }

            _scratch.Clear();
            foreach (KeyValuePair<uint, Plate> pair in _plates)
            {
                uint serial = pair.Key;
                Plate plate = pair.Value;
                for (int i = plate.Bubbles.Count - 1; i >= 0; i--)
                {
                    if (plate.Bubbles[i].Until < now)
                    {
                        plate.Bubbles[i].Line.RemoveFromHierarchy();
                        plate.Bubbles.RemoveAt(i);
                    }
                }

                GameObject go = null;
                bool show;
                if (plate.IsItem)
                {
                    foreach (KeyValuePair<uint, GameObject> item in view.Entities.Items)
                    {
                        if (item.Key == serial)
                        {
                            go = item.Value;
                            break;
                        }
                    }

                    bool selected = serial == _targeting.Current;
                    show = go != null && (plate.Bubbles.Count > 0 || selected);
                    plate.Name.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
                    plate.Health.style.display = DisplayStyle.None;
                    if (go == null && plate.Bubbles.Count == 0 && !selected)
                    {
                        _scratch.Add(serial);
                    }
                }
                else
                {
                    MobileView mv = view.Entities.Get(serial);
                    go = mv != null ? mv.gameObject : null;
                    if (!_world.Mobiles.TryGetValue(serial, out Mobile mobile) || go == null || !MobileView.IsVisible(mobile) ||
                        Mathf.Max(Mathf.Abs(mobile.X - player.X), Mathf.Abs(mobile.Y - player.Y)) > PlateRangeTiles)
                    {
                        _scratch.Add(serial);
                        continue;
                    }

                    bool isPlayer = mobile == player;
                    bool targeted = serial == _targeting.Current;
                    plate.Name.text = mobile.Name;
                    plate.Name.style.color = NotorietyColour(mobile.Notoriety);
                    NamePlateMode mode = GameSettings.Current.NamePlates;
                    bool wanted = mode == NamePlateMode.All || mode == NamePlateMode.HostileAndTarget && (targeted || IsHostile(mobile));
                    plate.Name.style.display = !isPlayer && wanted && mobile.Name.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    plate.Root.EnableInClassList("plate-target", targeted);
                    float fraction = mobile.HealthFraction;
                    bool hurt = fraction >= 0f && fraction < 0.999f;
                    plate.Health.style.display = !isPlayer && (hurt || targeted && fraction >= 0f) ? DisplayStyle.Flex : DisplayStyle.None;
                    plate.HealthFill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
                    show = true;
                }

                if (!show || go == null)
                {
                    plate.Root.style.display = DisplayStyle.None;
                    continue;
                }

                if (now - plate.AnchorCheckedAt > 0.25f)
                {
                    plate.AnchorCheckedAt = now;
                    plate.Anchor = AnchorOf(go) - go.transform.position;
                }

                if (now - plate.SightCheckedAt > SightCheckSeconds)
                {
                    plate.SightCheckedAt = now;
                    plate.InSight = serial == player.Serial || InSight(camera, go.transform.position, plate.Anchor);
                }

                if (!plate.InSight)
                {
                    plate.Root.style.display = DisplayStyle.None;
                    continue;
                }

                Place(plate.Root, camera, go.transform.position + plate.Anchor);
            }

            foreach (uint serial in _scratch)
            {
                OnRemoved(serial);
            }

            for (int i = _damage.Count - 1; i >= 0; i--)
            {
                DamageNumber number = _damage[i];
                float age = now - number.Born;
                MobileView mv = view.Entities.Get(number.Serial);
                if (age > DamageSeconds || mv == null)
                {
                    number.Label.RemoveFromHierarchy();
                    _damage.RemoveAt(i);
                    continue;
                }

                Vector3 anchor = _plates.TryGetValue(number.Serial, out Plate p) ? p.Anchor : Vector3.up * 2f;
                if (p != null && !p.InSight)
                {
                    number.Label.style.display = DisplayStyle.None;
                    continue;
                }

                Place(number.Label, camera, mv.transform.position + anchor + Vector3.up * (0.3f + age * 0.8f));
                number.Label.style.opacity = 1f - Mathf.Clamp01((age - DamageSeconds * 0.6f) / (DamageSeconds * 0.4f));
            }
        }

        /// <summary>
        /// Line of sight from the camera to a mobile: its head or its middle is visible past solid colliders
        /// (terrain, buildings). Click colliders are triggers, so other mobiles don't hide each other.
        /// </summary>
        private static bool InSight(Camera camera, Vector3 feet, Vector3 anchor)
        {
            Vector3 eye = camera.transform.position;
            Vector3 head = feet + anchor - Vector3.up * 0.3f;
            Vector3 middle = feet + anchor * 0.5f;
            return !Physics.Linecast(eye, head, ~0, QueryTriggerInteraction.Ignore) ||
                   !Physics.Linecast(eye, middle, ~0, QueryTriggerInteraction.Ignore);
        }

        private void Place(VisualElement element, Camera camera, Vector3 world)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                element.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panel = RuntimePanelUtils.ScreenToPanel(element.panel ?? _overhead.panel, new Vector2(screen.x, Screen.height - screen.y));
            element.style.display = DisplayStyle.Flex;
            element.style.left = panel.x;
            element.style.top = panel.y;
        }

        private static readonly List<Renderer> Renderers = new List<Renderer>();

        /// <summary>Where a plate sits: just above the head bone for humanoid rigs, else above the model's top.</summary>
        private static Vector3 AnchorOf(GameObject go)
        {
            // Humanoid rigs: over the head bone itself (skinned-mesh bounds are padded, and a big creature's head
            // isn't always over its feet).
            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head.position + Vector3.up * (0.35f * Mathf.Max(0.5f, animator.transform.lossyScale.y));
                }
            }

            return new Vector3(go.transform.position.x, TopOf(go) + 0.15f, go.transform.position.z);
        }

        private static float TopOf(GameObject go)
        {
            go.GetComponentsInChildren(Renderers);
            float top = go.transform.position.y + 1.8f;
            bool any = false;
            foreach (Renderer r in Renderers)
            {
                if (!r.enabled || r is ParticleSystemRenderer)
                {
                    continue;
                }

                top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
                any = true;
            }

            return top;
        }

        private bool IsOverUI(Vector2 screen)
        {
            IPanel panel = _root?.panel;
            if (panel == null)
            {
                return false;
            }

            Vector2 position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            VisualElement picked = panel.Pick(position);
            return picked != null && picked != _root && picked.name != "hud";
        }
    }
}
