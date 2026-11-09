using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.Net;

namespace UOClient
{
    /// <summary>
    /// Binds LoginScreen.uxml to the session: account login, shard pick, character pick/create.
    /// Remembers server, port and account name in PlayerPrefs; never the password.
    /// </summary>
    /// <remarks>
    /// The PanelRenderer hands us a fresh element tree through the reload callback (on enable and
    /// on UXML live reload), so every element reference is re-bound there and the current page is
    /// restored from <see cref="_currentPage"/> rather than assumed.
    /// </remarks>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class LoginScreenController : MonoBehaviour
    {
        private const string HostPref = "uo.host";
        private const string PortPref = "uo.port";
        private const string AccountPref = "uo.account";
        private const string DefaultHost = "192.168.68.91";
        private const string LoginPageName = "login-page";

        private UoSession _session;
        private string _currentPage = LoginPageName;
        private VisualElement _root, _loginPage, _serverPage, _characterPage, _worldPage;
        private TextField _host, _account, _password, _newCharacterName;
        private IntegerField _port;
        private Button _loginButton, _backButton;
        private ScrollView _serverList, _characterList;
        private Label _status, _worldSummary;

        private void OnEnable() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);

        private void OnDisable() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            _root = root;
            _loginPage = root.Q("login-page");
            _serverPage = root.Q("server-page");
            _characterPage = root.Q("character-page");
            _worldPage = root.Q("world-page");
            _host = root.Q<TextField>("host");
            _port = root.Q<IntegerField>("port");
            _account = root.Q<TextField>("account");
            _password = root.Q<TextField>("password");
            _newCharacterName = root.Q<TextField>("new-character-name");
            _loginButton = root.Q<Button>("login-button");
            _backButton = root.Q<Button>("back-button");
            _serverList = root.Q<ScrollView>("server-list");
            _characterList = root.Q<ScrollView>("character-list");
            _status = root.Q<Label>("status");
            _worldSummary = root.Q<Label>("world-summary");

            _host.value = PlayerPrefs.GetString(HostPref, DefaultHost);
            _port.value = PlayerPrefs.GetInt(PortPref, ClientIdentity.DefaultPort);
            _account.value = PlayerPrefs.GetString(AccountPref, string.Empty);

            _loginButton.clicked += OnLoginClicked;
            _backButton.clicked += OnBackClicked;
            root.Q<Button>("create-button").clicked += OnCreateClicked;
            _password.RegisterCallback<KeyDownEvent>(OnPasswordKeyDown, TrickleDown.TrickleDown);

            RestoreView();
            TryAutoLogin(); // the tree can arrive after Start
        }

        private void Start()
        {
            _session = UoClientRuntime.Instance.Session;
            _session.StateChanged += OnStateChanged;
            _session.Failed += OnFailed;
            _session.ServerListReceived += OnServerList;
            _session.CharacterListReceived += OnCharacterList;
            _session.EnteredWorld += OnEnteredWorld;
            RestoreView();
            TryAutoLogin();
        }

        // ------------------------------------------------------------------ editor autologin

        /// <summary>
        /// Editor-only: per-user login settings (host, port, account, password, character) in
        /// UserSettings/UOClientAutoLogin.txt, one key=value per line. When present, entering play mode logs
        /// straight in and picks the character. UserSettings is per-user and not shared with the project.
        /// </summary>
        private const string AutoLoginFile = "UserSettings/UOClientAutoLogin.txt";

        /// <summary>Once per play session, so Back and a failed login still behave normally.</summary>
        private static bool _autoLoginTried;
        private string _autoCharacter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAutoLogin() => _autoLoginTried = false; // also with domain reload off

        private void TryAutoLogin()
        {
#if UNITY_EDITOR
            if (_autoLoginTried || _session == null || _session.State != SessionState.Idle || _host == null)
            {
                return;
            }

            _autoLoginTried = true;
            // Give the runtime a moment to finish starting its session; a login sent in the very first
            // frames is dropped by the connection layer as it comes up.
            Invoke(nameof(AutoLoginNow), 0.5f);
#endif
        }

        private void AutoLoginNow()
        {
#if UNITY_EDITOR
            if (_session == null || _session.State != SessionState.Idle || _host == null)
            {
                return;
            }

            string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), AutoLoginFile);
            if (!System.IO.File.Exists(path))
            {
                return;
            }

            var settings = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith("#"))
                {
                    settings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }

            if (!settings.TryGetValue("account", out string account) || !settings.TryGetValue("password", out string password))
            {
                return;
            }

            if (settings.TryGetValue("host", out string host))
            {
                _host.value = host;
            }

            if (settings.TryGetValue("port", out string port) && int.TryParse(port, out int portNumber))
            {
                _port.value = portNumber;
            }

            settings.TryGetValue("character", out _autoCharacter);
            _account.value = account;
            _password.value = password;
            OnLoginClicked();
#endif
        }

        /// <summary>Picks the autologin character from the list, once.</summary>
        private bool TryAutoSelect(IReadOnlyList<CharacterSlot> characters)
        {
            if (string.IsNullOrEmpty(_autoCharacter))
            {
                return false;
            }

            string wanted = _autoCharacter;
            _autoCharacter = null;
            foreach (CharacterSlot character in characters)
            {
                if (string.Equals(character.Name, wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    _session.SelectCharacter(character);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Re-applies the current page (and its list contents) to a freshly loaded tree.</summary>
        private void RestoreView()
        {
            if (_root == null)
            {
                return;
            }

            if (_session != null && _session.State == SessionState.CharacterSelection)
            {
                OnCharacterList(_session.Characters);
                return;
            }

            ShowPage(_root.Q(_currentPage) ?? _loginPage);
        }

        private void OnDestroy()
        {
            if (_session == null)
            {
                return;
            }

            _session.StateChanged -= OnStateChanged;
            _session.Failed -= OnFailed;
            _session.ServerListReceived -= OnServerList;
            _session.CharacterListReceived -= OnCharacterList;
            _session.EnteredWorld -= OnEnteredWorld;
        }

        private void OnPasswordKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                OnLoginClicked();
            }
        }

        private void OnLoginClicked()
        {
            string host = _host.value.Trim();
            string account = _account.value.Trim();
            if (host.Length == 0 || account.Length == 0 || _password.value.Length == 0)
            {
                SetStatus("Enter a server, account and password.", true);
                return;
            }

            PlayerPrefs.SetString(HostPref, host);
            PlayerPrefs.SetInt(PortPref, _port.value);
            PlayerPrefs.SetString(AccountPref, account);
            PlayerPrefs.Save();

            _session.Login(host, _port.value, account, _password.value);
        }

        private void OnBackClicked()
        {
            _session.Disconnect();
            SetStatus(string.Empty, false);
            ShowPage(_loginPage);
        }

        private void OnCreateClicked()
        {
            string name = _newCharacterName.value.Trim();
            if (name.Length < 2)
            {
                SetStatus("Character names need at least two letters.", true);
                return;
            }

            // Default human warrior starting in Britain until a proper creation screen exists.
            _session.CreateCharacter(new NewCharacter { Name = name, CityIndex = FindCity("Britain") });
        }

        private int FindCity(string cityName)
        {
            IReadOnlyList<StartingCity> cities = _session.Cities;
            for (int i = 0; i < cities.Count; i++)
            {
                if (cities[i].City == cityName)
                {
                    return i;
                }
            }

            return 0;
        }

        private void OnStateChanged(SessionState state)
        {
            _loginButton.SetEnabled(state == SessionState.Idle || state == SessionState.Failed);
            switch (state)
            {
                case SessionState.Connecting:
                    SetStatus("Connecting…", false);
                    break;
                case SessionState.VerifyingAccount:
                    SetStatus("Verifying account…", false);
                    break;
                case SessionState.ConnectingToGameServer:
                    SetStatus("Connecting to the shard…", false);
                    break;
                case SessionState.EnteringWorld:
                    SetStatus("Entering Britannia…", false);
                    break;
                case SessionState.ServerSelection:
                case SessionState.CharacterSelection:
                    SetStatus(string.Empty, false);
                    break;
            }
        }

        private void OnFailed(string message)
        {
            _password.value = string.Empty;
            ShowPage(_loginPage);
            SetStatus(message, true);
        }

        private void OnServerList(IReadOnlyList<ServerInfo> servers)
        {
            if (servers.Count == 1)
            {
                _session.SelectServer(servers[0]); // nothing to choose between
                return;
            }

            _serverList.Clear();
            foreach (ServerInfo server in servers)
            {
                ServerInfo pick = server;
                _serverList.Add(MakeChoice($"{server.Name}   ({server.PercentFull}% full)", () => _session.SelectServer(pick)));
            }

            ShowPage(_serverPage);
        }

        private void OnCharacterList(IReadOnlyList<CharacterSlot> characters)
        {
            _password.value = string.Empty;
            if (TryAutoSelect(characters))
            {
                return;
            }

            _characterList.Clear();
            foreach (CharacterSlot character in characters)
            {
                CharacterSlot pick = character;
                _characterList.Add(MakeChoice(character.Name, () => _session.SelectCharacter(pick)));
            }

            if (characters.Count == 0)
            {
                _characterList.Add(new Label("No characters yet. Create one below.") { name = "empty-hint" });
            }

            ShowPage(_characterPage);
        }

        private void OnEnteredWorld(LoginConfirm player)
        {
            _worldSummary.text = $"In the world at {player.X}, {player.Y}, {player.Z}.\n" +
                                 "The 3D world view is the next milestone.";
            SetStatus(string.Empty, false);
            ShowPage(_worldPage);
        }

        private static Button MakeChoice(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("choice-button");
            return button;
        }

        private void ShowPage(VisualElement page)
        {
            _currentPage = page.name;
            foreach (VisualElement candidate in new[] { _loginPage, _serverPage, _characterPage, _worldPage })
            {
                candidate.EnableInClassList("hidden", candidate != page);
            }

            _backButton.EnableInClassList("hidden", page == _loginPage);
        }

        private void SetStatus(string message, bool isError)
        {
            _status.text = message;
            _status.EnableInClassList("error", isError);
        }
    }
}
