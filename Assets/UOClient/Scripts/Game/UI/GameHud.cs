using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOClient.Buildings;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// The in-world HUD: names over players (with what they last said, for a few seconds), the chat log, and the chat
    /// line (Enter to talk, Enter to send, Esc to drop it). Also E: use the door nearest the line under the cursor,
    /// through the server.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class GameHud : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;

        private const float BubbleSeconds = 6f;
        private const int MaxLines = 60;
        private const float NameHeight = 2.15f;
        private const float NameRange = 60f;
        private const float DoorReach = 4f;
        private const float DoorAim = 1.6f;

        private VisualElement _plates;
        private VisualElement _doorPrompt;
        private ScrollView _log, _combat;
        private Button _chatTab, _combatTab;
        private const int CombatLines = 300;

        private static GameHud _instance;

        /// <summary>
        /// A line in the combat log (the chat window's Combat tab): your hits and misses, what hit you, deaths
        /// nearby, skill and stat changes, server messages. Kind styles it: hit, hurt, miss, death, skill, system.
        /// </summary>
        public static void AddCombat(string text, string kind) => _instance?.AddCombatLine(text, kind);

        private void AddCombatLine(string text, string kind)
        {
            if (_combat == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var line = new Label($"{System.DateTime.Now:HH:mm}  {text}");
            line.AddToClassList("log-line");
            line.AddToClassList("log-" + kind);
            _combat.Add(line);
            while (_combat.contentContainer.childCount > CombatLines)
            {
                _combat.contentContainer.RemoveAt(0);
            }

            if (!_combat.ClassListContains("hidden"))
            {
                _chatActive = Time.unscaledTime; // the open tab's new line brings the window back
            }

            _combat.schedule.Execute(() => _combat.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        /// <summary>Chat or Combat: which list the window shows.</summary>
        private void ShowTab(bool combat)
        {
            _log.EnableInClassList("hidden", combat);
            _combat.EnableInClassList("hidden", !combat);
            _chatTab.EnableInClassList("chat-tab-active", !combat);
            _combatTab.EnableInClassList("chat-tab-active", combat);
            _chatActive = Time.unscaledTime;
            (combat ? _combat : _log).schedule.Execute(() => (combat ? _combat : _log).scrollOffset = new Vector2(0f, float.MaxValue));
        }
        private TextField _input;
        private readonly Dictionary<INamed, (VisualElement Plate, Label Name, Label Bubble)> _shown =
            new Dictionary<INamed, (VisualElement, Label, Label)>();
        private readonly Dictionary<int, (string Text, float Until)> _bubbles = new Dictionary<int, (string, float)>();
        private readonly List<INamed> _gone = new List<INamed>();

        private void OnEnable()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);
            WorldServices.Said += OnSaid;
            GameInput.PointerOverUI = OverAnyPanel; // only the old client set this, so the wheel zoomed over every window
        }

        private static readonly List<VisualElement> _uiRoots = new List<VisualElement>();
        private static readonly List<IPanel> _panels = new List<IPanel>();

        /// <summary>A HUD's UI (its panel's root) for the pointer check: on it, the wheel and clicks are the UI's.</summary>
        public static void RegisterUI(VisualElement root)
        {
            _uiRoots.RemoveAll(r => r == null || r.panel == null);
            if (root != null && !_uiRoots.Contains(root))
            {
                _uiRoots.Add(root);
            }
        }

        /// <summary>
        /// Is the pointer on any window, list, button or field of any of the game's UI panels (HUD, windows, chat,
        /// menus)? Then the mouse wheel scrolls that, and clicks go to it, instead of the camera and the world.
        /// </summary>
        private static bool OverAnyPanel(Vector2 screen)
        {
            _panels.Clear();
            foreach (VisualElement root in _uiRoots)
            {
                IPanel panel = root?.panel;
                if (panel != null && !_panels.Contains(panel))
                {
                    _panels.Add(panel);
                }
            }

            foreach (IPanel panel in _panels)
            {
                Vector2 position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                VisualElement picked = panel.Pick(position);
                if (picked != null && picked != panel.visualTree && picked.resolvedStyle.opacity > 0.05f)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);
            WorldServices.Said -= OnSaid;
        }

        private VisualElement _hudRoot, _chat;
        private float _chatActive;

        /// <summary>Seconds with no chat in or out before the chat window fades away.</summary>
        private const float ChatIdleSeconds = 12f;

        /// <summary>
        /// The chat window shows while lines come in or you're typing (Enter), and fades out after a quiet spell; it
        /// comes back with the next line.
        /// </summary>
        private void UpdateChatFade()
        {
            if (_chat == null)
            {
                return;
            }

            bool typing = _input != null && !_input.ClassListContains("hidden");
            if (typing)
            {
                _chatActive = Time.unscaledTime;
            }

            float target = Time.unscaledTime - _chatActive < ChatIdleSeconds ? 1f : 0f;
            float now = _chat.resolvedStyle.opacity;
            if (!Mathf.Approximately(now, target))
            {
                // Quick to appear, slow to go.
                _chat.style.opacity = Mathf.MoveTowards(now, target, Time.unscaledDeltaTime * (target > now ? 6f : 0.7f));
            }

            // Faded away, the window lets clicks and the mouse wheel through to the world (the camera zooms again).
            PickingMode picking = _chat.resolvedStyle.opacity > 0.1f ? PickingMode.Position : PickingMode.Ignore;
            if (_log.pickingMode != picking)
            {
                foreach (VisualElement e in new VisualElement[] { _log, _combat, _chatTab, _combatTab })
                {
                    e.pickingMode = picking;
                }
            }
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            _hudRoot = root;
            _chat = root.Q("chat");
            _chatActive = Time.unscaledTime;
            _plates = root.Q("nameplates");
            _doorPrompt = ControlPrompts.Create("{Use} Open");
            _doorPrompt.AddToClassList("door-prompt");
            _doorPrompt.style.display = DisplayStyle.None;
            _plates.Add(_doorPrompt);
            _instance = this;
            RegisterUI(root);
            _log = root.Q<ScrollView>("chat-log");
            _combat = root.Q<ScrollView>("combat-log");
            _chatTab = root.Q<Button>("chat-tab-chat");
            _combatTab = root.Q<Button>("chat-tab-combat");
            _chatTab.clicked += () => ShowTab(false);
            _combatTab.clicked += () => ShowTab(true);
            // Pointing at the window keeps it up (and the wheel scrolls it, not the camera: GameInput.IsPointerOverUI).
            _chat.RegisterCallback<PointerMoveEvent>(_ => _chatActive = Time.unscaledTime, TrickleDown.TrickleDown);
            _input = root.Q<TextField>("chat-input");
            _shown.Clear();
            _input.RegisterCallback<KeyDownEvent>(OnChatKey, TrickleDown.TrickleDown);
            // A text field can also turn Enter into a submit: send what's typed, or just close an empty box.
            _input.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                string text = _input.value?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    _network.ClientManager.Broadcast(new SayRequest { Text = text });
                }

                CloseChat();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            _input.RegisterCallback<FocusOutEvent>(_ => CloseChat());
        }

        private void Update()
        {
            // The HUD (and everything in its panel) only once you're in the world: not over the main menu.
            if (_hudRoot != null)
            {
                DisplayStyle shown = PlayerMotor.Local != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (_hudRoot.style.display != shown)
                {
                    _hudRoot.style.display = shown;
                }
            }

            UpdateChatFade();
            if (_input == null || PlayerMotor.Local == null)
            {
                return;
            }

            // The Enter that closed the box mustn't open it again: wait for it to be let go first.
            if (_chatKeyHeld && !GameInput.Controls.Gameplay.Chat.IsPressed())
            {
                _chatKeyHeld = false;
            }

            if (!GameInput.Typing && !_chatKeyHeld && Time.frameCount > _chatClosedFrame + 1 &&
                GameInput.Controls.Gameplay.Chat.WasPressedThisFrame())
            {
                _input.RemoveFromClassList("hidden");
                _input.value = string.Empty;
                GameInput.SetTyping(true);
                _input.schedule.Execute(() => _input.Focus());
            }

            if (!GameInput.Typing && GameInput.Controls.Gameplay.Use.WasPressedThisFrame())
            {
                UseDoor();
            }
        }

        private void OnChatKey(KeyDownEvent e)
        {
            // Enter arrives as the key, or as a bare newline character, depending on the text field's state.
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == (char)10 || e.character == (char)13)
            {
                string text = _input.value?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    _network.ClientManager.Broadcast(new SayRequest { Text = text });
                }

                CloseChat();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseChat();
                e.StopPropagation();
            }
        }

        private void CloseChat()
        {
            if (_input == null || _input.ClassListContains("hidden"))
            {
                return;
            }

            _input.AddToClassList("hidden");
            _input.Blur();
            GameInput.SetTyping(false);
            _chatClosedFrame = Time.frameCount;
            _chatKeyHeld = true;
        }

        /// <summary>
        /// Closing the box with Enter: the same Enter is the chat key, and gameplay keys come back on while it's still
        /// down, which opened the box again at once (an empty box looked like Enter did nothing).
        /// </summary>
        private bool _chatKeyHeld;
        private int _chatClosedFrame = -10;

        private void OnSaid(SaidMessage message)
        {
            _bubbles[message.SpeakerObjectId] = (message.Text, Time.time + BubbleSeconds);
            if (_log == null)
            {
                return;
            }

            var line = new Label($"{message.Speaker}: {message.Text}");
            line.AddToClassList("chat-line");
            _log.Add(line);
            _chatActive = Time.unscaledTime; // a new line brings the chat back
            while (_log.contentContainer.childCount > MaxLines)
            {
                _log.contentContainer.RemoveAt(0);
            }

            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (_plates == null || camera == null)
            {
                return;
            }

            ShowDoorPrompt(camera);
            _gone.Clear();
            foreach (INamed player in _shown.Keys)
            {
                if (player as Object == null || !Nameplates.Seen.Contains(player))
                {
                    _gone.Add(player);
                }
            }

            foreach (INamed player in _gone)
            {
                _shown[player].Plate.RemoveFromHierarchy();
                _shown.Remove(player);
            }

            foreach (INamed player in Nameplates.Seen)
            {
                if (player as Object == null)
                {
                    continue;
                }

                var unseen = (player as Component) != null ? ((Component)player).GetComponent<UOGame.Combat.Combatant>() : null;
                if (unseen != null && (unseen.Hidden.Value || unseen.AdminMode.Value && !Server.AdminService.IsAdmin) && !(PlayerMotor.Local != null && ((Component)player).transform == PlayerMotor.Local.transform))
                {
                    if (_shown.TryGetValue(player, out var gone))
                    {
                        gone.Plate.RemoveFromHierarchy();
                        _shown.Remove(player);
                    }

                    continue; // no name over someone you can't see
                }

                if (!_shown.TryGetValue(player, out var plate))
                {
                    var box = new VisualElement { pickingMode = PickingMode.Ignore };
                    box.AddToClassList("nameplate");
                    var bubble = new Label { pickingMode = PickingMode.Ignore };
                    bubble.AddToClassList("nameplate-bubble");
                    var name = new Label { pickingMode = PickingMode.Ignore };
                    name.AddToClassList("nameplate-name");
                    box.Add(bubble);
                    box.Add(name);
                    _plates.Add(box);
                    plate = (box, name, bubble);
                    _shown[player] = plate;
                }

                Transform body = player.Body;
                Vector3 head = body.position + Vector3.up * player.NameHeight;
                Vector3 screen = camera.WorldToScreenPoint(head);
                bool visible = screen.z > 0f && screen.z < NameRange;
                plate.Plate.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    continue;
                }

                Vector2 panel = RuntimePanelUtils.ScreenToPanel(_plates.panel, new Vector2(screen.x, Screen.height - screen.y));
                plate.Plate.style.left = panel.x;
                plate.Plate.style.top = panel.y;
                plate.Name.text = player.DisplayName;
                plate.Name.style.color = player.NameColour;
                bool talking = _bubbles.TryGetValue(player.ObjectId, out var said) && Time.time < said.Until;
                plate.Bubble.text = talking ? said.Text : string.Empty;
                plate.Bubble.style.display = talking ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>E: asks the server to open the door the cursor is on (the one the prompt shows).</summary>
        private void UseDoor()
        {
            BuildingDoor door = PickDoor();
            if (door != null)
            {
                _network.ClientManager.Broadcast(new UseDoor { DoorId = door.WorldId });
            }
        }

        /// <summary>
        /// The door E would use: the one whose doorway is nearest the line under the cursor, within reach of the player
        /// (measured from the player's middle to the doorway, as the server measures).
        /// </summary>
        /// <summary>The door E would use right now, or null (other E uses, like looting, yield to doors).</summary>
        public static BuildingDoor DoorUnderCursor() => PickDoor();

        private static BuildingDoor PickDoor()
        {
            Camera camera = Camera.main;
            PlayerMotor player = PlayerMotor.Local;
            if (camera == null || player == null || GameInput.Typing)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            Vector3 body = player.transform.position + Vector3.up;
            BuildingDoor best = null;
            float bestOff = DoorAim;
            foreach (BuildingDoor door in BuildingDoor.All)
            {
                if (door == null || door.WorldId == 0 || door.LeafCount == 0)
                {
                    continue;
                }

                Vector3 middle = door.OpeningCentre;
                Vector3 toDoor = middle - ray.origin;
                if (Vector3.Dot(toDoor, ray.direction) <= 0f || (middle - body).magnitude > DoorReach)
                {
                    continue;
                }

                float off = Vector3.Cross(ray.direction, toDoor).magnitude;
                if (off < bestOff)
                {
                    bestOff = off;
                    best = door;
                }
            }

            return best;
        }

        /// <summary>Shows "[Use key] Open" on the door the Use key would open.</summary>
        private void ShowDoorPrompt(Camera camera)
        {
            if (_doorPrompt == null)
            {
                return;
            }

            BuildingDoor door = PickDoor();
            bool show = door != null && door.State == DoorState.Closed;
            _doorPrompt.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(door.OpeningCentre);
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(_plates.panel, new Vector2(screen.x, Screen.height - screen.y));
            _doorPrompt.style.left = panel.x;
            _doorPrompt.style.top = panel.y;
        }
    }
}
