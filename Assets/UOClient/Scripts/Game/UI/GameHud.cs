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
        private ScrollView _log;
        private TextField _input;
        private readonly Dictionary<INamed, (VisualElement Plate, Label Name, Label Bubble)> _shown =
            new Dictionary<INamed, (VisualElement, Label, Label)>();
        private readonly Dictionary<int, (string Text, float Until)> _bubbles = new Dictionary<int, (string, float)>();
        private readonly List<INamed> _gone = new List<INamed>();

        private void OnEnable()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);
            WorldServices.Said += OnSaid;
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
            _log = root.Q<ScrollView>("chat-log");
            _input = root.Q<TextField>("chat-input");
            _shown.Clear();
            _input.RegisterCallback<KeyDownEvent>(OnChatKey, TrickleDown.TrickleDown);
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

            if (!GameInput.Typing && GameInput.Controls.Gameplay.Chat.WasPressedThisFrame())
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
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
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
        }

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
