using System;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Net;
using UOGame.Server;

namespace UOGame
{
    /// <summary>
    /// Starts the game one of four ways, then walks the player through logging in and picking a character:
    /// <list type="bullet">
    /// <item><b>Headless server:</b> a build started with <c>-server</c> (or in batch mode) listens on <c>-port</c>
    /// (default 7770) and never shows a menu.</item>
    /// <item><b>Solo:</b> server and client in this process, listening only on this machine, on a local account.</item>
    /// <item><b>Host:</b> the same, open to friends on the network (UDP port allowed in the firewall).</item>
    /// <item><b>Join:</b> a client connecting to a host or a headless server.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class GameBoot : MonoBehaviour
    {
        public const ushort DefaultPort = 7770;
        /// <summary>The shard players connect to unless they type another address.</summary>
        public const string DefaultServer = "seriousfun.tplinkdns.com";

        /// <summary>The account Solo play logs in to (the server only listens on this machine then).</summary>
        private const string SoloAccount = "Solo";
        private const string SoloPassword = "solo-local";

        [SerializeField] private NetworkManager _network;

        private VisualElement _root, _connectPage, _characterPage;
        private ScrollView _characters;
        private TextField _account, _password, _address, _newName;
        private IntegerField _port;
        private Label _status;
        private Button _back;
        private bool _inWorld;
        private bool _answered;
        private float _giveUpAt = float.PositiveInfinity;
        private const float ConnectSeconds = 30f; // a busy server (or Solo starting its own) can take a while to answer

        private Tugboat Transport => _network.TransportManager.GetTransport<Tugboat>();

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isBatchMode || Array.IndexOf(args, "-server") >= 0)
            {
                ushort port = DefaultPort;
                int i = Array.IndexOf(args, "-port");
                if (i >= 0 && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort p))
                {
                    port = p;
                }

                Application.targetFrameRate = 60; // the menu and HUD panels stay off: a server has no screen
                Transport.SetServerBindAddress(string.Empty, IPAddressType.IPv4);
                Transport.SetPort(port);
                _network.ServerManager.StartConnection();
                Debug.Log($"[Game] Headless server listening on port {port}");
                return;
            }

            // Panels start switched off in the scene (a headless server never builds them); a client turns them on.
            foreach (PanelRenderer panel in FindObjectsByType<PanelRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                panel.enabled = true;
            }

            _network.ClientManager.OnClientConnectionState += OnClientState;
            GameAuthenticator.Answered += OnLoginAnswered;
            CharacterService.Refused += OnRefused;
        }

        private void OnEnable() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);

        private void OnDisable() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.ClientManager.OnClientConnectionState -= OnClientState;
            }

            GameAuthenticator.Answered -= OnLoginAnswered;
            CharacterService.Refused -= OnRefused;
            _creator?.Dispose();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            _root = root;
            _connectPage = root.Q("connect-page");
            _characterPage = root.Q("character-page");
            _characters = root.Q<ScrollView>("character-list");
            _account = root.Q<TextField>("account");
            _password = root.Q<TextField>("password");
            _address = root.Q<TextField>("address");
            _newName = root.Q<TextField>("new-character-name");
            _port = root.Q<IntegerField>("port");
            _status = root.Q<Label>("status");
            _back = root.Q<Button>("back-button");
            _account.value = PlayerPrefs.GetString("game.account", string.Empty);
            // Players connect to the shard by default; in the editor, to this machine (Solo and local tests).
            _address.value = PlayerPrefs.GetString("game.address", Application.isEditor ? "127.0.0.1" : DefaultServer);
            root.Q<Button>("solo-button").clicked += () => Play(host: true, open: false, SoloAccount, SoloPassword);
            root.Q<Button>("host-button").clicked += () => Play(host: true, open: true, _account.value, _password.value);
            root.Q<Button>("join-button").clicked += () => Play(host: false, open: false, _account.value, _password.value);
            // New characters are made on the creation screen (look, profession or your own stats and skills).
            _creator?.Dispose();
            _creator = new UI.CharacterCreator(root.Q("root") ?? root, request => _network.ClientManager.Broadcast(request));
            root.Q<Button>("create-button").text = "New…";
            root.Q<Button>("create-button").clicked += () => _creator.Open(_newName.value);
            _back.clicked += Leave;
            foreach (TextField field in new[] { _account, _password, _address, _newName })
            {
                field.RegisterCallback<FocusInEvent>(_ => GameInput.SetTyping(true));
                field.RegisterCallback<FocusOutEvent>(_ => GameInput.SetTyping(false));
            }

            ShowConnect();
        }

        private void Play(bool host, bool open, string account, string password)
        {
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                SetStatus("Enter your account and password.");
                return;
            }

            ushort port = (ushort)Mathf.Clamp(_port?.value ?? DefaultPort, 1, 65535);
            Transport.SetPort(port);
            GameAuthenticator.Account = account.Trim();
            GameAuthenticator.Password = password;
            if (account != SoloAccount)
            {
                PlayerPrefs.SetString("game.account", account.Trim());
            }

            if (host)
            {
                // Solo listens only on this machine; a hosted game on every network interface.
                Transport.SetServerBindAddress(open ? string.Empty : "127.0.0.1", IPAddressType.IPv4);
                _network.ServerManager.StartConnection();
                Transport.SetClientAddress("127.0.0.1");
            }
            else
            {
                string address = string.IsNullOrWhiteSpace(_address?.value) ? "127.0.0.1" : _address.value.Trim();
                PlayerPrefs.SetString("game.address", address);
                Transport.SetClientAddress(address);
            }

            SetStatus(host ? (open ? $"Hosting on port {port}..." : "Starting...") : $"Connecting to {_address?.value}:{port}...");
            _answered = false;
            _giveUpAt = Time.unscaledTime + ConnectSeconds;
            _network.ClientManager.StartConnection();
        }

        private void OnLoginAnswered(LoginResult result)
        {
            _answered = true;
            _giveUpAt = float.PositiveInfinity;
            if (!result.Ok)
            {
                SetStatus(result.Reason);
                Leave();
                return;
            }

            _characters.Clear();
            foreach (CharacterSummary character in result.Characters ?? Array.Empty<CharacterSummary>())
            {
                int id = character.Id;
                var button = new Button(() => _network.ClientManager.Broadcast(new PlayCharacter { Id = id })) { text = character.Name };
                button.AddToClassList("choice-button");
                _characters.Add(button);
            }

            _connectPage.AddToClassList("hidden");
            _characterPage.RemoveFromClassList("hidden");
            _back.RemoveFromClassList("hidden");
            SetStatus(result.Characters == null || result.Characters.Length == 0 ? "Create your first character." : string.Empty);
        }

        private void OnRefused(string reason)
        {
            SetStatus(reason);
            if (_creator != null && _creator.IsOpen)
            {
                _creator.Refused(reason);
            }
        }

        private UI.CharacterCreator _creator;

        private void Update()
        {
            _creator?.Tick();
            if (!_answered && Time.unscaledTime >= _giveUpAt)
            {
                _giveUpAt = float.PositiveInfinity;
                Leave();
                SetStatus("No answer from the server. Check the address and port, and that the server is running.");
            }

            // The menu goes once our character is in the world, and comes back if it leaves.
            bool inWorld = PlayerMotor.Local != null;
            if (inWorld != _inWorld && _root != null)
            {
                _inWorld = inWorld;
                if (inWorld)
                {
                    _creator?.Close();
                }
                _root.style.display = inWorld ? DisplayStyle.None : DisplayStyle.Flex;
                if (inWorld)
                {
                    GameInput.SetTyping(false);
                }
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (_network.IsServerStarted)
                {
                    _network.ServerManager.StopConnection(true);
                }

                ShowConnect();
                if (!_answered && _giveUpAt != float.PositiveInfinity)
                {
                    // Dropped before the server answered our login.
                    SetStatus("Couldn't reach the server, or it closed the connection.");
                }
                else if (_answered && !_inWorld && string.IsNullOrEmpty(_status?.text))
                {
                    SetStatus("Disconnected.");
                }
                else if (_inWorld || string.IsNullOrEmpty(_status?.text))
                {
                    SetStatus("Disconnected from the server.");
                }

                _giveUpAt = float.PositiveInfinity;
            }
        }

        private void Leave()
        {
            _network.ClientManager.StopConnection();
            if (_network.IsServerStarted)
            {
                _network.ServerManager.StopConnection(true);
            }

            ShowConnect();
        }

        private void ShowConnect()
        {
            _creator?.Close();
            _connectPage?.RemoveFromClassList("hidden");
            _characterPage?.AddToClassList("hidden");
            _back?.AddToClassList("hidden");
        }

        private void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text ?? string.Empty;
            }
        }
    }
}
